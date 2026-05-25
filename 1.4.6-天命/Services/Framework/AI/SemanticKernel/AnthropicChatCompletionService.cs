using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class AnthropicChatCompletionService : IChatCompletionService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;
        private readonly string _modelId;
        private readonly string _endpoint;
        private string _apiKey;
        private readonly JsonSerializerOptions _jsonOptions;

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
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AnthropicService] {key}: {ex.Message}");
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public void UpdateApiKey(string newKey)
        {
            _apiKey = newKey;
        }

        public AnthropicChatCompletionService(string apiKey, string modelId, string? baseUrl = null)
        {
            _modelId = modelId;
            _apiKey = apiKey;

            var root = string.IsNullOrWhiteSpace(baseUrl)
                ? "https://api.anthropic.com/v1"
                : baseUrl!;

            _endpoint = NormalizeEndpoint(root);
            _httpClient = ServiceLocator.Get<ProxyService>().CreateHttpClient(TimeSpan.FromMinutes(5));

            _jsonOptions = JsonHelper.Lenient;

            Attributes = new Dictionary<string, object?>
            {
                { "ModelId", _modelId },
                { "Provider", "Anthropic" }
            };

            TM.App.Log($"[AnthropicService] 初始化: Model={modelId}, Endpoint={_endpoint}");
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (messages, systemMessage) = ConvertChatHistory(chatHistory);

                var maxTokens = GetMaxTokens(executionSettings);

                var body = new Dictionary<string, object?>
                {
                    ["model"] = _modelId,
                    ["max_tokens"] = maxTokens,
                    ["messages"] = messages
                };

                if (!string.IsNullOrWhiteSpace(systemMessage))
                {
                    body["system"] = systemMessage;
                }

                InjectThinkingIfSupported(body, maxTokens);

                var request = BuildHttpRequest(body, stream: false);

                TM.App.Log($"[AnthropicService] 发送请求: {messages.Count} 条消息, MaxTokens={maxTokens}, HasSystem={!string.IsNullOrWhiteSpace(systemMessage)}");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    TM.App.Log($"[AnthropicService] 错误 ({(int)response.StatusCode}): {responseJson}");
                    response.EnsureSuccessStatusCode();
                }

                var content = TryExtractContent(responseJson);

                TM.App.Log($"[AnthropicService] 收到响应: {content.Length} 字符");

                return new List<ChatMessageContent>
                {
                    new ChatMessageContent(AuthorRole.Assistant, content)
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AnthropicService] 错误: {ex.Message}");
                throw;
            }
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var (messages, systemMessage) = ConvertChatHistory(chatHistory);

            var maxTokens = GetMaxTokens(executionSettings);

            var body = new Dictionary<string, object?>
            {
                ["model"] = _modelId,
                ["max_tokens"] = maxTokens,
                ["messages"] = messages,
                ["stream"] = true
            };

            if (!string.IsNullOrWhiteSpace(systemMessage))
            {
                body["system"] = systemMessage;
            }

            InjectThinkingIfSupported(body, maxTokens);

            var request = BuildHttpRequest(body, stream: true);

            TM.App.Log($"[AnthropicService] 流式请求: {messages.Count} 条消息, MaxTokens={maxTokens}, HasSystem={!string.IsNullOrWhiteSpace(systemMessage)}");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                TM.App.Log($"[AnthropicService] 流式请求失败 ({(int)response.StatusCode}): {errorBody}");
                response.EnsureSuccessStatusCode();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var delta = TryExtractStreamDeltaWithThinking(line);

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, delta.Content);
                }

                if (!string.IsNullOrEmpty(delta.Thinking))
                {
                    var metadata = new Dictionary<string, object?>
                    {
                        ["Thinking"] = delta.Thinking
                    };

                    var thinkingChunk = new StreamingChatMessageContent(AuthorRole.Assistant, content: string.Empty, metadata: metadata);
                    yield return thinkingChunk;
                }
            }
        }

        #region 辅助方法

        private static int GetMaxTokens(PromptExecutionSettings? executionSettings)
        {
            const int defaultMaxTokens = 4096;

            if (executionSettings?.ExtensionData == null)
            {
                return defaultMaxTokens;
            }

            try
            {
                if (executionSettings.ExtensionData.TryGetValue("max_tokens", out var value) && value != null)
                {
                    return value switch
                    {
                        int i => i,
                        long l => (int)l,
                        double d => (int)d,
                        string s when int.TryParse(s, out var parsed) => parsed,
                        _ => defaultMaxTokens
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AnthropicService] 解析 max_tokens 失败，使用默认值: {ex.Message}");
            }

            return defaultMaxTokens;
        }

        private HttpRequestMessage BuildHttpRequest(Dictionary<string, object?> body, bool stream)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = content
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("User-Agent", "TianMing-SK-Anthropic/1.0");

            if (stream)
            {
                request.Headers.Accept.ParseAdd("text/event-stream");
            }

            return request;
        }

        private (List<object> Messages, string? SystemMessage) ConvertChatHistory(ChatHistory chatHistory)
        {
            var messages = new List<object>();
            var systemParts = new List<string>();

            foreach (var msg in chatHistory)
            {
                if (msg.Role == AuthorRole.System)
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        systemParts.Add(msg.Content);
                    }
                    continue;
                }

                string role;
                if (msg.Role == AuthorRole.User)
                {
                    role = "user";
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    role = "assistant";
                }
                else
                {
                    role = "user";
                }

                var text = msg.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                messages.Add(new
                {
                    role,
                    content = new object[]
                    {
                        new { type = "text", text }
                    }
                });
            }

            if (messages.Count == 0)
            {
                messages.Add(new
                {
                    role = "user",
                    content = new object[] { new { type = "text", text = "Hello" } }
                });
            }

            var systemMessage = systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null;
            return (messages, systemMessage);
        }

        private void InjectThinkingIfSupported(Dictionary<string, object?> body, int maxTokens)
        {
            if (!SupportsExtendedThinking(_modelId))
                return;

            const int minMaxTokensForThinking = 2048;
            if (maxTokens < minMaxTokensForThinking)
                return;

            var budget = Math.Min((int)(maxTokens * 0.75), 32000);

            body["thinking"] = new Dictionary<string, object>
            {
                ["type"] = "enabled",
                ["budget_tokens"] = budget
            };
        }

        private static bool SupportsExtendedThinking(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return false;

            var lower = modelId.ToLowerInvariant();

            if (lower.Contains("claude-4") || lower.Contains("claude-sonnet-4") || lower.Contains("claude-opus-4"))
                return true;

            if (lower.Contains("claude-3-7") || lower.Contains("claude-3.7"))
                return true;

            if (lower.Contains("claude-3-5-sonnet") || lower.Contains("claude-3.5-sonnet"))
                return true;

            return false;
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            var trimmed = endpoint.Trim();
            var lower = trimmed.ToLowerInvariant();

            if (lower.Contains("/v1/messages"))
            {
                return trimmed;
            }

            if (lower.EndsWith("/v1") || lower.EndsWith("/v1/"))
            {
                var root = trimmed.TrimEnd('/');
                return root + "/messages";
            }

            return trimmed;
        }

        private string TryExtractContent(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseJson, new JsonDocumentOptions { AllowTrailingCommas = true });
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array &&
                    contentArray.GetArrayLength() > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("text", out var textProp))
                        {
                            sb.Append(textProp.GetString());
                        }
                    }

                    var merged = sb.ToString();
                    if (!string.IsNullOrEmpty(merged))
                    {
                        return merged;
                    }
                }

                return string.Empty;
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(TryExtractContent), ex);
                return responseJson;
            }
        }

        private readonly struct StreamDelta
        {
            public StreamDelta(string? thinking, string? content)
            {
                Thinking = thinking;
                Content = content;
            }

            public string? Thinking { get; }
            public string? Content { get; }
        }

        private StreamDelta TryExtractStreamDeltaWithThinking(string sseLine)
        {
            if (string.IsNullOrWhiteSpace(sseLine))
            {
                return new StreamDelta(null, null);
            }

            var trimmed = sseLine.Trim();
            if (trimmed.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(6);
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "[DONE]")
            {
                return new StreamDelta(null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed, new JsonDocumentOptions { AllowTrailingCommas = true });
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp))
                {
                    var eventType = typeProp.GetString();

                    if (eventType == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("type", out var deltaTypeProp))
                        {
                            var deltaType = deltaTypeProp.GetString();

                            if (deltaType == "thinking_delta" && delta.TryGetProperty("thinking", out var thinkingProp))
                            {
                                return new StreamDelta(thinkingProp.GetString(), null);
                            }

                            if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textProp))
                            {
                                return new StreamDelta(null, textProp.GetString());
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(TryExtractStreamDeltaWithThinking), ex);
            }

            return new StreamDelta(null, null);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _httpClient.Dispose();
            }
        }
    }
}
