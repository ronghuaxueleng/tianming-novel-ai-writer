using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Config;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;
using ConvPlanStep = TM.Services.Framework.AI.SemanticKernel.Conversation.Models.PlanStep;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        private static bool IsImitateStep(string? title, string? detail)
        {
            return ContainsImitateMarker(detail) || ContainsImitateMarker(title);

            static bool ContainsImitateMarker(string? text)
            {
                if (string.IsNullOrEmpty(text)) return false;
                return text.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("@imitate", StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool _wasExecutionCancelledByUser;

        private UIMessageItem? _currentExecutionAssistantMessage;

        private IReadOnlyList<ConvPlanStep>? _cachedPlanSteps;

        private ConversationModeProfile CurrentProfile => ModeProfileRegistry.GetProfile(_chatService.CurrentMode);

        private void OnSendMessageRequested(string message)
        {
            _ = OnSendMessageRequestedAsync(message);
        }

        private async Task OnSendMessageRequestedAsync(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message) || IsGenerating)
                    return;

                InputText = message;
                await SendMessageAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] SendMessageRequested处理失败: {ex.Message}");
                GlobalToast.Error("发送失败", $"发送失败：{ex.Message}");
            }
        }

        private void OnStartPlanExecutionRequested(IReadOnlyList<(int Index, string Title, string Detail)> steps)
        {
            _ = OnStartPlanExecutionRequestedAsync(steps);
        }

        private async Task OnStartPlanExecutionRequestedAsync(IReadOnlyList<(int Index, string Title, string Detail)> steps)
        {
            try
            {
                if (IsGenerating)
                    return;

                if (steps == null || steps.Count == 0)
                {
                    GlobalToast.Warning("无计划", "步骤列表为空");
                    TM.App.Log("[SKConversationViewModel] 收到空的步骤列表");
                    return;
                }

                TM.App.Log($"[SKConversationViewModel] 收到 {steps.Count} 个步骤，开始执行");

                var requestedNumbersInPlan = new SortedSet<int>();
                int? explicitVolumeInPlan = null;
                var hasVolumeConflict = false;
                var validationTextBuilder = new StringBuilder();
                foreach (var step in steps)
                {
                    var stepTitle = step.Title ?? string.Empty;
                    var stepDetail = step.Detail ?? string.Empty;
                    validationTextBuilder.Append(stepTitle).Append(' ').Append(stepDetail).Append('\n');

                    if (ChapterDirectiveParser.HasRewriteDirective(stepDetail)
                        || ChapterDirectiveParser.HasRewriteDirective(stepTitle)
                        || IsImitateStep(stepTitle, stepDetail))
                    {
                        continue;
                    }

                    var (volume, chapter) = TryExtractStepChapterRequest(stepTitle, stepDetail);
                    if (chapter > 0)
                    {
                        requestedNumbersInPlan.Add(chapter);
                    }

                    if (volume.HasValue && volume.Value > 0)
                    {
                        if (!explicitVolumeInPlan.HasValue)
                        {
                            explicitVolumeInPlan = volume.Value;
                        }
                        else if (explicitVolumeInPlan.Value != volume.Value)
                        {
                            hasVolumeConflict = true;
                        }
                    }
                }

                if (hasVolumeConflict)
                {
                    explicitVolumeInPlan = null;
                }

                var validateError = await ValidateRequestedChaptersAsync(
                    requestedNumbersInPlan,
                    explicitVolumeInPlan,
                    validationTextBuilder.ToString());
                if (!string.IsNullOrWhiteSpace(validateError))
                {
                    var errMsg = validateError;
                    Messages.Add(UIMessageItem.CreateErrorMessage(errMsg));
                    GlobalToast.Warning("已阻止执行", "计划执行前校验未通过，请按提示调整");
                    TM.App.Log($"[SKConversationViewModel] PlanView执行预校验阻断: {errMsg}");
                    return;
                }

                await RunTodoExecutionAsync(
                    ChatMode.Plan,
                    steps,
                    $"Thinking...");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] StartPlanExecutionRequested处理失败: {ex.Message}");
                GlobalToast.Error("执行失败", $"执行失败：{ex.Message}");
            }
        }

        private async Task RunTodoExecutionAsync(
            ChatMode mode,
            IReadOnlyList<(int Index, string Title, string Detail)> steps,
            string analysisSummary)
        {
            UIMessageItem? assistantMessage = null;
            var startTime = DateTime.Now;
            try
            {
                _lastExecutedMode = mode;
                IsGenerating = true;
                MonitorSubTitle = "执行中";

                ShowTodoOverlay = true;

                assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = analysisSummary;
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

                if (mode == ChatMode.Agent)
                {
                    _wasExecutionCancelledByUser = false;
                    _currentExecutionAssistantMessage = assistantMessage;
                }

                var tasks = new List<TodoExecutionTask>();
                var writerPlugin = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.Plugins.WriterPlugin>();

                foreach (var step in steps)
                {
                    var rawTitle = step.Title;
                    var rawDetail = step.Detail;

                    if (IsImitateStep(rawTitle, rawDetail))
                    {
                        var bpRef = TryParseImitateDirective(rawDetail) ?? TryParseImitateDirective(rawTitle);
                        if (string.IsNullOrWhiteSpace(bpRef))
                        {
                            tasks.Add(new TodoExecutionTask
                            {
                                StepIndex = step.Index,
                                Title = rawTitle,
                                Detail = rawDetail,
                                PluginName = "WriterPlugin",
                                FunctionName = "GenerateChapterFromBlueprint",
                                ExecuteAsync = _ => Task.FromResult<string?>("[生成失败] @仿写 指令格式错误，请使用：@仿写:蓝图ID 或 @仿写:蓝图名称")
                            });
                            continue;
                        }

                        var chapterNum = ChapterParserHelper.ExtractChapterNumber(rawTitle);
                        if (chapterNum <= 0) chapterNum = ChapterParserHelper.ExtractChapterNumber(rawDetail);
                        if (chapterNum <= 0) chapterNum = step.Index > 0 ? step.Index : 1;

                        var capturedBpRef = bpRef;
                        var capturedChapter = chapterNum;
                        tasks.Add(new TodoExecutionTask
                        {
                            StepIndex = step.Index,
                            Title = rawTitle,
                            Detail = rawDetail,
                            PluginName = "WriterPlugin",
                            FunctionName = "GenerateChapterFromBlueprint",
                            ExecuteAsync = async ct =>
                            {
                                var blueprintSvc = ServiceLocator.Get<TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.Services.ShortStoryBlueprintService>();
                                await blueprintSvc.InitializeAsync();
                                var blueprint = blueprintSvc.GetBlueprintById(capturedBpRef) ?? blueprintSvc.GetBlueprintByName(capturedBpRef);
                                if (blueprint == null)
                                    return $"[生成失败] 未找到短篇蓝图：{capturedBpRef}";

                                var r = await writerPlugin.GenerateChapterFromBlueprintAsync(ct, blueprint.Id, capturedChapter);
                                var (isPlanCancelled, _) = TM.Services.Framework.AI.SemanticKernel.UIMessageItem.TryExtractCancelledPartial(r.SavedContent);
                                if (isPlanCancelled)
                                    throw new OperationCanceledException("生成已取消");
                                if (r.SavedContent?.StartsWith("[错误]", StringComparison.Ordinal) == true)
                                    throw new ManualInterventionRequiredException(r.SavedContent);
                                return r.SavedContent;
                            }
                        });
                        continue;
                    }

                    if (!string.IsNullOrEmpty(_pendingContinueSourceId)
                        && !ChapterDirectiveParser.HasContinueDirective(rawDetail)
                        && !ChapterDirectiveParser.HasContinueDirective(rawTitle))
                    {
                        rawDetail = $"@续写:{_pendingContinueSourceId} {rawDetail}";
                        TM.App.Log($"[SKConversationViewModel] 注入缓存续写指令到步骤: @续写:{_pendingContinueSourceId}");
                        _pendingContinueSourceId = null;
                    }
                    else if (!string.IsNullOrEmpty(_pendingRewriteTargetId)
                        && !ChapterDirectiveParser.HasRewriteDirective(rawDetail)
                        && !ChapterDirectiveParser.HasRewriteDirective(rawTitle))
                    {
                        rawDetail = $"@重写:{_pendingRewriteTargetId} {rawDetail}";
                        TM.App.Log($"[SKConversationViewModel] 注入缓存重写指令到步骤: @重写:{_pendingRewriteTargetId}");
                        _pendingRewriteTargetId = null;
                    }

                    var sourceChapterId = ChapterDirectiveParser.ParseSourceChapterId(rawDetail)
                        ?? ChapterDirectiveParser.ParseSourceChapterId(rawTitle);
                    var targetChapterId = ChapterDirectiveParser.ParseTargetChapterId(rawDetail)
                        ?? ChapterDirectiveParser.ParseTargetChapterId(rawTitle);

                    if (!string.IsNullOrEmpty(sourceChapterId))
                    {
                        var resolvedSourceId = await ResolveChapterIdTokenAsync(sourceChapterId);
                        if (string.IsNullOrEmpty(resolvedSourceId))
                        {
                            tasks.Add(new TodoExecutionTask
                            {
                                StepIndex = step.Index,
                                Title = rawTitle,
                                Detail = rawDetail,
                                PluginName = "WriterPlugin",
                                FunctionName = "GenerateChapterFromSource",
                                ExecuteAsync = _ => Task.FromResult<string?>(
                                    "[生成失败] 无法解析@续写章节ID，请使用 @续写:volN_chM 或 @续写:第N卷第M章")
                            });
                            continue;
                        }
                        sourceChapterId = resolvedSourceId;
                    }

                    if (!string.IsNullOrEmpty(targetChapterId))
                    {
                        var resolvedTargetId = await ResolveChapterIdTokenAsync(targetChapterId);
                        if (string.IsNullOrEmpty(resolvedTargetId))
                        {
                            tasks.Add(new TodoExecutionTask
                            {
                                StepIndex = step.Index,
                                Title = rawTitle,
                                Detail = rawDetail,
                                PluginName = "WriterPlugin",
                                FunctionName = "RewriteChapter",
                                ExecuteAsync = _ => Task.FromResult<string?>(
                                    "[生成失败] 无法解析@重写章节ID，请使用 @重写:volN_chM 或 @重写:第N卷第M章")
                            });
                            continue;
                        }
                        targetChapterId = resolvedTargetId;
                    }

                    var normalizedTitle = NormalizeChapterHint(rawTitle, rawDetail);
                    var normalizedDetail = NormalizeChapterHint(rawDetail, rawTitle);

                    if (!string.IsNullOrEmpty(sourceChapterId))
                    {
                        var capturedSourceId = sourceChapterId;
                        tasks.Add(new TodoExecutionTask
                        {
                            StepIndex = step.Index,
                            Title = normalizedTitle,
                            Detail = normalizedDetail,
                            PluginName = "WriterPlugin",
                            FunctionName = "GenerateChapterFromSource",
                            ExecuteAsync = async ct => await writerPlugin.GenerateChapterFromSourceAsync(ct, capturedSourceId)
                        });
                    }
                    else if (!string.IsNullOrEmpty(targetChapterId))
                    {
                        var capturedTargetId = targetChapterId;
                        tasks.Add(new TodoExecutionTask
                        {
                            StepIndex = step.Index,
                            Title = normalizedTitle,
                            Detail = normalizedDetail,
                            PluginName = "WriterPlugin",
                            FunctionName = "RewriteChapter",
                            ExecuteAsync = async ct => await writerPlugin.RewriteChapterAsync(ct, capturedTargetId)
                        });
                    }
                    else
                    {
                        var exactChapterId = ExtractChapterIdFromDetail(rawDetail);

                        if (!string.IsNullOrEmpty(exactChapterId))
                        {
                            var capturedId = exactChapterId;
                            tasks.Add(new TodoExecutionTask
                            {
                                StepIndex = step.Index,
                                Title = normalizedTitle,
                                Detail = normalizedDetail,
                                PluginName = "WriterPlugin",
                                FunctionName = "GenerateChapter",
                                ExecuteAsync = async ct => await writerPlugin.GenerateChapterAsync(ct, capturedId)
                            });
                        }
                        else
                        {
                            var chapterNumber = 0;
                            int? resolvedVolume = null;

                            if (ChapterParserHelper.IsChapterTitle(normalizedTitle))
                            {
                                var (number, _) = ChapterParserHelper.ExtractChapterParts(normalizedTitle);
                                if (number.HasValue)
                                {
                                    chapterNumber = number.Value;
                                }
                            }

                            if (chapterNumber <= 0)
                            {
                                var (volFromTitle, chFromTitle) = ChapterParserHelper.ParseFromNaturalLanguage(normalizedTitle);
                                if (chFromTitle.HasValue)
                                {
                                    chapterNumber = chFromTitle.Value;
                                }
                                if (volFromTitle.HasValue)
                                {
                                    resolvedVolume = volFromTitle.Value;
                                }
                            }

                            if (chapterNumber <= 0)
                            {
                                var (volFromDetail, chFromDetail) = ChapterParserHelper.ParseFromNaturalLanguage(normalizedDetail);
                                if (chFromDetail.HasValue)
                                {
                                    chapterNumber = chFromDetail.Value;
                                }
                                if (volFromDetail.HasValue && !resolvedVolume.HasValue)
                                {
                                    resolvedVolume = volFromDetail.Value;
                                }
                            }

                            if (chapterNumber > 0 && resolvedVolume.HasValue && resolvedVolume.Value > 0)
                            {
                                var resolvedChapterId = ChapterParserHelper.BuildChapterId(resolvedVolume.Value, chapterNumber);
                                tasks.Add(new TodoExecutionTask
                                {
                                    StepIndex = step.Index,
                                    Title = normalizedTitle,
                                    Detail = normalizedDetail,
                                    PluginName = "WriterPlugin",
                                    FunctionName = "GenerateChapter",
                                    ExecuteAsync = async ct => await writerPlugin.GenerateChapterAsync(ct, resolvedChapterId)
                                });
                            }
                            else if (chapterNumber > 0)
                            {
                                tasks.Add(new TodoExecutionTask
                                {
                                    StepIndex = step.Index,
                                    Title = normalizedTitle,
                                    Detail = normalizedDetail,
                                    PluginName = "WriterPlugin",
                                    FunctionName = "GenerateChapterByNumber",
                                    ExecuteAsync = async ct => await writerPlugin.GenerateChapterByNumberAsync(ct, chapterNumber)
                                });
                            }
                            else
                            {
                                tasks.Add(new TodoExecutionTask
                                {
                                    StepIndex = step.Index,
                                    Title = normalizedTitle,
                                    Detail = normalizedDetail,
                                    PluginName = "WriterPlugin",
                                    FunctionName = "GenerateChapter",
                                    ExecuteAsync = _ => Task.FromResult<string?>("[生成失败] 未识别章节号，请在步骤标题中明确「第X章」，或使用@指令指定章节ID。")
                                });
                            }
                        }
                    }
                }

                using var traceCollector = new ExecutionTraceCollector();
                traceCollector.Start();

                var executionRunId = ExecutionEventHub.NewRunId();
                _chatService.SetLastRunId(executionRunId);
                assistantMessage.RunId = executionRunId;

                var runId = _todoExecutionService.StartSequentialRun(mode, tasks, executionRunId);

                if (runId == Guid.Empty)
                {
                    traceCollector.Stop();
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        assistantMessage.Content = SanitizeFinalBubbleContent(ConversationSummarizer.ForExecutionNotStarted());
                        assistantMessage.AnalysisSummary = "执行未启动";
                        assistantMessage.IsThinking = false;
                        assistantMessage.FinishStreaming();
                    });

                    _wasExecutionCancelledByUser = false;
                    _currentExecutionAssistantMessage = null;

                    var reason = _todoExecutionService.IsRunning
                        ? "当前已有任务在执行中，请稍后再试。"
                        : "检测到遗留运行态已自动复位，请重试。";
                    GlobalToast.Warning("执行未启动", reason);
                    return;
                }

                using var progressBridge = new ThinkingProgressBridge(assistantMessage, runId, mode);

                await Task.Run(async () =>
                {
                    while (_todoExecutionService.IsRunning)
                    {
                        await Task.Delay(500);
                    }
                });

                var executionTrace = traceCollector.Stop();
                var traceSummary = traceCollector.GetSummary();

                if (mode == ChatMode.Agent && _wasExecutionCancelledByUser)
                {
                    _wasExecutionCancelledByUser = false;
                    _currentExecutionAssistantMessage = null;

                    _chatService.SaveMessages(Messages);
                    TM.App.Log($"[SKConversationViewModel] 执行被用户取消，Mode={mode}, Steps={steps.Count}");

                    GlobalToast.Warning("已取消", "创作任务已取消");
                    return;
                }

                var dispatcher1 = Application.Current?.Dispatcher;
                if (dispatcher1 != null)
                {
                    await dispatcher1.InvokeAsync(() =>
                    {
                        var duration = DateTime.Now - assistantMessage.ThinkingStartTime;
                        var seconds = Math.Max(0.1, duration.TotalSeconds);

                        var finalPhase = traceSummary.FailedSteps > 0 && traceSummary.FailedStepSummaries.Count > 0
                            ? ProgressPhase.Failed
                            : ProgressPhase.Done;
                        assistantMessage.AnalysisDurationSeconds = seconds;
                        assistantMessage.IsThinking = false;
                        assistantMessage.Thinking.Complete(finalPhase);

                        if (traceSummary.FailedSteps > 0 && traceSummary.FailedStepSummaries.Count > 0)
                        {
                            var isPolishFatal = traceSummary.IsPolishFatal;

                            var failLines = new System.Text.StringBuilder();
                            if (isPolishFatal)
                            {
                                failLines.AppendLine("润色失败，已终止所有任务并清除计划：");
                            }
                            else
                            {
                                failLines.AppendLine("生成已停止（已达最大重试次数），原因如下：");
                            }
                            failLines.AppendLine();
                            foreach (var s in traceSummary.FailedStepSummaries)
                                failLines.AppendLine($"• {s}");
                            failLines.AppendLine();
                            failLines.Append(isPolishFatal
                                ? "→ 请排查润色失败原因后重新执行。"
                                : "→ 建议直接「重新生成」，或调整规则 / 章节任务后重试。");
                            assistantMessage.Content = SanitizeFinalBubbleContent(failLines.ToString().TrimEnd());
                            assistantMessage.IsError = true;
                            TM.App.Log($"[SKConversationViewModel] 执行失败，{traceSummary.ToSummaryText()}");

                            if (isPolishFatal)
                            {
                                _cachedPlanSteps = null;
                                _comm.PublishShowPlanViewChanged(false);
                                TM.App.Log("[SKConversationViewModel] 润色严格模式终止，已清空计划缓存并关闭 Plan 视图");
                            }
                        }
                        else
                        {
                            var profile = ModeProfileRegistry.GetProfile(mode);
                            if (profile.ExecutionResultMapper != null)
                            {
                                var context = new ExecutionResultContext
                                {
                                    RunId = runId,
                                    Mode = mode,
                                    Duration = duration,
                                    TraceSummaryText = traceSummary.ToSummaryText(),
                                    ExecutionTrace = executionTrace,
                                    ChapterId = null,
                                    ChapterTitle = null,
                                    OriginalMessage = assistantMessage.ToConversationMessage(),
                                    ThinkingRaw = assistantMessage.ThinkingContent,
                                    IsCancelled = false,
                                    IsError = false
                                };
                                var convMessage = profile.ExecutionResultMapper.MapExecutionResult(context);
                                assistantMessage.ApplyFromConversationMessage(convMessage);
                                TM.App.Log($"[SKConversationViewModel] {profile.Description} 执行完成，{traceSummary.ToSummaryText()}");
                            }
                            else
                            {
                                var summaryContent = ConversationSummarizer.ForExecutionCompleted(null, null, traceSummary);
                                assistantMessage.Content = SanitizeFinalBubbleContent(summaryContent);
                                TM.App.Log($"[SKConversationViewModel] 执行完成（无 Mapper），{traceSummary.ToSummaryText()}");
                            }
                        }

                        assistantMessage.FinishStreaming();
                    });
                }

                if (traceSummary.AllSucceeded)
                {
                    GlobalToast.Success("执行完成", ConversationSummarizer.ForExecutionCompleted(null, null, traceSummary));
                }
                else if (traceSummary.FailedSteps > 0)
                {
                    GlobalToast.Warning("执行完成（有失败）", ConversationSummarizer.ForExecutionCompleted(null, null, traceSummary));
                }
                else
                {
                    GlobalToast.Success("执行完成", ConversationSummarizer.ForExecutionCompleted(null, null, traceSummary));
                }

                _comm.PublishRefreshChapterList();

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();
                TM.App.Log($"[SKConversationViewModel] 执行结束，Mode={mode}, Steps={steps.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 执行失败，Mode={mode}: {ex.Message}");

                if (assistantMessage != null)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        assistantMessage.IsThinking = false;
                        assistantMessage.AnalysisSummary = "执行失败";
                        assistantMessage.Content = SanitizeFinalBubbleContent($"执行失败：{ex.Message}");
                        assistantMessage.IsError = true;
                        assistantMessage.FinishStreaming();
                    });
                }
                else
                {
                    Messages.Add(UIMessageItem.CreateErrorMessage($"执行失败：{ex.Message}"));
                }
                GlobalToast.Error("执行失败", $"执行失败：{ex.Message}");
            }
            finally
            {
                _pendingContinueSourceId = null;
                _pendingRewriteTargetId = null;

                IsGenerating = false;
                ShowTodoOverlay = false;
                _wasExecutionCancelledByUser = false;
                _currentExecutionAssistantMessage = null;
                RefreshContextUsage();

                FinalizeAssistantMessageIfIncomplete(assistantMessage, startTime);
            }
        }

        private static string SanitizeBubbleChunk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var trimmedStart = text.TrimStart();
            if (trimmedStart.StartsWith("analysis>", StringComparison.OrdinalIgnoreCase))
            {
                text = trimmedStart["analysis>".Length..].TrimStart('\r', '\n');
            }
            else if (trimmedStart.StartsWith("answer>", StringComparison.OrdinalIgnoreCase))
            {
                text = trimmedStart["answer>".Length..].TrimStart('\r', '\n');
            }

            if (!text.Contains('<'))
                return text;

            var baseText = text;
            baseText = baseText
                .Replace("<analysis>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</analysis>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("<thought>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</thought>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("<answer>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</answer>", string.Empty, StringComparison.OrdinalIgnoreCase);

            return baseText;
        }

        private static string SanitizeFinalBubbleContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return SanitizeBubbleChunk(text).Trim();
        }

        private static bool ShouldRunAgentExecution(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
                return false;

            if (ChapterDirectiveParser.HasContinueDirective(userText) || ChapterDirectiveParser.HasRewriteDirective(userText))
                return true;

            if (userText.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                || userText.Contains("@imitate", StringComparison.OrdinalIgnoreCase))
                return true;

            if (SingleChapterTaskDetector.IsSingleChapterTask(userText))
                return true;

            return false;
        }

        private static bool ShouldRunPlanMode(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
                return false;

            if (ChapterDirectiveParser.HasContinueDirective(userText) || ChapterDirectiveParser.HasRewriteDirective(userText))
                return true;

            if (userText.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                || userText.Contains("@imitate", StringComparison.OrdinalIgnoreCase))
                return true;

            var t = userText.Trim();
            if (t.Contains("@plan", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("@规划", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("todo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var hasActionVerb = t.Contains("生成") || t.Contains('写') || t.Contains("创作")
                || t.Contains("执行") || t.Contains("开始") || t.Contains("制定");
            if (hasActionVerb &&
                (t.Contains("计划") || t.Contains("规划") || t.Contains("拆解")
                 || t.Contains("分步") || t.Contains("步骤")))
            {
                return true;
            }

            var normalized = t.Replace(" ", string.Empty);
            if ((normalized.Contains("批量") ||
                    normalized.Contains("多章") ||
                    normalized.Contains("多章节") ||
                    normalized.Contains("几章") ||
                    normalized.Contains("几章节") ||
                    normalized.Contains("全部章") ||
                    normalized.Contains("所有章") ||
                    normalized.Contains("全部章节") ||
                    normalized.Contains("所有章节"))
                && (normalized.Contains('章') || normalized.Contains("章节")))
            {
                return true;
            }

            if (ChapterParserHelper.ParseChapterRanges(userText) != null)
            {
                return true;
            }

            if (ChapterParserHelper.ParseChapterRange(userText) != null)
            {
                return true;
            }

            if (ChapterParserHelper.ParseChapterNumberList(userText) != null)
            {
                return true;
            }

            if (SingleChapterTaskDetector.IsSingleChapterTask(userText))
                return true;

            return false;
        }

        private async Task StartAgentExecutionAsync(string userText)
        {
            var steps = new List<(int Index, string Title, string Detail)>
            {
                (1, "生成章节", userText)
            };

            TM.App.Log("[SKConversationViewModel] Agent 模式开始执行单步任务");

            await RunTodoExecutionAsync(
                ChatMode.Agent,
                steps,
                "Thinking...");
        }
    }
}
