using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ProgressiveSummaryService
    {
        private const string StatusFileName = "layer_completion_status.json";
        private LayerCompletionStatus _status = new();
        private volatile bool _loaded = false;

        public ProgressiveSummaryService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _loaded = false;
                    _status = new LayerCompletionStatus();
                    TM.App.Log("[ProgressiveSummary] 项目切换，已重置层级完成状态");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProgressiveSummary] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ProgressiveSummary] {key}: {ex.Message}");
        }

        private static readonly Dictionary<string, string[]> LayerUpstreams = new()
        {
            ["SmartParsing"] = Array.Empty<string>(),
            ["Templates"] = Array.Empty<string>(),
            ["Worldview"] = new[] { "Templates" },
            ["Characters"] = new[] { "Templates", "Worldview" },
            ["Plot"] = new[] { "Templates", "Worldview", "Characters" }
        };

        public async Task MarkLayerCompletedAsync(string layer)
        {
            await EnsureLoadedAsync();

            if (!_status.Layers.TryGetValue(layer, out var layerStatus))
            {
                TM.App.Log($"[ProgressiveSummary] 未知层级: {layer}");
                return;
            }

            layerStatus.IsCompleted = true;
            layerStatus.CompletedAt = DateTime.UtcNow;
            layerStatus.DataVersion = await GetDataVersionAsync(layer);
            layerStatus.SummaryVersion = layerStatus.DataVersion;

            await SaveStatusAsync();
            TM.App.Log($"[ProgressiveSummary] 层级已锁定: {layer}");
        }

        public async Task UnlockLayerAsync(string layer)
        {
            await EnsureLoadedAsync();

            if (!_status.Layers.TryGetValue(layer, out var layerStatus))
                return;

            if (!layerStatus.IsCompleted)
                return;

            layerStatus.IsCompleted = false;
            layerStatus.CompletedAt = null;

            await CascadeUnlockAsync(layer);

            await SaveStatusAsync();
            TM.App.Log($"[ProgressiveSummary] 层级已解锁: {layer}");
        }

        private async Task CascadeUnlockAsync(string unlockedLayer)
        {
            foreach (var (layer, upstreams) in LayerUpstreams)
            {
                if (Array.IndexOf(upstreams, unlockedLayer) >= 0)
                {
                    if (_status.Layers.TryGetValue(layer, out var status) && status.IsCompleted)
                    {
                        status.IsCompleted = false;
                        status.CompletedAt = null;
                        TM.App.Log($"[ProgressiveSummary] 级联解锁: {layer}");

                        await CascadeUnlockAsync(layer);
                    }
                }
            }
        }

        public async Task<bool> IsLayerCompletedAsync(string layer)
        {
            await EnsureLoadedAsync();
            return _status.Layers.TryGetValue(layer, out var status) && status.IsCompleted;
        }

        public async Task<bool> AreAllLayersCompletedAsync()
        {
            await EnsureLoadedAsync();
            foreach (var status in _status.Layers.Values)
            {
                if (!status.IsCompleted)
                    return false;
            }
            return true;
        }

        public async Task<List<string>> GetLayersNeedingRegenerationAsync()
        {
            await EnsureLoadedAsync();
            var result = new List<string>();

            foreach (var (layer, status) in _status.Layers)
            {
                if (status.IsCompleted && status.NeedsRegeneration)
                {
                    result.Add(layer);
                }
            }

            return result;
        }

        public async Task<LayerProceedResult> CheckLayerPrerequisitesAsync(string targetLayer)
        {
            await EnsureLoadedAsync();
            var result = new LayerProceedResult { CanProceed = true };

            var prerequisites = targetLayer switch
            {
                "SmartParsing" => Array.Empty<string>(),
                "Templates" => Array.Empty<string>(),
                "Worldview" => new[] { "Templates" },
                "Characters" => new[] { "Worldview" },
                "Plot" => new[] { "Characters" },
                "Outline" => new[] { "Plot" },
                "Planning" => new[] { "Plot" },
                "Blueprint" => new[] { "Plot" },
                "Content" => new[] { "Plot" },
                _ => new[] { "Plot" }
            };

            var incompleted = new List<string>();
            foreach (var prereq in prerequisites)
            {
                if (_status.Layers.TryGetValue(prereq, out var status) && !status.IsCompleted)
                {
                    incompleted.Add(prereq);
                }
            }

            if (incompleted.Count > 0)
            {
                result.IncompletedLayers = incompleted;
                result.Warnings.Add($"建议先完成以下层级：{string.Join("、", incompleted)}");

                foreach (var layer in incompleted)
                {
                    var suggestion = layer switch
                    {
                        "SmartParsing" => "智能拆书层：定义参考书籍分析",
                        "Templates" => "创作模板层：定义核心灵感和素材",
                        "Worldview" => "世界观层：建立基础规则和设定",
                        "Characters" => "角色层：完善主要角色设定",
                        "Plot" => "剧情层：确定故事结构和冲突",
                        _ => $"{layer}层"
                    };
                    result.Warnings.Add($"  • {suggestion}");
                }
            }

            return result;
        }

        public async Task<Dictionary<string, bool>> GetCompletionStatusAsync()
        {
            await EnsureLoadedAsync();
            var result = new Dictionary<string, bool>();

            foreach (var (layer, status) in _status.Layers)
            {
                result[layer] = status.IsCompleted;
            }

            return result;
        }

        #region 渐进式摘要生成（总纲 18.1）

        public async Task<GlobalSummary> GetAvailableSummaryAsync()
        {
            await EnsureLoadedAsync();
            var summary = new GlobalSummary();

            if (await IsLayerCompletedAsync("Templates"))
            {
                summary.Decisions.CoreInspiration = await GetTemplatesInspirationAsync();
            }

            if (await IsLayerCompletedAsync("Worldview"))
            {
                summary.Decisions.CoreWorldRules = await GetWorldRulesAsync();
                summary.CoreRules = await GetWorldConstraintsAsync();
            }

            if (await IsLayerCompletedAsync("Characters"))
            {
                summary.Decisions.CharacterDecisions = await GetCharacterDecisionsAsync();
                summary.MainCharacters = await GetMainCharactersAsync();
            }

            if (await IsLayerCompletedAsync("Plot"))
            {
                summary.Decisions.PlotDecisions = await GetPlotDecisionsAsync();
                summary.MainConflict = await GetMainConflictAsync();
                summary.StorySummary = await GetStorySummaryAsync();
            }

            foreach (var (layer, status) in _status.Layers)
            {
                if (status.IsCompleted)
                    summary.CompletedLayers.Add(layer);
            }

            summary.IsEmpty = summary.CompletedLayers.Count == 0;
            return summary;
        }

        public async Task<string> GetTemplatesInspirationAsync()
        {
            var path = Path.Combine(GetLayerStoragePath("Templates"), "CreativeMaterials", "creative_materials.json");
            if (!File.Exists(path)) return string.Empty;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var items = JsonSerializer.Deserialize<List<CreativeMaterialData>>(json, JsonOptions);
                if (items == null || items.Count == 0) return string.Empty;

                var inspirations = items
                    .Where(i => i.IsEnabled)
                    .Select(i => !string.IsNullOrWhiteSpace(i.OverallIdea) ? i.OverallIdea : i.Name)
                    .Where(s => !string.IsNullOrEmpty(s))
                    ;
                return string.Join("；", inspirations);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetTemplatesInspirationAsync), ex);
                return string.Empty;
            }
        }

        public async Task<List<RuleDecision>> GetWorldRulesAsync()
        {
            var result = new List<RuleDecision>();
            var path = Path.Combine(GetLayerStoragePath("Worldview"), "world_rules.json");
            if (!File.Exists(path)) return result;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var rules = JsonSerializer.Deserialize<List<WorldRulesData>>(json, JsonOptions);
                if (rules == null) return result;

                foreach (var rule in rules.Where(r => r.IsEnabled))
                {
                    result.Add(new RuleDecision
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Reason = !string.IsNullOrWhiteSpace(rule.OneLineSummary) ? rule.OneLineSummary : rule.HardRules,
                        Impact = rule.PowerSystem
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetWorldRulesAsync), ex);
            }
            return result;
        }

        public async Task<List<Models.Index.IndexItem>> GetWorldConstraintsAsync()
        {
            var result = new List<Models.Index.IndexItem>();
            var path = Path.Combine(GetLayerStoragePath("Worldview"), "world_rules.json");
            if (!File.Exists(path)) return result;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var rules = JsonSerializer.Deserialize<List<WorldRulesData>>(json, JsonOptions);
                if (rules == null) return result;

                foreach (var rule in rules.Where(r => r.IsEnabled))
                {
                    result.Add(new Models.Index.IndexItem
                    {
                        Id = rule.Id,
                        Name = rule.Name,
                        Type = "世界观规则",
                        BriefSummary = $"{rule.Name}(世界规则)",
                        DeepSummary = TruncateString(rule.GetDeepSummary(), 80)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetWorldConstraintsAsync), ex);
            }
            return result;
        }

        public async Task<List<CharacterDecision>> GetCharacterDecisionsAsync()
        {
            var result = new List<CharacterDecision>();
            var path = Path.Combine(GetLayerStoragePath("Characters"), "character_rules.json");
            if (!File.Exists(path)) return result;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var chars = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions);
                if (chars == null) return result;

                var mainChars = chars
                    .Where(c => c.IsEnabled)
                    .Where(c => c.CharacterType == "主角" || c.CharacterType == "主要角色")
                    .OrderByDescending(c => c.GetImportanceWeight());

                foreach (var c in mainChars)
                {
                    result.Add(new CharacterDecision
                    {
                        CharacterId = c.Id,
                        CharacterName = c.Name,
                        DesignReason = !string.IsNullOrWhiteSpace(c.Need) ? c.Need : c.Want,
                        RelatedRules = !string.IsNullOrWhiteSpace(c.GrowthPath) ? c.GrowthPath : c.FlawBelief
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetCharacterDecisionsAsync), ex);
            }
            return result;
        }

        public async Task<List<CharacterDeepSummary>> GetMainCharactersAsync()
        {
            var result = new List<CharacterDeepSummary>();
            var path = Path.Combine(GetLayerStoragePath("Characters"), "character_rules.json");
            if (!File.Exists(path)) return result;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var chars = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions);
                if (chars == null) return result;

                var mainChars = chars
                    .Where(c => c.IsEnabled)
                    .OrderByDescending(c => c.GetImportanceWeight())
                    .ThenByDescending(c => c.UpdatedAt)
                    .Take(5);

                foreach (var c in mainChars)
                {
                    var coreTraits = string.Join("；", new[] { c.Identity, c.FlawBelief, c.Race, c.Appearance }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    var abilityOrigin = string.Join("；", new[] { c.SpecialAbilities, c.CombatSkills, c.NonCombatSkills }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

                    result.Add(new CharacterDeepSummary
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Role = c.CharacterType,
                        CoreTraits = TruncateString(coreTraits, 40),
                        AbilityOrigin = TruncateString(abilityOrigin, 40),
                        KeyConstraint = TruncateString(!string.IsNullOrWhiteSpace(c.Need) ? c.Need : c.GrowthPath, 40)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetMainCharactersAsync), ex);
            }
            return result;
        }

        public async Task<List<PlotDecision>> GetPlotDecisionsAsync()
        {
            var result = new List<PlotDecision>();
            var path = Path.Combine(GetLayerStoragePath("Plot"), "plot_rules.json");
            if (!File.Exists(path)) return result;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);
                if (rules == null) return result;

                foreach (var rule in rules.Where(r => r.IsEnabled))
                {
                    result.Add(new PlotDecision
                    {
                        PlotPointId = rule.Id,
                        Description = rule.Name,
                        Reason = !string.IsNullOrWhiteSpace(rule.OneLineSummary) ? rule.OneLineSummary : rule.Goal,
                        ExpectedImpact = !string.IsNullOrWhiteSpace(rule.MainPlotPush) ? rule.MainPlotPush : rule.CharacterGrowth
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetPlotDecisionsAsync), ex);
            }
            return result;
        }

        public async Task<string> GetMainConflictAsync()
        {
            var path = Path.Combine(GetLayerStoragePath("Plot"), "plot_rules.json");
            if (!File.Exists(path)) return string.Empty;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);
                var main = rules
                    ?.Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Conflict))
                    .OrderByDescending(r => r.GetImportanceWeight())
                    .FirstOrDefault();
                if (main == null) return string.Empty;
                return $"{main.Name}：{TruncateString(main.Conflict, 60)}";
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetMainConflictAsync), ex);
                return string.Empty;
            }
        }

        public async Task<string> GetStorySummaryAsync()
        {
            var outlinePath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Generate", "GlobalSettings", "Outline", "outline_data.json");
            if (File.Exists(outlinePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(outlinePath);
                    var outlines = JsonSerializer.Deserialize<List<Models.Generate.StrategicOutline.OutlineData>>(json, JsonOptions);
                    var text = outlines?.Where(o => o.IsEnabled)
                        .OrderByDescending(o => o.UpdatedAt)
                        .FirstOrDefault()?.OneLineOutline;
                    if (!string.IsNullOrWhiteSpace(text))
                        return TruncateString(text, 100);
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(GetStorySummaryAsync), ex);
                }
            }

            var plotPath = Path.Combine(GetLayerStoragePath("Plot"), "plot_rules.json");
            if (!File.Exists(plotPath)) return string.Empty;

            try
            {
                var json = await File.ReadAllTextAsync(plotPath);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);
                var item = rules?.Where(r => r.IsEnabled)
                    .OrderByDescending(r => r.GetImportanceWeight())
                    .FirstOrDefault();
                if (item == null) return string.Empty;
                var brief = item.OneLineSummary;
                if (string.IsNullOrWhiteSpace(brief)) brief = item.Goal;
                return !string.IsNullOrWhiteSpace(brief) ? $"{item.Name}：{TruncateString(brief, 100)}" : string.Empty;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetStorySummaryAsync), ex);
                return string.Empty;
            }
        }

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static string TruncateString(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= maxLen ? s : s[..maxLen] + "...";
        }

        #endregion

        private async Task<long> GetDataVersionAsync(string layer)
        {
            var storagePath = GetLayerStoragePath(layer);
            if (!Directory.Exists(storagePath))
                return 0;

            var files = Directory.GetFiles(storagePath, "*.json", SearchOption.AllDirectories);
            if (files.Length == 0)
                return 0;

            long maxTicks = 0;
            foreach (var file in files)
            {
                var ticks = new FileInfo(file).LastWriteTimeUtc.Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;
            }

            await Task.CompletedTask;
            return maxTicks;
        }

        private string GetLayerStoragePath(string layer)
        {
            var basePath = StoragePathHelper.GetStorageRoot();
            return layer switch
            {
                "SmartParsing" => Path.Combine(basePath, "Modules", "Design", "SmartParsing"),
                "Templates" => Path.Combine(basePath, "Modules", "Design", "Templates"),
                "Worldview" => Path.Combine(basePath, "Modules", "Design", "GlobalSettings", "WorldRules"),
                "Characters" => Path.Combine(basePath, "Modules", "Design", "Elements", "CharacterRules"),
                "Plot" => Path.Combine(basePath, "Modules", "Design", "Elements", "PlotRules"),
                _ => basePath
            };
        }

        private async Task EnsureLoadedAsync()
        {
            if (_loaded)
                return;

            var path = GetStatusFilePath();
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path);
                    _status = JsonSerializer.Deserialize<LayerCompletionStatus>(json, JsonOptions) 
                              ?? new LayerCompletionStatus();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProgressiveSummary] 加载状态失败: {ex.Message}");
                    _status = new LayerCompletionStatus();
                }
            }
            else
            {
                _status = new LayerCompletionStatus();
            }

            _loaded = true;
        }

        private async Task SaveStatusAsync()
        {
            var path = GetStatusFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_status, JsonOptions);
            var tmp = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProgressiveSummary] 保存状态失败: {ex.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private string GetStatusFilePath()
        {
            return Path.Combine(
                StoragePathHelper.GetProjectConfigPath(),
                StatusFileName);
        }
    }
}
