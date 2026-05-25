using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace TM.Framework.Common.Helpers
{
    public static class JsonHelper
    {
        public static JsonSerializerOptions Default { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static JsonSerializerOptions Compact { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static JsonSerializerOptions Web { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static JsonSerializerOptions CnDefault { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static JsonSerializerOptions CnCompact { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static JsonSerializerOptions Lenient { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string Serialize<T>(T value, bool indented = true)
        {
            return JsonSerializer.Serialize(value, indented ? Default : Compact);
        }

        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Default);
        }

        public static T? TryDeserialize<T>(string? json, T? defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(json))
                return defaultValue;

            try
            {
                return JsonSerializer.Deserialize<T>(json, Default) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
