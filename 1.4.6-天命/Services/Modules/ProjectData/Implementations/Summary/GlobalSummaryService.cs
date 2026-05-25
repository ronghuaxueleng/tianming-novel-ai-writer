using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GlobalSummaryService : IGlobalSummaryService
    {
        private const string CacheFileName = "global_summary.json";

        private GlobalSummary? _cachedSummary;
        private DateTime _cacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (GlobalSummary Summary, DateTime Time)> _scopedCache = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public GlobalSummaryService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public async Task<GlobalSummary> GetGlobalSummaryAsync()
        {
            if (_cachedSummary != null && DateTime.UtcNow - _cacheTime < _cacheExpiry)
            {
                return _cachedSummary;
            }

            if (await CacheExistsAsync())
            {
                try
                {
                    var summary = await LoadFromCacheAsync();
                    if (summary != null)
                    {
                        _cachedSummary = summary;
                        _cacheTime = DateTime.UtcNow;
                        return summary;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 加载缓存失败: {ex.Message}");
                }
            }

            TM.App.Log("[GlobalSummaryService] cache miss");
            var computed = await ComputeRealTimeAsync();
            _cachedSummary = computed;
            _cacheTime = DateTime.UtcNow;
            return computed;
        }

        public async Task<GlobalSummary> GetGlobalSummaryAsync(string? sourceBookId)
        {
            if (string.IsNullOrEmpty(sourceBookId))
                return await GetGlobalSummaryAsync();

            if (_scopedCache.TryGetValue(sourceBookId, out var cached)
                && DateTime.UtcNow - cached.Time < _cacheExpiry)
            {
                return cached.Summary;
            }

            var computed = await ComputeRealTimeAsync(sourceBookId);
            _scopedCache[sourceBookId] = (computed, DateTime.UtcNow);
            return computed;
        }

        public async Task<GlobalSummary> ComputeRealTimeAsync()
        {
            var summary = new GlobalSummary();

            try
            {
                await ExtractMainCharactersAsync(summary);

                await ExtractCoreRulesAsync(summary);

                await ExtractCoreFactionsAsync(summary);

                await ExtractStorySummaryAsync(summary);

                await ExtractMainConflictAsync(summary);

                await ExtractUsedElementsAsync(summary);

                await BuildDecisionChainAsync(summary);

                await ExtractProgressInfoAsync(summary);

                summary.IsEmpty = summary.MainCharacters.Count == 0 
                    && summary.CoreRules.Count == 0 
                    && summary.CoreFactions.Count == 0
                    && string.IsNullOrEmpty(summary.StorySummary);

                TM.App.Log("[GlobalSummaryService] 实时计算完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 实时计算失败: {ex.Message}");
            }

            return summary;
        }

        private async Task<GlobalSummary> ComputeRealTimeAsync(string sourceBookId)
        {
            var summary = new GlobalSummary();

            try
            {
                await ExtractMainCharactersAsync(summary, sourceBookId);
                await ExtractCoreRulesAsync(summary, sourceBookId);
                await ExtractCoreFactionsAsync(summary, sourceBookId);
                await ExtractStorySummaryAsync(summary, sourceBookId);
                await ExtractMainConflictAsync(summary, sourceBookId);
                await ExtractUsedElementsAsync(summary, sourceBookId);
                await BuildDecisionChainAsync(summary, sourceBookId);
                await ExtractProgressInfoAsync(summary, sourceBookId);

                summary.IsEmpty = summary.MainCharacters.Count == 0
                    && summary.CoreRules.Count == 0
                    && summary.CoreFactions.Count == 0
                    && string.IsNullOrEmpty(summary.StorySummary);

                TM.App.Log($"[GlobalSummaryService] 实时计算完成（Scope={sourceBookId}）");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 实时计算失败（Scope={sourceBookId}）: {ex.Message}");
            }

            return summary;
        }

        public async Task<bool> CacheExistsAsync()
        {
            await Task.CompletedTask;
            var path = GetCachePath();
            return File.Exists(path);
        }

        public void InvalidateCache()
        {
            _cachedSummary = null;
            _cacheTime = DateTime.MinValue;
            _scopedCache.Clear();
            TM.App.Log("[GlobalSummaryService] 缓存已清除");
        }

        public async Task SaveToCacheAsync(GlobalSummary summary)
        {
            try
            {
                var path = GetCachePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(summary, JsonOptions);
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, path, overwrite: true);

                _cachedSummary = summary;
                _cacheTime = DateTime.UtcNow;

                TM.App.Log("[GlobalSummaryService] 缓存已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 保存缓存失败: {ex.Message}");
            }
        }

        private async Task ExtractCoreFactionsAsync(GlobalSummary summary)
        {
            var factionsPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "FactionRules", "faction_rules.json");

            if (!File.Exists(factionsPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(factionsPath);
                var factions = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                if (factions == null) return;

                summary.CoreFactions = factions
                    .Where(f =>
                    {
                        if (!f.TryGetValue("IsEnabled", out var enabledEl))
                            return true;
                        return enabledEl.ValueKind != JsonValueKind.False;
                    })
                    .Select(f =>
                    {
                        var name = GetJsonString(f, "Name");
                        var type = GetJsonString(f, "FactionType");
                        var goal = GetJsonString(f, "Goal");
                        var leader = GetJsonString(f, "Leader");
                        var brief = string.IsNullOrWhiteSpace(type) ? name : $"{name}({type})";
                        var deep = string.Join("；", new[] { goal, leader }.Where(s => !string.IsNullOrWhiteSpace(s)));

                        return new IndexItem
                        {
                            Id = GetJsonString(f, "Id"),
                            Name = name,
                            Type = type,
                            BriefSummary = brief,
                            DeepSummary = TruncateString(deep, 80)
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取势力失败: {ex.Message}");
            }
        }

        private async Task ExtractCoreFactionsAsync(GlobalSummary summary, string sourceBookId)
        {
            var factionsPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "FactionRules", "faction_rules.json");

            if (!File.Exists(factionsPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(factionsPath);
                var factions = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                if (factions == null) return;

                summary.CoreFactions = factions
                    .Where(f => GetJsonString(f, "SourceBookId") == sourceBookId)
                    .Where(f =>
                    {
                        if (!f.TryGetValue("IsEnabled", out var enabledEl))
                            return true;
                        return enabledEl.ValueKind != JsonValueKind.False;
                    })
                    .Select(f =>
                    {
                        var name = GetJsonString(f, "Name");
                        var type = GetJsonString(f, "FactionType");
                        var goal = GetJsonString(f, "Goal");
                        var leader = GetJsonString(f, "Leader");
                        var brief = string.IsNullOrWhiteSpace(type) ? name : $"{name}({type})";
                        var deep = string.Join("；", new[] { goal, leader }.Where(s => !string.IsNullOrWhiteSpace(s)));

                        return new IndexItem
                        {
                            Id = GetJsonString(f, "Id"),
                            Name = name,
                            Type = type,
                            BriefSummary = brief,
                            DeepSummary = TruncateString(deep, 80)
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取势力失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        private async Task ExtractStorySummaryAsync(GlobalSummary summary, string sourceBookId)
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
                    var outline = outlines
                        ?.Where(o => o.IsEnabled && string.Equals(o.SourceBookId, sourceBookId, StringComparison.Ordinal))
                        .OrderByDescending(o => o.UpdatedAt)
                        .FirstOrDefault();

                    var text = outline?.OneLineOutline;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        summary.StorySummary = TruncateString(text, 100);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 提取主题失败（Scope={sourceBookId}）: {ex.Message}");
                }
            }

            var plotRulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(plotRulesPath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(plotRulesPath);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);
                var item = rules
                    ?.Where(p => p.IsEnabled && string.Equals(p.SourceBookId, sourceBookId, StringComparison.Ordinal))
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (item == null) return;

                var brief = item.OneLineSummary;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Goal;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Conflict;

                if (!string.IsNullOrWhiteSpace(brief))
                {
                    summary.StorySummary = $"{item.Name}：{TruncateString(brief, 100)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主题失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        public async Task<GlobalSummary> BuildAndSaveForPackageAsync()
        {
            TM.App.Log("[GlobalSummaryService] 开始打包生成 GlobalSummary...");

            var summary = await ComputeRealTimeAsync();

            await SaveToCacheAsync(summary);

            TM.App.Log("[GlobalSummaryService] 打包生成完成");
            return summary;
        }

        public async Task SaveTrackingStatusAsync(TrackingStatus status)
        {
            try
            {
                var path = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "tracking_status.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(status, JsonOptions);
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, path, overwrite: true);

                TM.App.Log("[GlobalSummaryService] TrackingStatus 已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 保存 TrackingStatus 失败: {ex.Message}");
            }
        }

        public async Task RollbackCacheAsync()
        {
            try
            {
                var backupPath = GetCachePath() + ".backup";
                var cachePath = GetCachePath();

                if (File.Exists(backupPath))
                {
                    if (File.Exists(cachePath))
                        File.Delete(cachePath);
                    File.Move(backupPath, cachePath);
                    TM.App.Log("[GlobalSummaryService] 缓存已回滚");
                }

                InvalidateCache();
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 回滚失败: {ex.Message}");
            }
        }

        public async Task BackupCacheAsync()
        {
            try
            {
                var cachePath = GetCachePath();
                var backupPath = cachePath + ".backup";

                if (File.Exists(cachePath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Copy(cachePath, backupPath);
                    TM.App.Log("[GlobalSummaryService] 缓存备份已创建");
                }
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 备份失败: {ex.Message}");
            }
        }

        public async Task RefreshSummaryAsync()
        {
            TM.App.Log("[GlobalSummaryService] 开始刷新摘要...");
            InvalidateCache();
            await GetGlobalSummaryAsync();
            TM.App.Log("[GlobalSummaryService] 摘要刷新完成");
        }

        private async Task<GlobalSummary?> LoadFromCacheAsync()
        {
            var path = GetCachePath();
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<GlobalSummary>(json, JsonOptions);
        }

        private string GetCachePath()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), CacheFileName);
        }

        #region 提取方法

        private async Task ExtractMainCharactersAsync(GlobalSummary summary)
        {
            var path = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");

            if (!File.Exists(path)) return;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var items = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions);
                if (items == null) return;

                var idToName = items
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

                summary.MainCharacters = items
                    .Where(c => c.IsEnabled)
                    .OrderByDescending(c => c.GetImportanceWeight())
                    .ThenByDescending(c => c.UpdatedAt)
                    .Take(5)
                    .Select(p => BuildCharacterDeepSummaryModel(p, idToName))
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取角色失败: {ex.Message}");
            }
        }

        private async Task ExtractMainCharactersAsync(GlobalSummary summary, string sourceBookId)
        {
            var path = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");

            if (!File.Exists(path)) return;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var items = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions);
                if (items == null) return;

                var idToName = items
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

                summary.MainCharacters = items
                    .Where(c => c.IsEnabled && string.Equals(c.SourceBookId, sourceBookId, StringComparison.Ordinal))
                    .OrderByDescending(c => c.GetImportanceWeight())
                    .ThenByDescending(c => c.UpdatedAt)
                    .Take(5)
                    .Select(p => BuildCharacterDeepSummaryModel(p, idToName))
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取角色失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        private CharacterDeepSummary BuildCharacterDeepSummaryModel(CharacterRulesData profile, Dictionary<string, string> idToName)
        {
            var coreTraits = string.Join("；", new[]
            {
                profile.Identity,
                profile.FlawBelief,
                profile.Race,
                profile.Appearance
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var abilityOrigin = string.Join("；", new[]
            {
                profile.SpecialAbilities,
                profile.CombatSkills,
                profile.NonCombatSkills
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var constraint = string.Join("；", new[]
            {
                profile.Need,
                profile.GrowthPath
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new CharacterDeepSummary
            {
                Id = profile.Id,
                Name = profile.Name,
                Role = profile.CharacterType,
                CoreTraits = TruncateString(coreTraits, 40),
                AbilityOrigin = TruncateString(abilityOrigin, 40),
                KeyConstraint = TruncateString(constraint, 40),
                KeyRelationships = ExtractKeyRelationships(profile, idToName)
            };
        }

        private string ExtractKeyRelationships(CharacterRulesData profile, Dictionary<string, string> idToName)
        {
            if (string.IsNullOrWhiteSpace(profile.TargetCharacterName))
                return string.Empty;

            var targetDisplay = idToName.TryGetValue(profile.TargetCharacterName, out var name)
                ? name
                : profile.TargetCharacterName;

            var parts = new List<string> { targetDisplay };
            if (!string.IsNullOrWhiteSpace(profile.RelationshipType))
                parts.Add(profile.RelationshipType);
            if (!string.IsNullOrWhiteSpace(profile.EmotionDynamic))
                parts.Add(profile.EmotionDynamic);
            return TruncateString(string.Join("：", parts), 50);
        }

        private async Task ExtractCoreRulesAsync(GlobalSummary summary)
        {
            var rulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "GlobalSettings", "WorldRules", "world_rules.json");

            if (!File.Exists(rulesPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(rulesPath);
                var rules = JsonSerializer.Deserialize<List<Models.Design.Worldview.WorldRulesData>>(json, JsonOptions);

                if (rules == null) return;

                summary.CoreRules = rules
                    .Where(r => r.IsEnabled)
                    .OrderByDescending(r => r.GetImportanceWeight())
                    .Select(r => new IndexItem
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Type = r.Category,
                        BriefSummary = $"{r.Name}({r.Category})",
                        DeepSummary = TruncateString(r.GetCoreSummary(), 80)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取规则失败: {ex.Message}");
            }
        }

        private async Task ExtractCoreRulesAsync(GlobalSummary summary, string sourceBookId)
        {
            var rulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "GlobalSettings", "WorldRules", "world_rules.json");

            if (!File.Exists(rulesPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(rulesPath);
                var rules = JsonSerializer.Deserialize<List<Models.Design.Worldview.WorldRulesData>>(json, JsonOptions);

                if (rules == null) return;

                summary.CoreRules = rules
                    .Where(r => r.IsEnabled && r.SourceBookId == sourceBookId)
                    .OrderByDescending(r => r.GetImportanceWeight())
                    .Select(r => new IndexItem
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Type = r.Category,
                        BriefSummary = $"{r.Name}({r.Category})",
                        DeepSummary = TruncateString(r.GetCoreSummary(), 80)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取规则失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        private async Task ExtractStorySummaryAsync(GlobalSummary summary)
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
                    var outline = outlines
                        ?.Where(o => o.IsEnabled)
                        .OrderByDescending(o => o.UpdatedAt)
                        .FirstOrDefault();

                    var text = outline?.OneLineOutline;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        summary.StorySummary = TruncateString(text, 100);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 提取主题失败: {ex.Message}");
                }
            }

            var plotRulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(plotRulesPath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(plotRulesPath);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);
                var item = rules
                    ?.Where(p => p.IsEnabled)
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (item == null) return;

                var brief = item.OneLineSummary;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Goal;
                if (string.IsNullOrWhiteSpace(brief))
                    brief = item.Conflict;

                if (!string.IsNullOrWhiteSpace(brief))
                {
                    summary.StorySummary = $"{item.Name}：{TruncateString(brief, 100)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主题失败: {ex.Message}");
            }
        }

        private async Task ExtractMainConflictAsync(GlobalSummary summary)
        {
            var plotRulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(plotRulesPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(plotRulesPath);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);

                var conflict = rules
                    ?.Where(p => p.IsEnabled)
                    .Where(p => !string.IsNullOrWhiteSpace(p.Conflict))
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (conflict != null)
                {
                    summary.MainConflict = $"{conflict.Name}：{TruncateString(conflict.Conflict, 80)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主线冲突失败: {ex.Message}");
            }
        }

        private async Task ExtractMainConflictAsync(GlobalSummary summary, string sourceBookId)
        {
            var plotRulesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(plotRulesPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(plotRulesPath);
                var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions);

                var conflict = rules
                    ?.Where(p => p.IsEnabled && string.Equals(p.SourceBookId, sourceBookId, StringComparison.Ordinal))
                    .Where(p => !string.IsNullOrWhiteSpace(p.Conflict))
                    .OrderByDescending(p => p.GetImportanceWeight())
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();

                if (conflict != null)
                {
                    summary.MainConflict = $"{conflict.Name}：{TruncateString(conflict.Conflict, 80)}";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取主线冲突失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        private async Task ExtractUsedElementsAsync(GlobalSummary summary)
        {
            try
            {
                var plotRulesPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                if (File.Exists(plotRulesPath))
                {
                    var json = await File.ReadAllTextAsync(plotRulesPath);
                    var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions) ?? new();
                    summary.UsedElements.UsedPlotPatterns = rules
                        .Where(p => p.IsEnabled)
                        .Select(p => p.EventType)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .Take(20)
                        .ToList();
                }

                var characterRulesPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");
                if (File.Exists(characterRulesPath))
                {
                    var json = await File.ReadAllTextAsync(characterRulesPath);
                    var chars = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions) ?? new();
                    summary.UsedElements.UsedAbilities = chars
                        .Where(c => c.IsEnabled)
                        .Select(c => c.SpecialAbilities)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => TruncateString(s, 20))
                        .Distinct()
                        .Take(20)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取已使用元素失败: {ex.Message}");
            }
        }

        private async Task ExtractUsedElementsAsync(GlobalSummary summary, string sourceBookId)
        {
            try
            {
                var plotRulesPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");
                if (File.Exists(plotRulesPath))
                {
                    var json = await File.ReadAllTextAsync(plotRulesPath);
                    var rules = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions) ?? new();
                    summary.UsedElements.UsedPlotPatterns = rules
                        .Where(p => p.IsEnabled && string.Equals(p.SourceBookId, sourceBookId, StringComparison.Ordinal))
                        .Select(p => p.EventType)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .Take(20)
                        .ToList();
                }

                var characterRulesPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "CharacterRules", "character_rules.json");
                if (File.Exists(characterRulesPath))
                {
                    var json = await File.ReadAllTextAsync(characterRulesPath);
                    var chars = JsonSerializer.Deserialize<List<CharacterRulesData>>(json, JsonOptions) ?? new();
                    summary.UsedElements.UsedAbilities = chars
                        .Where(c => c.IsEnabled && string.Equals(c.SourceBookId, sourceBookId, StringComparison.Ordinal))
                        .Select(c => c.SpecialAbilities)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => TruncateString(s, 20))
                        .Distinct()
                        .Take(20)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取已使用元素失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        private async Task BuildDecisionChainAsync(GlobalSummary summary)
        {
            var decisions = summary.Decisions;

            var materialsPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Templates", "CreativeMaterials", "creative_materials.json");

            if (File.Exists(materialsPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(materialsPath);
                    var materials = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                    if (materials != null && materials.Count > 0)
                    {
                        var main = materials.FirstOrDefault();
                        if (main != null)
                        {
                            decisions.CoreInspiration = TruncateString(
                                GetJsonString(main, "CoreTheme") ?? GetJsonString(main, "OverallIdea"), 
                                100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 构建决策链-素材失败: {ex.Message}");
                }
            }

            if (summary.CoreRules.Count > 0)
            {
                decisions.CoreWorldRules = summary.CoreRules
                    .Select(r => new RuleDecision
                    {
                        RuleId = r.Id,
                        RuleName = r.Name,
                        Reason = r.DeepSummary,
                        Impact = "影响全局设定"
                    })
                    .ToList();
            }

            if (summary.CoreFactions.Count > 0)
            {
                decisions.CoreWorldRules.AddRange(summary.CoreFactions
                    .Select(f => new RuleDecision
                    {
                        RuleId = f.Id,
                        RuleName = f.Name,
                        Reason = f.DeepSummary,
                        Impact = "影响势力格局"
                    }));
            }

            if (summary.MainCharacters.Count > 0)
            {
                decisions.CharacterDecisions = summary.MainCharacters
                    .Where(c => c.Type == "主角" || c.Type == "主要角色")
                    .Select(c => new CharacterDecision
                    {
                        CharacterId = c.Id,
                        CharacterName = c.Name,
                        DesignReason = c.KeyConstraint,
                        RelatedRules = c.AbilityOrigin
                    })
                    .ToList();
            }
        }

        private async Task BuildDecisionChainAsync(GlobalSummary summary, string sourceBookId)
        {
            var decisions = summary.Decisions;

            var templatesPath = Path.Combine(
                StoragePathHelper.GetStorageRoot(),
                "Modules", "Design", "Templates", "CreativeMaterials", "creative_materials.json");

            if (File.Exists(templatesPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(templatesPath);
                    var materials = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                    if (materials != null && materials.Count > 0)
                    {
                        var main = materials.FirstOrDefault(m => GetJsonString(m, "SourceBookId") == sourceBookId);
                        if (main != null)
                        {
                            decisions.CoreInspiration = TruncateString(
                                GetJsonString(main, "OverallIdea")
                                ?? GetJsonString(main, "CoreTheme"),
                                100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GlobalSummaryService] 构建决策链-素材失败（Scope={sourceBookId}）: {ex.Message}");
                }
            }

            if (summary.CoreRules.Count > 0)
            {
                decisions.CoreWorldRules = summary.CoreRules
                    .Select(r => new RuleDecision
                    {
                        RuleId = r.Id,
                        RuleName = r.Name,
                        Reason = r.DeepSummary,
                        Impact = "影响全局设定"
                    })
                    .ToList();
            }

            if (summary.MainCharacters.Count > 0)
            {
                decisions.CharacterDecisions = summary.MainCharacters
                    .Where(c => c.Type == "主角" || c.Type == "主要角色")
                    .Select(c => new CharacterDecision
                    {
                        CharacterId = c.Id,
                        CharacterName = c.Name,
                        DesignReason = c.DeepSummary,
                        RelatedRules = "关联核心规则"
                    })
                    .ToList();
            }

            await Task.CompletedTask;
        }

        private async Task ExtractProgressInfoAsync(GlobalSummary summary)
        {
            try
            {
                var statusPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(),
                    "layer_completion_status.json");

                if (File.Exists(statusPath))
                {
                    var json = await File.ReadAllTextAsync(statusPath);
                    var status = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);

                    if (status != null && status.TryGetValue("Layers", out var layersElement))
                    {
                        var layers = layersElement.Deserialize<Dictionary<string, JsonElement>>(JsonOptions);
                        if (layers != null)
                        {
                            foreach (var (layer, layerStatus) in layers)
                            {
                                if (layerStatus.TryGetProperty("IsCompleted", out var isCompleted) 
                                    && isCompleted.GetBoolean())
                                {
                                    summary.CompletedLayers.Add(layer);
                                }
                            }
                        }
                    }
                }

                var blueprintPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Generate", "Elements", "Blueprint", "blueprint_data.json");

                if (File.Exists(blueprintPath))
                {
                    var json = await File.ReadAllTextAsync(blueprintPath);
                    var blueprints = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                    if (blueprints != null)
                    {
                        summary.Progress.TotalChapters = blueprints.Select(b => GetJsonString(b, "ChapterId")).Distinct().Count();
                        summary.Progress.CompletedChapters = blueprints
                            .Count(b => !string.IsNullOrEmpty(GetJsonString(b, "OneLineStructure")));
                    }
                }

                if (summary.CompletedLayers.Count == 0)
                {
                    summary.Progress.CurrentPhase = "Design";
                }
                else if (summary.CompletedLayers.Contains("Plot"))
                {
                    summary.Progress.CurrentPhase = summary.Progress.CompletedChapters > 0 
                        ? "Generate" 
                        : "Generate";
                }
                else
                {
                    summary.Progress.CurrentPhase = "Design";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取进度信息失败: {ex.Message}");
            }
        }

        private async Task ExtractProgressInfoAsync(GlobalSummary summary, string sourceBookId)
        {
            try
            {
                var statusPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(),
                    "layer_completion_status.json");

                if (File.Exists(statusPath))
                {
                    var json = await File.ReadAllTextAsync(statusPath);
                    var status = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);

                    if (status != null && status.TryGetValue("Layers", out var layersElement))
                    {
                        var layers = layersElement.Deserialize<Dictionary<string, JsonElement>>(JsonOptions);
                        if (layers != null)
                        {
                            foreach (var (layer, layerStatus) in layers)
                            {
                                if (layerStatus.TryGetProperty("IsCompleted", out var isCompleted)
                                    && isCompleted.GetBoolean())
                                {
                                    summary.CompletedLayers.Add(layer);
                                }
                            }
                        }
                    }
                }

                var blueprintPath2 = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Generate", "Elements", "Blueprint", "blueprint_data.json");

                if (File.Exists(blueprintPath2))
                {
                    var json = await File.ReadAllTextAsync(blueprintPath2);
                    var blueprints = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                    if (blueprints != null)
                    {
                        var scoped = blueprints.Where(b => GetJsonString(b, "SourceBookId") == sourceBookId).ToList();
                        summary.Progress.TotalChapters = scoped.Select(b => GetJsonString(b, "ChapterId")).Distinct().Count();
                        summary.Progress.CompletedChapters = scoped.Count(b => !string.IsNullOrEmpty(GetJsonString(b, "OneLineStructure")));
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GlobalSummaryService] 提取进度失败（Scope={sourceBookId}）: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > maxLength ? text[..maxLength] + "..." : text;
        }

        #endregion
    }
}
