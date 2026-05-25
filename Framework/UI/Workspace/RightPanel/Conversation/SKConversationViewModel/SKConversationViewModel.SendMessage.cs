using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Config;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 消息发送

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            if (_chatService.IsWorkspaceBatchGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "工作台批量生成正在进行，继续需要中断批量生成，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;

                _chatService.CancelWorkspaceBatch();
                TM.App.Log("[SKConversationViewModel] 用户确认中断工作台批量生成，主界面对话继续执行");
            }

            var userText = InputText.Trim();
            _lastSentUserText = userText;
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            PlanContinueEndText = string.Empty;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasSuggestedActions));
            InputText = string.Empty;

            var effectiveMode = CurrentMode;
            var isUserSelectedEdit = effectiveMode == ChatMode.Edit;
            if (effectiveMode == ChatMode.Agent && !ShouldRunAgentExecution(userText))
            {
                effectiveMode = ChatMode.Edit;
                _pendingModeHint = "[提示] Agent 模式未识别到章节生成指令，已按问答处理。\n" +
                    "如需生成章节，可使用：创建/生成/写第X章、@续写、@重写 等指令。";
            }
            else if (effectiveMode == ChatMode.Plan && !ShouldRunPlanMode(userText))
            {
                effectiveMode = ChatMode.Edit;
                _pendingModeHint = "[提示] Plan 模式未识别到章节规划指令，已按问答处理。\n" +
                    "如需规划章节，可使用：@plan、生成第X到Y章 等指令。";
            }
            else
            {
                _pendingModeHint = null;
            }

            _lastExecutedMode = effectiveMode;

            var runType = TM.Services.Framework.AI.SemanticKernel.RunType.Task;
            if (effectiveMode == ChatMode.Edit && !isUserSelectedEdit)
            {
                runType = TM.Services.Framework.AI.SemanticKernel.RunType.Chat;
            }
            else if (effectiveMode == ChatMode.Edit && isUserSelectedEdit && !ShouldUseTools(userText))
            {
                runType = TM.Services.Framework.AI.SemanticKernel.RunType.Chat;
            }
            else if (effectiveMode == ChatMode.Plan || effectiveMode == ChatMode.Agent)
            {
                runType = TM.Services.Framework.AI.SemanticKernel.RunType.Execution;
            }

            if (effectiveMode == ChatMode.Edit)
            {
                var editRedirect = await TryBuildEditModeGenerationRedirectMessageAsync(userText);
                if (!string.IsNullOrWhiteSpace(editRedirect))
                {
                    Messages.Add(UIMessageItem.CreateUserMessage(userText));
                    Messages.Add(UIMessageItem.CreateErrorMessage(editRedirect));
                    GlobalToast.Warning("已阻止执行", "Edit 模式不执行生成，请切换到 Plan/Agent");
                    return;
                }
            }

            if (effectiveMode == ChatMode.Plan || effectiveMode == ChatMode.Agent)
            {
                if (!userText.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                    && !userText.Contains("@imitate", StringComparison.OrdinalIgnoreCase))
                {
                    var validateError = await ValidateChapterGenerationRequestBeforeExecutionAsync(userText);
                    if (!string.IsNullOrWhiteSpace(validateError))
                    {
                        var userMessage = UIMessageItem.CreateUserMessage(userText);
                        Messages.Add(userMessage);
                        Messages.Add(UIMessageItem.CreateErrorMessage(validateError));
                        GlobalToast.Warning("已阻止执行", "请按提示调整后重试");

                        try
                        {
                            _chatService.SaveMessages(Messages);
                            SyncSessionFromServiceAfterPersist();
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[SKConversationViewModel] 保存阻断消息失败: {ex.Message}");
                        }

                        return;
                    }
                }
            }

            if (TM.Services.Framework.AI.SemanticKernel.Prompts.PromptLibrary.IsIdentityQuestion(userText))
            {
                TM.App.Log($"[SKConversationViewModel] 检测到开发级问题，短路处理: {userText}");

                if (_todoExecutionService.IsRunning)
                {
                    _todoExecutionService.CancelCurrentRun();
                    TM.App.Log("[SKConversationViewModel] 已取消 Agent 执行");
                }

                await SendDeveloperLevelResponseAsync(userText);
                return;
            }

            if (userText.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                || userText.Contains("@imitate", StringComparison.OrdinalIgnoreCase))
            {
                if (effectiveMode != ChatMode.Plan)
                {
                    Messages.Add(UIMessageItem.CreateUserMessage(userText));
                    Messages.Add(UIMessageItem.CreateErrorMessage("@仿写 仅支持 Plan 模式，请切换到 Plan 模式后重试。"));
                    GlobalToast.Warning("模式不支持", "请切换到 Plan 模式使用 @仿写");
                    return;
                }

                var blueprintRef = TryParseImitateDirective(userText);
                if (string.IsNullOrWhiteSpace(blueprintRef))
                {
                    Messages.Add(UIMessageItem.CreateUserMessage(userText));
                    Messages.Add(UIMessageItem.CreateErrorMessage("[生成失败] @仿写 指令格式错误，请使用：@仿写:蓝图名称 或 @仿写:蓝图ID"));
                    GlobalToast.Warning("格式错误", "请使用 @仿写:蓝图名称 或 @仿写:蓝图ID");
                    return;
                }

                try
                {
                    var blueprintSvc = ServiceLocator.Get<TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.Services.ShortStoryBlueprintService>();
                    await blueprintSvc.InitializeAsync();
                    var matchedBlueprint = blueprintSvc.GetBlueprintByName(blueprintRef)
                        ?? blueprintSvc.GetBlueprintById(blueprintRef);
                    if (matchedBlueprint == null)
                    {
                        Messages.Add(UIMessageItem.CreateUserMessage(userText));
                        Messages.Add(UIMessageItem.CreateErrorMessage($"[生成失败] 未找到短篇蓝图：{blueprintRef}"));
                        GlobalToast.Error("蓝图不存在", $"未找到：{blueprintRef}");
                        return;
                    }

                    Messages.Add(UIMessageItem.CreateUserMessage(userText));
                    TM.App.Log($"[SKConversationViewModel] @仿写蓝图拦截(Plan): {matchedBlueprint.Name}");

                    if (!int.TryParse(matchedBlueprint.TotalChapters, out var tc) || tc <= 0)
                    {
                        Messages.Add(UIMessageItem.CreateErrorMessage("[生成失败] 蓝图总章节数无效，请先在短篇直出中设置总章节数"));
                        GlobalToast.Warning("无法执行", "请先设置总章节数");
                        return;
                    }

                    var planSteps = new List<PlanStep>();
                    for (int i = 1; i <= tc; i++)
                    {
                        var chapterBp = matchedBlueprint.ChapterBlueprints.FirstOrDefault(c => c.ChapterIndex == i);
                        var stepTitle = chapterBp != null && !string.IsNullOrWhiteSpace(chapterBp.Title)
                            ? $"生成第{i}章：{chapterBp.Title}"
                            : $"生成第{i}章";
                        planSteps.Add(new PlanStep
                        {
                            Index = i,
                            Title = stepTitle,
                            Detail = $"@仿写:{matchedBlueprint.Id} 第{i}章",
                            ChapterNumber = i
                        });
                    }

                    var planPayload = new PlanPayload { Steps = planSteps };
                    var runId = ExecutionEventHub.NewRunId();
                    _chatService.SetLastRunId(runId);
                    _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload, runId);
                    _comm.PublishShowPlanViewChanged(true);

                    var assistantMsg = UIMessageItem.CreateAssistantPlaceholder();
                    assistantMsg.Content = $"已为「{matchedBlueprint.Name}」构建 {tc} 章生成计划，请在右侧步骤面板点击「执行计划」开始生成。";
                    assistantMsg.FinishStreaming();
                    Messages.Add(assistantMsg);

                    TM.App.Log($"[SKConversationViewModel] @仿写 Plan 本地计划已构建: {tc} 步");
                    return;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKConversationViewModel] 蓝图拦截查询失败: {ex.Message}");
                    Messages.Add(UIMessageItem.CreateUserMessage(userText));
                    Messages.Add(UIMessageItem.CreateErrorMessage($"[生成失败] 蓝图查询失败：{ex.Message}"));
                    GlobalToast.Error("蓝图查询失败", $"蓝图查询失败：{ex.Message}");
                    return;
                }
            }

            if (effectiveMode == ChatMode.Agent)
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                await StartAgentExecutionAsync(userText);
                return;
            }

            IsGenerating = true;

            var startTime = DateTime.Now;
            UIMessageItem? assistantMessage = null;

            try
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                if (!string.IsNullOrEmpty(_pendingModeHint))
                {
                    Messages.Add(UIMessageItem.CreateSystemMessage(_pendingModeHint));
                    _pendingModeHint = null;
                }

                assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = "Thinking...";
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

                assistantMessage.Thinking.WriteStatus("等待端点响应...");

                var isPlanMode = effectiveMode == ChatMode.Plan;
                var planContentBuilder = isPlanMode ? new StringBuilder() : null;

                TM.App.Log($"[SKConversationViewModel] 发送消息: {userText.Substring(0, Math.Min(50, userText.Length))}...");

                string finalUserText = userText;

                string? imitateBookId = null;
                string? imitateBookTitle = null;

                async Task<string?> TryResolveImitateBookIdAsync(string rawName)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(rawName))
                        {
                            return null;
                        }

                        if (Guid.TryParse(rawName, out _))
                        {
                            return rawName;
                        }

                        return await Task.Run(async () =>
                        {
                            var crawledBasePath = StoragePathHelper.GetModulesStoragePath("Design/SmartParsing/BookAnalysis/CrawledBooks");
                            if (!System.IO.Directory.Exists(crawledBasePath))
                            {
                                return (string?)null;
                            }

                            foreach (var bookDir in System.IO.Directory.GetDirectories(crawledBasePath))
                            {
                                var bookId = System.IO.Path.GetFileName(bookDir);
                                if (string.IsNullOrWhiteSpace(bookId))
                                {
                                    continue;
                                }

                                var bookInfoPath = System.IO.Path.Combine(bookDir, "book_info.json");
                                if (!System.IO.File.Exists(bookInfoPath))
                                {
                                    continue;
                                }

                                var json = await System.IO.File.ReadAllTextAsync(bookInfoPath).ConfigureAwait(false);
                                using var doc = JsonDocument.Parse(json);
                                var root = doc.RootElement;

                                string? title = null;
                                if (root.TryGetProperty("title", out var titleProp))
                                {
                                    title = titleProp.GetString();
                                }
                                else if (root.TryGetProperty("Title", out var titleProp2))
                                {
                                    title = titleProp2.GetString();
                                }

                                if (!string.IsNullOrWhiteSpace(title)
                                    && string.Equals(title.Trim(), rawName.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    return bookId;
                                }
                            }

                            return (string?)null;
                        });
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("TryResolveImitateBookId_ReadBookInfo", ex);
                        return null;
                    }
                }

                if (CurrentMode == ChatMode.Agent)
                {
                    try
                    {
                        var referenceParser = ServiceLocator.Get<ReferenceParser>();
                        var refs = referenceParser.ParseReferences(userText);
                        var imitateRef = refs.FirstOrDefault(r =>
                            string.Equals(r.Type, "imitate", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(r.Name));

                        if (imitateRef != null && !string.IsNullOrWhiteSpace(imitateRef.Name))
                        {
                            imitateBookId = (await TryResolveImitateBookIdAsync(imitateRef.Name)) ?? imitateRef.Name;
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SKConversationViewModel] 解析仿写引用失败: {ex.Message}");
                    }
                }
                else if (userText.Contains("@仿写", StringComparison.OrdinalIgnoreCase)
                    || userText.Contains("@imitate", StringComparison.OrdinalIgnoreCase))
                {
                    GlobalToast.Warning("仿写仅限 Agent 模式", "请切换到 Agent 模式后重试");
                }

                if (!string.IsNullOrWhiteSpace(imitateBookId))
                {
                    try
                    {
                        var crawler = _novelCrawlerService;
                        var crawledTask = crawler.LoadCrawledContentAsync(imitateBookId);
                        var excerptTask = crawler.LoadCrawledExcerptAsync(imitateBookId);
                        await Task.WhenAll(crawledTask, excerptTask);

                        var crawled = await crawledTask;
                        var excerpt = await excerptTask;

                        imitateBookTitle = !string.IsNullOrWhiteSpace(crawled?.BookTitle)
                            ? crawled!.BookTitle
                            : imitateBookId;

                        var authorLine = !string.IsNullOrWhiteSpace(crawled?.Author)
                            ? $"作者：{crawled!.Author}\n"
                            : string.Empty;

                        var templateSection = string.Empty;
                        try
                        {
                            var templates = await _guideContextService.GetAllTemplatesAsync();
                            var templateLines = templates
                                .Select(t =>
                                {
                                    var parts = new List<string>();
                                    if (!string.IsNullOrWhiteSpace(t.Genre))
                                    {
                                        parts.Add($"题材:{t.Genre}");
                                    }
                                    if (!string.IsNullOrWhiteSpace(t.OverallIdea))
                                    {
                                        parts.Add(t.OverallIdea);
                                    }
                                    var summary = parts.Count > 0 ? string.Join(" / ", parts) : "";
                                    return string.IsNullOrWhiteSpace(summary)
                                        ? $"- {t.Name}"
                                        : $"- {t.Name}：{summary}";
                                })
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Take(5)
                                .ToList();

                            if (templateLines.Count > 0)
                            {
                                templateSection = $"<context_block type=\"imitate_templates\">\n{string.Join("\n", templateLines)}\n</context_block>";
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogOnce("BuildImitateTemplateSection", ex);
                        }

                        var cleanedUserText = userText
                            .Replace($"@仿写:{imitateBookId}", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Replace($"@imitate:{imitateBookId}", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Replace($"@仿写:{imitateBookTitle}", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Replace($"@imitate:{imitateBookTitle}", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Trim();

                        if (string.IsNullOrWhiteSpace(cleanedUserText))
                        {
                            cleanedUserText = "请基于以上素材进行仿写，写出一个可供后续续写的开篇。";
                        }

                        if (string.IsNullOrWhiteSpace(excerpt))
                        {
                            TM.App.Log($"[SKConversationViewModel] 仿写素材为空: {imitateBookId}");
                            finalUserText = string.IsNullOrWhiteSpace(templateSection)
                                ? cleanedUserText
                                : $"{templateSection}\n\n{cleanedUserText}";
                        }
                        else
                        {
                            TM.App.Log($"[SKConversationViewModel] 已注入仿写上下文: {imitateBookId}");
                            var templateBlock = string.IsNullOrWhiteSpace(templateSection)
                                ? string.Empty
                                : $"\n\n{templateSection}";
                            finalUserText = $"<writing_context type=\"mimicry\">\n书名：{imitateBookTitle}\n{authorLine}\n{excerpt}{templateBlock}\n</writing_context>\n\n{cleanedUserText}";
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SKConversationViewModel] 获取仿写上下文失败: {ex.Message}");
                    }
                }
                else
                {
                    var chapterId = await ResolveChapterIdFromTextAsync(userText);

                    if (!string.IsNullOrEmpty(chapterId))
                    {
                        try
                        {
                            string? contextPrompt = null;

                            if (_prebuiltChapterId == chapterId && !string.IsNullOrWhiteSpace(_prebuiltContextPrompt))
                            {
                                contextPrompt = _prebuiltContextPrompt;
                                TM.App.Log($"[SKConversationViewModel] OPT-010: 命中预构建缓存 {chapterId}");
                            }
                            else
                            {
                                var bridge = ServiceLocator.Get<ChapterGenerationBridge>();
                                contextPrompt = await bridge.GetGenerationPromptAsync(chapterId);
                            }

                            if (!string.IsNullOrWhiteSpace(contextPrompt))
                            {
                                TM.App.Log($"[SKConversationViewModel] 已注入章节上下文: {chapterId}");
                                var cleanedUserText = CleanChapterReferences(userText);

                                if (ChapterDirectiveParser.HasContinueDirective(userText))
                                {
                                    var title = await _guideContextService.GetChapterTitleAsync(chapterId) ?? chapterId;
                                    cleanedUserText = $"续写「{title}」之后的下一章内容 {cleanedUserText}".Trim();
                                }
                                else if (ChapterDirectiveParser.HasRewriteDirective(userText))
                                {
                                    var title = await _guideContextService.GetChapterTitleAsync(chapterId) ?? chapterId;
                                    cleanedUserText = $"重写「{title}」 {cleanedUserText}".Trim();
                                }

                                finalUserText = $"<writing_context type=\"chapter\">\n{contextPrompt}\n</writing_context>\n\n{cleanedUserText}";
                            }
                            else
                            {
                                TM.App.Log($"[SKConversationViewModel] 章节上下文为空: {chapterId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[SKConversationViewModel] 获取章节上下文失败: {ex.Message}");
                        }
                    }
                }

                ConversationMessage? prebuiltPlanMessage = null;
                if (isPlanMode)
                {
                    var planProfile = ModeProfileRegistry.GetProfile(ChatMode.Plan);
                    if (planProfile.Mapper is PlanModeMapper planMapper)
                    {
                        prebuiltPlanMessage = await planMapper.TryBuildPlanWithoutModelAsync(userText).ConfigureAwait(true);
                    }
                }

                if (runType != TM.Services.Framework.AI.SemanticKernel.RunType.Chat && IsBusinessFactQuery(userText))
                {
                    var orchestratedData = await OrchestrateBusinessQueryAsync(userText).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(orchestratedData))
                    {
                        finalUserText = $"<business_data>\n{orchestratedData}\n</business_data>\n\n请根据以上业务数据回答用户问题。如果数据中包含「未找到」，如实告知用户，不要编造。\n\n用户原始问题：{finalUserText}";
                        runType = TM.Services.Framework.AI.SemanticKernel.RunType.Chat;
                        TM.App.Log($"[SKConversationViewModel] 查询编排成功，注入 {orchestratedData.Length} 字符，切换到 Chat 模式");
                    }
                    else
                    {
                        TM.App.Log("[SKConversationViewModel] 查询编排未命中，降级到工具调用路径");
                    }
                }

                var promptParts = ChatPromptBridge.BuildParts(effectiveMode, finalUserText);

                string result;
                bool isError;
                bool isCancelled;
                bool isCancelledWithPartial = false;

                bool hasRealThinkingChunk = false;

                if (prebuiltPlanMessage != null)
                {
                    result = "[基于打包数据直接生成计划]";
                    isError = false;
                    isCancelled = false;

                    var prebuiltRunId = ExecutionEventHub.NewRunId();
                    _chatService.SetLastRunId(prebuiltRunId);
                    userMessage.RunId = prebuiltRunId;
                    assistantMessage.RunId = prebuiltRunId;

                    var oldSimCts = _prebuiltSimulationCts;
                    oldSimCts?.Cancel();
                    oldSimCts?.Dispose();
                    _prebuiltSimulationCts = new CancellationTokenSource();
                    var simCt = _prebuiltSimulationCts.Token;

                    try
                    {
                        if (!string.IsNullOrEmpty(prebuiltPlanMessage.AnalysisRaw))
                        {
                            var thinkingText = prebuiltPlanMessage.AnalysisRaw;
                            var rng = new Random();
                            var i = 0;
                            while (i < thinkingText.Length)
                            {
                                simCt.ThrowIfCancellationRequested();

                                var chunkLen = Math.Min(rng.Next(1, 4), thinkingText.Length - i);
                                var chunk = thinkingText.Substring(i, chunkLen);
                                i += chunkLen;

                                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                {
                                    assistantMessage.Thinking.EnqueueRaw(chunk);
                                });

                                var lastChar = chunk[^1];
                                if (lastChar == '\n')
                                    await Task.Delay(rng.Next(300, 600), simCt);
                                else if ("，。、！？：；".Contains(lastChar))
                                    await Task.Delay(rng.Next(80, 200), simCt);
                                else
                                    await Task.Delay(rng.Next(30, 70), simCt);
                            }
                            await Task.Delay(rng.Next(1500, 2500), simCt);
                        }

                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            var duration = DateTime.Now - startTime;
                            var seconds = Math.Max(0.1, duration.TotalSeconds);
                            assistantMessage.AnalysisDurationSeconds = seconds;
                            assistantMessage.AnalysisSummary = $"Thought for {seconds:F1} s";
                            assistantMessage.IsThinking = false;
                            assistantMessage.FinishStreaming();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        isCancelled = true;
                        _pendingContinueSourceId = null;
                        _pendingRewriteTargetId = null;
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            var seconds = Math.Max(0.1, (DateTime.Now - startTime).TotalSeconds);
                            assistantMessage.AnalysisDurationSeconds = seconds;
                            assistantMessage.Thinking.EnterPhase(ProgressPhase.Cancelled);
                            assistantMessage.Thinking.Complete(ProgressPhase.Cancelled);
                            assistantMessage.FinishStreaming();
                            assistantMessage.IsError = true;
                            assistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                        });
                        TM.App.Log("[SKConversationViewModel] 预构建计划模拟已取消");
                    }
                    finally
                    {
                        _prebuiltSimulationCts = null;
                    }
                }
                else
                {
                    var pendingContentQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    var legacyContentSkipQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    var legacyThinkingSkipQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
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

                    void EnqueueContentFromChunk(string content)
                    {
                        if (string.IsNullOrEmpty(content)) return;

                        if (isPlanMode)
                        {
                            planContentBuilder?.Append(content);
                        }
                        else
                        {
                            pendingContentQueue.Enqueue(content);
                            if (System.Threading.Interlocked.CompareExchange(ref contentDispatchFlag, 1, 0) == 0)
                            {
                                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                    DrainContentQueue,
                                    System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    }

                    void EnqueueThinkingFromChunk(string thinking, string? kind)
                    {
                        if (!string.IsNullOrEmpty(thinking))
                        {
                            hasRealThinkingChunk = true;
                            var normalizedKind = UIMessageItem.NormalizeAnalysisKind(kind ?? _chatService.LastThinkingKind);
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                if (!string.Equals(assistantMessage.AnalysisKind, normalizedKind, StringComparison.Ordinal))
                                    assistantMessage.AnalysisKind = normalizedKind;
                                if (!assistantMessage.AnalysisDurationSeconds.HasValue)
                                    assistantMessage.AnalysisSummary = $"{normalizedKind}...";
                            });
                        }

                        assistantMessage?.Thinking.EnqueueRaw(thinking);
                    }

                    bool TryConsumeQueuedSegments(System.Collections.Concurrent.ConcurrentQueue<string> queue, string text)
                    {
                        if (string.IsNullOrEmpty(text)) return false;

                        var snapshot = queue.ToArray();
                        if (snapshot.Length == 0) return false;

                        var sb = new StringBuilder();
                        var count = 0;
                        foreach (var segment in snapshot)
                        {
                            sb.Append(segment);
                            count++;
                            if (sb.Length >= text.Length) break;
                        }

                        if (sb.Length == text.Length && string.Equals(sb.ToString(), text, StringComparison.Ordinal))
                        {
                            for (var i = 0; i < count; i++)
                            {
                                queue.TryDequeue(out _);
                            }
                            return true;
                        }

                        return false;
                    }

                    bool ShouldSkipLegacyContent(string content)
                    {
                        return TryConsumeQueuedSegments(legacyContentSkipQueue, content);
                    }

                    bool ShouldSkipLegacyThinking(string thinking)
                    {
                        return TryConsumeQueuedSegments(legacyThinkingSkipQueue, thinking);
                    }

                    void OnAIChunk(IStreamChunk chunk)
                    {
                        var currentRunId = _chatService.LastRunId;
                        if (currentRunId == Guid.Empty || chunk.RunId != currentRunId) return;

                        switch (chunk)
                        {
                            case TextDeltaChunk textChunk:
                                var content = SanitizeBubbleChunk(TM.Services.Framework.AI.Core.ModelNameSanitizer.SanitizeChunk(textChunk.Content));
                                if (string.IsNullOrEmpty(content)) return;
                                legacyContentSkipQueue.Enqueue(content);
                                EnqueueContentFromChunk(content);
                                break;
                            case ThinkingDeltaChunk thinkingChunk:
                                var thinking = thinkingChunk.Content ?? string.Empty;
                                legacyThinkingSkipQueue.Enqueue(thinking);
                                EnqueueThinkingFromChunk(thinking, thinkingChunk.Kind);
                                break;
                        }
                    }

                    _chatService.SetLastRunId(Guid.Empty);
                    using var progressBridge = new ThinkingProgressBridge(
                        assistantMessage, () => _chatService.LastRunId, effectiveMode);

                    string? lastStatusSeen = "等待端点响应...";

                    string[]? forcedFunctions = null;
                    if (runType != TM.Services.Framework.AI.SemanticKernel.RunType.Chat && IsBusinessFactQuery(userText))
                        forcedFunctions = GetForcedFunctionNames(userText);
                    using (_chatService.UseTransientMode(effectiveMode, runType, forcedFunctions))
                    {
                        AIChunkBus.Published += OnAIChunk;
                        try
                        {
                            result = await _chatService.SendStreamMessageAsync(
                                userText,
                                promptParts,
                                chunk =>
                                {
                                    var content = SanitizeBubbleChunk(chunk);
                                    if (ShouldSkipLegacyContent(content)) return;
                                    EnqueueContentFromChunk(content);
                                },
                                thinkingChunk =>
                                {
                                    if (ShouldSkipLegacyThinking(thinkingChunk)) return;
                                    EnqueueThinkingFromChunk(thinkingChunk, null);
                                },
                                System.Threading.CancellationToken.None,
                                status =>
                                {
                                    if (string.IsNullOrEmpty(status) || status == lastStatusSeen) return;
                                    lastStatusSeen = status;
                                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                    {
                                        assistantMessage?.Thinking.WriteStatus(status);
                                    });
                                });
                        }
                        finally
                        {
                            AIChunkBus.Published -= OnAIChunk;
                        }
                    }

                    var (isAnyCancel, partial) = UIMessageItem.TryExtractCancelledPartial(result);
                    var isResultEmpty = string.IsNullOrWhiteSpace(result);
                    isError = result.StartsWith("[错误]", StringComparison.Ordinal) || isResultEmpty;
                    isCancelledWithPartial = isAnyCancel && !string.IsNullOrEmpty(partial);
                    isCancelled = isAnyCancel && !isCancelledWithPartial;

                    var dispatcher2 = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher2 != null)
                    {
                        await dispatcher2.InvokeAsync(() =>
                        {
                            var sb = new StringBuilder();
                            while (pendingContentQueue.TryDequeue(out var c))
                                sb.Append(c);
                            if (sb.Length > 0)
                                assistantMessage?.AppendContent(sb.ToString());

                            var runId = _chatService.LastRunId;
                            userMessage.RunId = runId;
                            assistantMessage!.RunId = runId;

                            assistantMessage.FlushThinkingImmediately();

                            if (!hasRealThinkingChunk && !isError && !isCancelled && !isCancelledWithPartial)
                                AppendNoThinkingFeedback(assistantMessage);

                            if (!assistantMessage.AnalysisDurationSeconds.HasValue)
                            {
                                var seconds = Math.Max(0.1, (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds);
                                assistantMessage.AnalysisDurationSeconds = seconds;
                                assistantMessage.AnalysisSummary = UIMessageItem.FormatAnalysisSummary(assistantMessage.AnalysisKind, seconds);
                            }

                            assistantMessage.FinishStreaming();

                            if (!isError && !isCancelled)
                                assistantMessage.References = _chatService.GetLastToolReferences();

                            if (isCancelledWithPartial)
                            {
                                assistantMessage.AnalysisSummary = string.IsNullOrEmpty(assistantMessage.ThinkingContent)
                                    ? "Stopped"
                                    : $"Stopped · {(assistantMessage.AnalysisDurationSeconds ?? (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds):F1} s";
                            }
                            else if (isError || isCancelled)
                            {
                                assistantMessage.IsError = true;
                                assistantMessage.StatusText = null;

                                if (isCancelled)
                                {
                                    assistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                                }
                                else if (isError)
                                {
                                    var errorText = !string.IsNullOrWhiteSpace(result)
                                        ? result
                                        : "[错误] 模型未返回任何内容。可能原因：端点无响应、API 密钥无效、模型配置错误或网络故障。请检查端点/密钥或切换其他模型重试。";
                                    assistantMessage.Content = SanitizeFinalBubbleContent(errorText);
                                }
                            }
                            else
                            {
                                assistantMessage.StatusText = null;
                            }
                        });
                    }
                }

                if (assistantMessage != null && string.IsNullOrWhiteSpace(assistantMessage.Content) && !string.IsNullOrWhiteSpace(result))
                {
                    assistantMessage.Content = SanitizeFinalBubbleContent(result);
                    TM.App.Log("[SKConversationViewModel] 防御: 流式结束后 Content 为空，使用 result 兜底");
                }

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();

                if (!isError && !isCancelled && !isCancelledWithPartial)
                {
                    var profile = ModeProfileRegistry.GetProfile(effectiveMode);
                    ConversationMessage convMessage;

                    if (isPlanMode && prebuiltPlanMessage != null)
                    {
                        convMessage = prebuiltPlanMessage;
                        var planPayload = convMessage.Payload as PlanPayload;
                        if (planPayload != null && planPayload.Steps.Count > 0)
                        {
                            _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload, _chatService.LastRunId);
                            _comm.PublishShowPlanViewChanged(true);
                        }
                        else
                        {
                            _cachedPlanSteps = null;
                        }
                    }
                    else if (isPlanMode)
                    {
                        var planContent = planContentBuilder?.ToString() ?? result;
                        convMessage = await ParseAndPublishPlanStepsAsync(userText, planContent, assistantMessage?.ThinkingContent ?? string.Empty, _chatService.LastRunId);
                    }
                    else
                    {
                        convMessage = await Task.Run(async () => await profile.Mapper.MapFromStreamingResultAsync(userText, result, assistantMessage?.ThinkingContent ?? string.Empty).ConfigureAwait(false));
                    }

                    TM.App.Log($"[SKConversationViewModel] {profile.Description} 消息映射完成");

                    var dispatcher3 = Application.Current?.Dispatcher;
                    if (dispatcher3 != null)
                    {
                        await dispatcher3.InvokeAsync(() =>
                        {
                            string displayContent;
                            if (profile.DisplayPolicy.HideRawContentInBubble)
                            {
                                displayContent = profile.DisplayPolicy.SummarySelector(convMessage);
                            }
                            else
                            {
                                displayContent = convMessage.Summary;
                            }

                            displayContent = SanitizeFinalBubbleContent(displayContent);

                            convMessage = new ConversationMessage
                            {
                                Role = convMessage.Role,
                                Timestamp = convMessage.Timestamp,
                                Summary = displayContent,
                                AnalysisRaw = convMessage.AnalysisRaw,
                                Payload = convMessage.Payload
                            };

                            assistantMessage?.ApplyFromConversationMessage(convMessage);

                            if (isPlanMode && convMessage.Payload == null && assistantMessage != null)
                            {
                                assistantMessage.IsError = true;
                            }
                        });
                    }

                    if (string.IsNullOrWhiteSpace(assistantMessage!.Content) && !string.IsNullOrWhiteSpace(result))
                    {
                        assistantMessage.Content = SanitizeFinalBubbleContent(result);
                        TM.App.Log("[SKConversationViewModel] 防御: Content 为空，使用 result 兜底");
                    }

                    _chatService.SaveMessages(Messages);

                    if (CurrentMode == ChatMode.Agent && !string.IsNullOrWhiteSpace(imitateBookId))
                    {
                        try
                        {
                            var chapterTitle = $"仿写：{(string.IsNullOrWhiteSpace(imitateBookTitle) ? imitateBookId : imitateBookTitle)}";

                            var writer = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.Plugins.WriterPlugin>();
                            var saved = await writer.SaveExternalChapterAsync(CancellationToken.None, chapterTitle, result);

                            Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                _comm.PublishRefreshChapterList();
                                _comm.PublishChapterSelected(saved.ChapterId, chapterTitle, saved.DisplayContent);
                            });

                            TM.App.Log($"[SKConversationViewModel] 仿写已保存为章节: {saved.ChapterId}");

                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[SKConversationViewModel] 仿写保存章节失败: {ex.Message}");
                            GlobalToast.Error("仿写保存失败", $"仿写保存失败：{ex.Message}");
                        }
                    }
                }

                TM.App.Log($"[SKConversationViewModel] 消息完成: {result.Length} 字符");
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[SKConversationViewModel] 用户已取消生成");
                if (assistantMessage != null)
                {
                    assistantMessage.IsThinking = false;
                    assistantMessage.FinishStreaming();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 发送失败: {ex.Message}");

                var friendly = FormatErrorMessage(ex);
                if (assistantMessage != null)
                {
                    assistantMessage.IsThinking = false;
                    assistantMessage.IsError = true;
                    assistantMessage.Content = $"发送失败：{friendly}";
                    assistantMessage.FinishStreaming();
                }
                else
                {
                    Messages.Add(UIMessageItem.CreateErrorMessage($"发送失败：{friendly}"));
                }
            }
            finally
            {
                IsGenerating = false;
                RefreshContextUsage();

                FinalizeAssistantMessageIfIncomplete(assistantMessage, startTime);
            }
        }

        #endregion
    }
}

