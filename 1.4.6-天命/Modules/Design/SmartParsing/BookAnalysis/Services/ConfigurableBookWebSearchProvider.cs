using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public class ConfigurableBookWebSearchProvider : IBookWebSearchProvider
    {
        private readonly ProxyService _proxyService;
        private readonly string _settingsPath;

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        public ConfigurableBookWebSearchProvider(ProxyService proxyService)
        {
            _proxyService = proxyService;
            _settingsPath = StoragePathHelper.GetFilePath("Modules", "Design/SmartParsing/BookAnalysis", "search_api_settings.json");
        }

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

            System.Diagnostics.Debug.WriteLine($"[ConfigurableBookWebSearchProvider] {key}: {ex.Message}");
        }

        public async Task<BookWebSearchResult> SearchAsync(string query, int timeoutSeconds = 10)
        {
            var result = new BookWebSearchResult();

            try
            {
                var settings = await LoadSettingsAsync();
                if (settings == null || !settings.Enabled)
                {
                    result.Success = false;
                    result.ErrorMessage = "Search API 未配置";
                    return result;
                }

                var provider = (settings.Provider ?? string.Empty).Trim();
                if (string.Equals(provider, "bingv7", StringComparison.OrdinalIgnoreCase))
                {
                    return await SearchBingV7Async(settings, query, timeoutSeconds);
                }

                result.Success = false;
                result.ErrorMessage = $"不支持的Search Provider: {provider}";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<BookWebSearchSettings?> LoadSettingsAsync()
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<BookWebSearchSettings>(json, _readOptions);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(LoadSettingsAsync), ex);
                return null;
            }
        }

        private async Task<BookWebSearchResult> SearchBingV7Async(BookWebSearchSettings settings, string query, int timeoutSeconds)
        {
            var result = new BookWebSearchResult();

            var apiKey = (settings.ApiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                result.Success = false;
                result.ErrorMessage = "Bing API Key 未配置";
                return result;
            }

            var endpoint = string.IsNullOrWhiteSpace(settings.Endpoint)
                ? "https://api.bing.microsoft.com/v7.0/search"
                : settings.Endpoint.Trim();

            var mkt = string.IsNullOrWhiteSpace(settings.Market) ? "zh-CN" : settings.Market.Trim();
            var count = settings.Count <= 0 ? 5 : Math.Min(settings.Count, 10);

            try
            {
                using var client = _proxyService.CreateHttpClient(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

                var url = $"{endpoint}?q={Uri.EscapeDataString(query)}&mkt={Uri.EscapeDataString(mkt)}&count={count}&textFormat=Raw";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Bing搜索失败: {(int)response.StatusCode}";
                    return result;
                }

                var json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.Success = false;
                    result.ErrorMessage = "Bing返回为空";
                    return result;
                }

                var summary = BuildBingSummary(json, count);
                if (string.IsNullOrWhiteSpace(summary))
                {
                    result.Success = false;
                    result.ErrorMessage = "Bing结果解析为空";
                    return result;
                }

                result.Success = true;
                result.Summary = summary;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static string BuildBingSummary(string json, int maxItems)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("webPages", out var webPages) ||
                    !webPages.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Array)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder();
                var i = 0;

                foreach (var item in value.EnumerateArray().Take(Math.Max(1, maxItems)))
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    var name = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                        ? nameProp.GetString() ?? string.Empty
                        : string.Empty;

                    var snippet = item.TryGetProperty("snippet", out var snippetProp) && snippetProp.ValueKind == JsonValueKind.String
                        ? snippetProp.GetString() ?? string.Empty
                        : string.Empty;

                    var url = item.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                        ? urlProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(snippet))
                    {
                        continue;
                    }

                    i++;
                    sb.AppendLine($"{i}. {name}".Trim());
                    if (!string.IsNullOrWhiteSpace(snippet)) sb.AppendLine(snippet.Trim());
                    if (!string.IsNullOrWhiteSpace(url)) sb.AppendLine(url.Trim());
                    sb.AppendLine();
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(BuildBingSummary), ex);
                return string.Empty;
            }
        }

        private class BookWebSearchSettings
        {
            [System.Text.Json.Serialization.JsonPropertyName("Enabled")] public bool Enabled { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Provider")] public string Provider { get; set; } = "bingv7";
            [System.Text.Json.Serialization.JsonPropertyName("Endpoint")] public string Endpoint { get; set; } = "https://api.bing.microsoft.com/v7.0/search";
            [System.Text.Json.Serialization.JsonPropertyName("ApiKey")] public string ApiKey { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("Market")] public string Market { get; set; } = "zh-CN";
            [System.Text.Json.Serialization.JsonPropertyName("Count")] public int Count { get; set; } = 5;
        }
    }
}
