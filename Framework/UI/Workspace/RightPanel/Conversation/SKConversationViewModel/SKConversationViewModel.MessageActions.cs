using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 导出消息

        private void ExportMessages()
        {
            var items = IsMultiSelectMode && SelectedMessages.Count > 0
                ? SelectedMessages.ToList()
                : (SelectedMessage != null ? new System.Collections.Generic.List<UIMessageItem> { SelectedMessage } : new System.Collections.Generic.List<UIMessageItem>());

            if (items.Count == 0) return;

            try
            {
                bool exportAsMarkdown = StandardDialog.ShowConfirm(
                    "选择“是”导出为 Markdown，选择“否”导出为 JSON。", "选择导出格式") == true;

                if (exportAsMarkdown)
                {
                    var parts = items.Select(m =>
                    {
                        string role = m.IsUser ? "用户" : (m.IsAssistant ? "助手" : m.Role.Label);
                        return $"### {role} @ {m.Timestamp:HH:mm:ss}\n\n{m.Content}";
                    });

                    string mdAll = string.Join("\n\n---\n\n", parts);
                    System.Windows.Clipboard.SetText(mdAll);
                    GlobalToast.Success("已导出", "消息已以 Markdown 形式复制到剪贴板");
                }
                else
                {
                    var data = items.Select(m => new
                    {
                        role = m.IsUser ? "user" : (m.IsAssistant ? "assistant" : m.Role.Label),
                        timestamp = m.Timestamp,
                        content = m.Content
                    });

                    string json = JsonSerializer.Serialize(data, JsonHelper.Default);
                    System.Windows.Clipboard.SetText(json);
                    GlobalToast.Success("已导出", "消息已以 JSON 形式复制到剪贴板");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 导出消息失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败：{ex.Message}", "导出失败");
            }
        }

        #endregion

        #region 消息操作

        public async Task CopyMessageAsync(UIMessageItem message)
        {
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(message.Content);
                    GlobalToast.Success("已复制", "消息内容已复制到剪贴板");
                    return;
                }
                catch (Exception) when (i < maxRetries - 1)
                {
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKConversation] 复制消息失败: {ex.Message}");
                    GlobalToast.Warning("复制失败", "剪贴板被占用，请稍后重试");
                }
            }
        }

        public void ToggleStar(UIMessageItem message)
        {
            message.IsStarred = !message.IsStarred;
        }

        public void DeleteMessage(UIMessageItem message)
        {
            Messages.Remove(message);
            _chatService.RebuildHistoryFromMessages(Messages);
            RefreshContextUsage();
        }

        public void DeleteUserWithAssistant(UIMessageItem message)
        {
            if (!message.IsUser)
            {
                return;
            }

            var index = Messages.IndexOf(message);
            if (index < 0)
            {
                return;
            }

            if (index < Messages.Count - 1)
            {
                var next = Messages[index + 1];
                if (next.IsAssistant)
                {
                    Messages.Remove(next);
                }
            }

            Messages.Remove(message);
            _chatService.RebuildHistoryFromMessages(Messages);
            RefreshContextUsage();
        }

        public void RecallToInput(UIMessageItem message)
        {
            if (!message.IsUser)
            {
                return;
            }

            InputText = message.Content;

            var index = Messages.IndexOf(message);
            if (index < 0)
            {
                return;
            }

            for (int i = Messages.Count - 1; i >= index; i--)
            {
                Messages.RemoveAt(i);
            }

            _chatService.RebuildHistoryFromMessages(Messages);
            RefreshContextUsage();
        }

        public async Task RegenerateAsync(UIMessageItem message)
        {
            UIMessageItem? userMessage = null;

            if (message.IsUser)
            {
                userMessage = message;
                var index = Messages.IndexOf(message);
                if (index >= 0 && index < Messages.Count - 1)
                {
                    var nextMsg = Messages[index + 1];
                    if (nextMsg.IsAssistant)
                    {
                        Messages.Remove(nextMsg);
                    }
                }
            }
            else if (message.IsAssistant)
            {
                var index = Messages.IndexOf(message);
                if (index <= 0)
                {
                    return;
                }

                for (int i = index - 1; i >= 0; i--)
                {
                    if (Messages[i].IsUser)
                    {
                        userMessage = Messages[i];
                        break;
                    }
                }

                if (userMessage == null)
                {
                    return;
                }

                Messages.Remove(message);
            }

            if (userMessage == null)
            {
                return;
            }

            _chatService.RebuildHistoryFromMessages(Messages);

            await RegenerateResponseAsync(userMessage.Content);
        }

        public async Task RegenerateFromHereAsync(UIMessageItem message)
        {
            if (IsGenerating) return;

            var index = Messages.IndexOf(message);
            if (index < 0) return;

            UIMessageItem? userMessage = null;
            int truncateFrom;

            if (message.IsUser)
            {
                userMessage = message;
                truncateFrom = index + 1;
            }
            else if (message.IsAssistant)
            {
                for (int i = index - 1; i >= 0; i--)
                {
                    if (Messages[i].IsUser)
                    {
                        userMessage = Messages[i];
                        break;
                    }
                }

                if (userMessage == null) return;
                truncateFrom = index;
            }
            else
            {
                return;
            }

            var removedCount = Messages.Count - truncateFrom;
            if (removedCount > 0)
            {
                for (int i = Messages.Count - 1; i >= truncateFrom; i--)
                {
                    Messages.RemoveAt(i);
                }
                TM.App.Log($"[SKConversationViewModel] 对话分支：截断 {removedCount} 条消息，从第 {truncateFrom} 条开始");
            }

            _chatService.RebuildHistoryFromMessages(Messages);

            await RegenerateResponseAsync(userMessage.Content);
        }

        private async Task RegenerateResponseAsync(string userText)
        {
            if (IsGenerating)
            {
                return;
            }

            IsGenerating = true;
            var startTime = DateTime.Now;
            UIMessageItem? assistantMessage = null;

            try
            {
                assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = "Thinking...";
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

                assistantMessage.Thinking.WriteStatus("等待端点响应...");

                var finalUserText = userText;

                var promptParts = ChatPromptBridge.BuildParts(CurrentMode, finalUserText);

                var pendingContentQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                int contentDispatchFlag = 0;

                void DrainContentQueue()
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        System.Threading.Interlocked.Exchange(ref contentDispatchFlag, 0);
                        return;
                    }

                    var sb = new StringBuilder();
                    while (sb.Length < 2048 && pendingContentQueue.TryDequeue(out var c))
                        sb.Append(c);

                    if (sb.Length > 0)
                        assistantMessage?.AppendContent(sb.ToString());

                    System.Threading.Interlocked.Exchange(ref contentDispatchFlag, 0);
                    if (!pendingContentQueue.IsEmpty
                        && System.Threading.Interlocked.CompareExchange(ref contentDispatchFlag, 1, 0) == 0)
                    {
                        dispatcher.InvokeAsync(DrainContentQueue, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }

                string result;

                _chatService.SetLastRunId(Guid.Empty);
                using var progressBridge = new ThinkingProgressBridge(
                    assistantMessage, () => _chatService.LastRunId, CurrentMode);

                string? lastStatusSeen = "等待端点响应...";

                bool hasRealThinkingChunk = false;

                var regenRunType = TM.Services.Framework.AI.SemanticKernel.RunType.Chat;
                if (CurrentMode == TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode.Edit && ShouldUseTools(userText))
                    regenRunType = TM.Services.Framework.AI.SemanticKernel.RunType.Task;
                string[]? forcedFunctions = null;
                if (regenRunType != TM.Services.Framework.AI.SemanticKernel.RunType.Chat && IsBusinessFactQuery(userText))
                    forcedFunctions = GetForcedFunctionNames(userText);
                using (_chatService.UseTransientMode(CurrentMode, regenRunType, forcedFunctions))
                {
                    result = await _chatService.SendStreamMessageAsync(
                        userText,
                        promptParts,
                        chunk =>
                        {
                            pendingContentQueue.Enqueue(SanitizeBubbleChunk(chunk));
                            if (System.Threading.Interlocked.CompareExchange(ref contentDispatchFlag, 1, 0) == 0)
                            {
                                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                    DrainContentQueue,
                                    System.Windows.Threading.DispatcherPriority.Background);
                            }
                        },
                        thinkingChunk =>
                        {
                            if (!string.IsNullOrEmpty(thinkingChunk))
                            {
                                hasRealThinkingChunk = true;
                                var kind = UIMessageItem.NormalizeAnalysisKind(_chatService.LastThinkingKind);
                                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                {
                                    if (!string.Equals(assistantMessage.AnalysisKind, kind, StringComparison.Ordinal))
                                        assistantMessage.AnalysisKind = kind;
                                    if (!assistantMessage.AnalysisDurationSeconds.HasValue)
                                        assistantMessage.AnalysisSummary = $"{kind}...";
                                });
                            }
                            assistantMessage?.Thinking.EnqueueRaw(thinkingChunk);
                        },
                        System.Threading.CancellationToken.None,
                        status =>
                        {
                            if (string.IsNullOrEmpty(status) || status == lastStatusSeen) return;
                            lastStatusSeen = status;
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                assistantMessage.Thinking.WriteStatus(status);
                            });
                        });
                }

                var dispatcher5 = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher5 != null)
                {
                    bool isError = false;
                    IConversationMessageMapper? mapperForMapping = null;
                    string? thinkingForMapping = null;
                    await dispatcher5.InvokeAsync(() =>
                    {

                        var sb = new StringBuilder();
                        while (pendingContentQueue.TryDequeue(out var c))
                            sb.Append(c);
                        if (sb.Length > 0)
                            assistantMessage?.AppendContent(sb.ToString());

                        var runId = _chatService.LastRunId;
                        assistantMessage!.RunId = runId;

                        if (!string.IsNullOrEmpty(assistantMessage.ThinkingContent) && !assistantMessage.AnalysisDurationSeconds.HasValue)
                        {
                            var seconds = Math.Max(0.1, (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds);
                            assistantMessage.AnalysisDurationSeconds = seconds;
                            assistantMessage.AnalysisSummary = UIMessageItem.FormatAnalysisSummary(assistantMessage.AnalysisKind, seconds);
                        }

                        assistantMessage.FlushThinkingImmediately();

                        var (isRetryCancelled, _) = UIMessageItem.TryExtractCancelledPartial(result);
                        var isResultEmpty = string.IsNullOrWhiteSpace(result);
                        bool isErrorResult = result.StartsWith("[错误]", StringComparison.Ordinal) || isResultEmpty;

                        if (!hasRealThinkingChunk && !isErrorResult && !isRetryCancelled)
                            AppendNoThinkingFeedback(assistantMessage);

                        assistantMessage.FinishStreaming();

                        if (isErrorResult || isRetryCancelled)
                        {
                            assistantMessage.IsError = true;
                            isError = true;

                            if (isRetryCancelled)
                            {
                                assistantMessage.StatusText = "已取消";
                                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                                    assistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                            }
                            else if (isResultEmpty)
                            {
                                const string defaultErr = "模型未返回任何内容。可能原因：端点无响应、API 密钥无效、模型配置错误或网络故障。";
                                assistantMessage.StatusText = defaultErr;
                                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                                    assistantMessage.Content = SanitizeFinalBubbleContent("[错误] " + defaultErr);
                            }
                            else
                            {
                                const string errPrefix = "[错误] ";
                                assistantMessage.StatusText = result.StartsWith(errPrefix, StringComparison.Ordinal)
                                    ? result[errPrefix.Length..]
                                    : result;
                                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                                    assistantMessage.Content = SanitizeFinalBubbleContent(result);
                            }
                        }
                        else
                        {
                            assistantMessage.StatusText = null;
                            mapperForMapping = CurrentProfile.Mapper;
                            thinkingForMapping = assistantMessage.ThinkingContent;
                        }
                    });

                    if (!isError && mapperForMapping != null)
                    {
                        var convMessage = await Task.Run(async () => await mapperForMapping.MapFromStreamingResultAsync(userText, result, thinkingForMapping).ConfigureAwait(false));
                        await dispatcher5.InvokeAsync(() =>
                        {
                            assistantMessage.ApplyFromConversationMessage(convMessage);
                        });
                    }
                }

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();
                RefreshContextUsage();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 重新生成失败: {ex.Message}");

                if (assistantMessage != null)
                {
                    assistantMessage.IsThinking = false;
                    assistantMessage.IsError = true;
                    assistantMessage.Content = $"重新生成失败：{ex.Message}";
                    assistantMessage.FinishStreaming();
                }
                GlobalToast.Error("重新生成失败", $"重新生成失败：{ex.Message}");
            }
            finally
            {
                IsGenerating = false;

                FinalizeAssistantMessageIfIncomplete(assistantMessage, startTime);
            }
        }

        private static void FinalizeAssistantMessageIfIncomplete(UIMessageItem? assistantMessage, DateTime startTime)
        {
            if (assistantMessage == null) return;

            bool incomplete = assistantMessage.IsThinking
                           || assistantMessage.IsStreaming
                           || string.IsNullOrWhiteSpace(assistantMessage.Content);
            if (!incomplete) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            Action finalize = () =>
            {
                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                {
                    assistantMessage.IsError = true;
                    assistantMessage.StatusText = null;
                    assistantMessage.Content = SanitizeFinalBubbleContent(
                        "[错误] 未收到模型响应。可能原因：端点无响应、网络中断或请求超时。请检查端点/密钥或切换其他模型重试。");
                }

                if (!assistantMessage.AnalysisDurationSeconds.HasValue)
                {
                    var seconds = Math.Max(0.1, (DateTime.Now - startTime).TotalSeconds);
                    assistantMessage.AnalysisDurationSeconds = seconds;
                    assistantMessage.AnalysisSummary = UIMessageItem.FormatAnalysisSummary(assistantMessage.AnalysisKind, seconds);
                }

                assistantMessage.IsThinking = false;
                assistantMessage.FinishStreaming();

                TM.App.Log($"[SKConversationViewModel] 终态兜底触发: IsError={assistantMessage.IsError}, Content长度={assistantMessage.Content?.Length ?? 0}");
            };

            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(finalize);
            else
                finalize();
        }

        private static void AppendNoThinkingFeedback(UIMessageItem message)
        {
            message.Thinking.WriteCompletion("已回复，当前模型未返回思考");
        }

        #endregion

        #region 其他消息操作

        private void EditUserMessage()
        {
            var userMessage = SelectedMessage;
            if (userMessage == null || !userMessage.IsUser) return;

            RecallToInput(userMessage);
        }

        private async Task SwitchModelAnswerAsync()
        {
            var assistantMessage = SelectedMessage;
            if (assistantMessage == null || !assistantMessage.IsAssistant)
            {
                return;
            }

            var activeConfig = ActiveConfiguration;
            if (activeConfig == null)
            {
                GlobalToast.Warning("模型切换", "当前未选择模型，无法切换回答。");
                return;
            }

            try
            {
                _aiService.SetActiveConfiguration(activeConfig.Id);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 切换模型回答失败: {ex.Message}");
                GlobalToast.Error("模型切换失败", $"模型切换失败：{ex.Message}");
                return;
            }

            var index = Messages.IndexOf(assistantMessage);
            if (index <= 0)
            {
                return;
            }

            UIMessageItem? userMessage = null;
            for (int i = index - 1; i >= 0; i--)
            {
                if (Messages[i].IsUser)
                {
                    userMessage = Messages[i];
                    break;
                }
            }

            if (userMessage == null)
            {
                return;
            }

            var text = userMessage.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Messages.Remove(assistantMessage);
            InputText = text;
            await SendMessageAsync();
            _chatService.RebuildHistoryFromMessages(Messages);
            RefreshContextUsage();
        }

        private async Task TranslateMessageAsync()
        {
            var assistantMessage = SelectedMessage;
            if (assistantMessage == null || !assistantMessage.IsAssistant)
            {
                return;
            }

            var content = assistantMessage.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            string instruction;

            if (IsProbablyEnglish(content))
            {
                instruction = "请把下面这段英文内容准确翻译成简体中文，只返回译文：\n\n" + content;
            }
            else
            {
                instruction = "请把下面这段中文内容准确翻译成英文，只返回译文：\n\n" + content;
            }

            try
            {
                var prompt = instruction;
                InputText = content;

                await SendMessageAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 翻译消息失败: {ex.Message}");
                StandardDialog.ShowError($"翻译失败：{ex.Message}", "翻译失败");
            }
        }

        private static bool IsProbablyEnglish(string text)
        {
            int letterCount = 0;
            int chineseCount = 0;

            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    chineseCount++;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    letterCount++;
                }
            }

            return letterCount > chineseCount;
        }

        #endregion

        #region 多选模式

        private void ToggleMultiSelectMode()
        {
            IsMultiSelectMode = !IsMultiSelectMode;
            SelectedMessages.Clear();
            TM.App.Log($"[SKConversationViewModel] 多选模式: {IsMultiSelectMode}");
        }

        #endregion

        #region 星标消息

        private void ShowStarredMessages()
        {
            try
            {
                var starred = Messages.Where(m => m.IsStarred).ToList();
                if (starred.Count == 0)
                {
                    StandardDialog.ShowInfo("星标消息", "当前没有星标消息。");
                    return;
                }

                var sb = new StringBuilder();
                foreach (var msg in starred)
                {
                    var role = msg.IsUser ? "用户" : (msg.IsAssistant ? "助手" : msg.Role.Label);
                    sb.AppendLine($"[{role} @ {msg.Timestamp:HH:mm:ss}]");
                    sb.AppendLine(msg.Content);
                    sb.AppendLine();
                }

                StandardDialog.ShowInfo("星标消息", sb.ToString());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 显示星标消息失败: {ex.Message}");
                StandardDialog.ShowError($"显示失败：{ex.Message}", "星标消息");
            }
        }

        #endregion
    }
}
