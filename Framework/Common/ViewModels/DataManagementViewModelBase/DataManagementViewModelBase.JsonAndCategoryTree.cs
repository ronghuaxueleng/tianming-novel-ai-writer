using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService>
    {

        private async System.Threading.Tasks.Task ExecuteAIGenerateWithConfigAsync(AIGenerationConfig config)
        {
            var vmName = GetType().Name;
            TM.App.Log($"[{vmName}] 开始AI生成（配置化）");

            var categoryForSingle = GetCurrentCategoryValue();

            if (!CheckConsistencyBeforeGenerate())
            {
                TM.App.Log($"[{vmName}] 一致性检查未通过，取消生成");
                return;
            }

            try
            {
                _singleCancellationTokenSource?.Dispose();
                _singleCancellationTokenSource = new System.Threading.CancellationTokenSource();
                var cancellationToken = _singleCancellationTokenSource.Token;

                await PrepareReferenceDataForAIGenerationAsync(config, isBatch: false, categoryName: categoryForSingle, cancellationToken);

                var repo = GetPromptRepository();
                if (repo == null)
                {
                    GlobalToast.Warning("配置错误", "请在子类重写GetPromptRepository()提供提示词仓库");
                    return;
                }
                var templates = repo.GetTemplatesByCategory(config.Category);
                var enabled = templates
                    .Where(t => t.IsEnabled)
                    .Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .ToList();
                var candidates = enabled.Count > 0
                    ? enabled
                    : templates.Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt)).ToList();
                var template = candidates
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .ThenByDescending(t => t.IsEnabled)
                    .FirstOrDefault();
                if (template == null || string.IsNullOrWhiteSpace(template.SystemPrompt))
                {
                    GlobalToast.Warning("提示", $"请先在「提示词管理」中配置「{config.Category}」分类的模板");
                    return;
                }

                var prompt = template.SystemPrompt;
                prompt = Helpers.AI.SystemPromptTrimHelper.Trim(prompt, config.ActiveModuleHint);
                foreach (var (varName, getValue) in config.InputVariables)
                {
                    prompt = prompt.Replace($"{{{varName}}}", getValue?.Invoke() ?? string.Empty);
                }

                Func<System.Threading.Tasks.Task<string>>? initialContextProvider = null;
                string singleContextText = string.Empty;
                if (config.ContextProvider != null)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        singleContextText = await config.ContextProvider();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{vmName}] 单次生成：获取上下文失败 - {ex.Message}");
                    }
                    var _singleCtxSnapshot = singleContextText;
                    initialContextProvider = () => System.Threading.Tasks.Task.FromResult(_singleCtxSnapshot);
                }
                prompt = prompt.Replace("{上下文数据}", string.Empty);

                if (CoherenceEnabledCategories.Contains(config.Category))
                {
                    prompt += BuildCoherencePromptAppendix();
                }

                var range = GetNextGenerationRange(GetCurrentCategoryValue() ?? string.Empty, 1);
                if (range.HasValue)
                {
                    _currentBatchRange = range;
                    prompt = ApplyGenerationRangeToPrompt(prompt, range.Value);
                }

                var singleJsonContract = BuildSingleJsonContract(config);
                if (!string.IsNullOrWhiteSpace(singleJsonContract))
                {
                    prompt += singleJsonContract;
                }

                TM.App.Log($"[{vmName}] 构建提示词完成，长度: {prompt.Length}");

                GlobalToast.Info("AI生成中", config.ProgressMessage);
                var progress = new Progress<string>(msg =>
                    BatchProgressText = $"正在生成... | {msg}");
                string result;

                var ai = _aiService;
                AIService.GenerationResult aiResult;
                var attempts = 0;
                while (true)
                {
                    attempts++;
                    aiResult = await ai.GenerateInBusinessSessionAsync(vmName, initialContextProvider, prompt, progress, cancellationToken);
                    if (!aiResult.Success)
                    {
                        TM.App.Log($"[{vmName}] AI生成失败: {aiResult.ErrorMessage}");
                        GlobalToast.Error("生成失败", $"生成失败：{TrimForToast(aiResult.ErrorMessage)}");
                        return;
                    }
                    result = aiResult.Content ?? string.Empty;

                    var entity = ParseSingleJsonEntity(result);
                    if (entity.Count > 0)
                    {
                        if (IsSingleMissingFieldsTooHigh(entity, config) && attempts < 2)
                        {
                            prompt += "\n<retry_warning>你上一次输出字段缺失过多。请严格按字段白名单输出完整JSON对象，不要遗漏字段。</retry_warning>";
                            continue;
                        }

                        if (range.HasValue)
                        {
                            var list = new List<Dictionary<string, object>> { entity };
                            list = ValidateAndNormalizeGeneratedEntities(range.Value, list);
                            entity = list[0];
                        }

                        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in entity)
                        {
                            extracted[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                        }

                        FillFieldsFromExtracted(config, extracted);

                        if (extracted.TryGetValue("Name", out var singleName) && !string.IsNullOrWhiteSpace(singleName))
                        {
                            _batchGeneratedNames.Add(singleName);
                        }

                        break;
                    }

                    if (attempts >= 2)
                    {
                        GlobalToast.Warning("生成失败", "未能从AI返回中解析到有效JSON对象，请检查提示词模板与输出协议");
                        return;
                    }

                    prompt += "\n<retry_warning>你上一次输出不满足JSON协议（可能是格式错误/夹杂解释）。本次只输出JSON对象，不要任何解释文字。</retry_warning>";
                }

                if (CoherenceEnabledCategories.Contains(config.Category))
                {
                    EvaluateCoherenceC0(result);

                    if (HasCoherenceHardConflict)
                    {
                        GlobalToast.Warning("检测到硬冲突", CoherenceConflictMessage);
                    }
                }

                if (string.IsNullOrWhiteSpace(result))
                {
                    GlobalToast.Warning("生成失败", "AI未返回任何内容");
                    return;
                }

                TM.App.Log($"[{vmName}] AI生成完成（JSON-only）");

                RecordDependencyVersions();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{vmName}] AI生成失败: {ex.Message}");
                GlobalToast.Error("生成失败", $"生成失败：{TrimForToast(ex.Message)}");
            }
            finally
            {
                BatchProgressText = string.Empty;
                _singleCancellationTokenSource?.Dispose();
                _singleCancellationTokenSource = null;
            }
        }

        private void FillFieldsFromExtracted(AIGenerationConfig config, Dictionary<string, string> extracted)
        {
            int updated = 0;
            var missingFields = new List<string>();

            foreach (var (fieldName, setValue) in config.OutputFields)
            {
                string? currentValue = null;
                if (config.OutputFieldGetters?.TryGetValue(fieldName, out var getter) == true)
                {
                    currentValue = getter?.Invoke();
                }

                if (extracted.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    setValue?.Invoke(value);
                    updated++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(currentValue))
                    {
                        setValue?.Invoke("[待补充]");
                        missingFields.Add(fieldName);
                    }
                }
            }

            if (updated == config.OutputFields.Count)
            {
                GlobalToast.Success("生成完成", $"已更新全部 {updated} 个字段");
            }
            else if (updated > 0)
            {
                GlobalToast.Info("部分生成", $"已更新 {updated} 个字段，{missingFields.Count} 个需手动补充");
            }
            else
            {
                GlobalToast.Warning("生成失败", "未能从AI返回中提取任何字段，请检查提示词配置");
            }
        }

        private string BuildSingleJsonContract(AIGenerationConfig config)
        {
            if (config.OutputFields.Count == 0)
            {
                return string.Empty;
            }

            var fields = config.OutputFields.Keys.ToList();
            if (!fields.Contains("Name", StringComparer.OrdinalIgnoreCase))
            {
                fields.Insert(0, "Name");
            }

            var sb = new StringBuilder();
            sb.AppendLine();

            if (_batchGeneratedIndex.Count > 0)
            {
                sb.AppendLine("<generated_index note=\"已生成条目核心属性摘要，保持内容分布一致性，勿重复\">");
                sb.AppendLine(string.Join("\n", _batchGeneratedIndex));
                sb.AppendLine("</generated_index>");
                sb.AppendLine();
            }

            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine("1. 只输出一个有效的JSON对象（不要Markdown、不要代码块、不要额外解释文本）。");
            sb.AppendLine("2. 对象必须至少包含以下字段：");
            sb.AppendLine(string.Join(", ", fields.Select(f => $"\"{f}\"")));
            if (_batchGeneratedNames.Count > 0)
                sb.AppendLine($"3. Name 字段必须有区分度，且严禁与以下已生成的 Name 重复：{string.Join("、", _batchGeneratedNames)}");
            else
                sb.AppendLine("3. Name 字段必须有区分度，避免重复。");
            sb.AppendLine("4. 所有字段值必须是字符串，不要用数组或嵌套对象。多项内容请在字符串内换行。");
            sb.AppendLine("</output_requirements>");
            sb.AppendLine();
            sb.AppendLine("<output_example note=\"仅示意字段结构，内容请按模板规范生成\">");
            sb.AppendLine("{");
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var comma = i == fields.Count - 1 ? string.Empty : ",";
                sb.AppendLine($"  \"{field}\": \"...\"{comma}");
            }
            sb.AppendLine("}");
            sb.AppendLine("</output_example>");
            return sb.ToString();
        }

        private Dictionary<string, object> ParseSingleJsonEntity(string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                }

                var trimmed = response.Trim();
                string jsonPayload;
                if (trimmed.StartsWith('['))
                {
                    jsonPayload = ExtractJsonArrayFromResponse(response);
                }
                else
                {
                    jsonPayload = ExtractJsonObjectFromResponse(response);
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(jsonPayload);
                }
                catch (JsonException)
                {
                    var repaired = trimmed.StartsWith('[')
                        ? TryRepairTruncatedJson(jsonPayload)
                        : TryRepairTruncatedObjectJson(jsonPayload);
                    if (string.IsNullOrWhiteSpace(repaired))
                    {
                        throw;
                    }

                    document = JsonDocument.Parse(repaired);
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        if (root.GetArrayLength() != 1)
                        {
                            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        }

                        root = root[0];
                    }

                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    }

                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in root.EnumerateObject())
                    {
                        object value = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                            JsonValueKind.Number => property.Value.TryGetInt32(out var intVal) ? intVal : property.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Array => property.Value.EnumerateArray()
                                .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList(),
                            JsonValueKind.Object => property.Value.ToString(),
                            _ => string.Empty
                        };

                        dict[property.Name] = value;
                    }

                    return dict;
                }
            }
            catch
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }

        protected static string ExtractJsonObjectFromResponse(string response)
        {
            var trimmed = response.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('{');
                var end = trimmed.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    return trimmed[start..(end + 1)];
                }
            }

            var first = trimmed.IndexOf('{');
            var last = trimmed.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                return trimmed[first..(last + 1)];
            }

            return trimmed;
        }

        protected virtual List<Dictionary<string, object>> ParseBatchJsonResult(string jsonResponse)
        {
            var result = new List<Dictionary<string, object>>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 输入为空");
                return result;
            }

            try
            {
                var jsonArray = ExtractJsonArrayFromResponse(jsonResponse);

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(jsonArray);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    var repaired = TryRepairTruncatedJson(jsonArray);
                    if (repaired != null)
                    {
                        try
                        {
                            document = JsonDocument.Parse(repaired);
                            TM.App.Log($"[{GetType().Name}] JSON截断已自动修复，继续解析");
                        }
                        catch
                        {
                            throw new InvalidOperationException($"AI输出JSON解析失败，疑似截断: {ex.Message}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"AI输出JSON解析失败，疑似截断: {ex.Message}");
                    }
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException($"AI输出不是JSON数组格式，类型={root.ValueKind}");

                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object)
                        {
                            TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 跳过非对象元素，类型={element.ValueKind}");
                            continue;
                        }

                        var entity = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                        foreach (var property in element.EnumerateObject())
                        {
                            object value = property.Value.ValueKind switch
                            {
                                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                                JsonValueKind.Number => property.Value.TryGetInt32(out var intVal) ? intVal : property.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Array => property.Value.EnumerateArray()
                                    .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                                    .Select(s => s.Trim())
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .ToList(),
                                JsonValueKind.Object => property.Value.ToString(),
                                _ => string.Empty
                            };

                            entity[property.Name] = value;
                        }

                        result.Add(entity);
                    }

                    var config = GetAIGenerationConfig();
                    if (config?.BatchFieldKeyMap != null && config.BatchFieldKeyMap.Count > 0)
                    {
                        foreach (var entity in result)
                        {
                            foreach (var kv in config.BatchFieldKeyMap)
                            {
                                var sourceKey = kv.Key;
                                var targetKey = kv.Value;
                                if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey))
                                {
                                    continue;
                                }

                                if (entity.ContainsKey(targetKey))
                                {
                                    continue;
                                }
                                if (entity.TryGetValue(sourceKey, out var v) && v != null)
                                {
                                    entity[targetKey] = v;
                                    continue;
                                }
                                var matchedKey = FindBestEntityKey(entity, sourceKey);
                                if (matchedKey != null && entity.TryGetValue(matchedKey, out var mv) && mv != null)
                                {
                                    entity[targetKey] = mv;
                                }
                            }
                        }
                    }

                    TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 成功解析 {result.Count} 个实体");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult 解析失败: {ex.Message}");
            }

            return result;
        }
        private static string? TryRepairTruncatedJson(string json)
        {
            return TryRepairJsonPayload(json, '[', ']');
        }

        private static string? TryRepairTruncatedObjectJson(string json)
        {
            return TryRepairJsonPayload(json, '{', '}');
        }

        private static string? TryRepairJsonPayload(string json, char rootOpen, char rootClose)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var trimmed = json.Trim();
            var rootStart = trimmed.IndexOf(rootOpen);
            if (rootStart < 0) return null;

            var payload = trimmed[rootStart..];
            var sb = new System.Text.StringBuilder(payload.Length + 16);
            var inString = false;
            var quoteChar = '"';
            var escape = false;
            var lastSafe = -1;

            for (var i = 0; i < payload.Length; i++)
            {
                var c = payload[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c == quoteChar)
                    {
                        inString = false;
                        sb.Append('"');
                        continue;
                    }

                    if (c < ' ')
                    {
                        if (c == '\n') { sb.Append("\\n"); continue; }
                        if (c == '\r') { continue; }
                        if (c == '\t') { sb.Append("\\t"); continue; }
                        if (c == '\b') { sb.Append("\\b"); continue; }
                        if (c == '\f') { sb.Append("\\f"); continue; }
                        sb.Append($"\\u{(int)c:X4}");
                        continue;
                    }

                    sb.Append(c);
                    continue;
                }

                if (c == '\'')
                {
                    inString = true;
                    quoteChar = '\'';
                    sb.Append('"');
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    quoteChar = '"';
                    sb.Append('"');
                    continue;
                }

                if (c == '：')
                {
                    sb.Append(':');
                    continue;
                }

                if (c == '，')
                {
                    sb.Append(',');
                    continue;
                }

                if (c == ',')
                {
                    var j = i + 1;
                    while (j < payload.Length && char.IsWhiteSpace(payload[j])) j++;
                    if (j < payload.Length && (payload[j] == '}' || payload[j] == ']'))
                    {
                        continue;
                    }

                    lastSafe = sb.Length;
                    sb.Append(c);
                    continue;
                }

                sb.Append(c);
                if (c == rootOpen && lastSafe < 0)
                {
                    lastSafe = sb.Length;
                }
                else if (c == '}' || c == ']')
                {
                    lastSafe = sb.Length;
                }
            }

            var repaired = sb.ToString();
            if (lastSafe >= 0 && lastSafe < repaired.Length)
            {
                repaired = repaired[..lastSafe];
            }

            repaired = CloseJsonPayload(repaired);
            if (!repaired.TrimEnd().EndsWith(rootClose))
            {
                repaired += rootClose;
            }
            return string.Equals(repaired, payload, StringComparison.Ordinal) ? null : repaired;
        }

        private static string CloseJsonPayload(string json)
        {
            var sb = new System.Text.StringBuilder(json.Length + 8);
            var stack = new Stack<char>();
            var inString = false;
            var quoteChar = '"';
            var escape = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                sb.Append(c);

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == quoteChar)
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quoteChar = c;
                    continue;
                }

                if (c == '[' || c == '{')
                {
                    stack.Push(c);
                }
                else if (c == ']' && stack.Count > 0 && stack.Peek() == '[')
                {
                    stack.Pop();
                }
                else if (c == '}' && stack.Count > 0 && stack.Peek() == '{')
                {
                    stack.Pop();
                }
            }

            if (inString)
            {
                sb.Append('"');
            }

            while (stack.Count > 0)
            {
                var open = stack.Pop();
                sb.Append(open == '[' ? ']' : '}');
            }

            return sb.ToString();
        }

        private static string? FindBestEntityKey(Dictionary<string, object> entity, string expectedKey)
        {
            if (entity == null || entity.Count == 0 || string.IsNullOrWhiteSpace(expectedKey))
                return null;

            var normalizedExpected = NormalizeBatchKey(expectedKey);
            if (string.IsNullOrWhiteSpace(normalizedExpected))
                return null;

            string? bestMatch = null;
            int bestScore = -1;

            foreach (var key in entity.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var normalizedKey = NormalizeBatchKey(key);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                    continue;

                int score = -1;

                if (string.Equals(normalizedKey, normalizedExpected, StringComparison.Ordinal))
                {
                    score = 1000 + normalizedExpected.Length;
                }
                else if (normalizedKey.Contains(normalizedExpected, StringComparison.Ordinal))
                {
                    score = 500 + normalizedExpected.Length;
                }
                else if (normalizedExpected.Contains(normalizedKey, StringComparison.Ordinal))
                {
                    score = 100 + normalizedKey.Length;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = key;
                }
            }

            return bestMatch;
        }
        private static string NormalizeBatchKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return WhitespacePunctuationRegex.Replace(key, string.Empty).ToLowerInvariant();
        }

        protected static string ExtractJsonArrayFromResponse(string response)
        {
            var trimmed = response.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('[');
                var end = trimmed.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    return trimmed[start..(end + 1)];
                }
            }

            var firstBracket = trimmed.IndexOf('[');
            var lastBracket = trimmed.LastIndexOf(']');
            if (firstBracket >= 0 && lastBracket > firstBracket)
            {
                return trimmed[firstBracket..(lastBracket + 1)];
            }

            if (firstBracket >= 0 && lastBracket < 0)
            {
                return trimmed[firstBracket..];
            }

            throw new InvalidOperationException("AI响应中未找到有效的JSON数组内容");
        }

    }
}

