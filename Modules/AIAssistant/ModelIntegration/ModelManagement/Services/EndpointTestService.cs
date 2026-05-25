using System;
using System.Collections.Concurrent;
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
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;

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

    private static readonly AsyncLocal<bool> _suppressUrlInLogs = new();
    private static readonly Regex _sensitiveEndpointTextRegex = new(
        @"https?://[^\s""'<>，。；]+|(?<!@)\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}(?::\d+)?(?:/[^\s""'<>，。；]*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IDisposable BeginPrivateScope(bool active = true)
    {
        if (!active) return _NoopScope.Instance;
        var previous = _suppressUrlInLogs.Value;
        _suppressUrlInLogs.Value = true;
        return new _ScopeRestorer(() => _suppressUrlInLogs.Value = previous);
    }

    private static string MaskUrl(string? url)
        => _suppressUrlInLogs.Value ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel : (url ?? string.Empty);

    private static string MaskSensitiveText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (!_suppressUrlInLogs.Value) return text;
        return _sensitiveEndpointTextRegex.Replace(text, TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel);
    }

    private sealed class _NoopScope : IDisposable
    {
        public static readonly _NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed class _ScopeRestorer : IDisposable
    {
        private Action? _restore;
        public _ScopeRestorer(Action restore) { _restore = restore; }
        public void Dispose()
        {
            var r = _restore;
            _restore = null;
            r?.Invoke();
        }
    }

    private static readonly ConcurrentDictionary<string, (long Ticks, ModelCapabilityResult Result)>
        _probeResultCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan _probeCacheTtl = TimeSpan.FromHours(24);

    private static readonly object _probeCacheSaveLock = new();
    private static bool _probeCacheDirty;

    static EndpointTestService()
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try { await LoadProbeCacheAsync().ConfigureAwait(false); }
            catch (Exception ex) { TM.App.Log($"[EndpointTestService] 缓存加载失败: {ex.Message}"); }
        });
    }

    private static string GetProbeCacheFilePath()
    {
        try
        {
            return TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetFilePath(
                "Services",
                "AI/ModelManagement",
                "probe_cache.json");
        }
        catch { return string.Empty; }
    }

    private static async Task LoadProbeCacheAsync()
    {
        try
        {
            var path = GetProbeCacheFilePath();
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

            var jsonStr = await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return;

            var now = DateTime.UtcNow.Ticks;
            int loaded = 0, expired = 0;
            foreach (var item in entries.EnumerateArray())
            {
                if (!item.TryGetProperty("Key", out var keyEl)) continue;
                if (!item.TryGetProperty("Ticks", out var ticksEl) || !ticksEl.TryGetInt64(out var ticks)) continue;
                if (!item.TryGetProperty("Result", out var resultEl)) continue;

                if (now - ticks > _probeCacheTtl.Ticks) { expired++; continue; }

                var key = keyEl.GetString();
                if (string.IsNullOrEmpty(key)) continue;

                try
                {
                    var resultJson = resultEl.GetRawText();
                    var result = JsonSerializer.Deserialize<ModelCapabilityResult>(resultJson);
                    if (result != null)
                    {
                        _probeResultCache[key] = (ticks, result);
                        loaded++;
                    }
                }
                catch { }
            }

            TM.App.Log($"[EndpointTestService] 持久化探测缓存已加载: 命中={loaded}, 过期跳过={expired}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[EndpointTestService] 加载探测缓存失败: {ex.Message}");
        }
    }

    private static void SaveProbeCacheAsync()
    {
        lock (_probeCacheSaveLock) { _probeCacheDirty = true; }
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(500).ConfigureAwait(false);
                bool shouldSave;
                lock (_probeCacheSaveLock) { shouldSave = _probeCacheDirty; _probeCacheDirty = false; }
                if (!shouldSave) return;

                var path = GetProbeCacheFilePath();
                if (string.IsNullOrEmpty(path)) return;

                var now = DateTime.UtcNow.Ticks;
                var entries = new List<object>();
                foreach (var kv in _probeResultCache)
                {
                    if (now - kv.Value.Ticks > _probeCacheTtl.Ticks) continue;
                    entries.Add(new
                    {
                        Key = kv.Key,
                        Ticks = kv.Value.Ticks,
                        Result = kv.Value.Result
                    });
                }

                var json = JsonSerializer.Serialize(new { Entries = entries }, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                System.IO.File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EndpointTestService] 保存探测缓存失败: {ex.Message}");
            }
        });
    }

    private static string BuildProbeCacheKey(string? providerId, string? endpoint, string? modelId)
    {
        var pid = (providerId ?? string.Empty).Trim().ToLowerInvariant();
        var ep = (endpoint ?? string.Empty).TrimEnd('/').ToLowerInvariant();
        var mid = (modelId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(pid) && string.IsNullOrEmpty(ep)) return mid;
        if (string.IsNullOrEmpty(pid)) return $"{ep}::{mid}";
        if (string.IsNullOrEmpty(ep)) return $"{pid}::{mid}";
        return $"{pid}::{ep}::{mid}";
    }

    public static void InvalidateProbeCache(string? endpoint, string? modelId = null, string? providerId = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                _probeResultCache.Clear();
                SaveProbeCacheAsync();
                TM.App.Log("[EndpointTestService] 已清空全部探测缓存");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var key = BuildProbeCacheKey(providerId, endpoint, modelId);
            var legacyKey = BuildProbeCacheKey(null, endpoint, modelId);
            var ep = endpoint!.TrimEnd('/').ToLowerInvariant();
            var suffix = $"::{ep}::{modelId.Trim()}";
            var matchingKeys = _probeResultCache.Keys
                .Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(k, legacyKey, StringComparison.OrdinalIgnoreCase)
                         || (string.IsNullOrWhiteSpace(providerId) && k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var k in matchingKeys)
                _probeResultCache.TryRemove(k, out _);
            if (matchingKeys.Count > 0)
            {
                SaveProbeCacheAsync();
                TM.App.Log($"[EndpointTestService] 已清空探测缓存: {MaskUrl(string.Join(", ", matchingKeys))}");
            }
            return;
        }

        var epPrefix = endpoint!.TrimEnd('/').ToLowerInvariant() + "::";
        var providerPrefix = string.IsNullOrWhiteSpace(providerId)
            ? null
            : providerId.Trim().ToLowerInvariant() + "::" + epPrefix;
        var endpointSegment = "::" + epPrefix;
        var toRemove = _probeResultCache.Keys
            .Where(k => providerPrefix != null
                ? k.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)
                : k.StartsWith(epPrefix, StringComparison.OrdinalIgnoreCase)
                  || k.Contains(endpointSegment, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var k in toRemove)
            _probeResultCache.TryRemove(k, out _);
        if (toRemove.Count > 0)
        {
            SaveProbeCacheAsync();
            TM.App.Log($"[EndpointTestService] 已清空探测缓存: endpoint={MaskUrl(endpoint)}, count={toRemove.Count}");
        }
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

        System.Diagnostics.Debug.WriteLine($"[EndpointTestService] {key}: {ex.Message}");
    }

    private static readonly string[] ChatModelKeywords = { "gpt", "claude", "gemini", "qwen", "deepseek", "glm", "llama", "mistral", "mixtral", "grok", "chat", "turbo", "instruct" };

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

            candidates.Add(withoutVersion + "/compatible-mode/v1");

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
            candidates.Add(trimmed + "/compatible-mode/v1");
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
                        var m = new ModelInfo { Id = id, Name = id };

                        if (item.TryGetProperty("name", out var nameEl))
                        {
                            var n = nameEl.GetString();
                            if (!string.IsNullOrWhiteSpace(n)) m.Name = n;
                        }

                        if (item.TryGetProperty("description", out var descEl))
                            m.Description = descEl.GetString();

                        if (item.TryGetProperty("max_tokens", out var maxTokEl) && maxTokEl.ValueKind == JsonValueKind.Number)
                            m.MaxTokens = maxTokEl.GetInt32();

                        if (item.TryGetProperty("context_length", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.Number)
                            m.ContextLength = ctxEl.GetInt32();
                        else if (item.TryGetProperty("context_window", out var ctxWinEl) && ctxWinEl.ValueKind == JsonValueKind.Number)
                            m.ContextLength = ctxWinEl.GetInt32();
                        else if (item.TryGetProperty("max_context_length", out var maxCtxEl) && maxCtxEl.ValueKind == JsonValueKind.Number)
                            m.ContextLength = maxCtxEl.GetInt32();

                        if (m.MaxTokens <= 0 && item.TryGetProperty("top_provider", out var topProvider))
                        {
                            if (topProvider.TryGetProperty("max_completion_tokens", out var mct) && mct.ValueKind == JsonValueKind.Number)
                                m.MaxTokens = mct.GetInt32();
                            if (m.ContextLength <= 0 && topProvider.TryGetProperty("context_length", out var tpCtx) && tpCtx.ValueKind == JsonValueKind.Number)
                                m.ContextLength = tpCtx.GetInt32();
                        }

                        if (m.MaxTokens <= 0 && item.TryGetProperty("max_completion_tokens", out var mctTop) && mctTop.ValueKind == JsonValueKind.Number)
                            m.MaxTokens = mctTop.GetInt32();
                        if (m.MaxTokens <= 0 && item.TryGetProperty("max_output_tokens", out var motTop) && motTop.ValueKind == JsonValueKind.Number)
                            m.MaxTokens = motTop.GetInt32();

                        if (item.TryGetProperty("architecture", out var archEl))
                        {
                            if (archEl.TryGetProperty("input_modalities", out var inputMods)
                                && inputMods.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var mod in inputMods.EnumerateArray())
                                {
                                    if (string.Equals(mod.GetString(), "image", StringComparison.OrdinalIgnoreCase))
                                        m.SupportsVision = true;
                                }
                            }
                            if (archEl.TryGetProperty("output_modalities", out var outputMods)
                                && outputMods.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var mod in outputMods.EnumerateArray())
                                {
                                    if (string.Equals(mod.GetString(), "image", StringComparison.OrdinalIgnoreCase))
                                        m.SupportsImageGeneration = true;
                                }
                            }
                        }

                        if (item.TryGetProperty("pricing", out var pricingEl))
                        {
                            if (pricingEl.TryGetProperty("internal_reasoning", out var irEl))
                            {
                                var irVal = irEl.GetString();
                                if (!string.IsNullOrEmpty(irVal) && irVal != "0")
                                {
                                    m.SupportsThinking = true;
                                }
                            }
                        }

                        if (item.TryGetProperty("supported_parameters", out var spEl) && spEl.ValueKind == JsonValueKind.Array)
                        {
                            m.CapabilitiesDetected = true;
                            foreach (var sp in spEl.EnumerateArray())
                            {
                                var spStr = sp.GetString()?.ToLowerInvariant() ?? string.Empty;
                                if (spStr == "reasoning_effort" || spStr == "reasoning"
                                    || spStr == "include_reasoning" || spStr == "reasoning_max_tokens"
                                    || spStr == "effort")
                                    m.SupportsReasoningEffort = true;
                                if (spStr == "thinking" || spStr == "thinking_config" || spStr == "enable_thinking"
                                    || spStr == "thinkingconfig" || spStr == "thinking_budget"
                                    || spStr == "extended_thinking")
                                    m.SupportsThinking = true;
                                if (spStr == "tools" || spStr == "tool_choice"
                                    || spStr == "functions" || spStr == "function_call")
                                    m.SupportsTools = true;
                                if (spStr == "stream" || spStr == "stream_options" || spStr == "streaming")
                                    m.SupportsStreaming = true;
                            }
                        }

                        foreach (var levelsKey in new[] { "reasoning_effort_levels", "supported_effort_levels", "effort_levels" })
                        {
                            if (!item.TryGetProperty(levelsKey, out var levelsEl)
                                || levelsEl.ValueKind != JsonValueKind.Array) continue;

                            var parsed = new List<string>();
                            foreach (var lv in levelsEl.EnumerateArray())
                            {
                                var lvStr = lv.GetString()?.Trim().ToLowerInvariant();
                                if (string.IsNullOrEmpty(lvStr)) continue;
                                if (lvStr == "none" || lvStr == "minimal" || lvStr == "low"
                                    || lvStr == "medium" || lvStr == "high" || lvStr == "xhigh"
                                    || lvStr == "max")
                                {
                                    if (!parsed.Contains(lvStr)) parsed.Add(lvStr);
                                }
                            }
                            if (parsed.Count > 0)
                            {
                                m.SupportedEffortLevels = parsed;
                                m.SupportsReasoningEffort = true;
                                m.CapabilitiesDetected = true;
                                break;
                            }
                        }

                        if (m.ContextLength >= 1_000_000)
                            m.SupportsLongContext = true;

                        models.Add(m);
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
        var input = (apiEndpoint ?? string.Empty) + "|" + (apiKey ?? string.Empty);
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
            result.ErrorType = EndpointErrorType.Unknown;
            return result;
        }

        var tasks = validCandidates
            .Select(u => TestSingleModelsEndpointAsync(u, apiKey, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var successResults = results.Where(r => r.success).ToList();

        if (successResults.Count == 0)
        {
            var bestError = results.OrderBy(r => GetErrorTypeSpecificity(r.errorType)).First();
            result.ErrorMessage = bestError.error ?? "所有端点测试失败";
            result.ErrorType = bestError.errorType;
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
        return t.StartsWith('<')
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

    private async Task<(string endpoint, bool success, List<ModelInfo> models, string? error, EndpointErrorType errorType)> TestSingleModelsEndpointAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/models";
        var models = new List<ModelInfo>();

        async Task<(bool ok, string content, string? errMsg, EndpointErrorType errorType)> DoRequest(HttpClient client)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyStandardHeaders(req, apiKey);
            using var resp = await client.SendAsync(req, cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                if (IsHtmlResponse(body))
                    return (false, body, "端点返回 HTML 页面（疑似 WAF/Cloudflare 拦截），请检查 URL、密钥或代理设置", EndpointErrorType.WafBlock);

                var (errType, msg) = ClassifyHttpError(resp.StatusCode, resp.ReasonPhrase, body);
                return (false, body, msg, errType);
            }

            return (true, body, null, EndpointErrorType.None);
        }

        try
        {
            using var directClient = CreateDirectClient();
            var (ok1, body1, err1, type1) = await DoRequest(directClient);

            if (ok1 && !IsHtmlResponse(body1))
            {
                if (!ValidateModelsResponse(body1, out models))
                    return (baseUrl, false, models, "响应格式不符合 OpenAI 规范", EndpointErrorType.FormatError);
                return (baseUrl, true, models, null, EndpointErrorType.None);
            }

            if (ok1 && IsHtmlResponse(body1))
                TM.App.Log($"[EndpointTest] 直连返回 HTML，尝试代理: {MaskUrl(url)} | HTML头200字: {MaskUrl(body1[..Math.Min(200, body1.Length)])}");

            using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
            var (ok2, body2, err2, type2) = await DoRequest(proxyClient);

            if (!ok2)
                return (baseUrl, false, models, err2 ?? err1, type2 != EndpointErrorType.None ? type2 : type1);

            if (IsHtmlResponse(body2))
            {
                TM.App.Log($"[EndpointTest] 代理也返回 HTML: {MaskUrl(url)}");
                return (baseUrl, false, models, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", EndpointErrorType.WafBlock);
            }

            if (!ValidateModelsResponse(body2, out models))
                return (baseUrl, false, models, "响应格式不符合 OpenAI 规范", EndpointErrorType.FormatError);

            TM.App.Log($"[EndpointTest] 代理成功（直连回退）: {MaskUrl(url)}");
            return (baseUrl, true, models, null, EndpointErrorType.None);
        }
        catch (TaskCanceledException)
        {
            return (baseUrl, false, models, "连接超时，请检查网络或端点地址", EndpointErrorType.NetworkError);
        }
        catch (HttpRequestException ex)
        {
            if (ex.Message.Contains("屏蔽"))
                return (baseUrl, false, models, $"代理规则已屏蔽此域名，请检查代理规则设置", EndpointErrorType.ProxyOrSslError);

            TM.App.Log($"[EndpointTest] 直连异常，尝试代理: {MaskUrl(url)} | {MaskSensitiveText(ex.Message)}");
            try
            {
                using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
                var (okD, bodyD, errD, typeD) = await DoRequest(proxyClient);
                if (!okD) return (baseUrl, false, models, errD, typeD);
                if (IsHtmlResponse(bodyD))
                {
                    TM.App.Log($"[EndpointTest] 代理也返回 HTML: {MaskUrl(url)}");
                    return (baseUrl, false, models, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", EndpointErrorType.WafBlock);
                }
                if (!ValidateModelsResponse(bodyD, out models))
                    return (baseUrl, false, models, "响应格式不符合 OpenAI 规范", EndpointErrorType.FormatError);
                TM.App.Log($"[EndpointTest] 代理成功（直连回退）: {MaskUrl(url)}");
                return (baseUrl, true, models, null, EndpointErrorType.None);
            }
            catch
            {
                if (LooksLikeProxyOrSslError(ex))
                    return (baseUrl, false, models, $"代理或 SSL/证书错误：{MaskSensitiveText(ex.Message)}", EndpointErrorType.ProxyOrSslError);
                return (baseUrl, false, models, $"网络错误: {MaskSensitiveText(ex.Message)}", EndpointErrorType.NetworkError);
            }
        }
        catch (Exception ex)
        {
            return (baseUrl, false, models, $"测试失败: {MaskSensitiveText(ex.Message)}", EndpointErrorType.Unknown);
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
            result.ErrorType = EndpointErrorType.Unknown;
            return result;
        }

        var validCandidates = candidateBaseUrls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (validCandidates.Count == 0)
        {
            result.ErrorMessage = "端点地址为空";
            result.ErrorType = EndpointErrorType.Unknown;
            return result;
        }

        foreach (var endpoint in validCandidates)
        {
            var (success, error, rawBody, errorType) = await TestSingleChatEndpointAsync(endpoint, apiKey, testModelId, cancellationToken);
            if (success)
            {
                result.Success = true;
                result.SuccessfulEndpoint = endpoint;
                result.ErrorMessage = null;
                return result;
            }

            result.ErrorMessage = error;
            result.RawErrorBody = rawBody;
            result.ErrorType = errorType;
        }

        return result;
    }

    private async Task<(bool success, string? error, string? rawBody, EndpointErrorType errorType)> TestSingleChatEndpointAsync(
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

        async Task<(bool ok, string body, string? errMsg, EndpointErrorType errorType)> DoRequest(HttpClient client)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            ApplyStandardHeaders(req, apiKey);
            using var resp = await client.SendAsync(req, cancellationToken);

            var ec = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                if (IsHtmlResponse(ec))
                    return (false, ec, "端点返回 HTML 页面（疑似 WAF/Cloudflare 拦截），请检查 URL、密钥或代理设置", EndpointErrorType.WafBlock);

                var (errType, msg) = ClassifyHttpError(resp.StatusCode, resp.ReasonPhrase, ec);
                return (false, ec, msg, errType);
            }

            return (true, ec, null, EndpointErrorType.None);
        }

        try
        {
            using var directClient = CreateDirectClient();
            var (ok1, body1, err1, type1) = await DoRequest(directClient);

            bool directHtml = type1 == EndpointErrorType.WafBlock || (ok1 && IsHtmlResponse(body1));

            if (!directHtml)
            {
                if (!ok1) return (false, err1, body1, type1);
                if (!ValidateChatResponse(body1)) return (false, "响应格式不符合 OpenAI 规范", body1, EndpointErrorType.FormatError);
                return (true, null, null, EndpointErrorType.None);
            }

            TM.App.Log($"[EndpointTest] 直连返回 HTML，尝试代理 Chat: {MaskUrl(url)} | HTML头200字: {MaskUrl(body1[..Math.Min(200, body1.Length)])}");

            using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
            var (ok2, body2, err2, type2) = await DoRequest(proxyClient);

            if (!ok2)
            {
                return type2 == EndpointErrorType.WafBlock
                    ? (false, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", body2, EndpointErrorType.WafBlock)
                    : (false, err2, body2, type2);
            }

            if (IsHtmlResponse(body2))
                return (false, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", body2, EndpointErrorType.WafBlock);

            if (!ValidateChatResponse(body2))
                return (false, "响应格式不符合 OpenAI 规范", body2, EndpointErrorType.FormatError);

            TM.App.Log($"[EndpointTest] 代理 Chat 成功（直连回退）: {MaskUrl(url)}");
            return (true, null, null, EndpointErrorType.None);
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请检查网络或端点地址", null, EndpointErrorType.NetworkError);
        }
        catch (HttpRequestException ex)
        {
            if (ex.Message.Contains("屏蔽"))
                return (false, $"代理规则已屏蔽此域名，请检查代理规则设置", null, EndpointErrorType.ProxyOrSslError);

            TM.App.Log($"[EndpointTest] 直连异常，尝试代理 Chat: {MaskUrl(url)} | {MaskSensitiveText(ex.Message)}");
            try
            {
                using var proxyClient = _httpClientFactory(TimeSpan.FromSeconds(30));
                var (okD, bodyD, errD, typeD) = await DoRequest(proxyClient);
                if (!okD)
                    return typeD == EndpointErrorType.WafBlock
                        ? (false, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", bodyD, EndpointErrorType.WafBlock)
                        : (false, errD, bodyD, typeD);
                if (IsHtmlResponse(bodyD))
                    return (false, "端点返回 HTML 页面（直连和代理均被拦截），请检查 URL、密钥或代理设置", bodyD, EndpointErrorType.WafBlock);
                if (!ValidateChatResponse(bodyD))
                    return (false, "响应格式不符合 OpenAI 规范", bodyD, EndpointErrorType.FormatError);
                TM.App.Log($"[EndpointTest] 代理 Chat 成功（直连回退）: {MaskUrl(url)}");
                return (true, null, null, EndpointErrorType.None);
            }
            catch
            {
                if (LooksLikeProxyOrSslError(ex))
                    return (false, $"代理或 SSL/证书错误：{MaskSensitiveText(ex.Message)}", null, EndpointErrorType.ProxyOrSslError);
                return (false, $"网络错误: {MaskSensitiveText(ex.Message)}", null, EndpointErrorType.NetworkError);
            }
        }
        catch (Exception ex)
        {
            return (false, $"测试失败: {MaskSensitiveText(ex.Message)}", null, EndpointErrorType.Unknown);
        }
    }

    private static string? ParseErrorMessage(string errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(errorContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                    return errorElement.GetString();

                if (errorElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "message", "detail", "description" })
                    {
                        if (errorElement.TryGetProperty(key, out var el)
                            && el.ValueKind == JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
            }

            if (root.TryGetProperty("error_msg", out var baiduMsg)
                && baiduMsg.ValueKind == JsonValueKind.String)
            {
                var s = baiduMsg.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }

            if (root.TryGetProperty("Response", out var tencentResp)
                && tencentResp.ValueKind == JsonValueKind.Object
                && tencentResp.TryGetProperty("Error", out var tencentErr)
                && tencentErr.ValueKind == JsonValueKind.Object
                && tencentErr.TryGetProperty("Message", out var tencentMsg)
                && tencentMsg.ValueKind == JsonValueKind.String)
            {
                var s = tencentMsg.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }

            foreach (var key in new[] { "message", "detail", "msg" })
            {
                if (root.TryGetProperty(key, out var el)
                    && el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOnce(nameof(ParseErrorMessage), ex);
        }

        return null;
    }

    private static EndpointErrorType MapStatusToErrorType(System.Net.HttpStatusCode status) =>
        status switch
        {
            System.Net.HttpStatusCode.BadRequest => EndpointErrorType.BadRequest,
            System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden => EndpointErrorType.AuthError,
            System.Net.HttpStatusCode.PaymentRequired => EndpointErrorType.PaymentRequired,
            System.Net.HttpStatusCode.NotFound => EndpointErrorType.NotFound,
            System.Net.HttpStatusCode.MethodNotAllowed => EndpointErrorType.MethodNotAllowed,
            System.Net.HttpStatusCode.RequestTimeout => EndpointErrorType.RequestTimeout,
            System.Net.HttpStatusCode.Conflict => EndpointErrorType.Conflict,
            System.Net.HttpStatusCode.RequestEntityTooLarge => EndpointErrorType.PayloadTooLarge,
            System.Net.HttpStatusCode.UnprocessableEntity => EndpointErrorType.Unprocessable,
            System.Net.HttpStatusCode.TooManyRequests => EndpointErrorType.RateLimit,
            System.Net.HttpStatusCode.InternalServerError => EndpointErrorType.ServerError,
            System.Net.HttpStatusCode.NotImplemented => EndpointErrorType.NotImplemented,
            System.Net.HttpStatusCode.BadGateway => EndpointErrorType.BadGateway,
            System.Net.HttpStatusCode.ServiceUnavailable => EndpointErrorType.ServiceUnavailable,
            System.Net.HttpStatusCode.GatewayTimeout => EndpointErrorType.GatewayTimeout,
            _ => EndpointErrorType.Unknown
        };

    private static string GetDefaultMessageForStatus(System.Net.HttpStatusCode status, string? reasonPhrase) =>
        status switch
        {
            System.Net.HttpStatusCode.BadRequest => "请求参数错误（400）",
            System.Net.HttpStatusCode.Unauthorized => "API 密钥无效（401 Unauthorized）",
            System.Net.HttpStatusCode.Forbidden => "API 密钥无权限或被禁止访问（403 Forbidden）",
            System.Net.HttpStatusCode.PaymentRequired => "账户欠费或余额不足（402 Payment Required）",
            System.Net.HttpStatusCode.NotFound => "端点路径或模型不存在（404 Not Found）",
            System.Net.HttpStatusCode.MethodNotAllowed => "请求方法不被端点允许（405 Method Not Allowed）",
            System.Net.HttpStatusCode.RequestTimeout => "服务端等待请求超时（408 Request Timeout）",
            System.Net.HttpStatusCode.Conflict => "请求冲突（409 Conflict）",
            System.Net.HttpStatusCode.RequestEntityTooLarge => "请求体过大（413 Payload Too Large）",
            System.Net.HttpStatusCode.UnprocessableEntity => "请求语义错误或模型不可用（422 Unprocessable Entity）",
            System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁或配额已用尽（429 Too Many Requests）",
            System.Net.HttpStatusCode.InternalServerError => "服务端内部错误（500 Internal Server Error）",
            System.Net.HttpStatusCode.NotImplemented => "服务端未实现该接口（501 Not Implemented）",
            System.Net.HttpStatusCode.BadGateway => "上游网关错误（502 Bad Gateway）",
            System.Net.HttpStatusCode.ServiceUnavailable => "服务暂时不可用（503 Service Unavailable）",
            System.Net.HttpStatusCode.GatewayTimeout => "上游网关超时（504 Gateway Timeout）",
            _ => $"HTTP {(int)status}: {reasonPhrase ?? status.ToString()}"
        };

    private static readonly string[] QuotaKeywords =
    {
        "quota exceeded", "quota_exceeded", "exceeded your current quota",
        "insufficient_quota", "insufficient quota", "out of quota",
        "billing", "credit exhausted", "no credits", "balance",
        "daily limit", "monthly limit", "usage limit", "plan limit",
        "配额", "额度", "用尽", "余额不足", "超限", "超额", "已达上限"
    };

    private static bool LooksLikeQuotaExceeded(string? body, string? parsedMessage)
    {
        var haystack = ((body ?? string.Empty) + "\n" + (parsedMessage ?? string.Empty));
        if (string.IsNullOrWhiteSpace(haystack)) return false;
        foreach (var kw in QuotaKeywords)
        {
            if (haystack.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (EndpointErrorType errorType, string message) ClassifyHttpError(
        System.Net.HttpStatusCode status,
        string? reasonPhrase,
        string? body)
    {
        var parsed = ParseErrorMessage(body ?? string.Empty);
        var type = MapStatusToErrorType(status);

        type = RefineByMessage(type, body, parsed);

        if ((type == EndpointErrorType.RateLimit
             || type == EndpointErrorType.AuthError
             || type == EndpointErrorType.PaymentRequired
             || type == EndpointErrorType.Unknown)
            && LooksLikeQuotaExceeded(body, parsed))
        {
            type = EndpointErrorType.QuotaExceeded;
        }

        var fallback = GetDefaultMessageForStatus(status, reasonPhrase);
        var msg = !string.IsNullOrWhiteSpace(parsed)
            ? $"{fallback}\n服务端原文：{parsed}"
            : fallback;

        return (type, msg);
    }

    private static EndpointErrorType RefineByMessage(EndpointErrorType original, string? body, string? parsed)
    {
        if (original != EndpointErrorType.Unknown
            && original != EndpointErrorType.ServerError
            && original != EndpointErrorType.BadGateway
            && original != EndpointErrorType.ServiceUnavailable)
            return original;

        var haystack = ((body ?? string.Empty) + "\n" + (parsed ?? string.Empty));
        if (string.IsNullOrWhiteSpace(haystack)) return original;

        if (ContainsAny(haystack, "invalid_api_key", "invalid api key",
                "authentication", "unauthorized", "forbidden",
                "密钥无效", "未授权", "无权限"))
            return EndpointErrorType.AuthError;

        if (ContainsAny(haystack, "model_not_found", "model not found",
                "model does not exist", "no such model", "未找到模型", "模型不存在"))
            return EndpointErrorType.NotFound;

        return original;
    }

    private static readonly string[] ProxyOrSslKeywords =
    {
        "ssl", "tls", "handshake",
        "certificate", "cert_", "x509",
        "self-signed", "self signed",
        "unable_to_verify_leaf_signature",
        "unable to get local issuer",
        "proxy", "socks",
        "代理", "证书"
    };

    private static bool LooksLikeProxyOrSslError(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            var msg = e.Message;
            if (string.IsNullOrWhiteSpace(msg)) continue;
            foreach (var kw in ProxyOrSslKeywords)
            {
                if (msg.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool ContainsAny(string haystack, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (haystack.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int GetErrorTypeSpecificity(EndpointErrorType type) => type switch
    {
        EndpointErrorType.AuthError => 0,
        EndpointErrorType.PaymentRequired => 1,
        EndpointErrorType.QuotaExceeded => 2,
        EndpointErrorType.Unprocessable => 3,
        EndpointErrorType.BadRequest => 4,
        EndpointErrorType.Conflict => 5,
        EndpointErrorType.PayloadTooLarge => 6,
        EndpointErrorType.MethodNotAllowed => 7,
        EndpointErrorType.NotFound => 8,
        EndpointErrorType.WafBlock => 9,
        EndpointErrorType.ProxyOrSslError => 10,
        EndpointErrorType.RateLimit => 11,
        EndpointErrorType.RequestTimeout => 12,
        EndpointErrorType.ServiceUnavailable => 13,
        EndpointErrorType.BadGateway => 14,
        EndpointErrorType.GatewayTimeout => 15,
        EndpointErrorType.ServerError => 16,
        EndpointErrorType.NotImplemented => 17,
        EndpointErrorType.FormatError => 18,
        EndpointErrorType.NetworkError => 19,
        _ => 20
    };

    public static string GetErrorTypeDisplayName(EndpointErrorType type) => type switch
    {
        EndpointErrorType.None => "成功",
        EndpointErrorType.BadRequest => "请求参数错误（400）",
        EndpointErrorType.AuthError => "API 密钥无效或无权限访问（401/403）",
        EndpointErrorType.PaymentRequired => "账户欠费或余额不足（402）",
        EndpointErrorType.NotFound => "端点路径或模型不存在（404）",
        EndpointErrorType.MethodNotAllowed => "请求方法不被端点允许（405）",
        EndpointErrorType.RequestTimeout => "服务端等待请求超时（408）",
        EndpointErrorType.Conflict => "请求冲突（409）",
        EndpointErrorType.PayloadTooLarge => "请求体过大（413）",
        EndpointErrorType.Unprocessable => "请求语义错误或模型不可用（422）",
        EndpointErrorType.RateLimit => "请求过于频繁，请稍后再试（429）",
        EndpointErrorType.QuotaExceeded => "配额已用尽（429/403 + 配额超限）",
        EndpointErrorType.ServerError => "服务端内部错误（500）",
        EndpointErrorType.NotImplemented => "服务端未实现该接口（501）",
        EndpointErrorType.BadGateway => "上游网关错误（502）",
        EndpointErrorType.ServiceUnavailable => "服务暂时不可用（503）",
        EndpointErrorType.GatewayTimeout => "上游网关超时（504）",
        EndpointErrorType.WafBlock => "端点被 WAF/Cloudflare 拦截",
        EndpointErrorType.ProxyOrSslError => "代理或 SSL/证书错误，请检查代理或证书设置",
        EndpointErrorType.NetworkError => "网络连接失败，请检查网络或端点地址",
        EndpointErrorType.FormatError => "端点响应格式异常，可能不是兼容的 API",
        _ => "请检查端点地址和密钥是否正确"
    };

    public async Task<ModelCapabilityResult> ProbeModelCapabilitiesAsync(
        string chatBaseEndpoint,
        string apiKey,
        string modelId,
        CancellationToken cancellationToken = default,
        bool? knownSupportsReasoningEffort = null,
        bool? knownSupportsThinking = null,
        string? providerId = null,
        bool? knownSupportsLongContext = null)
    {
        var url = $"{chatBaseEndpoint.TrimEnd('/')}/chat/completions";
        var isOpenRouter = chatBaseEndpoint.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);
        var safeModelId = modelId ?? string.Empty;
        var hasKnownCapabilityMetadata = knownSupportsReasoningEffort.HasValue
            || knownSupportsThinking.HasValue
            || knownSupportsLongContext.HasValue;

        var cacheKey = BuildProbeCacheKey(providerId, chatBaseEndpoint, modelId);
        if (!string.IsNullOrEmpty(cacheKey)
            && _probeResultCache.TryGetValue(cacheKey, out var cached)
            && cached.Result != null
            && DateTime.UtcNow.Ticks - cached.Ticks < _probeCacheTtl.Ticks)
        {
            TM.App.Log($"[EndpointTestService] 探测缓存命中，跳过 LLM 请求: {MaskUrl(cacheKey)}");
            return cached.Result;
        }

        var modelLower = (modelId ?? string.Empty).ToLowerInvariant();
        var providerLower = (providerId ?? string.Empty).ToLowerInvariant();
        var requestMode = ModelFamilyClassifier.GetRequestParameterMode(modelId, providerId, chatBaseEndpoint);
        var isNeitherParamFamily = ModelFamilyClassifier.IsNeitherParamModel(modelId, providerId);

        bool shouldProbeEffort, shouldProbeEnableThinking, shouldProbeClaudeThinking;
        string familyShortcutReason;
        switch (requestMode)
        {
            case RequestParameterMode.None:
                if (isNeitherParamFamily)
                {
                    shouldProbeEffort = false;
                    shouldProbeEnableThinking = false;
                    shouldProbeClaudeThinking = false;
                    familyShortcutReason = "IsNeitherParamModel 命中（普通对话，全跳过）";
                }
                else
                {
                    shouldProbeEffort = true;
                    shouldProbeEnableThinking = !isOpenRouter;
                    shouldProbeClaudeThinking = !isOpenRouter;
                    familyShortcutReason = "mode=None 且非黑名单（自动思考型 / 未识别，兜底全发）";
                }
                break;

            case RequestParameterMode.OpenAIReasoningEffort:
            case RequestParameterMode.OpenRouterReasoning:
                shouldProbeEffort = true;
                shouldProbeEnableThinking = false;
                shouldProbeClaudeThinking = false;
                familyShortcutReason = $"{requestMode} 协议族（仅 ① reasoning_effort/reasoning）";
                break;

            case RequestParameterMode.AnthropicThinking:
                shouldProbeEffort = false;
                shouldProbeEnableThinking = false;
                shouldProbeClaudeThinking = !isOpenRouter;
                familyShortcutReason = "AnthropicThinking 协议族（仅 ③ claude thinking）";
                break;

            case RequestParameterMode.QwenEnableThinking:
            case RequestParameterMode.DoubaoEnableThinking:
            case RequestParameterMode.EnableThinkingFlag:
            case RequestParameterMode.DeepSeekV4Thinking:
            case RequestParameterMode.GoogleThinkingConfig:
                shouldProbeEffort = false;
                shouldProbeEnableThinking = !isOpenRouter;
                shouldProbeClaudeThinking = false;
                familyShortcutReason = $"{requestMode} 协议族（仅 ② 对应 thinking 协议）";
                break;

            default:
                shouldProbeEffort = true;
                shouldProbeEnableThinking = !isOpenRouter;
                shouldProbeClaudeThinking = !isOpenRouter;
                familyShortcutReason = $"未知 RequestParameterMode={requestMode}（兜底全发）";
                break;
        }
        TM.App.Log($"[EndpointTestService] 家族短路决策 model={MaskUrl(modelId)}: {familyShortcutReason} (Effort={shouldProbeEffort} Thinking={shouldProbeEnableThinking} Claude={shouldProbeClaudeThinking})");

        int probeRequestCount = 0;

        Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> effortTask;
        if (knownSupportsReasoningEffort.HasValue)
        {
            effortTask = Task.FromResult((knownSupportsReasoningEffort.Value, false, (string?)null));
            TM.App.Log($"[EndpointTestService] /models 已提示 reasoning_effort={knownSupportsReasoningEffort.Value}，跳过 Chat 探测");
        }
        else if (!shouldProbeEffort)
        {
            effortTask = Task.FromResult<(bool, bool, string?)>((false, false, null));
        }
        else
        {
            probeRequestCount++;
            effortTask = ProbeSingleParamAsync(url, apiKey, safeModelId,
                new Dictionary<string, object> { [isOpenRouter ? "reasoning" : "reasoning_effort"] = "low" },
                new[] { "reasoning_effort", "reasoning" },
                cancellationToken);
        }

        Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> thinkingTask;
        if (knownSupportsThinking.HasValue)
        {
            thinkingTask = Task.FromResult((knownSupportsThinking.Value, false, (string?)null));
            TM.App.Log($"[EndpointTestService] /models 已提示 thinking={knownSupportsThinking.Value}，跳过 thinking 探测");
        }
        else if (!shouldProbeEnableThinking)
        {
            thinkingTask = Task.FromResult<(bool, bool, string?)>((false, false, null));
        }
        else
        {
            probeRequestCount++;
            var (thinkingBody, thinkingParamKeys, thinkingProtocolDesc) = BuildThinkingProbePayload(requestMode);
            TM.App.Log($"[EndpointTestService] ② thinking 探测协议: {thinkingProtocolDesc} (model={MaskUrl(modelId)})");
            thinkingTask = ProbeSingleParamAsync(url, apiKey, safeModelId,
                thinkingBody, thinkingParamKeys, cancellationToken);
        }

        Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> claudeThinkingTask;
        if (knownSupportsThinking.HasValue)
        {
            claudeThinkingTask = Task.FromResult<(bool, bool, string?)>((false, false, null));
        }
        else if (!shouldProbeClaudeThinking)
        {
            claudeThinkingTask = Task.FromResult<(bool, bool, string?)>((false, false, null));
        }
        else
        {
            probeRequestCount++;
            claudeThinkingTask = ProbeSingleParamAsync(url, apiKey, safeModelId,
                new Dictionary<string, object>
                {
                    ["thinking"] = new Dictionary<string, object>
                    { ["type"] = "enabled", ["budget_tokens"] = 1024 }
                },
                new[] { "thinking", "budget_tokens" },
                cancellationToken);
        }

        Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> longContextTask;
        bool modelIdHasLongSuffix = !string.IsNullOrEmpty(modelId)
            && (modelId.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase)
                || modelId.EndsWith(":extended", StringComparison.OrdinalIgnoreCase));
        bool isQwenLongContextModel = !string.IsNullOrEmpty(modelLower)
            && modelLower.Contains("qwen")
            && (modelLower.Contains("turbo") || modelLower.Contains("long")
                || modelLower.Contains("plus") || modelLower.Contains("flash"));
        if (knownSupportsLongContext.HasValue)
        {
            longContextTask = Task.FromResult<(bool, bool, string?)>((knownSupportsLongContext.Value, false, null));
            TM.App.Log($"[EndpointTestService] /models 已提示 long_context={knownSupportsLongContext.Value}，跳过后缀探测");
        }
        else if (modelIdHasLongSuffix)
        {
            longContextTask = Task.FromResult<(bool, bool, string?)>((true, false, null));
            TM.App.Log($"[EndpointTestService] modelId 末尾已含 1M 后缀，默认 SupportsLongContext=true、跳过后缀探测");
        }
        else if (isQwenLongContextModel)
        {
            probeRequestCount++;
            longContextTask = ProbeSingleParamAsync(url, apiKey, safeModelId,
                new Dictionary<string, object> { ["max_input_tokens"] = 1_000_000 },
                new[] { "max_input_tokens" },
                cancellationToken);
            TM.App.Log($"[EndpointTestService] Qwen 1M 家族识别，走 max_input_tokens 探测: model={MaskUrl(modelId)}");
        }
        else
        {
            probeRequestCount++;
            longContextTask = ProbeLongContextSuffixAsync(url, apiKey, safeModelId, isOpenRouter, cancellationToken);
        }

        await Task.WhenAll(effortTask, thinkingTask, claudeThinkingTask, longContextTask);
        var effortRes = effortTask.Result;
        var thinkingRes = thinkingTask.Result;
        var claudeRes = claudeThinkingTask.Result;
        var longCtxRes = longContextTask.Result;

        var supportsEffort = effortRes.Supported;
        var supportsThinking = thinkingRes.Supported;
        var supportsClaudeThinking = claudeRes.Supported;
        var supportsLongContext = longCtxRes.Supported;
        var reasoningEffortRejected = !knownSupportsReasoningEffort.HasValue
            && effortRes.ReachedServer
            && !effortRes.Supported;
        var enableThinkingRejected = !isOpenRouter
            && !knownSupportsThinking.HasValue
            && thinkingRes.ReachedServer
            && !thinkingRes.Supported;
        var claudeThinkingRejected = !isOpenRouter
            && !knownSupportsThinking.HasValue
            && claudeRes.ReachedServer
            && !claudeRes.Supported;
        var longContextRejected = !knownSupportsLongContext.HasValue
            && !modelIdHasLongSuffix
            && longCtxRes.ReachedServer
            && !longCtxRes.Supported;

        bool anyReached;
        if (probeRequestCount == 0)
        {
            anyReached = true;
            TM.App.Log("[EndpointTestService] 全部参数探测被 /models 元数据替代，仅依赖最小可用性 Chat 验证");
        }
        else
        {
            anyReached = effortRes.ReachedServer || thinkingRes.ReachedServer
                || claudeRes.ReachedServer || longCtxRes.ReachedServer;
        }

        string? failureReason = null;
        bool finalChatOk = false;
        bool? passthrough = null;

        if (!anyReached)
        {
            failureReason = effortRes.ErrorDetail
                          ?? thinkingRes.ErrorDetail
                          ?? claudeRes.ErrorDetail
                          ?? "参数探测未触达服务端，请检查端点地址、密钥或网络";
        }
        else
        {
            bool tryPassthroughFirst = !hasKnownCapabilityMetadata
                && (supportsEffort || supportsThinking || supportsClaudeThinking);

            if (tryPassthroughFirst)
            {
                var (passResult, chatOkFromPass, skipped) = await ProbeThinkingPassthroughAsync(
                    url, apiKey, safeModelId, isOpenRouter,
                    supportsEffort, supportsThinking, supportsClaudeThinking,
                    requestMode, cancellationToken);

                if (skipped)
                {
                    var (chatOkFb, chatErrFb) = await ProbeMinimalChatAsync(url, apiKey, safeModelId, cancellationToken);
                    finalChatOk = chatOkFb;
                    if (!chatOkFb) failureReason = chatErrFb;
                }
                else if (chatOkFromPass)
                {
                    finalChatOk = true;
                    passthrough = passResult;
                    TM.App.Log($"[EndpointTestService] Phase B 节省: ⑥ 200 OK 替代 ⑤ minimal chat (model={MaskUrl(safeModelId)})");
                }
                else
                {
                    var (chatOkFb, chatErrFb) = await ProbeMinimalChatAsync(url, apiKey, safeModelId, cancellationToken);
                    finalChatOk = chatOkFb;
                    if (!chatOkFb) failureReason = chatErrFb;
                    TM.App.Log($"[EndpointTestService] Phase B fallback: ⑥ HTTP 失败 → ⑤ minimal chat (model={MaskUrl(safeModelId)}, chatOk={chatOkFb})");
                }
            }
            else
            {
                var (chatOk, chatErr) = await ProbeMinimalChatAsync(url, apiKey, safeModelId, cancellationToken);
                finalChatOk = chatOk;
                if (!chatOk) failureReason = chatErr;
            }
        }

        bool finalSupportsEffort = supportsEffort;
        bool finalSupportsThinking = supportsThinking || supportsClaudeThinking;
        bool effortFromProbe = !knownSupportsReasoningEffort.HasValue;
        bool thinkingFromProbe = !knownSupportsThinking.HasValue && !isOpenRouter;
        if (effortFromProbe || thinkingFromProbe)
        {
            if (ModelFamilyClassifier.IsNeitherParamModel(modelId, providerId))
            {
                var beforeEffort = finalSupportsEffort;
                var beforeThinking = finalSupportsThinking;
                if (effortFromProbe) finalSupportsEffort = false;
                if (thinkingFromProbe) finalSupportsThinking = false;
                if (beforeEffort != finalSupportsEffort || beforeThinking != finalSupportsThinking)
                {
                    TM.App.Log($"[EndpointTestService] 普通对话模型清零(IsNeitherParamModel命中): model={MaskUrl(modelId)}, effort {beforeEffort}→false, thinking {beforeThinking}→false");
                }
            }
            else
            {
                var familyKnowsEffort = ModelFamilyClassifier.IsReasoningEffortModel(modelId, providerId);
                var familyKnowsThinking = ModelFamilyClassifier.IsThinkingModel(modelId, providerId);
                var postFilterFamilyKnows = familyKnowsEffort || familyKnowsThinking;
                if (postFilterFamilyKnows)
                {
                    var beforeEffort = finalSupportsEffort;
                    var beforeThinking = finalSupportsThinking;
                    if (effortFromProbe) finalSupportsEffort = finalSupportsEffort && familyKnowsEffort;
                    if (thinkingFromProbe) finalSupportsThinking = finalSupportsThinking && familyKnowsThinking;
                    if (beforeEffort != finalSupportsEffort || beforeThinking != finalSupportsThinking)
                    {
                        TM.App.Log($"[EndpointTestService] 探测∩家族过滤: model={MaskUrl(modelId)}, effort {beforeEffort}→{finalSupportsEffort}(家族={familyKnowsEffort}), thinking {beforeThinking}→{finalSupportsThinking}(家族={familyKnowsThinking})");
                    }
                }
            }
        }

        var result = new ModelCapabilityResult
        {
            SupportsReasoningEffort = finalSupportsEffort,
            SupportsThinking = finalSupportsThinking,
            SupportsLongContext = supportsLongContext,
            ThinkingPassthrough = passthrough,
            AnyRequestReachedServer = anyReached,
            FinalChatOk = finalChatOk,
            FailureReason = failureReason,
            ReasoningEffortRejected = reasoningEffortRejected,
            EnableThinkingRejected = enableThinkingRejected,
            ClaudeThinkingRejected = claudeThinkingRejected,
            LongContextRejected = longContextRejected
        };

        if (result.FinalChatOk && !string.IsNullOrEmpty(cacheKey))
        {
            _probeResultCache[cacheKey] = (DateTime.UtcNow.Ticks, result);
            SaveProbeCacheAsync();
            TM.App.Log($"[EndpointTestService] 探测结果已缓存 24h（含持久化）: {MaskUrl(cacheKey)}");
        }

        return result;
    }

    private static (Dictionary<string, object> Body, string[] ParamKeys, string ProtocolDesc)
        BuildThinkingProbePayload(RequestParameterMode mode)
    {
        switch (mode)
        {
            case RequestParameterMode.DeepSeekV4Thinking:
                return (
                    new Dictionary<string, object>
                    {
                        ["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" }
                    },
                    new[] { "thinking", "type" },
                    "DeepSeekV4Thinking: thinking={type:enabled}");

            case RequestParameterMode.GoogleThinkingConfig:
                return (
                    new Dictionary<string, object>
                    {
                        ["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = 100 }
                    },
                    new[] { "thinkingConfig", "thinkingBudget" },
                    "GoogleThinkingConfig: thinkingConfig={thinkingBudget:100}");

            case RequestParameterMode.DoubaoEnableThinking:
                return (
                    new Dictionary<string, object>
                    {
                        ["enable_thinking"] = true
                    },
                    new[] { "enable_thinking" },
                    "DoubaoEnableThinking: enable_thinking=true");

            case RequestParameterMode.EnableThinkingFlag:
            default:
                return (
                    new Dictionary<string, object>
                    {
                        ["enable_thinking"] = true,
                        ["thinking_budget"] = 100
                    },
                    new[] { "enable_thinking", "thinking_budget" },
                    "QwenEnableThinking/EnableThinkingFlag: enable_thinking=true + thinking_budget=100");
        }
    }

    private async Task<(bool? Passthrough, bool ChatOk, bool Skipped)> ProbeThinkingPassthroughAsync(
        string chatUrl,
        string apiKey,
        string modelId,
        bool isOpenRouter,
        bool supportsReasoningEffort,
        bool supportsThinking,
        bool supportsClaudeThinking,
        RequestParameterMode requestMode,
        CancellationToken cancellationToken)
    {
        if (isOpenRouter && !supportsReasoningEffort)
        {
            return (null, false, true);
        }

        Dictionary<string, object>? protocolBody = null;

        if (supportsReasoningEffort)
        {
            protocolBody = new Dictionary<string, object>
            {
                [isOpenRouter ? "reasoning" : "reasoning_effort"] = "high"
            };
        }
        else if (supportsThinking)
        {
            protocolBody = requestMode switch
            {
                RequestParameterMode.DeepSeekV4Thinking => new Dictionary<string, object>
                {
                    ["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" }
                },
                RequestParameterMode.GoogleThinkingConfig => new Dictionary<string, object>
                {
                    ["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = 64 }
                },
                RequestParameterMode.DoubaoEnableThinking => new Dictionary<string, object>
                {
                    ["enable_thinking"] = true
                },
                RequestParameterMode.QwenEnableThinking
                or RequestParameterMode.EnableThinkingFlag
                or _ => new Dictionary<string, object>
                {
                    ["enable_thinking"] = true,
                    ["thinking_budget"] = 64
                }
            };
        }
        else if (supportsClaudeThinking)
        {
            protocolBody = new Dictionary<string, object>
            {
                ["thinking"] = new Dictionary<string, object>
                { ["type"] = "enabled", ["budget_tokens"] = 1024 }
            };
        }
        else
        {
            return (null, false, true);
        }

        var (passthrough, chatOk) = await ProbePassthroughSingleProtocolAsync(chatUrl, apiKey, modelId, protocolBody, cancellationToken);
        return (passthrough, chatOk, false);
    }

    private async Task<(bool? Passthrough, bool ChatOk)> ProbePassthroughSingleProtocolAsync(
        string chatUrl,
        string apiKey,
        string modelId,
        Dictionary<string, object> protocolParams,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory(System.Threading.Timeout.InfiniteTimeSpan);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["messages"] = new[] { new { role = "user", content = "请一步步思考后回答：3 + 5 = ?" } },
                ["max_tokens"] = 64,
                ["stream"] = false
            };
            foreach (var kv in protocolParams) body[kv.Key] = kv.Value;

            var json = System.Text.Json.JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, chatUrl);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new System.Net.Http.StringContent(
                json, System.Text.Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return (null, false);

            var respBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(respBody))
                return (null, true);

            using var doc = System.Text.Json.JsonDocument.Parse(respBody);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != System.Text.Json.JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return (null, true);

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message))
                return (null, true);

            foreach (var field in new[] { "reasoning_content", "reasoning", "thinking", "thinking_content" })
            {
                if (message.TryGetProperty(field, out var fieldVal))
                {
                    if (fieldVal.ValueKind == System.Text.Json.JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(fieldVal.GetString()))
                        return (true, true);
                    if (fieldVal.ValueKind == System.Text.Json.JsonValueKind.Object
                        || fieldVal.ValueKind == System.Text.Json.JsonValueKind.Array)
                        return (true, true);
                }
            }

            if (doc.RootElement.TryGetProperty("usage", out var usage)
                && usage.ValueKind == System.Text.Json.JsonValueKind.Object
                && usage.TryGetProperty("completion_tokens_details", out var details)
                && details.ValueKind == System.Text.Json.JsonValueKind.Object
                && details.TryGetProperty("reasoning_tokens", out var rt)
                && rt.ValueKind == System.Text.Json.JsonValueKind.Number
                && rt.TryGetInt32(out var rtVal)
                && rtVal > 0)
            {
                return (true, true);
            }

            return (false, true);
        }
        catch (OperationCanceledException) { return (null, false); }
        catch (Exception ex)
        {
            DebugLogOnce($"{nameof(ProbePassthroughSingleProtocolAsync)}_{modelId}", ex);
            return (null, false);
        }
    }

    private async Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> ProbeLongContextSuffixAsync(
        string chatUrl,
        string apiKey,
        string modelId,
        bool isOpenRouter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(modelId))
            return (false, false, "modelId 为空");

        var probeModelId = modelId.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase)
            || modelId.EndsWith(":extended", StringComparison.OrdinalIgnoreCase)
            ? modelId
            : modelId + (isOpenRouter ? ":extended" : "[1m]");

        try
        {
            using var client = _httpClientFactory(System.Threading.Timeout.InfiniteTimeSpan);
            var body = new Dictionary<string, object>
            {
                ["model"] = probeModelId,
                ["messages"] = new[] { new { role = "user", content = "Hi" } },
                ["max_tokens"] = 1
            };
            var json = System.Text.Json.JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, chatUrl);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new System.Net.Http.StringContent(
                json, System.Text.Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
                return (true, true, null);

            var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);

            string[] rejectionMarkers =
            {
                "[1m]", "1m]", ":extended",
                "model not found", "model_not_found", "no such model",
                "invalid model", "unknown model", "unsupported model",
                "does not exist", "model does not support"
            };
            foreach (var marker in rejectionMarkers)
            {
                if (errorBody.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return (false, true, null);
            }

            return (false, false, BuildHttpFailureReasonZh((int)resp.StatusCode, errorBody));
        }
        catch (OperationCanceledException)
        {
            return (false, false, "请求取消或超时（可能是网络过慢或用户手动取消）");
        }
        catch (Exception ex)
        {
            return (false, false, BuildNetworkFailureReasonZh(ex));
        }
    }

    private async Task<(bool Supported, bool ReachedServer, string? ErrorDetail)> ProbeSingleParamAsync(
        string chatUrl,
        string apiKey,
        string modelId,
        Dictionary<string, object> extraParams,
        string[] paramKeywords,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory(System.Threading.Timeout.InfiniteTimeSpan);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["messages"] = new[] { new { role = "user", content = "Hi" } },
                ["max_tokens"] = 1
            };
            foreach (var kv in extraParams) body[kv.Key] = kv.Value;

            var json = System.Text.Json.JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, chatUrl);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new System.Net.Http.StringContent(
                json, System.Text.Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);

            if (resp.IsSuccessStatusCode)
                return (true, true, null);

            var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);

            foreach (var kw in paramKeywords)
            {
                if (errorBody.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return (false, true, null);
            }

            return (false, false, BuildHttpFailureReasonZh((int)resp.StatusCode, errorBody));
        }
        catch (OperationCanceledException)
        {
            return (false, false, "请求取消或超时（可能是网络过慢或用户手动取消）");
        }
        catch (Exception ex)
        {
            return (false, false, BuildNetworkFailureReasonZh(ex));
        }
    }

    private static string BuildHttpFailureReasonZh(int statusCode, string errorBody)
    {
        var category = statusCode switch
        {
            400 => "请求格式错误",
            401 => "密钥鉴权失败",
            402 => "配额/账户欠费",
            403 => "访问被拒绝",
            404 => "模型或端点不存在",
            408 => "请求超时",
            409 => "请求冲突",
            410 => "端点已废弃",
            413 => "请求体过大",
            422 => "请求参数不合法",
            429 => "触发限流",
            >= 500 and < 600 => "服务端错误",
            _ => "端点响应异常"
        };

        var trimmed = MaskSensitiveText(errorBody ?? string.Empty).Trim();
        if (trimmed.Length > 160) trimmed = trimmed.Substring(0, 160) + "…";
        if (string.IsNullOrWhiteSpace(trimmed))
            return $"{category}（HTTP {statusCode}）";
        return $"{category}（HTTP {statusCode}）：{trimmed}";
    }

    private static string BuildNetworkFailureReasonZh(Exception ex)
    {
        var category = ex switch
        {
            System.Net.Sockets.SocketException => "网络连接失败",
            System.Net.Http.HttpRequestException httpReq when httpReq.InnerException is System.Net.Sockets.SocketException
                => "网络连接失败",
            System.Net.Http.HttpRequestException => "HTTP 请求失败",
            System.Security.Authentication.AuthenticationException => "SSL/证书错误",
            System.Text.Json.JsonException => "响应解析失败（非标准 JSON）",
            TimeoutException => "请求超时",
            _ => "请求异常"
        };

        var msg = MaskSensitiveText(ex.Message).Trim();
        if (msg.Length > 140) msg = msg.Substring(0, 140) + "…";
        return string.IsNullOrEmpty(msg) ? category : $"{category}：{msg}";
    }

    private async Task<(bool Ok, string? ErrorDetail)> ProbeMinimalChatAsync(
        string chatUrl,
        string apiKey,
        string modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory(System.Threading.Timeout.InfiniteTimeSpan);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelId,
                ["messages"] = new[] { new { role = "user", content = "ping" } },
                ["max_tokens"] = 5,
                ["stream"] = false
            };

            var json = System.Text.Json.JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, chatUrl);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new System.Net.Http.StringContent(
                json, System.Text.Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);

            if (resp.IsSuccessStatusCode)
                return (true, null);

            var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            return (false, BuildHttpFailureReasonZh((int)resp.StatusCode, errorBody));
        }
        catch (OperationCanceledException)
        {
            return (false, "请求取消或超时（可能是网络过慢或用户手动取消）");
        }
        catch (Exception ex)
        {
            return (false, BuildNetworkFailureReasonZh(ex));
        }
    }
}

