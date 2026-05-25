using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public sealed class StructuredMemoryExtractor
    {
        private readonly Func<string, string, CancellationToken, Task<string>>? _llmExtractor;
        private readonly JsonSerializerOptions _jsonOptions;

        public class StructuredMemory
        {
            public Dictionary<string, CharacterState> Characters { get; set; } = new();

            public PlotProgress Plot { get; set; } = new();

            public WorldState World { get; set; } = new();

            public TaskState Task { get; set; } = new();

            public DateTime LastUpdated { get; set; } = DateTime.Now;
        }

        public class CharacterState
        {
            public string? Location { get; set; }
            public string? Emotion { get; set; }
            public string? Goal { get; set; }
            public string? Status { get; set; }
            public List<string> Relationships { get; set; } = new();
        }

        public class PlotProgress
        {
            public int CurrentChapter { get; set; }
            public string? CurrentVolume { get; set; }
            public List<string> Milestones { get; set; } = new();
            public List<string> ForeshadowingPending { get; set; } = new();
            public List<string> ForeshadowingResolved { get; set; } = new();
        }

        public class WorldState
        {
            public List<string> Rules { get; set; } = new();
            public List<string> CurrentState { get; set; } = new();
        }

        public class TaskState
        {
            public string? Current { get; set; }
            public List<string> Pending { get; set; } = new();
            public List<string> Completed { get; set; } = new();
        }

        private StructuredMemory _memory = new();

        public StructuredMemoryExtractor(Func<string, string, CancellationToken, Task<string>>? llmExtractor = null)
        {
            _llmExtractor = llmExtractor;
            _jsonOptions = JsonHelper.CnCompact;
        }

        public StructuredMemory GetMemory() => _memory;

        public void ClearMemory()
        {
            _memory = new StructuredMemory();
        }

        public void ExtractFromResponse(string assistantResponse)
        {
            if (string.IsNullOrWhiteSpace(assistantResponse)) return;

            try
            {
                ExtractCharacterLocations(assistantResponse);

                ExtractChapterInfo(assistantResponse);

                ExtractForeshadowing(assistantResponse);

                _memory.LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StructuredMemoryExtractor] 规则抽取失败: {ex.Message}");
            }
        }

        public async Task ExtractWithLLMAsync(string content, CancellationToken ct = default)
        {
            if (_llmExtractor == null || string.IsNullOrWhiteSpace(content)) return;

            try
            {
                var systemPrompt = BuildExtractionSystemPrompt();
                var result = await _llmExtractor(systemPrompt, content, ct);

                if (string.IsNullOrWhiteSpace(result) || 
                    result.StartsWith("[错误]") || 
                    result.StartsWith("[已取消]"))
                {
                    return;
                }

                MergeExtractedData(result);
                _memory.LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StructuredMemoryExtractor] LLM抽取失败: {ex.Message}");
            }
        }

        public string ToTextFormat()
        {
            var sb = new System.Text.StringBuilder();

            if (_memory.Characters.Count > 0)
            {
                sb.AppendLine("<section name=\"角色状态\">");
                foreach (var (name, state) in _memory.Characters)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(state.Location)) parts.Add($"位置:{state.Location}");
                    if (!string.IsNullOrEmpty(state.Emotion)) parts.Add($"情绪:{state.Emotion}");
                    if (!string.IsNullOrEmpty(state.Goal)) parts.Add($"目标:{state.Goal}");
                    if (!string.IsNullOrEmpty(state.Status)) parts.Add($"状态:{state.Status}");
                    if (parts.Count > 0)
                    {
                        sb.AppendLine($"  {name}: {string.Join(", ", parts)}");
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (_memory.Plot.CurrentChapter > 0 || _memory.Plot.Milestones.Count > 0)
            {
                sb.AppendLine("<section name=\"剧情进展\">");
                if (_memory.Plot.CurrentChapter > 0)
                {
                    var vol = string.IsNullOrEmpty(_memory.Plot.CurrentVolume) ? "" : $"{_memory.Plot.CurrentVolume} ";
                    sb.AppendLine($"  当前: {vol}第{_memory.Plot.CurrentChapter}章");
                }
                foreach (var m in _memory.Plot.Milestones)
                {
                    sb.AppendLine($"  里程碑: {m}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (_memory.Plot.ForeshadowingPending.Count > 0 || _memory.Plot.ForeshadowingResolved.Count > 0)
            {
                sb.AppendLine("<section name=\"伏笔清单\">");
                foreach (var f in _memory.Plot.ForeshadowingPending)
                {
                    sb.AppendLine($"  待收: {f}");
                }
                foreach (var f in _memory.Plot.ForeshadowingResolved)
                {
                    sb.AppendLine($"  已收: {f}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (_memory.World.Rules.Count > 0 || _memory.World.CurrentState.Count > 0)
            {
                sb.AppendLine("<section name=\"世界状态\">");
                foreach (var r in _memory.World.Rules)
                {
                    sb.AppendLine($"  规则: {r}");
                }
                foreach (var s in _memory.World.CurrentState)
                {
                    sb.AppendLine($"  状态: {s}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(_memory.Task.Current) || _memory.Task.Pending.Count > 0)
            {
                sb.AppendLine("<section name=\"当前任务\">");
                if (!string.IsNullOrEmpty(_memory.Task.Current))
                {
                    sb.AppendLine($"  进行中: {_memory.Task.Current}");
                }
                foreach (var t in _memory.Task.Pending)
                {
                    sb.AppendLine($"  待办: {t}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        public bool HasMemory()
        {
            return _memory.Characters.Count > 0 ||
                   _memory.Plot.CurrentChapter > 0 ||
                   _memory.Plot.Milestones.Count > 0 ||
                   _memory.Plot.ForeshadowingPending.Count > 0 ||
                   _memory.World.Rules.Count > 0 ||
                   !string.IsNullOrEmpty(_memory.Task.Current);
        }

        #region 规则抽取实现

        private void ExtractCharacterLocations(string text)
        {
            var patterns = new[]
            {
                @"([^\s，。！？""'']{2,6})(?:在|来到|前往|抵达|进入|走进|离开)([^\s，。！？""'']{2,10})",
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(text, pattern);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count >= 3)
                    {
                        var name = m.Groups[1].Value.Trim();
                        var location = m.Groups[2].Value.Trim();

                        if (IsValidCharacterName(name))
                        {
                            if (!_memory.Characters.ContainsKey(name))
                            {
                                _memory.Characters[name] = new CharacterState();
                            }
                            _memory.Characters[name].Location = location;
                        }
                    }
                }
            }
        }

        private void ExtractChapterInfo(string text)
        {
            var chapterMatch = Regex.Match(text, @"第(\d+)章");
            if (chapterMatch.Success && int.TryParse(chapterMatch.Groups[1].Value, out var chapter))
            {
                _memory.Plot.CurrentChapter = chapter;
            }

            var volumeMatch = Regex.Match(text, @"第(\d+)卷");
            if (volumeMatch.Success)
            {
                _memory.Plot.CurrentVolume = $"第{volumeMatch.Groups[1].Value}卷";
            }
        }

        private void ExtractForeshadowing(string text)
        {
            var pendingMatches = Regex.Matches(text, @"(?:埋下|设置|留下)(?:了)?伏笔[：:]\s*([^，。！？\n]{2,20})");
            foreach (Match m in pendingMatches)
            {
                var foreshadow = m.Groups[1].Value.Trim();
                if (!_memory.Plot.ForeshadowingPending.Contains(foreshadow))
                {
                    _memory.Plot.ForeshadowingPending.Add(foreshadow);
                }
            }

            var resolvedMatches = Regex.Matches(text, @"(?:回收|揭示|揭开)(?:了)?伏笔[：:]\s*([^，。！？\n]{2,20})");
            foreach (Match m in resolvedMatches)
            {
                var foreshadow = m.Groups[1].Value.Trim();
                if (!_memory.Plot.ForeshadowingResolved.Contains(foreshadow))
                {
                    _memory.Plot.ForeshadowingResolved.Add(foreshadow);
                }
                _memory.Plot.ForeshadowingPending.Remove(foreshadow);
            }
        }

        private static bool IsValidCharacterName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 6)
                return false;

            var excludes = new[] { "他们", "她们", "我们", "你们", "这里", "那里", "所有", "一些", "其他", "自己" };
            return !Array.Exists(excludes, e => e == name);
        }

        #endregion

        #region LLM抽取实现

        private static string BuildExtractionSystemPrompt()
        {
            return @"<role>Structured Information Extractor for novel writing. Extract key information from given content.</role>

<output_format type=""json"" strict=""true"">
输出JSON格式（只输出JSON，不要解释）：
{
  ""characters"": {
    ""角色名"": { ""location"": ""位置"", ""emotion"": ""情绪"", ""goal"": ""目标"", ""status"": ""状态"" }
  },
  ""plot"": {
    ""chapter"": 章节数字,
    ""milestones"": [""里程碑1""],
    ""foreshadowing_pending"": [""待收伏笔""],
    ""foreshadowing_resolved"": [""已收伏笔""]
  },
  ""world"": {
    ""rules"": [""世界规则""],
    ""state"": [""当前状态""]
  },
  ""task"": {
    ""current"": ""当前任务"",
    ""pending"": [""待办任务""]
  }
}

只抽取明确提到的信息，没有的字段留空或空数组。
</output_format>";
        }

        private void MergeExtractedData(string jsonResult)
        {
            try
            {
                var json = jsonResult.Trim();
                if (json.StartsWith("```"))
                {
                    var lines = json.Split('\n');
                    json = string.Join("\n", lines[1..^1]);
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("characters", out var chars))
                {
                    foreach (var prop in chars.EnumerateObject())
                    {
                        var name = prop.Name;
                        if (!_memory.Characters.ContainsKey(name))
                        {
                            _memory.Characters[name] = new CharacterState();
                        }

                        var state = _memory.Characters[name];
                        if (prop.Value.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String)
                            state.Location = loc.GetString();
                        if (prop.Value.TryGetProperty("emotion", out var emo) && emo.ValueKind == JsonValueKind.String)
                            state.Emotion = emo.GetString();
                        if (prop.Value.TryGetProperty("goal", out var goal) && goal.ValueKind == JsonValueKind.String)
                            state.Goal = goal.GetString();
                        if (prop.Value.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                            state.Status = status.GetString();
                    }
                }

                if (root.TryGetProperty("plot", out var plot))
                {
                    if (plot.TryGetProperty("chapter", out var ch) && ch.ValueKind == JsonValueKind.Number)
                        _memory.Plot.CurrentChapter = ch.GetInt32();

                    MergeStringArray(plot, "milestones", _memory.Plot.Milestones);
                    MergeStringArray(plot, "foreshadowing_pending", _memory.Plot.ForeshadowingPending);
                    MergeStringArray(plot, "foreshadowing_resolved", _memory.Plot.ForeshadowingResolved);
                }

                if (root.TryGetProperty("world", out var world))
                {
                    MergeStringArray(world, "rules", _memory.World.Rules);
                    MergeStringArray(world, "state", _memory.World.CurrentState);
                }

                if (root.TryGetProperty("task", out var task))
                {
                    if (task.TryGetProperty("current", out var curr) && curr.ValueKind == JsonValueKind.String)
                        _memory.Task.Current = curr.GetString();

                    MergeStringArray(task, "pending", _memory.Task.Pending);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StructuredMemoryExtractor] 合并JSON失败: {ex.Message}");
            }
        }

        private static void MergeStringArray(JsonElement parent, string propertyName, List<string> target)
        {
            if (!parent.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val) && !target.Contains(val))
                    {
                        target.Add(val);
                    }
                }
            }
        }

        #endregion

        #region 持久化

        public void SaveToFile(string filePath)
        {
            if (!HasMemory()) return;
            try
            {
                var json = JsonSerializer.Serialize(_memory, _jsonOptions);
                var dir = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                var tmp = filePath + ".tmp";
                System.IO.File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                System.IO.File.Move(tmp, filePath, overwrite: true);
                TM.App.Log($"[StructuredMemoryExtractor] 记忆已保存: {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StructuredMemoryExtractor] 记忆保存失败（非致命）: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task SaveToFileAsync(string filePath)
        {
            if (!HasMemory()) return;
            var json = JsonSerializer.Serialize(_memory, _jsonOptions);
            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    var tmp = filePath + ".tmp";
                    System.IO.File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                    System.IO.File.Move(tmp, filePath, overwrite: true);
                    TM.App.Log($"[StructuredMemoryExtractor] 记忆已保存: {filePath}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[StructuredMemoryExtractor] 记忆保存失败（非致命）: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }

        public void LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return;
            try
            {
                var json = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<StructuredMemory>(json, _jsonOptions);
                if (loaded != null)
                {
                    _memory = loaded;
                    TM.App.Log($"[StructuredMemoryExtractor] 记忆已加载: {filePath}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StructuredMemoryExtractor] 记忆加载失败（非致命）: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task LoadFromFileAsync(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return;
            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var json = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                    return JsonSerializer.Deserialize<StructuredMemory>(json, _jsonOptions);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[StructuredMemoryExtractor] 记忆加载失败（非致命）: {ex.Message}");
                    return null;
                }
            }).ConfigureAwait(false);
            if (result != null)
            {
                _memory = result;
                TM.App.Log($"[StructuredMemoryExtractor] 记忆已加载: {filePath}");
            }
        }

        #endregion
    }
}
