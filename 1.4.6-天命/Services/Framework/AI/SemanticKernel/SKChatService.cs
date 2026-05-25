#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Framework.AI.Interfaces.Prompts;
using Microsoft.SemanticKernel.Connectors.Google;
using TM.Framework.Common.Services;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;
using System.Diagnostics;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TM.Services.Framework.AI.Monitoring;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0070

namespace TM.Services.Framework.AI.SemanticKernel
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class SKChatService : IAIChatService
    {

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[SKChatService] {key}: {ex.Message}");
        }

        private Kernel? _kernel;
        private IChatCompletionService? _chatService;
        private ChatHistory _chatHistory = new();
        private CancellationTokenSource? _currentCts;
        private CancellationTokenSource? _businessCts;
        private TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode _currentMode = TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Ask;
        private bool _isSessionCompressed;
        private readonly ChatHistoryCompressionService _compression;
        private readonly StructuredMemoryExtractor _memoryExtractor;
        private readonly VectorSearchService _vectorSearch;
        private readonly IAIUsageStatisticsService _statistics;
        private int _turnIndex;
        private readonly object _kernelLock = new();
        private string? _lastKernelConfigKey;
        private HttpClient? _kernelHttpClient;
        private Agents.NovelAgent? _novelAgent;
        private Agents.Providers.RAGContextProvider? _ragProvider;
        private Agents.Providers.NovelMemoryProvider? _memoryProvider;

        private string _currentProviderType = "TagBased";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _streamingUnsupportedEndpoints = new();
        private static readonly TimeSpan StreamingMarkExpiry = TimeSpan.FromMinutes(30);

        private string GetEndpointKey()
        {
            var cfg = AI.GetActiveConfiguration();
            return cfg == null ? "" : $"{cfg.ProviderId}|{cfg.CustomEndpoint}";
        }

        private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(90);

        private async Task<string> AdaptiveGenerateAsync(
            ChatHistory history,
            PromptExecutionSettings settings,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            if (_chatService == null)
                return "[错误] AI 服务未配置";

            var endpointKey = GetEndpointKey();
            var useStreaming = true;
            if (_streamingUnsupportedEndpoints.TryGetValue(endpointKey, out var markedTime))
            {
                if (DateTime.UtcNow - markedTime > StreamingMarkExpiry)
                    _streamingUnsupportedEndpoints.TryRemove(endpointKey, out _);
                else
                    useStreaming = false;
            }

            if (useStreaming)
            {
                int totalChunksReceived = 0;
                try
                {
                    var sb = new System.Text.StringBuilder();
                    int chunks = 0;
                    var lastThinkingReport = DateTime.MinValue;
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idleCts.CancelAfter(StreamIdleTimeout);

                    await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                        history, settings, _kernel, idleCts.Token))
                    {
                        idleCts.CancelAfter(StreamIdleTimeout);
                        totalChunksReceived++;

                        if (!string.IsNullOrEmpty(chunk.Content))
                        {
                            sb.Append(chunk.Content);
                            chunks++;
                            if (chunks % 5 == 0)
                                progress?.Report($"已接收 {sb.Length} 字符...");
                        }
                        else
                        {
                            var now = DateTime.UtcNow;
                            if ((now - lastThinkingReport).TotalSeconds >= 2)
                            {
                                progress?.Report("思考中...");
                                lastThinkingReport = now;
                            }
                        }
                    }
                    progress?.Report($"接收完成，共 {sb.Length} 字符");
                    TM.App.Log($"[SKChatService] 流式生成完成: {sb.Length} 字符, {chunks} 块");
                    return sb.ToString();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (totalChunksReceived == 0)
                    {
                        TM.App.Log("[SKChatService] 流式空闲超时且无任何chunk，疑似假流，降级非流式");
                        _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow;
                        progress?.Report("流式无响应，切换标准模式重试...");
                    }
                    else
                    {
                        TM.App.Log($"[SKChatService] 流式空闲超时（90s无数据），已接收 {totalChunksReceived} chunks");
                        progress?.Report("响应超时（90秒无数据）");
                        return "[错误] 响应超时：服务器超过90秒未返回数据";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 流式不支持，降级非流式: {ex.Message}");
                    _streamingUnsupportedEndpoints[endpointKey] = DateTime.UtcNow;
                    progress?.Report("端点不支持流式，使用标准模式...");
                }
            }
            else
            {
                progress?.Report("等待响应...");
            }

            var response = await _chatService.GetChatMessageContentAsync(history, settings, _kernel, ct);
            var content = response.Content ?? string.Empty;
            progress?.Report($"生成完成，共 {content.Length} 字符");
            return content;
        }

        private sealed class DisposableAction : IDisposable
        {
            private Action? _dispose;

            public DisposableAction(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }

        public Guid LastRunId { get; private set; }

        public void SetLastRunId(Guid runId) => LastRunId = runId;

        public StructuredMemoryExtractor.StructuredMemory GetMemory() => _memoryExtractor.GetMemory();

        public bool HasMemory() => _memoryExtractor.HasMemory();

        public void ClearMemory() => _memoryExtractor.ClearMemory();

        public async System.Threading.Tasks.Task TriggerLLMMemoryExtractionAsync()
        {
            var messages = Sessions.LoadCurrentMessages();
            if (messages == null || messages.Count == 0) return;
            var content = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Summary}"));
            await _memoryExtractor.ExtractWithLLMAsync(content);
        }

        public IReadOnlyList<SearchResult>? GetLastRAGReferences()
        {
            var refs = _ragProvider?.LastChapterResults;
            return (refs == null || refs.Count == 0) ? null : refs;
        }

        public string LastThinkingContent { get; private set; } = string.Empty;

        private readonly AIService _ai;
        private readonly ProxyService _proxy;
        private readonly SessionManager _sessions;

        private static readonly ResiliencePipeline _streamingPipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio          = 0.8,
                MinimumThroughput     = 3,
                SamplingDuration      = TimeSpan.FromMinutes(1),
                BreakDuration         = TimeSpan.FromSeconds(30),
                ShouldHandle          = new PredicateBuilder().Handle<Exception>(ex =>
                    ex is not OperationCanceledException and not TimeoutRejectedException
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden }
                    and not System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.PaymentRequired }
                    and not System.Net.Http.HttpRequestException { StatusCode: (System.Net.HttpStatusCode)429 })
            })
            .Build();

        private AIService AI => _ai;
        private ProxyService Proxy => _proxy;

        public SessionManager Sessions => _sessions;

        public bool IsSessionCompressed => _isSessionCompressed;

        public bool IsMainConversationGenerating => _currentCts != null && !_currentCts.IsCancellationRequested;

        private Action? _cancelWorkspaceBatchAction;

        public bool IsWorkspaceBatchGenerating => _cancelWorkspaceBatchAction != null;

        public void RegisterWorkspaceBatch(Action cancelAction) => _cancelWorkspaceBatchAction = cancelAction;

        public void UnregisterWorkspaceBatch() => _cancelWorkspaceBatchAction = null;

        public void CancelWorkspaceBatch()
        {
            _cancelWorkspaceBatchAction?.Invoke();
            _cancelWorkspaceBatchAction = null;
        }

        public SKChatService(AIService ai, ProxyService proxy, SessionManager sessions)
        {
            _ai = ai;
            _proxy = proxy;
            _sessions = sessions;
            _compression = new ChatHistoryCompressionService(
                (systemPrompt, userPrompt, ct) => GenerateOneShotAsync(systemPrompt, userPrompt, ct),
                GetModelContextWindow);
            _memoryExtractor = new StructuredMemoryExtractor(
                (systemPrompt, userPrompt, ct) => GenerateOneShotAsync(systemPrompt, userPrompt, ct));
            _vectorSearch = ServiceLocator.Get<VectorSearchService>();
            _statistics = ServiceLocator.Get<IAIUsageStatisticsService>();

            TM.App.Log("[SKChatService] 初始化");

            _proxy.ConfigChanged += (_, _) =>
            {
                try
                {
                    _lastKernelConfigKey = null;
                    TM.App.Log("[SKChatService] 代理配置变更，已标记重建Kernel（延迟销毁）");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 处理代理变更失败: {ex.Message}");
                }
            };

            var allSessions = Sessions.GetAllSessions();
            if (allSessions.Count > 0)
            {
                var initialSessionId = allSessions[0].Id;
                _chatHistory = Sessions.SwitchSession(initialSessionId);
                _memoryExtractor.ClearMemory();
                _vectorSearch.SwitchConversationSession(initialSessionId);
                _turnIndex = 0;
                _ragProvider?.ResetDedup();
                LoadConversationIndexForSession(initialSessionId);
                LoadMemoryForSession(initialSessionId);
            }

            _ = Conversation.Mapping.PlanModeMapper.PrewarmContentGuideCacheAsync();
        }

        public IDisposable UseTransientMode(TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode mode)
        {
            var oldMode = _currentMode;
            var oldFilter = PlanModeFilter.IsEnabled;

            _currentMode = mode;
            PlanModeFilter.IsEnabled = ChatModeSettings.RequiresFunctionConfirmation(mode);

            return new DisposableAction(() =>
            {
                _currentMode = oldMode;
                PlanModeFilter.IsEnabled = oldFilter;
            });
        }

        #region IAIChatService 实现

        public async Task<string> SendMessageAsync(string displayText, string promptForModel)
        {
            return await SendMessageAsync(displayText, promptForModel, CancellationToken.None);
        }

        public async Task<string> SendMessageAsync(string displayText, string promptForModel, CancellationToken cancellationToken)
        {
            var runId = ShortIdGenerator.NewGuid();
            LastRunId = runId;
            var mode = _currentMode;

            CancellationTokenSource? localCts = null;
            var sw = Stopwatch.StartNew();

            try
            {
                EnsureKernelInitialized();

                if (_chatService == null)
                {
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _currentCts = localCts;

                EnsureSystemPrompt(null);

                await EnsureCompressionIfNeededAsync(promptForModel, localCts.Token);

                _chatHistory.AddUserMessage(promptForModel);

                TM.App.Log($"[SKChatService] 发送消息: {displayText.Substring(0, Math.Min(50, displayText.Length))}...");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunStarted,
                    Title = displayText.Length > 30 ? displayText[..30] : displayText,
                    Detail = displayText
                });

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.UserMessage,
                    Title = "User",
                    Detail = promptForModel
                });

                var settings = GetCurrentModeSettings();

                var response = await InvokeApiWithRotationAsync(
                    async innerCt => await _chatService!.GetChatMessageContentAsync(
                        _chatHistory, settings, _kernel, innerCt),
                    localCts.Token);

                sw.Stop();
                var content = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(response.Content ?? string.Empty);

                _chatHistory.AddAssistantMessage(content);

                _memoryExtractor.ExtractFromResponse(content);

                TM.App.Log($"[SKChatService] 收到响应: {content.Substring(0, Math.Min(50, content.Length))}...");

                var cfg = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg?.ModelId ?? "unknown", Provider = cfg?.ProviderId ?? "Chat", Success = true, ResponseTimeMs = (int)sw.ElapsedMilliseconds });

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.AssistantMessage,
                    Title = "Assistant",
                    Detail = content,
                    Succeeded = true
                });

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunCompleted,
                    Title = "Run completed",
                    Succeeded = true
                });

                PlanModeFilter.ResetRun(runId);

                return content;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                var cfg = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg?.ModelId ?? "unknown", Provider = cfg?.ProviderId ?? "Chat", Success = false, ResponseTimeMs = (int)sw.ElapsedMilliseconds, ErrorMessage = "请求超时" });
                TM.App.Log($"[SKChatService] 请求超时或被底层取消: {ex.Message}");
                GlobalToast.Warning("请求超时", "请检查网络或代理连接后重试");
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "请求超时",
                    Detail = "[错误] 请求超时",
                    Succeeded = false
                });
                PlanModeFilter.ResetRun(runId);
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[SKChatService] 请求已取消");
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "已取消",
                    Detail = "[已取消]",
                    Succeeded = false
                });
                PlanModeFilter.ResetRun(runId);
                return "[已取消]";
            }
            catch (Exception ex)
            {
                sw.Stop();
                var cfg = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg?.ModelId ?? "unknown", Provider = cfg?.ProviderId ?? "Chat", Success = false, ResponseTimeMs = (int)sw.ElapsedMilliseconds, ErrorMessage = ex.Message });
                TM.App.Log($"[SKChatService] 错误: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 请求失败", ex.Message);
                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunFailed,
                    Title = "错误",
                    Detail = ex.Message,
                    Succeeded = false
                });
                PlanModeFilter.ResetRun(runId);
                return $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    if (ReferenceEquals(_currentCts, localCts))
                    {
                        _currentCts = null;
                    }

                    localCts.Dispose();
                }
            }
        }

        public async Task<string> GenerateWithChatHistoryAsync(ChatHistory history, string userPrompt, CancellationToken cancellationToken = default)
            => await GenerateWithChatHistoryAsync(history, userPrompt, null, cancellationToken);

        public async Task<string> GenerateWithChatHistoryAsync(ChatHistory history, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken = default)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));

            CancellationTokenSource? localCts = null;

            try
            {
                EnsureKernelInitialized();
                if (_chatService == null)
                {
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _businessCts = localCts;

                var config = AI.GetActiveConfiguration();
                if (config == null || string.IsNullOrEmpty(config.ModelId))
                {
                    return "[错误] 当前没有激活的AI模型";
                }

                string? businessStructuredMemory = null;
                const int memoryExtractionThreshold = 6;
                if (history.Count >= memoryExtractionThreshold)
                {
                    var tempExtractor = new StructuredMemoryExtractor();
                    int scanned = 0;
                    const int maxScan = 10;
                    for (int i = history.Count - 1; i >= 0 && scanned < maxScan; i--)
                    {
                        var msg = history[i];
                        if (msg.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant &&
                            !string.IsNullOrWhiteSpace(msg.Content))
                        {
                            tempExtractor.ExtractFromResponse(msg.Content);
                            scanned++;
                        }
                    }
                    if (tempExtractor.HasMemory())
                    {
                        businessStructuredMemory = tempExtractor.ToTextFormat();
                        const int maxChars = 2000;
                        if (businessStructuredMemory.Length > maxChars)
                        {
                            businessStructuredMemory = businessStructuredMemory[..maxChars];
                        }
                    }
                }

                var (compressedHistory, compressed) = await _compression.EnsureCompressionIfNeededAsync(
                    history,
                    config.ModelId,
                    userPrompt,
                    localCts.Token,
                    structuredMemory: businessStructuredMemory);

                if (compressed)
                {
                    TM.App.Log($"[SKChatService] 业务会话触发压缩: model={config.ModelId}, historyCount={history.Count}");

                    history.Clear();
                    foreach (var message in compressedHistory)
                    {
                        history.Add(message);
                    }

                }

                history.AddUserMessage(userPrompt);
                var settings = ChatModeSettings.GetExecutionSettings(TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Ask, history);
                TM.App.Log($"[SKChatService] GenerateWithChatHistory 调用开始: model={config.ModelId}, historyCount={history.Count}, promptLen={userPrompt.Length}");
                var swBiz = Stopwatch.StartNew();
                var content = await InvokeApiWithRotationAsync(
                    async innerCt => await AdaptiveGenerateAsync(history, settings, progress, innerCt),
                    localCts.Token);
                swBiz.Stop();
                history.AddAssistantMessage(content);
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = config.ModelId, Provider = config.ProviderId ?? "Business", Success = true, ResponseTimeMs = (int)swBiz.ElapsedMilliseconds });
                return content;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                TM.App.Log($"[SKChatService] GenerateWithChatHistoryAsync 请求超时: {ex.Message}");
                var cfg2 = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg2?.ModelId ?? "unknown", Provider = cfg2?.ProviderId ?? "Business", Success = false, ResponseTimeMs = 0, ErrorMessage = "请求超时" });
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[SKChatService] GenerateWithChatHistoryAsync 请求已取消");
                return "[已取消]";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] GenerateWithChatHistoryAsync 错误: {ex.GetType().Name}: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 业务生成失败", ex.Message);
                var cfg2 = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfg2?.ModelId ?? "unknown", Provider = cfg2?.ProviderId ?? "Business", Success = false, ResponseTimeMs = 0, ErrorMessage = ex.Message });
                return $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    if (ReferenceEquals(_businessCts, localCts))
                    {
                        _businessCts = null;
                    }

                    localCts.Dispose();
                }
            }
        }

        public async Task<string> SendSilentMessageAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureKernelInitialized();

                if (_chatService == null)
                {
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                var tempHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    tempHistory.AddSystemMessage(systemPrompt);
                }
                tempHistory.AddUserMessage(userMessage);

                var settings = ChatModeSettings.GetExecutionSettings(_currentMode, tempHistory);

                var response = await InvokeApiWithRotationAsync(
                    async innerCt => await _chatService!.GetChatMessageContentAsync(
                        tempHistory, settings, _kernel, innerCt),
                    cancellationToken);

                var rawContent = response.Content ?? string.Empty;

                if (rawContent.Contains(TM.Services.Modules.ProjectData.Implementations.GenerationGate.ChangesSeparator, StringComparison.Ordinal))
                {
                    return rawContent;
                }

                if (rawContent.IndexOf("<analysis>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rawContent.IndexOf("<think>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rawContent.IndexOf("<answer>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var answerMatch = System.Text.RegularExpressions.Regex.Match(
                        rawContent, @"<answer>(.*?)</answer>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (answerMatch.Success)
                        return answerMatch.Groups[1].Value.Trim();

                    var cleaned = System.Text.RegularExpressions.Regex.Replace(
                        rawContent, @"<(analysis|think)>.*?</(analysis|think)>", string.Empty,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    cleaned = cleaned.Trim();
                    return string.IsNullOrWhiteSpace(cleaned) ? rawContent : cleaned;
                }

                return rawContent;
            }
            catch (OperationCanceledException)
            {
                return "[已取消]";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] SendSilentMessageAsync 错误: {ex.Message}");
                return $"[错误] {ex.Message}";
            }
        }

        public async Task<string> SendStreamMessageAsync(string displayText, string promptForModel, Action<string> onChunk, CancellationToken cancellationToken = default)
        {
            var parts = new ChatPromptParts
            {
                SystemPrompt = string.Empty,
                UserPrompt = promptForModel
            };

            return await SendStreamMessageAsync(displayText, parts, onChunk, null, cancellationToken);
        }

        public Task<string> SendStreamMessageAsync(string displayText, ChatPromptParts promptParts, Action<string> onChunk, CancellationToken cancellationToken = default)
        {
            return SendStreamMessageAsync(displayText, promptParts, onChunk, null, cancellationToken);
        }

        public async Task<string> SendStreamMessageAsync(string displayText, ChatPromptParts promptParts, Action<string> onChunk, Action<string>? onThinkingChunk, CancellationToken cancellationToken)
        {
            if (promptParts == null) throw new ArgumentNullException(nameof(promptParts));

            var runId = ShortIdGenerator.NewGuid();
            LastRunId = runId;
            var mode = _currentMode;

            const int maxRetries = 2;
            int? fallbackMaxTokens = null;
            int? fallbackContextWindow = null;

            var streamRotation = ServiceLocator.Get<TM.Services.Framework.AI.Core.ApiKeyRotationService>();
            var streamConfig = AI.GetActiveConfiguration();
            var streamExcludeKeyIds = new HashSet<string>();
            TM.Services.Framework.AI.Core.KeySelection? streamCurrentKey = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                CancellationTokenSource? localCts = null;
                var answerBuilder = new System.Text.StringBuilder();
                var thinkingBuilder = new System.Text.StringBuilder();
                try
                {
                    if (streamConfig != null)
                    {
                        var poolStatus = streamRotation.GetPoolStatus(streamConfig.ProviderId);
                        if (poolStatus?.TotalKeys > 0)
                        {
                            streamCurrentKey = streamRotation.GetNextKey(streamConfig.ProviderId, streamExcludeKeyIds);
                            if (streamCurrentKey != null
                                && !string.Equals(streamConfig.ApiKey, streamCurrentKey.ApiKey, StringComparison.Ordinal))
                            {
                                streamConfig.ApiKey = streamCurrentKey.ApiKey;
                            }
                        }
                    }

                    EnsureKernelInitialized();

                    if (_chatService == null)
                    {
                        var error = "[错误] AI 服务未配置";
                        onChunk(error);
                        return error;
                    }

                    localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _currentCts = localCts;

                    TM.App.Log($"[SKChatService] SystemPrompt长度: {promptParts.SystemPrompt?.Length ?? 0}");
                    EnsureSystemPrompt(promptParts.SystemPrompt);

                var userPromptForModel = string.IsNullOrWhiteSpace(promptParts.UserPrompt) ? displayText : promptParts.UserPrompt;
                await EnsureCompressionIfNeededAsync(userPromptForModel, localCts.Token, fallbackContextWindow);

                _chatHistory.AddUserMessage(userPromptForModel);

                TM.App.Log($"[SKChatService] 流式发送: {displayText.Substring(0, Math.Min(50, displayText.Length))}...");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunStarted,
                    Title = displayText.Length > 30 ? displayText[..30] : displayText,
                    Detail = displayText
                });

                Action<string> effectiveThinkingSink;
                if (onThinkingChunk != null)
                {
                    effectiveThinkingSink = t =>
                    {
                        onThinkingChunk(t);
                        thinkingBuilder.Append(t);
                    };
                }
                else if (mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Plan ||
                         mode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Agent)
                {
                    effectiveThinkingSink = t =>
                    {
                        if (!string.IsNullOrWhiteSpace(t)) thinkingBuilder.AppendLine(t);
                    };
                }
                else
                {
                    effectiveThinkingSink = t => thinkingBuilder.Append(t);
                }

                var settings = GetCurrentModeSettings(fallbackMaxTokens);

                await _streamingPipeline.ExecuteAsync(async innerCt =>
                {
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                    idleCts.CancelAfter(StreamIdleTimeout);

                    await foreach (var streamChunk in _novelAgent!.InvokeStreamingAsync(_chatHistory, settings, idleCts.Token))
                    {
                        idleCts.CancelAfter(StreamIdleTimeout);

                        switch (streamChunk)
                        {
                            case TextDeltaChunk textChunk:
                                var sanitizedChunk = TM.Services.Framework.AI.Core.ModelNameSanitizer.SanitizeChunk(textChunk.Content);
                                answerBuilder.Append(sanitizedChunk);
                                onChunk(sanitizedChunk);
                                break;
                            case ThinkingDeltaChunk thinkingChunk:
                                effectiveThinkingSink(thinkingChunk.Content);
                                break;
                            case StreamCompleteChunk:
                                break;
                        }
                    }
                }, localCts.Token);

                var result = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(answerBuilder.ToString());

                LastThinkingContent = thinkingBuilder.ToString();

                if (string.IsNullOrWhiteSpace(result) && attempt < maxRetries)
                {
                    TM.App.Log($"[SKChatService] 模型返回空回复，自动重试（第 {attempt + 1} 次）");
                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                    {
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(result))
                {
                    result = "（模型未输出正式回答，请重试。）";
                    onChunk(result);
                }

                if (_chatHistory.Count == 0 ||
                    _chatHistory[^1].Role != AuthorRole.Assistant ||
                    _chatHistory[^1].Content != result)
                {
                    _chatHistory.AddAssistantMessage(result);
                }

                TriggerBackgroundMemoryExtraction();

                var userTextForIndex = _chatHistory.Count >= 2
                    ? _chatHistory[^2].Content ?? string.Empty
                    : string.Empty;
                var turnIdx = _turnIndex++;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { _vectorSearch.IndexConversationTurn(turnIdx, userTextForIndex, result); }
                    catch (Exception ex) { TM.App.Log($"[SKChatService] RAG索引失败（非致命）: {ex.Message}"); }
                });

                TM.App.Log($"[SKChatService] 流式完成，总长度: {result.Length}");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.AssistantMessage,
                    Title = "Assistant",
                    Detail = result,
                    Succeeded = true
                });

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunCompleted,
                    Title = "Run completed",
                    Succeeded = true
                });

                    PlanModeFilter.ResetRun(runId);
                    return result;
                }
                catch (BrokenCircuitException)
                {
                    TM.App.Log("[SKChatService] 熍断器开路，端点暂时不可用");
                    GlobalToast.Error("端点暂时不可用", "请等开30秒后重试，或切换到其他模型");
                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId, Mode = mode, EventType = ExecutionEventType.RunFailed,
                        Title = "熍断器开路", Detail = "[错误] 端点暂时不可用", Succeeded = false
                    });
                    PlanModeFilter.ResetRun(runId);
                    return "[错误] 端点暂时不可用，请等开30秒";
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TM.App.Log("[SKChatService] 流式空闲超时（90s无数据），强制中止流");
                    GlobalToast.Warning("响应超时", "AI 流式超过90秒无响应，请检查网络或端点状态");
                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId, Mode = mode, EventType = ExecutionEventType.RunFailed,
                        Title = "空闲超时", Detail = "[错误] 请求超时", Succeeded = false
                    });
                    PlanModeFilter.ResetRun(runId);
                    return "[错误] 请求空闲超时";
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    TM.App.Log($"[SKChatService] 流式请求超时或被底层取消: {ex.Message}");
                    GlobalToast.Warning("请求超时", "请检查网络或代理连接后重试");

                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                    {
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    }

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.RunFailed,
                        Title = "请求超时",
                        Detail = "[错误] 请求超时",
                        Succeeded = false
                    });
                    PlanModeFilter.ResetRun(runId);
                    return "[错误] 请求超时";
                }
                catch (OperationCanceledException)
                {
                    var partialAnswer = TM.Services.Framework.AI.Core.ModelNameSanitizer.Sanitize(answerBuilder.ToString());
                    LastThinkingContent = thinkingBuilder.ToString();

                    if (!string.IsNullOrWhiteSpace(partialAnswer))
                    {
                        if (_chatHistory.Count == 0 ||
                            _chatHistory[^1].Role != AuthorRole.Assistant ||
                            _chatHistory[^1].Content != partialAnswer)
                        {
                            _chatHistory.AddAssistantMessage(partialAnswer);
                        }
                        _memoryExtractor.ExtractFromResponse(partialAnswer);
                        TM.App.Log($"[SKChatService] 流式取消，保留部分回答: {partialAnswer.Length} 字符");
                    }
                    else
                    {
                        if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                        {
                            _chatHistory.RemoveAt(_chatHistory.Count - 1);
                        }
                        TM.App.Log("[SKChatService] 流式取消，无部分内容");
                    }

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.RunFailed,
                        Title = "已取消",
                        Detail = "[已取消]",
                        Succeeded = false
                    });

                    PlanModeFilter.ResetRun(runId);
                    return string.IsNullOrWhiteSpace(partialAnswer) ? "[已取消]" : $"[已取消:部分]{partialAnswer}";
                }
                catch (Exception ex)
                {
                    if (answerBuilder.Length == 0 && !cancellationToken.IsCancellationRequested
                        && streamCurrentKey != null && streamConfig != null)
                    {
                        var (keyUseResult, keyRawMsg) = ClassifyException(ex);

                        if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.NetworkError)
                        {
                            NotifyRealError("网络连接失败", keyRawMsg);
                            throw new AlreadyNotifiedApiException(keyRawMsg, ex);
                        }

                        var shouldRotate =
                            keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                                or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                                or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                                or TM.Services.Framework.AI.Core.KeyUseResult.RateLimited
                            || (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                                    or TM.Services.Framework.AI.Core.KeyUseResult.Unknown
                                && attempt < maxRetries);

                        if (shouldRotate)
                        {
                            var kLabel = !string.IsNullOrWhiteSpace(streamCurrentKey.Remark)
                                ? streamCurrentKey.Remark
                                : streamCurrentKey.ApiKey.Length > 10 ? streamCurrentKey.ApiKey[..10] + "..." : streamCurrentKey.ApiKey;
                            streamRotation.ReportKeyResult(streamConfig.ProviderId, streamCurrentKey.KeyId, keyUseResult, keyRawMsg);

                            if (keyUseResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                                or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                                or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                                or TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                                NotifyKeyError(keyUseResult, kLabel, keyRawMsg);

                            streamExcludeKeyIds.Add(streamCurrentKey.KeyId);
                            streamCurrentKey = null;

                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            TM.App.Log($"[SKChatService] 流式密钥轮换: {keyUseResult}，已排除，等待下次 attempt 换 key");

                            if (keyUseResult == TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                            {
                                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); }
                                catch (OperationCanceledException) { throw; }
                            }

                            continue;
                        }
                    }

                    if (attempt < maxRetries && ChatModeSettings.IsMaxTokensError(ex))
                    {
                        var currentMax = ChatModeSettings.LastUsedMaxTokens;
                        fallbackMaxTokens = ChatModeSettings.GetFallbackMaxTokens(currentMax);

                        var currentWindow = fallbackContextWindow ?? GetModelContextWindow(AI.GetActiveConfiguration()?.ModelId ?? string.Empty);
                        var nextWindow = currentWindow / 2;
                        if (nextWindow < 4096)
                        {
                            nextWindow = 4096;
                        }
                        if (nextWindow > 0 && nextWindow < currentWindow)
                        {
                            fallbackContextWindow = nextWindow;
                        }

                        if (fallbackMaxTokens >= currentMax)
                        {
                            TM.App.Log($"[SKChatService] max_tokens min reached: {currentMax}");
                        }
                        else
                        {
                            TM.App.Log($"[SKChatService] max_tokens retry: {currentMax} -> {fallbackMaxTokens}, attempt {attempt + 1}");

                            if (fallbackContextWindow.HasValue && fallbackContextWindow.Value > 0)
                            {
                                async Task<bool> TryCompressAsync(int contextWindow)
                                {
                                    try
                                    {
                                        var config = AI.GetActiveConfiguration();
                                        if (config == null || string.IsNullOrEmpty(config.ModelId))
                                        {
                                            return false;
                                        }

                                        _chatHistory = await _compression.CompressChatHistoryAsync(
                                            _chatHistory,
                                            config.ModelId,
                                            contextWindow,
                                            cancellationToken);
                                        _isSessionCompressed = true;
                                        return true;
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogOnce("TryCompressAsync", ex);
                                        return false;
                                    }
                                }

                                await TryCompressAsync(fallbackContextWindow.Value);
                            }

                            if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                            {
                                _chatHistory.RemoveAt(_chatHistory.Count - 1);
                            }

                            continue;
                        }
                    }

                    TM.App.Log($"[SKChatService] 流式错误: {ex}");
                    NotifyRealError("AI 流式请求失败", ex.Message);

                    if (_chatHistory.Count > 0 && _chatHistory[^1].Role == AuthorRole.User)
                    {
                        _chatHistory.RemoveAt(_chatHistory.Count - 1);
                    }

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.RunFailed,
                        Title = "错误",
                        Detail = ex.ToString(),
                        Succeeded = false
                    });

                    PlanModeFilter.ResetRun(runId);
                    return $"[错误] {ex.Message}";
                }
                finally
                {
                    if (localCts != null)
                    {
                        if (ReferenceEquals(_currentCts, localCts))
                        {
                            _currentCts = null;
                        }

                        localCts.Dispose();
                    }
                }
            }

            return "[错误] 未知错误";
        }

        public async Task<string> GenerateOneShotAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => await GenerateOneShotAsync(systemPrompt, userPrompt, null, cancellationToken);

        public async Task<string> GenerateOneShotAsync(string systemPrompt, string userPrompt, IProgress<string>? progress, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? localCts = null;
            try
            {
                EnsureKernelInitialized();

                if (_chatService == null)
                {
                    return "[错误] AI 服务未配置，请先在设置中配置 API Key";
                }

                var history = string.IsNullOrWhiteSpace(systemPrompt)
                    ? new ChatHistory()
                    : new ChatHistory(systemPrompt);

                if (!string.IsNullOrWhiteSpace(userPrompt))
                {
                    history.AddUserMessage(userPrompt);
                }

                var settings = ChatModeSettings.GetExecutionSettings(TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Ask, history);

                TM.App.Log("[SKChatService] OneShot 生成开始");

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _businessCts = localCts;

                var swOs = Stopwatch.StartNew();
                var content = await InvokeApiWithRotationAsync(
                    async innerCt => await AdaptiveGenerateAsync(history, settings, progress, innerCt),
                    localCts.Token);
                swOs.Stop();

                TM.App.Log($"[SKChatService] OneShot 生成完成，长度: {content.Length}");
                var cfgOs = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = true, ResponseTimeMs = (int)swOs.ElapsedMilliseconds });
                return content;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                TM.App.Log($"[SKChatService] OneShot 请求超时或被底层取消: {ex.Message}");
                var cfgOs = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = false, ResponseTimeMs = 0, ErrorMessage = "请求超时" });
                return "[错误] 请求超时";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[SKChatService] OneShot 请求已取消");
                return "[已取消]";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] OneShot 错误: {ex.Message}");
                if (ex is not AlreadyNotifiedApiException) NotifyRealError("AI 生成失败", ex.Message);
                var cfgOs = AI.GetActiveConfiguration();
                _statistics.RecordCall(new ApiCallRecord { Timestamp = DateTime.Now, ModelName = cfgOs?.ModelId ?? "unknown", Provider = cfgOs?.ProviderId ?? "OneShot", Success = false, ResponseTimeMs = 0, ErrorMessage = ex.Message });
                return $"[错误] {ex.Message}";
            }
            finally
            {
                if (localCts != null)
                {
                    if (ReferenceEquals(_businessCts, localCts))
                    {
                        _businessCts = null;
                    }

                    localCts.Dispose();
                }
            }
        }

        #endregion

        public void CancelCurrentRequest()
        {
            try
            {
                _currentCts?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogOnce("CancelCurrentRequest_Current", ex);
            }

            try
            {
                _businessCts?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogOnce("CancelCurrentRequest_Business", ex);
            }

            TM.App.Log("[SKChatService] 取消当前请求");
        }

        private void EnsureSystemPrompt(string? explicitSystemPrompt)
        {
            string systemPrompt = explicitSystemPrompt ?? string.Empty;

            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                var config = AI.GetActiveConfiguration();
                systemPrompt = AIService.GetEffectiveDeveloperMessage(config);
            }

            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                return;
            }

            if (_currentMode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Plan ||
                _currentMode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Agent)
            {
                const string enforceBlock = "\n\n<output_format_enforcement mandatory=\"true\">\nYou MUST output in exactly this structure:\n<analysis>(thinking/reasoning ONLY here)</analysis><answer>(final output ONLY here)</answer>\n\nRules:\n1) NEVER include reasoning in <answer>.\n2) NEVER output meta-info like 'Thought for 50.1 s'. Record thoughts in <analysis> only.\n3) NEVER omit <analysis>/<answer> tags.\n</output_format_enforcement>\n";

                if (!systemPrompt.Contains("<output_format_enforcement", StringComparison.Ordinal))
                {
                    systemPrompt += enforceBlock;
                }
            }

            if (_chatHistory != null)
            {
                foreach (var msg in _chatHistory)
                {
                    if (msg.Role == AuthorRole.System && !string.IsNullOrWhiteSpace(msg.Content))
                    {
                        if (string.Equals(msg.Content, systemPrompt, StringComparison.Ordinal))
                        {
                            return;
                        }
                        break;
                    }
                }
            }

            var oldHistory = _chatHistory ?? new ChatHistory();
            var newHistory = new ChatHistory(systemPrompt);
            bool skippedFirstSystem = false;

            foreach (var msg in oldHistory)
            {
                if (msg.Role == AuthorRole.System)
                {
                    if (!skippedFirstSystem)
                    {
                        skippedFirstSystem = true;
                        continue;
                    }

                    var systemText = msg.Content;
                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        newHistory.AddSystemMessage(systemText);
                    }

                    continue;
                }

                var text = msg.Content;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (msg.Role == AuthorRole.User)
                {
                    newHistory.AddUserMessage(text);
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    newHistory.AddAssistantMessage(text);
                }
            }

            _chatHistory = newHistory;
            _isSessionCompressed = false;
        }

        #region Kernel 管理

        private void EnsureKernelInitialized()
        {
            var config = AI.GetActiveConfiguration();
            if (config == null)
            {
                TM.App.Log("[SKChatService] 无激活配置");
                _kernel = null;
                _chatService = null;
                return;
            }

            var key = BuildKernelConfigKey(config);
            lock (_kernelLock)
            {
                if (_kernel != null && _chatService != null && string.Equals(_lastKernelConfigKey, key, StringComparison.Ordinal))
                {
                    return;
                }

                BuildKernel(config, key);
            }
        }

        private void BuildKernel(UserConfiguration config, string key)
        {
            try
            {
                var builder = Kernel.CreateBuilder();

                var provider = AI.GetProviderById(config.ProviderId);
                var model = AI.GetModelById(config.ModelId);

                if (provider == null)
                {
                    TM.App.Log($"[SKChatService] 未找到供应商: {config.ProviderId}");
                    _kernel = null;
                    _chatService = null;
                    return;
                }

                var modelName = model?.Name ?? config.ModelId;
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    TM.App.Log($"[SKChatService] 模型名称为空: {config.ModelId}");
                    _kernel = null;
                    _chatService = null;
                    return;
                }

                modelName = StripModelNamePrefix(modelName);

                if (model == null)
                {
                    TM.App.Log($"[SKChatService] 使用自定义模型: {modelName}");
                }
                var apiKey = config.ApiKey;
                var baseUrl = config.CustomEndpoint;

                TM.App.Log($"[SKChatService] 构建 Kernel: Provider={provider.Name}, Model={modelName}");

                _kernelHttpClient?.Dispose();

                var innerHandler = Proxy.CreateHttpMessageHandler();
                var httpTimeout = IsLocalEndpoint(baseUrl)
                    ? System.Threading.Timeout.InfiniteTimeSpan
                    : TimeSpan.FromMinutes(5);
                _kernelHttpClient = new HttpClient(
                    new CherryStudioDelegatingHandler(apiKey ?? string.Empty, innerHandler),
                    disposeHandler: true)
                { Timeout = httpTimeout };
                TM.App.Log($"[SKChatService] HTTP超时策略: {(httpTimeout == System.Threading.Timeout.InfiniteTimeSpan ? "无限（本地端点）" : "5分钟（远程端点）")}");

                builder.Services.AddSingleton<IFunctionInvocationFilter, PlanModeFilter>();

                var providerNameLower = provider.Name.ToLower();
                var protocol = ResolveProtocol(baseUrl, providerNameLower);
                TM.App.Log($"[SKChatService] 协议推断: Provider={provider.Name}, BaseUrl={baseUrl}, Protocol={protocol}");

                switch (protocol)
                {
                    case "anthropic":
                        if (_chatService is IDisposable oldAnthropicDisposable)
                            oldAnthropicDisposable.Dispose();

                        var anthropicService = new AnthropicChatCompletionService(apiKey ?? string.Empty, modelName, baseUrl);
                        _chatService = anthropicService;
                        builder.Services.AddSingleton<IChatCompletionService>(anthropicService);
                        _kernel = builder.Build();
                        _currentProviderType = "Anthropic";
                        RegisterPlugins();
                        _ragProvider = new Agents.Providers.RAGContextProvider(_vectorSearch, () => _isSessionCompressed);
                        _memoryProvider = new Agents.Providers.NovelMemoryProvider(_memoryExtractor);
                        _novelAgent = new Agents.NovelAgent(_kernel, _currentProviderType, new Microsoft.SemanticKernel.AIContextProvider[] { _ragProvider, _memoryProvider });
                        _lastKernelConfigKey = key;
                        TM.App.Log("[SKChatService] provider ok (Anthropic)");
                        return;

                    case "gemini":
                        builder.AddGoogleAIGeminiChatCompletion(
                            modelId: modelName,
                            apiKey: apiKey ?? string.Empty);
                        _currentProviderType = "Google";
                        TM.App.Log("[SKChatService] provider ok (Gemini)");
                        break;

                    case "azure-openai":
                        builder.AddAzureOpenAIChatCompletion(
                            deploymentName: modelName,
                            endpoint: baseUrl ?? "",
                            apiKey: apiKey ?? string.Empty,
                            httpClient: _kernelHttpClient);
                        _currentProviderType = "TagBased";
                        TM.App.Log("[SKChatService] provider ok (Azure)");
                        break;

                    default:
                        if (!string.IsNullOrEmpty(baseUrl))
                        {
                            var chatBaseUrl = EnsureApiVersion(baseUrl);
                            builder.AddOpenAIChatCompletion(
                                modelId: modelName,
                                endpoint: new Uri(chatBaseUrl),
                                apiKey: apiKey ?? string.Empty,
                                httpClient: _kernelHttpClient);
                        }
                        else
                        {
                            builder.AddOpenAIChatCompletion(
                                modelId: modelName,
                                apiKey: apiKey ?? string.Empty,
                                httpClient: _kernelHttpClient);
                        }
                        _currentProviderType = "TagBased";
                        TM.App.Log("[SKChatService] provider ok (OpenAI-compat)");
                        break;
                }

                _kernel = builder.Build();
                _chatService = _kernel.GetRequiredService<IChatCompletionService>();

                RegisterPlugins();

                _ragProvider = new Agents.Providers.RAGContextProvider(_vectorSearch, () => _isSessionCompressed);
                _memoryProvider = new Agents.Providers.NovelMemoryProvider(_memoryExtractor);
                _novelAgent = new Agents.NovelAgent(_kernel, _currentProviderType, new Microsoft.SemanticKernel.AIContextProvider[] { _ragProvider, _memoryProvider });
                TM.App.Log("[SKChatService] Kernel 构建成功");
                _lastKernelConfigKey = key;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] Kernel 构建失败: {ex.Message}");
                _kernel = null;
                _chatService = null;
                _lastKernelConfigKey = null;
            }
        }

        private static string BuildKernelConfigKey(UserConfiguration config)
        {
            return string.Join("|",
                config.ProviderId ?? string.Empty,
                config.ModelId ?? string.Empty,
                config.CustomEndpoint ?? string.Empty,
                config.ApiKey ?? string.Empty);
        }

        public void ClearHistory()
        {
            _chatHistory = new ChatHistory();
            _memoryExtractor.ClearMemory();
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (!string.IsNullOrEmpty(sessionId))
            {
                _vectorSearch.SwitchConversationSession(sessionId, forceClear: true);
            }
            Sessions.SaveCurrentMessages(new List<SerializedMessageRecord>());
            TM.App.Log("[SKChatService] 对话历史已清空");
            _isSessionCompressed = false;
        }

        public void SetSystemPrompt(string systemPrompt)
        {
            _chatHistory = new ChatHistory(systemPrompt);
            _memoryExtractor.ClearMemory();
            TM.App.Log("[SKChatService] 系统提示词已设置");
        }

        private void RegisterPlugins()
        {
            if (_kernel == null) return;

            try
            {
                _kernel.Plugins.AddFromObject(new WriterPlugin(), "Writer");
                _kernel.Plugins.AddFromObject(new SystemPlugin(), "System");
                _kernel.Plugins.AddFromObject(new DataLookupPlugin(), "DataLookup");

                TM.App.Log($"[SKChatService] 已注册 {_kernel.Plugins.Count} 个 Plugin");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] Plugin 注册失败: {ex.Message}");
            }
        }

        #endregion

        #region ChatMode 管理

        public TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    PlanModeFilter.IsEnabled = ChatModeSettings.RequiresFunctionConfirmation(value);
                    var sessionId = Sessions.GetCurrentSessionIdOrNull();
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        Sessions.UpdateSessionMode(sessionId, ((int)value).ToString());
                    }
                    TM.App.Log($"[SKChatService] 切换模式: {value}, Filter={PlanModeFilter.IsEnabled}");
                }
            }
        }

        public void BeginDraftSession()
        {
            _ = System.Threading.Tasks.Task.Run(SaveCurrentConversationIndex);
            Sessions.ResetCurrentSession();
            _chatHistory = new ChatHistory();
            _memoryExtractor.ClearMemory();
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            _vectorSearch.SwitchConversationSession(null, forceClear: true);
            _isSessionCompressed = false;
            TM.App.Log("[SKChatService] 已进入草稿会话状态");
        }

        public void DeleteCurrentSession()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
            {
                _chatHistory = new ChatHistory();
                _memoryExtractor.ClearMemory();
                _turnIndex = 0;
                _ragProvider?.ResetDedup();
                _isSessionCompressed = false;
                return;
            }

            try
            {
                var indexPath = GetConversationIndexPath(sessionId);
                if (System.IO.File.Exists(indexPath))
                    System.IO.File.Delete(indexPath);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] 删除对话索引文件失败（非致命）: {ex.Message}");
            }

            Sessions.DeleteSession(sessionId);
            Sessions.ResetCurrentSession();
            _chatHistory = new ChatHistory();
            _memoryExtractor.ClearMemory();
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            _vectorSearch.SwitchConversationSession(null, forceClear: true);
            _isSessionCompressed = false;
            TM.App.Log($"[SKChatService] 当前会话已删除: {sessionId}");
        }

        public PromptExecutionSettings GetCurrentModeSettings(int? overrideMaxTokens = null)
        {
            var settings = ChatModeSettings.GetExecutionSettings(_currentMode, _chatHistory, overrideMaxTokens: overrideMaxTokens);

            try
            {
                var config = AI.GetActiveConfiguration();
                if (config != null && settings.FunctionChoiceBehavior != null)
                {
                    if (AI.IsCompatibilityFallbackEnabled(config.ProviderId, config.ModelId))
                    {
                        TM.App.Log($"[SKChatService] 兼容回退已启用，禁用函数调用: {config.ProviderId}/{config.ModelId}");
                        settings.FunctionChoiceBehavior = null;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] 检查函数调用支持失败: {ex.Message}");
            }

            try
            {
                var config = AI.GetActiveConfiguration();
                if (config != null)
                    ThinkingRouter.InjectRequestParameters(settings, _currentProviderType, config.ModelId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] 注入思考参数失败（非致命）: {ex.Message}");
            }

            return settings;
        }

        #endregion

        #region 上下文用量估算

        public (int EstimatedTokens, int ContextWindow, double UsagePercent) GetContextUsage(string? additionalText = null, int? overrideContextWindow = null)
        {
            var config = AI.GetActiveConfiguration();
            if (config == null || string.IsNullOrEmpty(config.ModelId))
            {
                return (0, 0, 0);
            }

            return _compression.GetContextUsage(_chatHistory, config.ModelId, additionalText, overrideContextWindow);
        }

        private void TriggerBackgroundMemoryExtraction()
        {
            const int llmExtractThreshold = 200;

            var currentMemory = _memoryExtractor.HasMemory() ? _memoryExtractor.ToTextFormat() : null;
            if ((currentMemory?.Length ?? 0) >= llmExtractThreshold)
            {
                return;
            }

            if (_chatHistory.Count <= 2)
            {
                return;
            }

            var recentAssistant = _chatHistory
                .Where(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)
                .TakeLast(3)
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            var contentForLLM = string.Join("\n---\n", recentAssistant);
            if (contentForLLM.Length <= 100)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _memoryExtractor.ExtractWithLLMAsync(contentForLLM);
                    TM.App.Log($"[SKChatService] 后台LLM记忆预抽取完成，长度: {(_memoryExtractor.HasMemory() ? _memoryExtractor.ToTextFormat()?.Length ?? 0 : 0)}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKChatService] 后台LLM记忆预抽取失败（非致命）: {ex.Message}");
                }
            });
        }

        private async Task EnsureCompressionIfNeededAsync(string? upcomingText, CancellationToken cancellationToken, int? overrideContextWindow = null)
        {
            var config = AI.GetActiveConfiguration();
            if (config == null || string.IsNullOrEmpty(config.ModelId))
            {
                return;
            }

            var (_, contextWindow, usagePercent) = _compression.GetContextUsage(_chatHistory, config.ModelId, upcomingText, overrideContextWindow);
            bool willCompress = usagePercent >= 90;
            if (!willCompress)
            {
                return;
            }

            var structuredMemory = _memoryExtractor.HasMemory() ? _memoryExtractor.ToTextFormat() : null;
            const int structuredMemoryMaxChars = 2000;

            if (!string.IsNullOrEmpty(structuredMemory) && structuredMemory.Length > structuredMemoryMaxChars)
            {
                structuredMemory = structuredMemory[..structuredMemoryMaxChars];
            }

            if (willCompress)
            {
                GlobalToast.Info("上下文压缩", $"正在压缩对话历史（用量{usagePercent:F0}%）...", 2000);
            }

            var (compressedHistory, compressed) = await _compression.EnsureCompressionIfNeededAsync(
                _chatHistory,
                config.ModelId,
                upcomingText,
                cancellationToken,
                overrideContextWindow,
                structuredMemory);

            if (!compressed)
            {
                return;
            }

            _chatHistory = compressedHistory;
            _isSessionCompressed = true;
            TM.App.Log($"[SKChatService] 会话已压缩，剩余消息数: {_chatHistory.Count}");
            GlobalToast.Success("压缩完成", $"保留{_chatHistory.Count}条消息", 2000);
        }

        private int GetModelContextWindow(string modelId)
        {
            var model = _ai.GetModelById(modelId);
            if (model != null && model.ContextWindow > 0)
            {
                return model.ContextWindow;
            }

            var config = _ai.GetActiveConfiguration();
            if (config != null && config.ContextWindow > 0)
            {
                return config.ContextWindow;
            }

            var modelName = model?.Name?.ToLower() ?? modelId.ToLower();

            if (modelName.Contains("claude"))
            {
                return 200000;
            }

            if (modelName.Contains("gpt-4"))
            {
                return 128000;
            }

            if (modelName.Contains("qwen"))
            {
                if (modelName.Contains("long") || modelName.Contains("max"))
                {
                    return 1000000;
                }
                return 128000;
            }

            if (modelName.Contains("deepseek"))
            {
                return 128000;
            }

            if (modelName.Contains("gemini"))
            {
                return 1000000;
            }

            return 128000;
        }

        #endregion

        #region 会话管理

        public void NewSession(string? title = null)
        {
            SaveCurrentConversationIndex();
            SaveCurrentMemory();

            Sessions.CreateSession(title);
            _chatHistory = new ChatHistory();
            _memoryExtractor.ClearMemory();
            _vectorSearch.SwitchConversationSession(Sessions.GetCurrentSessionId());
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            TM.App.Log("[SKChatService] 新会话已创建");
            _isSessionCompressed = false;
        }

        public void SwitchSession(string sessionId)
        {
            SaveCurrentConversationIndex();
            SaveCurrentMemory();

            _chatHistory = Sessions.SwitchSession(sessionId);
            _memoryExtractor.ClearMemory();
            _vectorSearch.SwitchConversationSession(sessionId);
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            _isSessionCompressed = false;

            LoadConversationIndexForSession(sessionId);
            LoadMemoryForSession(sessionId);

            TM.App.Log($"[SKChatService] 已切换到会话: {sessionId}");
        }

        public async System.Threading.Tasks.Task SwitchSessionAsync(string sessionId)
        {
            var saveIndexTask = System.Threading.Tasks.Task.Run(SaveCurrentConversationIndex);
            await SaveCurrentMemoryAsync().ConfigureAwait(false);
            await saveIndexTask.ConfigureAwait(false);

            var records = await Sessions.LoadMessagesAsync(sessionId).ConfigureAwait(false);

            _chatHistory = Sessions.SwitchSessionWithRecords(sessionId, records);
            _memoryExtractor.ClearMemory();
            _vectorSearch.SwitchConversationSession(sessionId);
            _turnIndex = 0;
            _ragProvider?.ResetDedup();
            _isSessionCompressed = false;

            LoadConversationIndexForSession(sessionId);
            await LoadMemoryForSessionAsync(sessionId).ConfigureAwait(false);

            TM.App.Log($"[SKChatService] 已切换到会话: {sessionId}");
        }

        public ChatHistory GetChatHistory() => _chatHistory;

        private void SaveCurrentConversationIndex()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId)) return;

            try
            {
                var path = GetConversationIndexPath(sessionId);
                if (_vectorSearch.IndexedConversationTurns == 0)
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                    return;
                }

                _vectorSearch.SaveConversationIndex(path);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] 保存对话索引失败（非致命）: {ex.Message}");
            }
        }

        private void LoadConversationIndexForSession(string sessionId)
        {
            try
            {
                var path = GetConversationIndexPath(sessionId);
                _vectorSearch.LoadConversationIndex(path);
                _turnIndex = _vectorSearch.IndexedConversationTurns;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] 加载对话索引失败（非致命）: {ex.Message}");
            }
        }

        private static string GetConversationIndexPath(string sessionId)
        {
            return System.IO.Path.Combine(
                StoragePathHelper.GetCurrentProjectPath(),
                "Sessions",
                $"{sessionId}.conversation_turns.json");
        }

        private static string GetMemoryPath(string sessionId)
        {
            return System.IO.Path.Combine(
                StoragePathHelper.GetCurrentProjectPath(),
                "Sessions",
                $"{sessionId}.memory.json");
        }

        private void SaveCurrentMemory()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId)) return;
            _memoryExtractor.SaveToFile(GetMemoryPath(sessionId));
        }

        private async System.Threading.Tasks.Task SaveCurrentMemoryAsync()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId)) return;
            await _memoryExtractor.SaveToFileAsync(GetMemoryPath(sessionId)).ConfigureAwait(false);
        }

        private void LoadMemoryForSession(string sessionId)
        {
            _memoryExtractor.LoadFromFile(GetMemoryPath(sessionId));
        }

        private async System.Threading.Tasks.Task LoadMemoryForSessionAsync(string sessionId)
        {
            await _memoryExtractor.LoadFromFileAsync(GetMemoryPath(sessionId)).ConfigureAwait(false);
        }

        #region R1

        public void SaveMessages(IEnumerable<UIMessageItem> messages)
        {
            var records = messages.Select(m => m.ToSerializedRecord()).ToList();
            Sessions.SaveCurrentMessages(records);

            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (!string.IsNullOrEmpty(sessionId))
            {
                Sessions.UpdateSessionMode(sessionId, ((int)_currentMode).ToString());
            }

            _chatHistory = Sessions.RebuildChatHistory(records);

            SaveCurrentMemory();
        }

        public List<SerializedMessageRecord> LoadMessages()
        {
            return Sessions.LoadCurrentMessages();
        }

        public async System.Threading.Tasks.Task<List<SerializedMessageRecord>> LoadMessagesAsync()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
                return new List<SerializedMessageRecord>();
            return await Sessions.LoadMessagesAsync(sessionId).ConfigureAwait(false);
        }

        public void RebuildHistoryFromMessages(IEnumerable<UIMessageItem> messages)
        {
            var list = messages.ToList();
            var records = list.Select(m => m.ToSerializedRecord()).ToList();
            _chatHistory = Sessions.RebuildChatHistory(records);

            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            var turns = new List<(int TurnIndex, string User, string Assistant)>();
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].IsUser && list[i + 1].IsAssistant)
                {
                    turns.Add((turns.Count, list[i].Content ?? string.Empty, list[i + 1].Content ?? string.Empty));
                    i++;
                }
            }

            _turnIndex = turns.Count;

            if (!string.IsNullOrEmpty(sessionId))
            {
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        _vectorSearch.SwitchConversationSession(sessionId, forceClear: true);
                        foreach (var t in turns)
                        {
                            _vectorSearch.IndexConversationTurn(t.TurnIndex, t.User, t.Assistant);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SKChatService] 重建对话索引失败（非致命）: {ex.Message}");
                    }
                });
            }

            TM.App.Log($"[SKChatService] 重建 ChatHistory，消息数: {_chatHistory.Count}");
        }

        #endregion

        private static bool IsLocalEndpoint(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "localhost"
                || host == "127.0.0.1"
                || host == "::1"
                || host.StartsWith("192.168.")
                || host.StartsWith("10.")
                || (host.StartsWith("172.") && System.Net.IPAddress.TryParse(host, out var ip)
                    && ip.GetAddressBytes() is { } b && b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }

        private static string EnsureApiVersion(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            var normalized = url.TrimEnd('/');
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"/v\d+(/|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return normalized;
            return normalized + "/v1";
        }

        private static string ResolveProtocol(string? baseUrl, string providerNameLower)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var url = baseUrl.ToLower();

                if (url.Contains("anthropic.com"))
                    return "anthropic";

                if (url.Contains("googleapis.com") ||
                    url.Contains("generativelanguage.google") ||
                    url.Contains("aiplatform.google"))
                    return "gemini";

                if (url.Contains("openai.azure.com") ||
                    url.Contains(".cognitiveservices.azure.com"))
                    return "azure-openai";

                return "openai-compat";
            }

            if (providerNameLower.Contains("anthropic") || providerNameLower.Contains("claude"))
                return "anthropic";

            if (providerNameLower.Contains("gemini") || providerNameLower.Contains("google"))
                return "gemini";

            if (providerNameLower.Contains("azure"))
                return "azure-openai";

            return "openai-compat";
        }

        private static string StripModelNamePrefix(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return modelName;

            while (modelName.StartsWith('['))
            {
                var closeBracket = modelName.IndexOf(']');
                if (closeBracket < 0 || closeBracket >= modelName.Length - 1)
                    break;
                modelName = modelName[(closeBracket + 1)..];
            }

            if (modelName.EndsWith("-free", StringComparison.OrdinalIgnoreCase))
            {
                modelName = modelName[..^5];
            }

            return modelName;
        }

        #endregion

        #region Inner Types

        private sealed class CherryStudioDelegatingHandler : DelegatingHandler
        {
            private const string ChromeUA =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36";

            private readonly string _apiKey;

            public CherryStudioDelegatingHandler(string apiKey, HttpMessageHandler inner) : base(inner)
            {
                _apiKey = apiKey;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", ChromeUA);

                if (!request.Headers.Contains("HTTP-Referer"))
                    request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://cherry-ai.com");

                if (!request.Headers.Contains("X-Title"))
                    request.Headers.TryAddWithoutValidation("X-Title", "Cherry Studio");

                if (!string.IsNullOrWhiteSpace(_apiKey) && !request.Headers.Contains("X-Api-Key"))
                    request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);

                return base.SendAsync(request, cancellationToken);
            }
        }

        #endregion

        private sealed class AlreadyNotifiedApiException : Exception
        {
            public AlreadyNotifiedApiException(string message, Exception? inner = null)
                : base(message, inner) { }
        }

        #region 密钥轮询 + 统一错误处理

        private async Task<T> InvokeApiWithRotationAsync<T>(
            Func<CancellationToken, Task<T>> apiCall,
            CancellationToken ct,
            int maxKeyRetries = 2)
        {
            var config = AI.GetActiveConfiguration();
            if (config == null) throw new InvalidOperationException("未配置 AI 模型");

            var rotation = ServiceLocator.Get<TM.Services.Framework.AI.Core.ApiKeyRotationService>();
            var excludeKeyIds = new HashSet<string>();
            var failedKeyDetails = new List<string>();

            var poolSize = rotation.GetPoolStatus(config.ProviderId)?.ActiveKeys ?? maxKeyRetries + 1;
            var effectiveMaxRetries = Math.Max(poolSize - 1, maxKeyRetries);
            var allKeysExhausted = false;

            for (int attempt = 0; attempt <= effectiveMaxRetries; attempt++)
            {
                var selection = rotation.GetNextKey(config.ProviderId, excludeKeyIds);
                if (selection == null) { allKeysExhausted = true; break; }

                if (!string.Equals(config.ApiKey, selection.ApiKey, StringComparison.Ordinal))
                {
                    config.ApiKey = selection.ApiKey;
                    EnsureKernelInitialized();
                }

                try
                {
                    var result = await apiCall(ct);
                    rotation.ReportKeyResult(config.ProviderId, selection.KeyId, TM.Services.Framework.AI.Core.KeyUseResult.Success);
                    return result;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var (useResult, rawMessage) = ClassifyException(ex);
                    rotation.ReportKeyResult(config.ProviderId, selection.KeyId, useResult, rawMessage);

                    var keyLabel = !string.IsNullOrWhiteSpace(selection.Remark)
                        ? selection.Remark
                        : selection.ApiKey.Length > 10 ? selection.ApiKey[..10] + "..." : selection.ApiKey;
                    failedKeyDetails.Add($"[{keyLabel}] → {rawMessage}");

                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.NetworkError)
                    {
                        NotifyRealError("网络连接失败", rawMessage);
                        throw new AlreadyNotifiedApiException(rawMessage, ex);
                    }

                    if (useResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                        or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                        or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted
                        or TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                    {
                        NotifyKeyError(useResult, keyLabel, rawMessage);
                        excludeKeyIds.Add(selection.KeyId);
                        if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                        {
                            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                            catch (OperationCanceledException) { throw; }
                        }
                        continue;
                    }

                    if ((useResult == TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                         || useResult == TM.Services.Framework.AI.Core.KeyUseResult.Unknown)
                        && attempt < maxKeyRetries)
                    {
                        excludeKeyIds.Add(selection.KeyId);
                        continue;
                    }

                    NotifyRealError("AI 请求失败", rawMessage);
                    throw new AlreadyNotifiedApiException(rawMessage, ex);
                }
            }

            if (allKeysExhausted && failedKeyDetails.Count > 0)
                NotifyAllKeysExhausted(failedKeyDetails);
            else if (failedKeyDetails.Count > 0)
                NotifyRealError("AI 请求失败", $"已重试 {failedKeyDetails.Count} 次，均失败");
            throw new AlreadyNotifiedApiException($"所有密钥不可用:\n{string.Join("\n", failedKeyDetails)}");
        }

        private static (TM.Services.Framework.AI.Core.KeyUseResult Type, string RawMessage) ClassifyException(Exception ex)
        {
            if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                var rawMsg = ex.Message;

                var msgLower = rawMsg.ToLowerInvariant();
                if (msgLower.Contains("insufficient_quota") || msgLower.Contains("billing_hard_limit")
                    || msgLower.Contains("credit balance"))
                {
                    return (TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted, rawMsg);
                }

                var type = code switch
                {
                    401 => TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure,
                    403 => TM.Services.Framework.AI.Core.KeyUseResult.Forbidden,
                    429 => TM.Services.Framework.AI.Core.KeyUseResult.RateLimited,
                    402 => TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted,
                    >= 500 => TM.Services.Framework.AI.Core.KeyUseResult.ServerError,
                    _ => TM.Services.Framework.AI.Core.KeyUseResult.Unknown
                };
                return (type, rawMsg);
            }

            if (ex is System.Net.Http.HttpRequestException { InnerException: System.Net.Sockets.SocketException })
                return (TM.Services.Framework.AI.Core.KeyUseResult.NetworkError, ex.Message);

            return (TM.Services.Framework.AI.Core.KeyUseResult.Unknown, ex.Message);
        }

        private static void NotifyKeyError(TM.Services.Framework.AI.Core.KeyUseResult type, string keyLabel, string rawMessage)
        {
            try
            {
                var (title, isError) = type switch
                {
                    TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure    => ("密钥认证失败", false),
                    TM.Services.Framework.AI.Core.KeyUseResult.Forbidden      => ("密钥已被封禁", true),
                    TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted => ("密钥额度用完", false),
                    TM.Services.Framework.AI.Core.KeyUseResult.RateLimited    => ("密钥限速", false),
                    _ => ("密钥错误", false)
                };
                var body = $"{keyLabel} 已自动禁用并切换到下一个密钥。\n原始错误：{rawMessage}";
                if (isError) GlobalToast.Error(title, body);
                else GlobalToast.Warning(title, body);
            }
            catch {}
        }

        private static void NotifyRealError(string title, string rawMessage)
        {
            try { GlobalToast.Error(title, rawMessage); }
            catch { }
        }

        private static void NotifyAllKeysExhausted(List<string> failedDetails)
        {
            try
            {
                var detail = failedDetails.Count > 0
                    ? $"已尝试 {failedDetails.Count} 个密钥，全部失败：\n{string.Join("\n", failedDetails)}\n请在模型管理中添加新密钥或检查现有密钥状态。"
                    : "未配置 API 密钥，请在模型管理中配置后重试。";
                GlobalToast.Error("所有密钥不可用", detail);
            }
            catch { }
        }

        #endregion

        #region 错误提示（旧版，仅保留兼容）

        [Obsolete("已被 InvokeApiWithRotationAsync 中的精准通知替代")]
        private static void NotifyAIError(Exception ex)
        {
            try
            {
                var msg = (ex.Message + " " + (ex.InnerException?.Message ?? string.Empty)).ToLowerInvariant();

                if (ex is System.Net.Http.HttpRequestException httpEx &&
                    httpEx.InnerException is System.Net.Sockets.SocketException)
                {
                    GlobalToast.Error("端点不可达", "无法连接到服务地址，请检查端点配置是否正确");
                }
                else if (ContainsAny(msg, "401", "unauthorized", "invalid api key", "authentication failed"))
                {
                    GlobalToast.Error("认证失败", "API Key 无效或已过期，请在模型配置中更新");
                }
                else if (ContainsAny(msg, "429", "rate limit", "too many requests"))
                {
                    GlobalToast.Warning("频率限制", "请求过于频繁，请稍后重试或切换模型");
                }
                else if (ContainsAny(msg, "context_length", "context length", "token limit", "maximum context"))
                {
                    GlobalToast.Warning("上下文过长", "对话历史超出模型限制，请清理历史记录后重试");
                }
                else if (ContainsAny(msg, "502", "503", "504", "service unavailable", "bad gateway", "overloaded"))
                {
                    GlobalToast.Warning("服务不可用", "AI 服务暂时故障，请稍后重试");
                }
                else if (ContainsAny(msg, "timeout", "timed out"))
                {
                    GlobalToast.Error("请求超时", "请检查网络或代理连接");
                }
                else
                {
                    var brief = ex.Message.Length > 60 ? ex.Message[..60] + "…" : ex.Message;
                    GlobalToast.Error("AI 请求失败", brief);
                }
            }
            catch
            {
            }
        }

        private static bool ContainsAny(string source, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (source.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        #endregion
    }
}
