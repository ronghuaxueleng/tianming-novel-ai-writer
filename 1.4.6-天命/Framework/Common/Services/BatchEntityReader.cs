using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TM.Framework.Common.Services
{
    public sealed class BatchEntityReader
    {
        private readonly Dictionary<string, object> _entity;

        public BatchEntityReader(Dictionary<string, object> entity)
        {
            _entity = entity ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            return _entity.ContainsKey(key);
        }

        public string GetString(string key)
        {
            if (!TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            if (value is string s)
            {
                return s.Trim();
            }

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.String)
                {
                    return (je.GetString() ?? string.Empty).Trim();
                }

                return je.ToString().Trim();
            }

            return value.ToString()?.Trim() ?? string.Empty;
        }

        public int GetInt(string key)
        {
            if (!TryGetValue(key, out var value) || value == null)
            {
                return 0;
            }

            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is float f) return (int)f;
            if (value is decimal m) return (int)m;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number)
                {
                    if (je.TryGetInt32(out var i32)) return i32;
                    if (je.TryGetInt64(out var i64)) return (int)i64;
                    if (je.TryGetDouble(out var dd)) return (int)dd;
                }

                if (je.ValueKind == JsonValueKind.String)
                {
                    var s = je.GetString();
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                }
            }

            var str = value.ToString();
            return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        public bool GetBool(string key)
        {
            if (!TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            if (value is bool b) return b;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
                if (je.ValueKind == JsonValueKind.String)
                {
                    return ParseBool(je.GetString());
                }
            }

            if (value is string s)
            {
                return ParseBool(s);
            }

            return ParseBool(value.ToString());
        }

        public List<string> GetStringList(string key)
        {
            if (!TryGetValue(key, out var value) || value == null)
            {
                return new List<string>();
            }

            if (value is List<string> list)
            {
                return list
                    .Select(x => x?.Trim() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !IsIgnoredValue(x))
                    .ToList();
            }

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    return je.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s) && !IsIgnoredValue(s))
                        .ToList();
                }

                if (je.ValueKind == JsonValueKind.String)
                {
                    return SplitToList(je.GetString() ?? string.Empty);
                }

                return SplitToList(je.ToString());
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    var s = item?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(s) && !IsIgnoredValue(s))
                    {
                        items.Add(s);
                    }
                }
                return items;
            }

            return SplitToList(value.ToString() ?? string.Empty);
        }

        private bool TryGetValue(string key, out object? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key)) return false;

            if (_entity.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }

            return false;
        }

        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var s = value.Trim();
            if (bool.TryParse(s, out var b)) return b;

            return string.Equals(s, "是", StringComparison.Ordinal)
                   || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(s, "1", StringComparison.Ordinal)
                   || string.Equals(s, "启用", StringComparison.Ordinal)
                   || string.Equals(s, "已启用", StringComparison.Ordinal);
        }

        private static List<string> SplitToList(string value)
        {
            return value
                .Split(new[] { ',', '，', '、', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !IsIgnoredValue(x))
                .ToList();
        }

        private static bool IsIgnoredValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;

            var v = value.Trim();
            return string.Equals(v, "无", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "暂无", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "空", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "无所属", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "不适用", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "N/A", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "NA", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "None", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "-", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "/", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "null", StringComparison.OrdinalIgnoreCase);
        }
    }
}
