using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TM.Framework.Common.Services
{
    public class SmartFieldExtractor
    {
        private readonly List<string> _targetFields;
        private readonly Dictionary<string, string[]>? _fieldAliases;
        private readonly bool _enableKeywordExtract;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SmartFieldExtractor] {key}: {ex.Message}");
        }

        public SmartFieldExtractor(
            List<string> targetFields,
            Dictionary<string, string[]>? fieldAliases = null,
            bool enableKeywordExtract = false)
        {
            _targetFields = targetFields ?? throw new ArgumentNullException(nameof(targetFields));
            _fieldAliases = fieldAliases;
            _enableKeywordExtract = enableKeywordExtract;
        }

        public static object? ConvertValue(string? value, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return GetDefaultValue(targetType);
            }

            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlyingType == typeof(string))
                    return value;

                if (underlyingType == typeof(int))
                {
                    var numMatch = Regex.Match(value, @"-?\d+");
                    return numMatch.Success && int.TryParse(numMatch.Value, out var i) ? i : 0;
                }

                if (underlyingType == typeof(double))
                {
                    var numMatch = Regex.Match(value, @"-?\d+\.?\d*");
                    return numMatch.Success && double.TryParse(numMatch.Value, out var d) ? d : 0.0;
                }

                if (underlyingType == typeof(float))
                {
                    var numMatch = Regex.Match(value, @"-?\d+\.?\d*");
                    return numMatch.Success && float.TryParse(numMatch.Value, out var f) ? f : 0f;
                }

                if (underlyingType == typeof(decimal))
                {
                    var numMatch = Regex.Match(value, @"-?\d+\.?\d*");
                    return numMatch.Success && decimal.TryParse(numMatch.Value, out var m) ? m : 0m;
                }

                if (underlyingType == typeof(bool))
                {
                    var lower = value.ToLowerInvariant();
                    return lower == "true" || lower == "是" || lower == "yes" || lower == "1";
                }

                return value;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ConvertValue), ex);
                return GetDefaultValue(targetType);
            }
        }

        private static object? GetDefaultValue(Type type)
        {
            if (type == typeof(string)) return string.Empty;
            if (type.IsValueType) return Activator.CreateInstance(type);
            return null;
        }

        public Dictionary<string, string> Extract(string aiResponse)
        {
            if (string.IsNullOrWhiteSpace(aiResponse))
                return new Dictionary<string, string>();

            var result = new Dictionary<string, string>();

            TryExtractFromJson(aiResponse, result);

            if (result.Count < _targetFields.Count)
            {
                TryExtractFromMarkdown(aiResponse, result);
            }

            if (_enableKeywordExtract && result.Count < _targetFields.Count)
            {
                TryExtractFromKeywords(aiResponse, result);
            }

            return result;
        }

        #region 策略1：JSON 解析

        private void TryExtractFromJson(string aiResponse, Dictionary<string, string> result)
        {
            try
            {
                var jsonContent = ExtractJsonBlock(aiResponse);
                if (string.IsNullOrWhiteSpace(jsonContent))
                    return;

                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                var fieldsElement = GetNewBusinessFieldsElement(root);

                if (fieldsElement.ValueKind != JsonValueKind.Object)
                    return;

                foreach (var prop in fieldsElement.EnumerateObject())
                {
                    var matchedField = FindBestMatch(prop.Name, _targetFields, _fieldAliases);
                    if (matchedField != null && !result.ContainsKey(matchedField))
                    {
                        var value = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            result[matchedField] = value;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(TryExtractFromJson), ex);
            }
        }

        private static JsonElement GetNewBusinessFieldsElement(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
                {
                    return fields;
                }

                if (root.TryGetProperty("Fields", out var fieldsPascal) && fieldsPascal.ValueKind == JsonValueKind.Object)
                {
                    return fieldsPascal;
                }
            }

            return root;
        }

        private static string? ExtractJsonBlock(string text)
        {
            var codeBlockMatch = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.IgnoreCase);
            if (codeBlockMatch.Success)
            {
                return codeBlockMatch.Groups[1].Value.Trim();
            }

            var trimmed = text.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                return trimmed;
            }

            var jsonMatch = Regex.Match(text, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                return jsonMatch.Value;
            }

            return null;
        }

        #endregion

        #region 策略2：Markdown 分段

        private void TryExtractFromMarkdown(string aiResponse, Dictionary<string, string> result)
        {
            var sections = Regex.Split(aiResponse, @"(?=^#{2,3}\s*)", RegexOptions.Multiline);

            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section))
                    continue;

                var headerMatch = Regex.Match(section, @"^#{2,3}\s*(.+?)[\r\n]+([\s\S]*)", RegexOptions.Multiline);
                if (!headerMatch.Success)
                    continue;

                var title = headerMatch.Groups[1].Value.Trim();
                var content = headerMatch.Groups[2].Value.Trim();

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var matchedField = FindBestMatch(title, _targetFields, _fieldAliases);
                if (matchedField != null && !result.ContainsKey(matchedField))
                {
                    result[matchedField] = content;
                }
            }
        }

        #endregion

        #region 策略3：关键词匹配（可选）

        private void TryExtractFromKeywords(string aiResponse, Dictionary<string, string> result)
        {
            var missingFields = _targetFields.Where(f => !result.ContainsKey(f)).ToList();

            foreach (var field in missingFields)
            {
                var index = aiResponse.IndexOf(field, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                var startIndex = index + field.Length;

                while (startIndex < aiResponse.Length && 
                       (char.IsWhiteSpace(aiResponse[startIndex]) || 
                        aiResponse[startIndex] == '：' || 
                        aiResponse[startIndex] == ':' ||
                        aiResponse[startIndex] == '-'))
                {
                    startIndex++;
                }

                if (startIndex >= aiResponse.Length)
                    continue;

                var endIndex = aiResponse.Length;
                foreach (var otherField in _targetFields)
                {
                    if (otherField == field)
                        continue;
                    var otherIndex = aiResponse.IndexOf(otherField, startIndex, StringComparison.OrdinalIgnoreCase);
                    if (otherIndex > 0 && otherIndex < endIndex)
                    {
                        endIndex = otherIndex;
                    }
                }

                var paragraphEnd = aiResponse.IndexOf("\n\n", startIndex);
                if (paragraphEnd > 0 && paragraphEnd < endIndex)
                {
                    endIndex = paragraphEnd;
                }

                var content = aiResponse.Substring(startIndex, endIndex - startIndex).Trim();
                if (!string.IsNullOrWhiteSpace(content) && content.Length > 5)
                {
                    result[field] = content;
                }
            }
        }

        #endregion

        #region 匹配逻辑

        private static string? FindBestMatch(
            string aiTitle,
            IEnumerable<string> targetFields,
            Dictionary<string, string[]>? aliases)
        {
            if (string.IsNullOrWhiteSpace(aiTitle))
                return null;

            var normalized = Normalize(aiTitle);
            var fieldList = targetFields.ToList();

            var exact = fieldList.FirstOrDefault(f => Normalize(f) == normalized);
            if (exact != null)
                return exact;

            if (aliases != null)
            {
                foreach (var (field, aliasList) in aliases)
                {
                    if (aliasList.Any(a => Normalize(a) == normalized))
                        return field;
                }
            }

            var containsMatches = fieldList
                .Where(f => normalized.Contains(Normalize(f)))
                .OrderByDescending(f => f.Length)
                .ToList();

            if (containsMatches.Count > 0)
                return containsMatches[0];

            if (aliases != null)
            {
                var aliasMatches = aliases
                    .Where(kv => kv.Value.Any(a => normalized.Contains(Normalize(a))))
                    .OrderByDescending(kv => kv.Value.Max(a => a.Length))
                    .ToList();

                if (aliasMatches.Count > 0)
                    return aliasMatches[0].Key;
            }

            return null;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var result = Regex.Replace(text, @"[\s\p{P}]", "").ToLowerInvariant();
            return result;
        }

        #endregion
    }
}