public class ModelCapabilityResult
{
    public bool SupportsReasoningEffort { get; set; }
    public bool SupportsThinking { get; set; }
    public bool SupportsLongContext { get; set; }
    public bool ReasoningEffortRejected { get; set; }
    public bool EnableThinkingRejected { get; set; }
    public bool ClaudeThinkingRejected { get; set; }
    public bool LongContextRejected { get; set; }
    public bool? ThinkingPassthrough { get; set; }

    public bool AnyRequestReachedServer { get; set; }

    public bool FinalChatOk { get; set; }

    public string? FailureReason { get; set; }
}

public class ModelInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")] public string? Id { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Name")] public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Description")] public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ContextLength")] public int ContextLength { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsReasoningEffort")] public bool SupportsReasoningEffort { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsThinking")] public bool SupportsThinking { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsTools")] public bool SupportsTools { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsVision")] public bool SupportsVision { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsImageGeneration")] public bool SupportsImageGeneration { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportsLongContext")] public bool SupportsLongContext { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("CapabilitiesDetected")] public bool CapabilitiesDetected { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("SupportedEffortLevels")]
    public List<string>? SupportedEffortLevels { get; set; }
}

public enum EndpointErrorType
{
    None,
    BadRequest,
    AuthError,
    PaymentRequired,
    NotFound,
    MethodNotAllowed,
    RequestTimeout,
    Conflict,
    PayloadTooLarge,
    Unprocessable,
    RateLimit,
    QuotaExceeded,
    ServerError,
    NotImplemented,
    BadGateway,
    ServiceUnavailable,
    GatewayTimeout,
    WafBlock,
    ProxyOrSslError,
    NetworkError,
    FormatError,
    Unknown
}

public class ModelsTestResult
{
    [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessfulEndpoint")] public string? SuccessfulEndpoint { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Models")] public List<ModelInfo> Models { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
    public EndpointErrorType ErrorType { get; set; } = EndpointErrorType.None;
}

public class ChatTestResult
{
    [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("SuccessfulEndpoint")] public string? SuccessfulEndpoint { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
    public string? RawErrorBody { get; set; }
    public EndpointErrorType ErrorType { get; set; } = EndpointErrorType.None;
}
