using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

public class EndpointTestService
{
    private readonly Func<TimeSpan, HttpClient> _httpClientFactory;

    public EndpointTestService(ProxyService proxyService)
    {
        _httpClientFactory = timeout => proxyService.CreateHttpClient(timeout);
    }

    public EndpointTestService()
    {
        _httpClientFactory = timeout => new HttpClient { Timeout = timeout };
    }

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

        System.Diagnostics.Debug.WriteLine($"[EndpointTestService] {key}: {ex.Message}");
    }

    private static readonly string[] ChatModelKeywords = { "gpt", "chat", "turbo", "instruct" };

    private static readonly Regex VersionPrefixRegex =
        new(@"/v\d+(?:beta\d*|alpha\d*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] KnownApiPathSuffixes =
    {
        "/chat/completions",
        "/completions",
        "/models",
        "/embeddings",
        "/images/generations",
        "/audio/transcriptions",
        "/audio/translations",
        "/audio/speech",
        "/moderations",
    };

    public List<string> GenerateCandidateUrls(string apiEndpoint)
    {
        if (string.IsNullOrWhiteSpace(apiEndpoint))
            return new List<string>();

        var trimmed = apiEndpoint.Trim().TrimEnd('/');

        foreach (var suffix in KnownApiPathSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        var candidates = new List<string>();
        var match = VersionPrefixRegex.Match(trimmed);

        if (match.Success)
        {
            var withVersion = trimmed;
            var withoutVersion = trimmed[..^match.Value.Length].TrimEnd('/');

            candidates.Add(withVersion);
            if (!string.IsNullOrWhiteSpace(withoutVersion))
                candidates.Add(withoutVersion);

            if (!match.Value.Equals("/v1", StringComparison.OrdinalIgnoreCase))
                candidates.Add(withoutVersion + "/v1");

            if (withoutVersion.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                var withoutApi = withoutVersion[..^4].TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(withoutApi))
                    candidates.Add(withoutApi + match.Value);
            }
        }
        else
        {
            candidates.Add(trimmed);
            candidates.Add(trimmed + "/v1");
            candidates.Add(trimmed + "/openai/v1");
            candidates.Add(trimmed + "/api/v1");
            candidates.Add(trimmed + "/openai");
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool ValidateModelsResponse(string jsonContent, out List<ModelInfo> models)
    {
        models = new List<ModelInfo>();

        if (string.IsNullOrWhiteSpace(jsonContent))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var dataElement))
                return false;

            if (dataElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in dataElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        models.Add(new ModelInfo { Id = id });
                    }
                }
            }

            return models.Count > 0;
        }
        catch (Exception ex)
        {
            DebugLogOnce(nameof(ValidateModelsResponse), ex);
            return false;
        }
    }

    public bool ValidateChatResponse(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choicesElement))
                return false;

            if (choicesElement.ValueKind != JsonValueKind.Array)
                return false;

            if (choicesElement.GetArrayLength() == 0)
                return false;

            var firstChoice = choicesElement[0];
            return firstChoice.TryGetProperty("message", out _);
        }
        catch (Exception ex)
        {
            DebugLogOnce(nameof(ValidateChatResponse), ex);
            return false;
        }
    }

    public string SelectTestModel(IEnumerable<ModelInfo> models)
    {
        var modelList = models?.ToList() ?? new List<ModelInfo>();

        if (modelList.Count == 0)
            return string.Empty;

        foreach (var keyword in ChatModelKeywords)
        {
            var match = modelList.FirstOrDefault(m =>
                m.Id?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);

            if (match != null)
                return match.Id!;
        }

        return modelList.First().Id ?? string.Empty;
    }

    public string ComputeEndpointSignature(string apiEndpoint, string? apiKey = null)
    {
        var input = apiEndpoint ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    public async Task<ModelsTestResult> TestModelsEndpointAsync(
        List<string> candidateBaseUrls,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var result = new ModelsTestResult();

        var validCandidates = candidateBaseUrls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (validCandidates.Count == 0)
        {
            result.ErrorMessage = "端点地址为空";
            return result;
        }

        var tasks = validCandidates
            .Select(u => TestSingleModelsEndpointAsync(u, apiKey, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var successResults = results.Where(r => r.success).ToList();

        if (successResults.Count == 0)
        {
            result.ErrorMessage = results.FirstOrDefault().error ?? "所有端点测试失败";
            return result;
        }

        var best = successResults
            .OrderByDescending(r => r.models.Count)
            .ThenBy(r => validCandidates.IndexOf(r.endpoint))
            .First();

        result.Success = true;
        result.SuccessfulEndpoint = best.endpoint;
        result.Models = best.models;
        return result;
    }

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/132.0.0.0 Safari/537.36";

    private static void ApplyStandardHeaders(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        }

        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://cherry-ai.com");
        request.Headers.TryAddWithoutValidation("X-Title", "Cherry Studio");

        request.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");
        request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"132\", \"Google Chrome\";v=\"132\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "cross-site");
        request.Headers.TryAddWithoutValidation("Origin", "https://cherry-ai.com");
    }

    private static bool IsHtmlResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var t = content.TrimStart();
        return t.StartsWith("<", StringComparison.Ordinal)
            || t.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || t.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateDirectClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                   | System.Net.DecompressionMethods.Deflate
                                   | System.Net.DecompressionMethods.Brotli,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private async Task<(string endpoint, bool success, List<ModelInfo> models, string? error)> TestSingleModelsEndpointAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/models";
        var models = new List<ModelInfo>();

        async Task<(bool ok, string content, string? errMsg)> DoRequest(HttpClient client)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyStandardHeaders(req, apiKey);
            using var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = resp.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "API 密钥无效或权限不足",
                    System.Net.HttpStatusCode.Forbidden    => "API 密钥无效或权限不足",
                    System.Net.HttpStatusCode.NotFound     => "端点不存在，请检查 URL",
                    _ => $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}"
                };
                return (false, string.Empty, msg);
            }
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            return (true, body, null);
        }

        try
        {
            using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
            var (ok1, body1, err1) = await DoRequest(proxyClient);

            if (!ok1)
                return (baseUrl, false, models, err1);

            if (!IsHtmlResponse(body1))
            {
                if (!ValidateModelsResponse(body1, out models))
                    return (baseUrl, false, models, "响应格式不符合 OpenAI 规范");
                return (baseUrl, true, models, null);
            }

            TM.App.Log($"[EndpointTest] 代理返回 HTML，尝试直连: {url} | HTML头200字: {body1[..Math.Min(200, body1.Length)]}");

            using var directClient = CreateDirectClient();
            var (ok2, body2, err2) = await DoRequest(directClient);

            if (!ok2)
                return (baseUrl, false, models, err2);

            if (IsHtmlResponse(body2))
            {
                TM.App.Log($"[EndpointTest] 直连也返回 HTML: {url}");
                return (baseUrl, false, models, "端点返回 HTML 页面（代理和直连均被拦截），请检查 URL、密钥或代理设置");
            }

            if (!ValidateModelsResponse(body2, out models))
                return (baseUrl, false, models, "响应格式不符合 OpenAI 规范");

            TM.App.Log($"[EndpointTest] 直连成功: {url}");
            return (baseUrl, true, models, null);
        }
        catch (TaskCanceledException)
        {
            return (baseUrl, false, models, "连接超时，请检查网络或端点地址");
        }
        catch (HttpRequestException ex)
        {
            return (baseUrl, false, models, $"网络错误: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (baseUrl, false, models, $"测试失败: {ex.Message}");
        }
    }

    public async Task<ChatTestResult> TestChatEndpointAsync(
        List<string> candidateBaseUrls,
        string apiKey,
        string testModelId,
        CancellationToken cancellationToken = default)
    {
        var result = new ChatTestResult();

        if (string.IsNullOrWhiteSpace(testModelId))
        {
            result.ErrorMessage = "未指定测试模型";
            return result;
        }

        var validCandidates = candidateBaseUrls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (validCandidates.Count == 0)
        {
            result.ErrorMessage = "端点地址为空";
            return result;
        }

        foreach (var endpoint in validCandidates)
        {
            var (success, error) = await TestSingleChatEndpointAsync(endpoint, apiKey, testModelId, cancellationToken);
            if (success)
            {
                result.Success = true;
                result.SuccessfulEndpoint = endpoint;
                result.ErrorMessage = null;
                return result;
            }

            result.ErrorMessage = error;
        }

        return result;
    }

    private async Task<(bool success, string? error)> TestSingleChatEndpointAsync(
        string baseUrl,
        string apiKey,
        string modelId,
        CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/chat/completions";

        var requestBody = new
        {
            model = modelId,
            messages = new[] { new { role = "user", content = "ping" } },
            max_tokens = 1,
            temperature = 0
        };
        var jsonContent = JsonSerializer.Serialize(requestBody);

        async Task<(bool ok, string body, string? errMsg)> DoRequest(HttpClient client)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            ApplyStandardHeaders(req, apiKey);
            using var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var ec = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (IsHtmlResponse(ec))
                    return (false, ec, "_HTML_");
                var msg = resp.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized   => "API 密钥无效或权限不足",
                    System.Net.HttpStatusCode.Forbidden      => "API 密钥无效或权限不足",
                    System.Net.HttpStatusCode.NotFound       => "端点不存在，请检查 URL",
                    System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后重试",
                    System.Net.HttpStatusCode.PaymentRequired => "账户余额不足",
                    _ => ParseErrorMessage(ec) ?? $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}"
                };
                return (false, ec, msg);
            }
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            return (true, body, null);
        }

        try
        {
            using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
            var (ok1, body1, err1) = await DoRequest(proxyClient);

            bool proxyHtml = err1 == "_HTML_" || (ok1 && IsHtmlResponse(body1));

            if (!proxyHtml)
            {
                if (!ok1) return (false, err1);
                if (!ValidateChatResponse(body1)) return (false, "响应格式不符合 OpenAI 规范");
                return (true, null);
            }

            TM.App.Log($"[EndpointTest] 代理返回 HTML，尝试直连 Chat: {url} | HTML头200字: {body1[..Math.Min(200, body1.Length)]}");

            using var directClient = CreateDirectClient();
            var (ok2, body2, err2) = await DoRequest(directClient);

            if (!ok2)
            {
                return err2 == "_HTML_"
                    ? (false, "端点返回 HTML 页面（代理和直连均被拦截），请检查 URL、密钥或代理设置")
                    : (false, err2);
            }

            if (IsHtmlResponse(body2))
                return (false, "端点返回 HTML 页面（代理和直连均被拦截），请检查 URL、密钥或代理设置");

            if (!ValidateChatResponse(body2))
                return (false, "响应格式不符合 OpenAI 规范");

            TM.App.Log($"[EndpointTest] 直连 Chat 成功: {url}");
            return (true, null);
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请检查网络或端点地址");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"网络错误: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"测试失败: {ex.Message}");
        }
    }

    private static string? ParseErrorMessage(string errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(errorContent);
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOnce(nameof(ParseErrorMessage), ex);
        }

        return null;
    }
}

public class ModelInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string? Id { get; set; }
}

public class ModelsTestResult
{
    [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessfulEndpoint")] public string? SuccessfulEndpoint { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Models")] public List<ModelInfo> Models { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
}

public class ChatTestResult
{
    [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessfulEndpoint")] public string? SuccessfulEndpoint { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
}
