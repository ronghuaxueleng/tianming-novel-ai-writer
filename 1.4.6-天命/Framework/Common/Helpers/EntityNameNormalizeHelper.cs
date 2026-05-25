using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TM.Framework.Common.Helpers
{
    public enum EntityMatchMode
    {
        Lenient,

        Strict
    }

    public class EntityMatchResult
    {
        public string Input { get; set; } = string.Empty;
        public string? Matched { get; set; }
        public bool IsMatched { get; set; }
        public string MatchType { get; set; } = "None";
    }

    public static class EntityNameNormalizeHelper
    {
        #region 核心匹配方法（带模式参数）

        public static string NormalizeSingle(string value, IEnumerable<string> candidates, EntityMatchMode mode)
        {
            var result = MatchSingleCore(value, candidates);
            if (result.IsMatched)
                return result.Matched!;

            return mode == EntityMatchMode.Strict ? string.Empty : result.Input;
        }

        public static string NormalizeSingle(string value, IEnumerable<string> candidates)
        {
            return NormalizeSingle(value, candidates, EntityMatchMode.Lenient);
        }

        public static string NormalizeMultiple(string value, IEnumerable<string> candidates, EntityMatchMode mode, string separator = "、")
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var names = SplitNames(value);
            if (names.Count == 0)
                return string.Empty;

            var matched = new List<string>();
            foreach (var name in names)
            {
                var normalized = NormalizeSingle(name, candidates, mode);
                if (!string.IsNullOrWhiteSpace(normalized))
                    matched.Add(normalized);
            }

            return string.Join(separator, matched.Distinct());
        }

        public static string NormalizeMultiple(string value, IEnumerable<string> candidates, string separator = "、")
        {
            return NormalizeMultiple(value, candidates, EntityMatchMode.Lenient, separator);
        }

        #endregion

        #region 严格过滤便捷方法（语义更清晰）

        public static string FilterToCandidate(string value, IEnumerable<string> candidates)
        {
            return NormalizeSingle(value, candidates, EntityMatchMode.Strict);
        }

        public static string FilterToCandidates(string value, IEnumerable<string> candidates, string separator = "、")
        {
            return NormalizeMultiple(value, candidates, EntityMatchMode.Strict, separator);
        }

        #endregion

        #region 诊断方法（用于校验和提示）

        public static EntityMatchResult MatchSingle(string value, IEnumerable<string> candidates)
        {
            return MatchSingleCore(value, candidates);
        }

        public static List<EntityMatchResult> MatchMultiple(string value, IEnumerable<string> candidates)
        {
            var results = new List<EntityMatchResult>();
            if (string.IsNullOrWhiteSpace(value))
                return results;

            var names = SplitNames(value);
            foreach (var name in names)
            {
                results.Add(MatchSingleCore(name, candidates));
            }

            return results;
        }

        private static EntityMatchResult MatchSingleCore(string value, IEnumerable<string> candidates)
        {
            var result = new EntityMatchResult();

            if (string.IsNullOrWhiteSpace(value))
            {
                result.Input = string.Empty;
                result.IsMatched = false;
                result.MatchType = "Empty";
                return result;
            }

            var normalized = value.Trim();
            result.Input = normalized;

            if (IsIgnoredValue(normalized))
            {
                result.IsMatched = true;
                result.Matched = string.Empty;
                result.MatchType = "Ignored";
                return result;
            }

            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0)
            {
                result.IsMatched = false;
                result.MatchType = "NoCandidates";
                return result;
            }

            var exact = list.FirstOrDefault(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exact))
            {
                result.IsMatched = true;
                result.Matched = exact;
                result.MatchType = "Exact";
                return result;
            }

            var contains = list.FirstOrDefault(c => NameExistsInContent(c, normalized) || NameExistsInContent(normalized, c));
            if (!string.IsNullOrEmpty(contains))
            {
                result.IsMatched = true;
                result.Matched = contains;
                result.MatchType = "Contains";
                return result;
            }

            string? bestMatch = null;
            int bestLen = 1;
            foreach (var candidate in list)
            {
                int lcs = LongestCommonSubstringLength(normalized, candidate);
                if (lcs > bestLen)
                {
                    bestLen = lcs;
                    bestMatch = candidate;
                }
            }
            if (bestMatch != null)
            {
                result.IsMatched = true;
                result.Matched = bestMatch;
                result.MatchType = "Substring";
                return result;
            }

            result.IsMatched = false;
            result.MatchType = "None";
            return result;
        }

        public static bool IsIgnoredValue(string value)
        {
            return string.Equals(value, "无", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "暂无", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "空", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "无所属", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "不适用", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "NA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "-", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "/", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        public static List<string> SplitNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();

            return value
                .Split(new[] { ',', '，', '、', '\n', '\r', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        public static string StripBracketAnnotation(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var t = name.Trim();
            t = Regex.Replace(t, @"\s*[\(（\[【].*?[\)）\]】]\s*$", string.Empty);
            return t.Trim();
        }

        public static bool NameExistsInContent(string content, string fullName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(fullName))
                return false;

            if (content.Contains(fullName, StringComparison.OrdinalIgnoreCase))
                return true;

            var primaryName = StripBracketAnnotation(fullName);
            if (!string.IsNullOrWhiteSpace(primaryName) && primaryName != fullName
                && content.Contains(primaryName, StringComparison.OrdinalIgnoreCase))
                return true;

            var aliasMatches = Regex.Matches(fullName, @"[\(（\[【](.+?)[\)）\]】]");
            foreach (Match m in aliasMatches)
            {
                var alias = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(alias) && alias.Length >= 2
                    && content.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static string NormalizeBatchEntityName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var t = name.Trim();
            t = Regex.Replace(t, @"^\s*\d+\s*[\.、\-:\)）]\s*", string.Empty);
            t = Regex.Replace(t, @"^.{0,30}?[-_—–\s]+(?=第\s*[\d一二三四五六七八九十百千零]+\s*[章卷])", string.Empty);
            t = Regex.Replace(t, @"^\s*第\s*\d+\s*卷\s*[-_\s]*第\s*\d+\s*章\s*[-_\s]*", string.Empty);
            t = Regex.Replace(t, @"^\s*第\s*[一二三四五六七八九十百千零]+\s*卷\s*[-_\s]*第\s*[一二三四五六七八九十百千零]+\s*章\s*[-_\s]*", string.Empty);
            t = Regex.Replace(t, @"^\s*第\s*\d+\s*[章卷]\s*[：:、\-—–_]*\s*", string.Empty);
            t = Regex.Replace(t, @"^\s*第\s*[一二三四五六七八九十百千零]+\s*[章卷]\s*[：:、\-—–_]*\s*", string.Empty);
            t = Regex.Replace(t, @"^\s*场景蓝图[-_]*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^\s*场景\s*[-_]?\d+(?:-\d+)?\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^\s*vol\s*\d+\s*[_-]?ch\s*\d+\s*[-_]*\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^\s*ch\s*\d+\s*[-_]*\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"(^|[-_\s])scene\s*[-_]?\d+(?:-\d+)?", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"(^|[-_\s])vol\d+(_?ch\d+)?(-\d+)?", " ", RegexOptions.IgnoreCase);
            t = t.Replace("__", " ").Replace("--", " ");
            t = t.Trim(' ', '-', '_');
            return t.Trim();
        }

        private static int LongestCommonSubstringLength(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            int maxLen = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    int len = 0;
                    while (i + len < a.Length && j + len < b.Length &&
                           char.ToLowerInvariant(a[i + len]) == char.ToLowerInvariant(b[j + len]))
                    {
                        len++;
                    }
                    if (len > maxLen)
                        maxLen = len;
                }
            }
            return maxLen;
        }

        public static List<string> GetUnmatchedNames(string value, IEnumerable<string> candidates)
        {
            var names = SplitNames(value);
            if (names.Count == 0)
                return new List<string>();

            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0)
                return new List<string>();

            bool IsMatched(string name)
            {
                var r = MatchSingleCore(name, list);
                return r.IsMatched;
            }

            return names.Where(n => !IsMatched(n)).Distinct().ToList();
        }
    }
}
