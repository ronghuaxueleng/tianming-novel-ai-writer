using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Framework.Common.Helpers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Framework.Common.Services;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Config;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;
using ConvPlanStep = TM.Services.Framework.AI.SemanticKernel.Conversation.Models.PlanStep;
using TM.Framework.UI.Workspace.RightPanel.Dialogs;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SKConversationViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SKChatService _chatService;
        private readonly PanelCommunicationService _comm;
        private readonly AIService _aiService;
        private readonly TodoExecutionService _todoExecutionService;
        private readonly GuideContextService _guideContextService;
        private readonly NovelCrawlerService _novelCrawlerService;
        private readonly TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService _modelService;

        private string _inputText = string.Empty;
        private string _lastSentUserText = string.Empty;
        private bool _hasPlanContinueAction;
        private string _planContinueDisplayPrefix = string.Empty;
        private int _planContinueStartNum;
        private string _planContinueEndText = string.Empty;
        private string? _pendingPlanContinueText;
        private bool _hasAgentActions;
        private string _agentContinueLabel = string.Empty;
        private string _agentContinueText = string.Empty;
        private string _agentRewriteLabel = string.Empty;
        private string _agentRewriteText = string.Empty;
        private string _resolvedChapterHint = string.Empty;
        private CancellationTokenSource? _hintDebounceCts;
        private CancellationTokenSource? _prebuiltSimulationCts;
        private bool _isGenerating;
        private ChatMode _currentMode = ChatMode.Ask;
        private ChatMode _lastExecutedMode = ChatMode.Ask;
        private UIMessageItem? _selectedMessage;
        private string _sessionTitle = "新会话";
        private string _monitorTitle = "ASK";
        private string _monitorSubTitle = "空闲";
        private bool _isMultiSelectMode;

        private string? _pendingContinueSourceId;
        private string? _pendingRewriteTargetId;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private EventHandler? _cfgChangedHandler;

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

            System.Diagnostics.Debug.WriteLine($"[SKConversationViewModel] {key}: {ex.Message}");
        }

        private string? _currentSessionId;

        private bool _hasDraftConversation;

        public bool HasDraftConversation
        {
            get => _hasDraftConversation;
            private set
            {
                if (_hasDraftConversation != value)
                {
                    _hasDraftConversation = value;
                    OnPropertyChanged();
                }
            }
        }

        private static string? ExtractChapterIdFromDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(detail, @"章节ID:\s*(\S+)");
            if (!match.Success)
                return null;

            var candidate = match.Groups[1].Value.Trim();

            var parsed = ChapterParserHelper.ParseChapterId(candidate);
            if (parsed.HasValue)
                return candidate;

            return null;
        }

        private async Task<string?> ValidateChapterGenerationRequestBeforeExecutionAsync(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                return null;
            }

            if (ChapterDirectiveParser.HasRewriteDirective(userText) || userText.Contains("重写", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!IsExplicitChapterGenerationRequest(userText))
            {
                return null;
            }

            var ranges = ChapterParserHelper.ParseChapterRanges(userText);
            var range = ChapterParserHelper.ParseChapterRange(userText);
            var chapterList = ChapterParserHelper.ParseChapterNumberList(userText);

            int? explicitVolume = null;
            var (volFromNl, chFromNl) = ChapterParserHelper.ParseFromNaturalLanguage(userText);
            if (volFromNl.HasValue && volFromNl.Value > 0)
            {
                explicitVolume = volFromNl.Value;
            }
            else
            {
                var extractedVol = ChapterParserHelper.ExtractVolumeNumber(userText);
                if (extractedVol > 0)
                {
                    explicitVolume = extractedVol;
                }
            }

            var requestedNumbers = new SortedSet<int>();
            if (ranges != null && ranges.Count > 0)
            {
                foreach (var (start, end) in ranges)
                {
                    for (var i = start; i <= end; i++)
                    {
                        requestedNumbers.Add(i);
                        if (requestedNumbers.Count >= 500)
                        {
                            break;
                        }
                    }
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (range.HasValue)
            {
                for (var i = range.Value.start; i <= range.Value.end; i++)
                {
                    requestedNumbers.Add(i);
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (chapterList != null && chapterList.Count > 0)
            {
                foreach (var n in chapterList)
                {
                    requestedNumbers.Add(n);
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (chFromNl.HasValue && chFromNl.Value > 0)
            {
                requestedNumbers.Add(chFromNl.Value);
            }

            if (requestedNumbers.Count == 0)
            {
                return null;
            }

            var contentService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
            var existing = new List<string>();
            var ambiguousExisting = new List<string>();

            List<int>? availableVolumes = null;
            IList<TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData>? designs = null;

            if (!explicitVolume.HasValue)
            {
                try
                {
                    var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                    await volumeService.InitializeAsync();
                    var all = volumeService.GetAllVolumeDesigns();

                    var scopeId = ServiceLocator.Get<TM.Services.Modules.ProjectData.Interfaces.IWorkScopeService>().CurrentSourceBookId;
                    if (!string.IsNullOrEmpty(scopeId))
                    {
                        all = all.Where(v => string.Equals(v.SourceBookId, scopeId, StringComparison.Ordinal)).ToList();
                    }

                    designs = all;
                    availableVolumes = all
                        .Where(v => v.VolumeNumber > 0)
                        .Select(v => v.VolumeNumber)
                        .Distinct()
                        .OrderBy(v => v)
                        .ToList();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKConversationViewModel] 预校验加载分卷设计失败: {ex.Message}");
                }
            }

            foreach (var chapterNumber in requestedNumbers)
            {
                if (chapterNumber <= 0)
                {
                    continue;
                }

                if (explicitVolume.HasValue && explicitVolume.Value > 0)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(explicitVolume.Value, chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (designs == null || availableVolumes == null || availableVolumes.Count == 0)
                {
                    continue;
                }

                var matches = designs
                    .Where(v => v.VolumeNumber > 0)
                    .Where(v => v.StartChapter > 0)
                    .Where(v => v.EndChapter <= 0
                        ? chapterNumber >= v.StartChapter
                        : chapterNumber >= v.StartChapter && chapterNumber <= v.EndChapter)
                    .Select(v => v.VolumeNumber)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                if (matches.Count == 1)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(matches[0], chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (matches.Count == 0 && availableVolumes.Count == 1)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(availableVolumes[0], chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (matches.Count > 0)
                {
                    foreach (var vol in matches)
                    {
                        var candidateId = ChapterParserHelper.BuildChapterId(vol, chapterNumber);
                        if (contentService.ChapterExists(candidateId))
                        {
                            ambiguousExisting.Add(candidateId);
                        }
                    }
                }
            }

            if (existing.Count > 0)
            {
                var list = string.Join("、", existing.Distinct().Take(6));
                var suffix = existing.Distinct().Count() > 6 ? "..." : string.Empty;
                var first = existing[0];
                return $"检测到目标章节已存在：{list}{suffix}。\n" +
                       $"如需重新生成请使用 @重写:{first}；\n" +
                       $"如需生成新章，请从未生成的章节开始，或明确卷号（如“生成第X卷第Y章”）。";
            }

            if (ambiguousExisting.Count > 0)
            {
                var list = string.Join("、", ambiguousExisting.Distinct().Take(6));
                var suffix = ambiguousExisting.Distinct().Count() > 6 ? "..." : string.Empty;
                return $"检测到请求的章节在多个卷中可能已生成：{list}{suffix}。\n" +
                       $"为避免误生成，请明确卷号（如“生成第X卷第Y章”），或使用 @重写:volN_chM。";
            }

            return null;
        }

        private static bool IsExplicitChapterGenerationRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!ChapterParserHelper.ContainsChapterReference(text)
                && ChapterParserHelper.ParseChapterRange(text) == null
                && ChapterParserHelper.ParseChapterRanges(text) == null
                && ChapterParserHelper.ParseChapterNumberList(text) == null)
            {
                return false;
            }

            var t = text.Replace(" ", string.Empty);
            if (t.Contains("重写", StringComparison.OrdinalIgnoreCase) || t.Contains("改写", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return t.Contains("生成", StringComparison.OrdinalIgnoreCase)
                || t.Contains("写", StringComparison.OrdinalIgnoreCase)
                || t.Contains("创作", StringComparison.OrdinalIgnoreCase)
                || t.Contains("续写", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeChapterHint(string userInput, string rawContent)
        {
            var (start, end) = ChapterParserHelper.ParseChapterRange(userInput) ?? (0, 0);
            var ranges = ChapterParserHelper.ParseChapterRanges(userInput);
            var list = ChapterParserHelper.ParseChapterNumberList(userInput);
            var (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(userInput);

            if (!vol.HasValue && !ch.HasValue && start <= 0 && (ranges == null || ranges.Count == 0) && (list == null || list.Count == 0))
            {
                (start, end) = ChapterParserHelper.ParseChapterRange(rawContent) ?? (0, 0);
                ranges = ChapterParserHelper.ParseChapterRanges(rawContent);
                list = ChapterParserHelper.ParseChapterNumberList(rawContent);
                (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(rawContent);
            }

            if (ranges != null && ranges.Count > 0)
            {
                var parts = new List<string>();
                foreach (var (rangeStart, rangeEnd) in ranges)
                {
                    if (rangeStart > 0 && rangeEnd >= rangeStart)
                    {
                        parts.Add($"第{rangeStart}到{rangeEnd}章");
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join("和", parts);
                }
            }

            if (start > 0 && end >= start)
            {
                return $"第{start}到{end}章";
            }

            if (list != null && list.Count > 0)
            {
                return $"第{string.Join("、", list)}章";
            }

            if (vol.HasValue && ch.HasValue)
            {
                return $"第{vol.Value}卷第{ch.Value}章";
            }

            if (ch.HasValue)
            {
                return $"第{ch.Value}章";
            }

            return userInput;
        }

        private void SyncSessionFromServiceAfterPersist()
        {
            var sessionId = _chatService.Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            _currentSessionId = sessionId;
            HasDraftConversation = false;

            var sessions = _chatService.Sessions.GetAllSessions();
            var current = sessions.Find(s => s.Id == sessionId);
            if (current != null)
            {
                SessionTitle = current.Title;
            }
        }

        private bool _wasExecutionCancelledByUser;

        private UIMessageItem? _currentExecutionAssistantMessage;

        private IReadOnlyList<ConvPlanStep>? _cachedPlanSteps;

        private ConversationModeProfile CurrentProfile => ModeProfileRegistry.GetProfile(_chatService.CurrentMode);

        public SKConversationViewModel(
            SKChatService chatService,
            PanelCommunicationService comm,
            AIService aiService,
            TodoExecutionService todoExecutionService,
            GuideContextService guideContextService,
            NovelCrawlerService novelCrawlerService,
            TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService modelService)
        {
            _chatService = chatService;
            _comm = comm;
            _aiService = aiService;
            _todoExecutionService = todoExecutionService;
            _guideContextService = guideContextService;
            _novelCrawlerService = novelCrawlerService;
            _modelService = modelService;

            SendCommand = new AsyncRelayCommand(async () => await SendMessageAsync(), () => CanSend);
            CancelCommand = new RelayCommand(() => CancelGeneration(), () => IsGenerating);
            NewSessionCommand = new RelayCommand(() => NewSession());
            ClearHistoryCommand = new RelayCommand(() => ClearHistory());
            ClearSessionCommand = new RelayCommand(() => ClearHistory());
            ShowHistoryCommand = new RelayCommand(ShowHistory);

            CopyMessageCommand = new RelayCommand(
                () => { if (SelectedMessage != null) CopyMessage(SelectedMessage); },
                () => SelectedMessage != null);

            DeleteMessageCommand = new RelayCommand(
                () => { if (SelectedMessage != null) DeleteMessage(SelectedMessage); },
                () => SelectedMessage != null);

            DeleteUserWithAssistantCommand = new RelayCommand(
                () => { if (SelectedMessage != null) DeleteUserWithAssistant(SelectedMessage); },
                () => SelectedMessage != null && SelectedMessage.IsUser);

            RecallToInputCommand = new RelayCommand(
                () => { if (SelectedMessage != null) RecallToInput(SelectedMessage); },
                () => SelectedMessage != null && SelectedMessage.IsUser);

            RegenerateAssistantMessageCommand = new AsyncRelayCommand(
                async () => { if (SelectedMessage != null) await RegenerateAsync(SelectedMessage); },
                () => SelectedMessage != null);

            RegenerateFromUserMessageCommand = new AsyncRelayCommand(
                async () => { if (SelectedMessage != null) await RegenerateAsync(SelectedMessage); },
                () => SelectedMessage != null);

            RegenerateFromHereCommand = new AsyncRelayCommand(
                async () => { if (SelectedMessage != null) await RegenerateFromHereAsync(SelectedMessage); },
                () => SelectedMessage != null && !IsGenerating);

            ToggleStarCommand = new RelayCommand(
                () => { if (SelectedMessage != null) ToggleStar(SelectedMessage); },
                () => SelectedMessage != null);

            ExportMessageCommand = new RelayCommand(ExportMessages, () => SelectedMessage != null || SelectedMessages.Count > 0);

            ShowStarredMessagesCommand = new RelayCommand(ShowStarredMessages);

            EditUserMessageCommand = new RelayCommand(EditUserMessage, () => SelectedMessage != null && SelectedMessage.IsUser);
            SwitchModelAnswerCommand = new AsyncRelayCommand(SwitchModelAnswerAsync, () => SelectedMessage != null && SelectedMessage.IsAssistant);
            TranslateMessageCommand = new AsyncRelayCommand(TranslateMessageAsync, () => SelectedMessage != null && SelectedMessage.IsAssistant);

            ToggleMultiSelectCommand = new RelayCommand(ToggleMultiSelectMode);

            QuickFillInputCommand = new RelayCommand(param =>
            {
                if (param is string prefix)
                {
                    InputText = prefix;
                    SuggestedActions.Clear();
                    OnPropertyChanged(nameof(HasSuggestedActions));
                    QuickFillInputRequested?.Invoke(this, EventArgs.Empty);
                }
            });

            QuickSendCommand = new AsyncRelayCommand(async param =>
            {
                if (param is string text && !string.IsNullOrWhiteSpace(text))
                {
                    SuggestedActions.Clear();
                    OnPropertyChanged(nameof(HasSuggestedActions));
                    InputText = text;
                    await SendMessageAsync();
                }
            });

            SendPlanContinueCommand = new AsyncRelayCommand(async _ =>
            {
                var rawEndText = PlanContinueEndText.Trim();
                if (string.IsNullOrWhiteSpace(rawEndText))
                {
                    return;
                }

                var normalized = rawEndText
                    .Replace("章节", string.Empty)
                    .Replace("章", string.Empty)
                    .Replace("第", string.Empty)
                    .Trim();

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    GlobalToast.Warning("请输入结束章", "例如：70");
                    return;
                }

                int endNum;
                if (!int.TryParse(normalized, out endNum))
                {
                    endNum = ChapterParserHelper.ExtractChapterNumber($"第{normalized}章");
                }

                if (endNum <= 0)
                {
                    GlobalToast.Warning("结束章不合法", "请输入正确的章节号");
                    return;
                }

                if (endNum < _planContinueStartNum)
                {
                    GlobalToast.Warning("结束章不能小于起始章", $"起始章为：{_planContinueStartNum}");
                    return;
                }

                var fullText = $"{_planContinueDisplayPrefix}{_planContinueStartNum}-{endNum}章";
                _hasPlanContinueAction = false;
                OnPropertyChanged(nameof(HasPlanContinueAction));
                OnPropertyChanged(nameof(HasSuggestedActions));
                PlanContinueEndText = string.Empty;

                if (IsGenerating)
                {
                    _pendingPlanContinueText = fullText;
                    GlobalToast.Info("已排队", $"当前生成结束后自动发送：{fullText}");
                    return;
                }

                InputText = fullText;
                await SendMessageAsync();
            });

            AgentContinueCommand = new AsyncRelayCommand(async _ =>
            {
                if (string.IsNullOrWhiteSpace(_agentContinueText)) return;
                _hasAgentActions = false;
                OnPropertyChanged(nameof(HasAgentActions));
                OnPropertyChanged(nameof(HasAgentContinue));
                OnPropertyChanged(nameof(HasSuggestedActions));
                InputText = _agentContinueText;
                await SendMessageAsync();
            });

            AgentRewriteCommand = new AsyncRelayCommand(async _ =>
            {
                _hasAgentActions = false;
                OnPropertyChanged(nameof(HasAgentActions));
                OnPropertyChanged(nameof(HasSuggestedActions));
                InputText = _agentRewriteText;
                await SendMessageAsync();
            });

            _ = LoadHistoryMessagesAsync();

            RefreshModelConfigurations();

            var cfgService = (TM.Services.Framework.AI.Interfaces.AI.IAIConfigurationService)_aiService;
            _cfgChangedHandler = (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(RefreshModelConfigurations));
            };
            cfgService.ConfigurationsChanged += _cfgChangedHandler;

            _currentSessionId = _chatService.Sessions.GetCurrentSessionIdOrNull();
            HasDraftConversation = false;

            if (!string.IsNullOrEmpty(_currentSessionId))
            {
                LoadSessionMode();

                var initSession = _chatService.Sessions.GetAllSessions()
                    .Find(s => s.Id == _currentSessionId);
                if (initSession != null && !string.IsNullOrEmpty(initSession.ContextChapterId))
                {
                    CurrentChapterTracker.SetCurrentChapter(initSession.ContextChapterId);
                    TM.App.Log($"[SKConversationViewModel] 启动：章节上下文恢复为 {initSession.ContextChapterId}");
                }
            }

            ExecutionEventHub.Published += OnExecutionEvent;

            _comm.HighlightExecutionRequested += OnHighlightExecutionRequested;

            _comm.SendMessageRequested += OnSendMessageRequested;

            _comm.StartPlanExecutionRequested += OnStartPlanExecutionRequested;

            RefreshContextUsage();

            TM.App.Log("[SKConversationViewModel] 初始化完成");
        }

        public void Dispose()
        {
            ExecutionEventHub.Published -= OnExecutionEvent;
            _comm.HighlightExecutionRequested -= OnHighlightExecutionRequested;
            _comm.SendMessageRequested -= OnSendMessageRequested;
            _comm.StartPlanExecutionRequested -= OnStartPlanExecutionRequested;

            if (_cfgChangedHandler != null)
            {
                var cfgService = (TM.Services.Framework.AI.Interfaces.AI.IAIConfigurationService)_aiService;
                cfgService.ConfigurationsChanged -= _cfgChangedHandler;
                _cfgChangedHandler = null;
            }
        }

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
                GlobalToast.Error("发送失败", ex.Message);
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

                var contentServicePlan = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
                var existingInPlan = new List<string>();
                foreach (var step in steps)
                {
                    var chId = ExtractChapterIdFromDetail(step.Detail);
                    if (!string.IsNullOrWhiteSpace(chId) && contentServicePlan.ChapterExists(chId))
                    {
                        existingInPlan.Add(chId);
                    }
                }

                if (existingInPlan.Count > 0)
                {
                    var list = string.Join("、", existingInPlan.Distinct().Take(6));
                    var suffix = existingInPlan.Distinct().Count() > 6 ? "..." : string.Empty;
                    var first = existingInPlan[0];
                    var errMsg = $"检测到计划中包含已存在的章节：{list}{suffix}。\n" +
                                 $"如需重新生成请使用 @重写:{first}；\n" +
                                 $"如需生成新章，请从未生成的章节开始。";
                    Messages.Add(UIMessageItem.CreateErrorMessage(errMsg));
                    GlobalToast.Warning("已阻止执行", "计划中包含已生成章节，请按提示调整");
                    TM.App.Log($"[SKConversationViewModel] PlanView执行预校验阻断: {list}{suffix}");
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
                GlobalToast.Error("执行失败", ex.Message);
            }
        }

        private async Task RunTodoExecutionAsync(
            ChatMode mode,
            IReadOnlyList<(int Index, string Title, string Detail)> steps,
            string analysisSummary)
        {
            UIMessageItem? assistantMessage = null;
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

                var startTime = DateTime.Now;

                var tasks = new List<TodoExecutionTask>();
                var writerPlugin = new TM.Services.Framework.AI.SemanticKernel.Plugins.WriterPlugin();

                foreach (var step in steps)
                {
                    var rawTitle = step.Title;
                    var rawDetail = step.Detail;

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

                var runId = _todoExecutionService.StartSequentialRun(mode, tasks, _chatService.LastRunId);

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

                var capturedAssistant = assistantMessage;
                Action<string> onProgress = msg =>
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        capturedAssistant.AppendThinking(msg + "\n");
                    });
                };
                GenerationProgressHub.ProgressReported += onProgress;

                try
                {
                    await Task.Run(async () =>
                    {
                        while (_todoExecutionService.IsRunning)
                        {
                            await Task.Delay(500);
                        }
                    });
                }
                finally
                {
                    GenerationProgressHub.ProgressReported -= onProgress;
                }

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
                        assistantMessage.AnalysisDurationSeconds = seconds;
                        assistantMessage.AnalysisSummary = $"Thought for {seconds:F1} s";
                        assistantMessage.IsThinking = false;

                        if (traceSummary.FailedSteps > 0 && traceSummary.FailedStepSummaries.Count > 0)
                        {
                            var failLines = new System.Text.StringBuilder();
                            failLines.AppendLine("生成未能完成，原因如下：");
                            failLines.AppendLine();
                            foreach (var s in traceSummary.FailedStepSummaries)
                                failLines.AppendLine($"• {s}");
                            assistantMessage.Content = SanitizeFinalBubbleContent(failLines.ToString().TrimEnd());
                            assistantMessage.IsError = true;
                            TM.App.Log($"[SKConversationViewModel] 执行失败，{traceSummary.ToSummaryText()}");
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

                        var (oldChapter, newChapter) = TM.Services.Framework.AI.SemanticKernel.Plugins.ChapterDiffContext.Take();
                        if (!string.IsNullOrEmpty(oldChapter) && !string.IsNullOrEmpty(newChapter))
                        {
                            assistantMessage.OldChapterContent = oldChapter;
                            assistantMessage.NewChapterContent = newChapter;
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
                        assistantMessage.Content = SanitizeFinalBubbleContent($"执行失败: {ex.Message}");
                        assistantMessage.IsError = true;
                        assistantMessage.FinishStreaming();
                    });
                }
                else
                {
                    Messages.Add(UIMessageItem.CreateErrorMessage($"执行失败: {ex.Message}"));
                }
                GlobalToast.Error("执行失败", ex.Message);
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
            }
        }

        private static string SanitizeBubbleChunk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
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

            var t = userText.Trim();
            if (t.Contains("@plan", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("@规划", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("todo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var hasActionVerb = t.Contains("生成") || t.Contains("写") || t.Contains("创作")
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
                && (normalized.Contains("章") || normalized.Contains("章节")))
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

        #region 属性

        public ObservableCollection<UIMessageItem> Messages { get; } = new();

        public ObservableCollection<UIMessageItem> SelectedMessages { get; } = new();

        public ObservableCollection<ExecutionEvent> RunEvents { get; } = new();

        public TodoPanelViewModel TodoPanelViewModel { get; } = new();

        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(InputCharacterCount));
                RefreshContextUsage();
                _ = UpdateChapterHintAsync();
            }
        }

        public string InputCharacterCount => $"{_inputText?.Length ?? 0} 字符";

        public string ResolvedChapterHint
        {
            get => _resolvedChapterHint;
            private set
            {
                if (_resolvedChapterHint != value)
                {
                    _resolvedChapterHint = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasResolvedChapter));
                }
            }
        }

        public bool HasResolvedChapter => !string.IsNullOrEmpty(_resolvedChapterHint);

        private async Task UpdateChapterHintAsync()
        {
            _hintDebounceCts?.Cancel();
            _hintDebounceCts = new CancellationTokenSource();
            var ct = _hintDebounceCts.Token;

            if (string.IsNullOrWhiteSpace(_inputText))
            {
                ResolvedChapterHint = string.Empty;
                return;
            }

            try
            {
                await Task.Delay(300, ct);
                ct.ThrowIfCancellationRequested();

                var chapterId = await ResolveChapterIdFromTextAsync(_inputText);
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(chapterId))
                {
                    var title = await _guideContextService.GetChapterTitleAsync(chapterId) ?? chapterId;
                    ct.ThrowIfCancellationRequested();
                    ResolvedChapterHint = $"上下文章节：{title}";
                }
                else
                {
                    ResolvedChapterHint = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(UpdateChapterHintAsync), ex);
                ResolvedChapterHint = string.Empty;
            }
        }

        private static string GetModeDisplayName(ChatMode mode)
        {
            return mode switch
            {
                ChatMode.Ask => "Ask",
                ChatMode.Agent => "Agent",
                ChatMode.Plan => "Plan",
                _ => mode.ToString()
            };
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                var wasGenerating = _isGenerating;
                _isGenerating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(IsSending));

                if (!wasGenerating && value)
                {
                    SuggestedActions.Clear();
                    _hasPlanContinueAction = false;
                    _hasAgentActions = false;
                    PlanContinueEndText = string.Empty;
                    OnPropertyChanged(nameof(HasPlanContinueAction));
                    OnPropertyChanged(nameof(HasAgentActions));
                    OnPropertyChanged(nameof(HasSuggestedActions));
                }
                else if (wasGenerating && !value)
                {
                    if (!string.IsNullOrWhiteSpace(_pendingPlanContinueText))
                    {
                        var pendingText = _pendingPlanContinueText;
                        _pendingPlanContinueText = null;
                        Application.Current?.Dispatcher.BeginInvoke(async () =>
                        {
                            InputText = pendingText;
                            await SendMessageAsync();
                        });
                    }
                    else
                    {
                        RefreshSuggestedActions();
                    }
                }
            }
        }

        public ChatMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    _chatService.CurrentMode = value;
                    MonitorTitle = GetModeDisplayName(value);
                    OnPropertyChanged();
                }
            }
        }

        private bool _showTodoOverlay;
        public bool ShowTodoOverlay
        {
            get => _showTodoOverlay;
            set
            {
                if (_showTodoOverlay != value)
                {
                    _showTodoOverlay = value;
                    OnPropertyChanged();
                }
            }
        }

        public UIMessageItem? SelectedMessage
        {
            get => _selectedMessage;
            set { _selectedMessage = value; OnPropertyChanged(); }
        }

        public string SessionTitle
        {
            get => _sessionTitle;
            set { _sessionTitle = value; OnPropertyChanged(); }
        }

        private ExecutionEvent? _selectedRunEvent;
        public ExecutionEvent? SelectedRunEvent
        {
            get => _selectedRunEvent;
            set
            {
                if (_selectedRunEvent != value)
                {
                    _selectedRunEvent = value;
                    OnPropertyChanged();

                    if (value != null)
                    {
                        HighlightMessagesForRun(value.RunId);
                    }
                }
            }
        }

        public string MonitorTitle
        {
            get => _monitorTitle;
            set { _monitorTitle = value; OnPropertyChanged(); }
        }

        public string MonitorSubTitle
        {
            get => _monitorSubTitle;
            set { _monitorSubTitle = value; OnPropertyChanged(); }
        }

        public bool CanSend => !string.IsNullOrWhiteSpace(InputText) && !IsGenerating;

        public bool IsSending => IsGenerating;

        public ObservableCollection<QuickActionItem> SuggestedActions { get; } = new();

        public bool HasSuggestedActions => SuggestedActions.Count > 0 || _hasPlanContinueAction || _hasAgentActions;

        public bool HasPlanContinueAction => _hasPlanContinueAction;
        public bool HasAgentActions => _hasAgentActions;
        public bool HasAgentContinue => !string.IsNullOrEmpty(_agentContinueLabel);
        public string AgentContinueLabel => _agentContinueLabel;
        public string AgentRewriteLabel => _agentRewriteLabel;
        public string PlanContinueDisplayPrefix => _planContinueDisplayPrefix;
        public int PlanContinueStartNum => _planContinueStartNum;
        public string PlanContinueEndText
        {
            get => _planContinueEndText;
            set { _planContinueEndText = value; OnPropertyChanged(); }
        }

        public event EventHandler? QuickFillInputRequested;

        private string _lastRunStatus = "Idle";

        public string CurrentModeActiveColor
        {
            get
            {
                return _lastRunStatus switch
                {
                    "Running" => "#22C55E",
                    "Failed"  => "#DC2626",
                    _          => "#9CA3AF"
                };
            }
        }

        public ObservableCollection<UserConfiguration> ModelConfigurations { get; } = new();

        public UserConfiguration? ActiveConfiguration
        {
            get
            {
                var active = _aiService.GetActiveConfiguration();
                if (active == null) return null;
                return ModelConfigurations.FirstOrDefault(c => c.Id == active.Id);
            }
            set
            {
                if (value != null)
                {
                    _aiService.SetActiveConfiguration(value.Id);
                    OnPropertyChanged();
                    RefreshContextUsage();
                }
            }
        }

        public double ContextUsagePercent
        {
            get
            {
                var (_, _, percent) = _chatService.GetContextUsage(InputText);
                return percent;
            }
        }

        public string ContextUsagePercentText
        {
            get
            {
                var percent = ContextUsagePercent;
                return $"{percent:F0}";
            }
        }

        public string ContextUsageDetailLine1
        {
            get
            {
                var (tokens, contextWindow, _) = _chatService.GetContextUsage(InputText);
                if (contextWindow <= 0) return "未选择模型";
                return $"{FormatTokenCount(tokens)} / {FormatTokenCount(contextWindow)}";
            }
        }

        public string ContextUsageStatusText
        {
            get
            {
                var (_, _, percent) = _chatService.GetContextUsage(InputText);
                if (percent >= 95) return "⚠ 即将自动压缩对话";
                if (percent >= 80) return "⚠ 接近上下文上限";
                if (percent >= 60) return "注意：上下文使用较多";
                return string.Empty;
            }
        }

        public string ContextUsageColor
        {
            get
            {
                var percent = ContextUsagePercent;
                if (percent >= 95) return "#DC2626";
                if (percent >= 80) return "#F59E0B";
                if (percent >= 60) return "#EAB308";
                return "#22C55E";
            }
        }

        public bool IsSessionCompressed => _chatService.IsSessionCompressed;

        public bool IsMultiSelectMode
        {
            get => _isMultiSelectMode;
            set
            {
                if (_isMultiSelectMode != value)
                {
                    _isMultiSelectMode = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 导出消息

        private void ExportMessages()
        {
            var items = IsMultiSelectMode && SelectedMessages.Any()
                ? SelectedMessages.ToList()
                : (SelectedMessage != null ? new System.Collections.Generic.List<UIMessageItem> { SelectedMessage } : new System.Collections.Generic.List<UIMessageItem>());

            if (!items.Any()) return;

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
                StandardDialog.ShowError("导出失败", ex.Message);
            }
        }

        #endregion

        #region 命令

        public ICommand SendCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ClearSessionCommand { get; }
        public ICommand ShowHistoryCommand { get; }

        public ICommand CopyMessageCommand { get; }
        public ICommand DeleteMessageCommand { get; }
        public ICommand DeleteUserWithAssistantCommand { get; }
        public ICommand RecallToInputCommand { get; }
        public ICommand RegenerateAssistantMessageCommand { get; }
        public ICommand RegenerateFromUserMessageCommand { get; }
        public ICommand RegenerateFromHereCommand { get; }
        public ICommand ToggleStarCommand { get; }
        public ICommand ExportMessageCommand { get; }
        public ICommand ShowStarredMessagesCommand { get; }
        public ICommand EditUserMessageCommand { get; }
        public ICommand SwitchModelAnswerCommand { get; }
        public ICommand TranslateMessageCommand { get; }
        public ICommand ToggleMultiSelectCommand { get; }

        #endregion

        #region 快捷面板命令

        public ICommand QuickFillInputCommand { get; }
        public ICommand QuickSendCommand { get; }
        public ICommand SendPlanContinueCommand { get; }
        public ICommand AgentContinueCommand { get; }
        public ICommand AgentRewriteCommand { get; }

        #endregion

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
            SuggestedActions.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            PlanContinueEndText = string.Empty;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasSuggestedActions));
            InputText = string.Empty;

            var effectiveMode = CurrentMode;
            if (effectiveMode == ChatMode.Agent && !ShouldRunAgentExecution(userText))
            {
                effectiveMode = ChatMode.Ask;
            }
            else if (effectiveMode == ChatMode.Plan && !ShouldRunPlanMode(userText))
            {
                effectiveMode = ChatMode.Ask;
            }

            _lastExecutedMode = effectiveMode;

            if (effectiveMode == ChatMode.Plan || effectiveMode == ChatMode.Agent)
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

            if (effectiveMode == ChatMode.Agent)
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                await StartAgentExecutionAsync(userText);
                return;
            }

            IsGenerating = true;

            var startTime = DateTime.Now;

            try
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                var assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = "Thinking...";
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

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

                        return await Task.Run(() =>
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

                                var json = System.IO.File.ReadAllText(bookInfoPath);
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

                if (!string.IsNullOrWhiteSpace(imitateBookId))
                {
                    try
                    {
                        var crawler = _novelCrawlerService;
                        var crawled = await crawler.LoadCrawledContentAsync(imitateBookId);
                        var excerpt = await crawler.LoadCrawledExcerptAsync(imitateBookId);

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
                                : $"{templateSection}\n\n<user_request>\n{cleanedUserText}\n</user_request>";
                        }
                        else
                        {
                            TM.App.Log($"[SKConversationViewModel] 已注入仿写上下文: {imitateBookId}");
                            var templateBlock = string.IsNullOrWhiteSpace(templateSection)
                                ? string.Empty
                                : $"\n\n{templateSection}";
                            finalUserText = $"<writing_context type=\"mimicry\">\n书名：{imitateBookTitle}\n{authorLine}\n{excerpt}{templateBlock}\n</writing_context>\n\n<user_request>\n{cleanedUserText}\n</user_request>";
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
                            var bridge = ServiceLocator.Get<ChapterGenerationBridge>();
                            var contextPrompt = await bridge.GetGenerationPromptAsync(chapterId);

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

                                finalUserText = $"<writing_context type=\"chapter\">\n{contextPrompt}\n</writing_context>\n\n<user_request>\n{cleanedUserText}\n</user_request>";
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
                        prebuiltPlanMessage = planMapper.TryBuildPlanWithoutModel(userText);
                    }
                }

                var promptParts = ChatPromptBridge.BuildParts(effectiveMode, finalUserText);

                string result;
                bool isError;
                bool isCancelled;
                bool isCancelledWithPartial = false;

                if (prebuiltPlanMessage != null)
                {
                    result = "[基于打包数据直接生成计划]";
                    isError = false;
                    isCancelled = false;

                    var prebuiltRunId = ExecutionEventHub.NewRunId();
                    _chatService.SetLastRunId(prebuiltRunId);
                    userMessage.RunId = prebuiltRunId;
                    assistantMessage.RunId = prebuiltRunId;

                    _prebuiltSimulationCts?.Cancel();
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
                                    assistantMessage.AppendThinking(chunk);
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
                            assistantMessage.AnalysisSummary = $"Stopped · {seconds:F1} s";
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
                    using (_chatService.UseTransientMode(effectiveMode))
                    {
                        result = await _chatService.SendStreamMessageAsync(
                            userText,
                            promptParts,
                            chunk =>
                            {
                                if (isPlanMode)
                                {
                                    planContentBuilder?.Append(chunk);
                                }
                                else
                                {
                                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                    {
                                        assistantMessage.AppendContent(SanitizeBubbleChunk(chunk));
                                    });
                                }
                            },
                            thinkingChunk =>
                            {
                                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                {
                                    assistantMessage.AppendThinking(thinkingChunk);
                                });
                            },
                            System.Threading.CancellationToken.None);
                    }

                    isError = result.StartsWith("[错误]");
                    isCancelledWithPartial = result.StartsWith("[已取消:部分]");
                    isCancelled = !isCancelledWithPartial && result.StartsWith("[已取消]");

                    var dispatcher2 = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher2 != null)
                    {
                        await dispatcher2.InvokeAsync(() =>
                        {
                            var runId = _chatService.LastRunId;
                            userMessage.RunId = runId;
                            assistantMessage.RunId = runId;

                            if (!string.IsNullOrEmpty(assistantMessage.ThinkingContent) && !assistantMessage.AnalysisDurationSeconds.HasValue)
                            {
                                var seconds = Math.Max(0.1, (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds);
                                assistantMessage.AnalysisDurationSeconds = seconds;
                                assistantMessage.AnalysisSummary = $"Thought for {seconds:F1} s";
                            }

                            assistantMessage.FinishStreaming();

                            if (!isError && !isCancelled)
                                assistantMessage.References = _chatService.GetLastRAGReferences();

                            if (isCancelledWithPartial)
                            {
                                assistantMessage.AnalysisSummary = string.IsNullOrEmpty(assistantMessage.ThinkingContent)
                                    ? "Stopped"
                                    : $"Stopped · {(assistantMessage.AnalysisDurationSeconds ?? (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds):F1} s";
                            }
                            else if (isError || isCancelled)
                            {
                                assistantMessage.IsError = true;

                                if (isPlanMode)
                                {
                                    if (isCancelled)
                                    {
                                        assistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                                    }
                                    else if (isError)
                                    {
                                        assistantMessage.Content = SanitizeFinalBubbleContent(result);
                                    }
                                }
                            }
                        });
                    }
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
                        convMessage = await ParseAndPublishPlanStepsAsync(userText, planContent, assistantMessage.ThinkingContent, _chatService.LastRunId);
                    }
                    else
                    {
                        convMessage = profile.Mapper.MapFromStreamingResult(userText, result, assistantMessage.ThinkingContent);
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

                            assistantMessage.ApplyFromConversationMessage(convMessage);

                            if (isPlanMode && convMessage.Payload == null)
                            {
                                assistantMessage.IsError = true;
                            }
                        });
                    }

                    _chatService.SaveMessages(Messages);

                    if (!isPlanMode && !string.IsNullOrWhiteSpace(imitateBookId))
                    {
                        try
                        {
                            var chapterTitle = $"仿写：{(string.IsNullOrWhiteSpace(imitateBookTitle) ? imitateBookId : imitateBookTitle)}";

                            var writer = new TM.Services.Framework.AI.SemanticKernel.Plugins.WriterPlugin();
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
                            GlobalToast.Error("仿写保存失败", ex.Message);
                        }
                    }
                }

                TM.App.Log($"[SKConversationViewModel] 消息完成: {result.Length} 字符");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 发送失败: {ex.Message}");

                Messages.Add(UIMessageItem.CreateErrorMessage($"发送失败: {ex.Message}"));
            }
            finally
            {
                IsGenerating = false;
                RefreshContextUsage();
            }
        }

        private async Task SendDeveloperLevelResponseAsync(string userText)
        {
            IsGenerating = true;
            var startTime = DateTime.Now;

            try
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                var assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = "Thinking...";
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

                var promptParts = ChatPromptBridge.BuildParts(ChatMode.Ask, userText);

                var result = await _chatService.SendStreamMessageAsync(
                    userText,
                    promptParts,
                    chunk =>
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            assistantMessage.AppendContent(SanitizeBubbleChunk(chunk));
                        });
                    },
                    thinkingChunk =>
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            assistantMessage.AppendThinking(thinkingChunk);
                        });
                    },
                    System.Threading.CancellationToken.None);

                var dispatcher4 = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher4 != null)
                {
                    await dispatcher4.InvokeAsync(() =>
                    {
                        var runId = _chatService.LastRunId;
                        userMessage.RunId = runId;
                        assistantMessage.RunId = runId;

                        if (!string.IsNullOrEmpty(assistantMessage.ThinkingContent) && !assistantMessage.AnalysisDurationSeconds.HasValue)
                        {
                            var seconds = Math.Max(0.1, (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds);
                            assistantMessage.AnalysisDurationSeconds = seconds;
                            assistantMessage.AnalysisSummary = $"Thought for {seconds:F1} s";
                        }

                        assistantMessage.FinishStreaming();

                        if (result.StartsWith("[错误]") || result.StartsWith("[已取消]"))
                        {
                            assistantMessage.IsError = true;
                        }
                    });
                }

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();
                TM.App.Log($"[SKConversationViewModel] 开发级问题响应完成: {result.Length} 字符");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 开发级问题响应失败: {ex.Message}");
                Messages.Add(UIMessageItem.CreateErrorMessage($"响应失败: {ex.Message}"));
            }
            finally
            {
                IsGenerating = false;
                RefreshContextUsage();
            }
        }

        private void CancelGeneration()
        {
            if (CurrentMode == ChatMode.Agent && _currentExecutionAssistantMessage != null && _todoExecutionService.IsRunning)
            {
                _wasExecutionCancelledByUser = true;

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _currentExecutionAssistantMessage!.IsThinking = false;
                    _currentExecutionAssistantMessage.AnalysisSummary = "已取消";
                    _currentExecutionAssistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                    _currentExecutionAssistantMessage.IsError = true;
                    _currentExecutionAssistantMessage.FinishStreaming();
                });
            }

            _chatService.CancelCurrentRequest();

            try { _prebuiltSimulationCts?.Cancel(); } catch { }

            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
            }

            IsGenerating = false;
            TM.App.Log("[SKConversationViewModel] 已取消生成");
        }

        private async Task<ConversationMessage> ParseAndPublishPlanStepsAsync(string userInput, string rawContent, string? thinking, Guid runId)
        {
            try
            {
                var normalizedInput = NormalizeChapterHint(userInput, rawContent);
                var hasContinue = ChapterDirectiveParser.HasContinueDirective(userInput);
                var hasRewrite = ChapterDirectiveParser.HasRewriteDirective(userInput);
                var inputForPlan = hasContinue || hasRewrite ? userInput : normalizedInput;

                _pendingContinueSourceId = null;
                _pendingRewriteTargetId = null;
                if (hasContinue)
                {
                    var rawSourceToken = ChapterDirectiveParser.ParseSourceChapterId(userInput);
                    if (!string.IsNullOrEmpty(rawSourceToken))
                    {
                        _pendingContinueSourceId = await ResolveChapterIdTokenAsync(rawSourceToken);
                        TM.App.Log($"[SKConversationViewModel] Plan缓存续写来源: {rawSourceToken} → {_pendingContinueSourceId}");
                    }
                }
                else if (hasRewrite)
                {
                    var rawTargetToken = ChapterDirectiveParser.ParseTargetChapterId(userInput);
                    if (!string.IsNullOrEmpty(rawTargetToken))
                    {
                        _pendingRewriteTargetId = await ResolveChapterIdTokenAsync(rawTargetToken);
                        TM.App.Log($"[SKConversationViewModel] Plan缓存重写目标: {rawTargetToken} → {_pendingRewriteTargetId}");
                    }
                }

                var profile = ModeProfileRegistry.GetProfile(ChatMode.Plan);
                var message = profile.Mapper.MapFromStreamingResult(inputForPlan, rawContent, thinking);

                var planPayload = message.Payload as PlanPayload;
                if (planPayload != null && planPayload.Steps.Count > 0)
                {
                    _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload, runId);

                    _comm.PublishShowPlanViewChanged(true);
                }
                else
                {
                    _cachedPlanSteps = null;
                }

                if (_cachedPlanSteps == null)
                {
                    TM.App.Log("[SKConversationViewModel] 未解析到计划步骤");
                }

                return message;
            }
            catch (Exception ex)
            {
                _cachedPlanSteps = null;
                TM.App.Log($"[SKConversationViewModel] 解析计划步骤失败: {ex.Message}");

                return new ConversationMessage
                {
                    RunId = runId,
                    Summary = "⚠️ 计划解析失败，请重新描述您的需求。"
                };
            }
        }

        #endregion

        #region 会话管理

        private void NewSession()
        {
            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
                TM.App.Log("[SKConversationViewModel] 新会话：检测到执行仍在运行，已自动取消");
            }
            _chatService.BeginDraftSession();
            Messages.Clear();
            SuggestedActions.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));
            _currentSessionId = null;
            HasDraftConversation = true;
            SessionTitle = "新会话";

            TM.App.Log("[SKConversationViewModel] 已进入新会话草稿态");
            RefreshContextUsage();
        }

        private void ClearHistory()
        {
            _chatService.DeleteCurrentSession();
            Messages.Clear();
            SuggestedActions.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));
            _currentSessionId = null;
            HasDraftConversation = false;
            SessionTitle = "新会话";
            TM.App.Log("[SKConversationViewModel] 当前会话已删除");
            RefreshContextUsage();
        }

        public void EnterDraftConversation()
        {
            if (!HasDraftConversation)
            {
                HasDraftConversation = true;
            }
        }

        public async System.Threading.Tasks.Task SwitchSessionAsync(string sessionId)
        {
            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
                TM.App.Log("[SKConversationViewModel] 切换会话：检测到执行仍在运行，已自动取消");
            }

            await _chatService.SwitchSessionAsync(sessionId);

            SuggestedActions.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));

            await LoadHistoryMessagesAsync();
            RefreshContextUsage();

            _currentSessionId = sessionId;
            HasDraftConversation = false;

            var sessions = _chatService.Sessions.GetAllSessions();
            var session = sessions.Find(s => s.Id == sessionId);
            if (session != null)
            {
                SessionTitle = session.Title;

                if (!string.IsNullOrEmpty(session.ContextChapterId))
                {
                    CurrentChapterTracker.SetCurrentChapter(session.ContextChapterId);
                    TM.App.Log($"[SKConversationViewModel] 切换会话：章节上下文恢复为 {session.ContextChapterId}");
                }
            }

            LoadSessionMode();
        }

        private void LoadSessionMode()
        {
            if (string.IsNullOrEmpty(_currentSessionId))
                return;

            var modeStr = _chatService.Sessions.GetSessionMode(_currentSessionId);
            ChatMode mode;
            if (int.TryParse(modeStr, out var modeInt) && Enum.IsDefined(typeof(ChatMode), modeInt))
                mode = (ChatMode)modeInt;
            else if (Enum.TryParse<ChatMode>(modeStr, out mode)) { }
            else
                return;

            _currentMode = mode;
            _chatService.CurrentMode = mode;
            OnPropertyChanged(nameof(CurrentMode));
            MonitorTitle = GetModeDisplayName(mode);
            TM.App.Log($"[SKConversationViewModel] 恢复会话模式: {mode}");
        }

        private void ShowHistory()
        {
            try
            {
                var dialog = new SessionHistoryDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);

                var result = dialog.ShowDialog();
                if (result == true && !string.IsNullOrEmpty(dialog.SelectedSessionId))
                {
                    var sessionId = dialog.SelectedSessionId!;
                    _ = SwitchSessionAsync(sessionId);
                }
                else
                {
                    OnPropertyChanged(nameof(Messages));
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 显示会话历史失败: {ex.Message}");
                StandardDialog.ShowError("历史会话打开失败", ex.Message);
            }
        }

        public void RenameCurrentSession(string newTitle)
        {
            newTitle = newTitle?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(newTitle))
            {
                newTitle = $"会话 {DateTime.Now:MM-dd HH:mm}";
            }

            if (string.IsNullOrEmpty(_currentSessionId))
            {
                SessionTitle = newTitle;
                return;
            }

            try
            {
                _chatService.Sessions.RenameSession(_currentSessionId, newTitle);
                SessionTitle = newTitle;
                TM.App.Log($"[SKConversationViewModel] 会话重命名: {_currentSessionId} -> {newTitle}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 会话重命名失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadHistoryMessagesAsync()
        {
            var records = await _chatService.LoadMessagesAsync().ConfigureAwait(true);

            Messages.Clear();

            foreach (var record in records)
            {
                if (record.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var item = UIMessageItem.FromSerializedRecord(record);
                Messages.Add(item);

                if (item.IsAssistant)
                {
                    var payload = item.RestorePayload();
                    if (payload is PlanPayload planPayload && planPayload.Steps.Count > 0)
                    {
                        _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload);
                    }
                }
            }

            _chatService.RebuildHistoryFromMessages(Messages);
            TM.App.Log($"[SKConversationViewModel] 加载 {Messages.Count} 条历史消息（三层架构）");
        }

        public System.Collections.Generic.List<SessionInfo> GetRecentSessions()
        {
            return _chatService.Sessions.GetAllSessions();
        }

        private static string FormatTokenCount(int count)
        {
            if (count >= 1_000_000) return $"{count / 1_000_000.0:F1}M";
            if (count >= 1_000) return $"{count / 1_000.0:F1}k";
            return count.ToString();
        }

        public void RefreshContextUsage()
        {
            OnPropertyChanged(nameof(ContextUsagePercent));
            OnPropertyChanged(nameof(ContextUsagePercentText));
            OnPropertyChanged(nameof(ContextUsageDetailLine1));
            OnPropertyChanged(nameof(ContextUsageStatusText));
            OnPropertyChanged(nameof(ContextUsageColor));
            OnPropertyChanged(nameof(IsSessionCompressed));
        }

        #endregion

        #region 运行事件监控

        private void OnExecutionEvent(ExecutionEvent evt)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                TodoPanelViewModel.OnExecutionEvent(evt);

                if (evt.RunId == _chatService.LastRunId)
                {
                    if (evt.EventType == ExecutionEventType.RunStarted)
                    {
                        RunEvents.Clear();
                    }
                    RunEvents.Add(evt);
                    UpdateMonitorState(evt);
                }

                if (evt.RunId == _todoExecutionService.CurrentRunId)
                {
                    UpdateMonitorState(evt);
                }
            });
        }

        private void UpdateMonitorState(ExecutionEvent lastEvent)
        {
            var isExecutionEngineEvent = lastEvent.RunId != Guid.Empty
                && lastEvent.RunId == _todoExecutionService.CurrentRunId;

            switch (lastEvent.EventType)
            {
                case ExecutionEventType.RunStarted:
                    MonitorTitle = GetModeDisplayName(lastEvent.Mode);
                    MonitorSubTitle = isExecutionEngineEvent ? "执行中" : "运行";
                    _lastRunStatus = "Running";
                    break;
                case ExecutionEventType.RunCompleted:
                    MonitorSubTitle = isExecutionEngineEvent ? "结束" : "结束";
                    _lastRunStatus = "Completed";
                    break;
                case ExecutionEventType.RunFailed:
                    MonitorSubTitle = isExecutionEngineEvent ? "失败" : "失败";
                    _lastRunStatus = "Failed";
                    break;
            }

            OnPropertyChanged(nameof(CurrentModeActiveColor));
        }

        private void OnHighlightExecutionRequested(Guid runId, Guid? eventId)
        {
            if (runId != _chatService.LastRunId)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ExecutionEvent? targetEvent = null;
                if (eventId.HasValue && eventId.Value != Guid.Empty)
                {
                    targetEvent = RunEvents.FirstOrDefault(e => e.Id == eventId.Value);
                }

                targetEvent ??= RunEvents.FirstOrDefault(e => e.RunId == runId);

                if (targetEvent != null)
                {
                    SelectedRunEvent = targetEvent;
                }

                if (TodoPanelViewModel.Steps.Any())
                {
                    TodoStepViewModel? step = null;

                    if (eventId.HasValue && eventId.Value != Guid.Empty)
                    {
                        step = TodoPanelViewModel.Steps.FirstOrDefault(s => s.EventId == eventId.Value);
                    }

                    step ??= TodoPanelViewModel.Steps.FirstOrDefault(s => s.RunId == runId);

                    if (step != null)
                    {
                        TodoPanelViewModel.SelectedStep = step;
                    }
                }
            });
        }

        private void HighlightMessagesForRun(Guid runId)
        {
            if (runId == Guid.Empty || Messages.Count == 0)
            {
                return;
            }

            var target = Messages.FirstOrDefault(m => m.RunId == runId && m.IsAssistant)
                         ?? Messages.FirstOrDefault(m => m.RunId == runId && m.IsUser);

            if (target != null)
            {
                SelectedMessage = target;
            }
        }

        #endregion

        #region 消息操作

        public void CopyMessage(UIMessageItem message)
        {
            try
            {
                System.Windows.Clipboard.SetText(message.Content);
                GlobalToast.Success("已复制", "消息内容已复制到剪贴板");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversation] 复制消息失败: {ex.Message}");
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

            try
            {
                var assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                Messages.Add(assistantMessage);

                var promptParts = ChatPromptBridge.BuildParts(CurrentMode, userText);

                var result = await _chatService.SendStreamMessageAsync(
                    userText,
                    promptParts,
                    chunk =>
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            assistantMessage.AppendContent(SanitizeBubbleChunk(chunk));
                        });
                    },
                    thinkingChunk =>
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            assistantMessage.AppendThinking(thinkingChunk);
                        });
                    },
                    System.Threading.CancellationToken.None);

                var dispatcher5 = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher5 != null)
                {
                    await dispatcher5.InvokeAsync(() =>
                    {
                        var runId = _chatService.LastRunId;
                        assistantMessage.RunId = runId;

                        if (!string.IsNullOrEmpty(assistantMessage.ThinkingContent) && !assistantMessage.AnalysisDurationSeconds.HasValue)
                        {
                            var seconds = Math.Max(0.1, (DateTime.Now - assistantMessage.ThinkingStartTime).TotalSeconds);
                            assistantMessage.AnalysisDurationSeconds = seconds;
                            assistantMessage.AnalysisSummary = $"Thought for {seconds:F1} s";
                        }

                        assistantMessage.FinishStreaming();

                        if (result.StartsWith("[错误]") || result.StartsWith("[已取消]"))
                        {
                            assistantMessage.IsError = true;
                        }
                        else
                        {
                            var profile = CurrentProfile;
                            var convMessage = profile.Mapper.MapFromStreamingResult(userText, result, assistantMessage.ThinkingContent);
                            assistantMessage.ApplyFromConversationMessage(convMessage);
                        }
                    });
                }

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();
                RefreshContextUsage();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 重新生成失败: {ex.Message}");
                GlobalToast.Error("重新生成失败", ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
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
                GlobalToast.Error("模型切换失败", ex.Message);
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
                StandardDialog.ShowError("翻译失败", ex.Message);
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
                if (!starred.Any())
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
                StandardDialog.ShowError("星标消息", $"显示失败: {ex.Message}");
            }
        }

        #endregion

        #region 模型管理

        public void RefreshModelConfigurations()
        {
            ModelConfigurations.Clear();
            var configs = _aiService.GetAllConfigurations();
            foreach (var config in configs)
            {
                if (config.IsEnabled)
                {
                    ModelConfigurations.Add(config);
                }
            }
            OnPropertyChanged(nameof(ActiveConfiguration));
        }

        public void DeleteModel(UserConfiguration model)
        {
            if (model == null) return;

            try
            {
                model.IsEnabled = false;
                _aiService.UpdateConfiguration(model);

                try
                {
                    var modelService = _modelService;
                    var allConfigs = modelService.GetAllData();

                    var providerName = _aiService.GetAllProviders()
                        .FirstOrDefault(p => string.Equals(p.Id, model.ProviderId, StringComparison.OrdinalIgnoreCase))
                        ?.Name;

                    var matchingConfig = allConfigs.FirstOrDefault(c =>
                        string.Equals(c.ModelName, model.ModelId, StringComparison.OrdinalIgnoreCase) &&
                        (
                            string.Equals(c.CategoryId, model.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(providerName) && string.Equals(c.Category, providerName, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(providerName) && string.Equals(c.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
                        ));

                    if (matchingConfig != null)
                    {
                        matchingConfig.IsEnabled = false;
                        modelService.UpdateConfiguration(matchingConfig);
                    }
                }
                catch (Exception ex) { DebugLogOnce("DeleteModel_SyncDisable", ex);}

                var availableConfigs = _aiService.GetAllConfigurations();
                var newActive = availableConfigs.FirstOrDefault(c => c.IsEnabled && c.Id != model.Id);
                if (newActive != null)
                {
                    _aiService.SetActiveConfiguration(newActive.Id);
                }
                else
                {
                    RefreshModelConfigurations();
                    OnPropertyChanged(nameof(ActiveConfiguration));
                }

                GlobalToast.Success("已禁用", $"模型 {model.Name} 已禁用");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("操作失败", ex.Message);
            }
        }

        public void SetActiveConfiguration(UserConfiguration config)
        {
            _aiService.SetActiveConfiguration(config.Id);
            OnPropertyChanged(nameof(ActiveConfiguration));
        }

        #endregion

        #region 章节上下文解析

        private async Task<string?> ResolveChapterIdFromTextAsync(string userText)
        {
            ContentGuide? guide = null;
            try
            {
                guide = await _guideContextService.GetContentGuideAsync();
            }
            catch (Exception ex)
            {
                DebugLogOnce("LoadContentGuide", ex);
            }

            var referenceParser = ServiceLocator.Get<ReferenceParser>();
            var references = referenceParser.ParseReferences(userText);
            var chapterRef = references.FirstOrDefault(r => r.Type == "chapter" || r.Type == "rewrite");

            if (!string.IsNullOrEmpty(chapterRef?.Name))
            {
                if (ChapterExistsInGuide(guide, chapterRef.Name))
                {
                    TM.App.Log($"[SKConversationViewModel] 从 @{chapterRef.Type} 引用解析到章节: {chapterRef.Name}");
                    return chapterRef.Name;
                }
                TM.App.Log($"[SKConversationViewModel] @{chapterRef.Type} 引用的章节不存在: {chapterRef.Name}");
            }

            var chapterIdFromNL = await TryParseChapterIdFromNaturalLanguageAsync(userText, guide);
            if (!string.IsNullOrEmpty(chapterIdFromNL))
            {
                if (ChapterExistsInGuide(guide, chapterIdFromNL))
                {
                    TM.App.Log($"[SKConversationViewModel] 从自然语言解析到章节: {chapterIdFromNL}");
                    return chapterIdFromNL;
                }
                else
                {
                    TM.App.Log($"[SKConversationViewModel] 自然语言解析到的章节不存在: {chapterIdFromNL}");
                }
            }

            return null;
        }

        private async Task<string?> ResolveChapterIdTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var trimmed = token.Trim();

            ContentGuide? guide = null;
            try
            {
                guide = await _guideContextService.GetContentGuideAsync();
            }
            catch (Exception ex)
            {
                DebugLogOnce("LoadContentGuide_Token", ex);
            }

            var parsed = ChapterParserHelper.ParseChapterId(trimmed);
            if (parsed.HasValue)
            {
                var chapterId = ChapterParserHelper.BuildChapterId(parsed.Value.volumeNumber, parsed.Value.chapterNumber);
                return ChapterExistsInGuide(guide, chapterId) ? chapterId : null;
            }

            var nlChapterId = await TryParseChapterIdFromNaturalLanguageAsync(trimmed, guide);
            if (!string.IsNullOrEmpty(nlChapterId))
            {
                return ChapterExistsInGuide(guide, nlChapterId) ? nlChapterId : null;
            }

            return null;
        }

        private async Task<string?> TryParseChapterIdFromNaturalLanguageAsync(string text, ContentGuide? guide = null)
        {
            var (volume, chapter) = ChapterParserHelper.ParseFromNaturalLanguage(text);

            if (volume.HasValue && chapter.HasValue)
            {
                return ChapterParserHelper.BuildChapterId(volume.Value, chapter.Value);
            }

            if (chapter.HasValue)
            {
                return await FindUniqueChapterAcrossVolumesAsync(chapter.Value.ToString(), guide);
            }

            return null;
        }

        private async Task<string?> FindUniqueChapterAcrossVolumesAsync(string chapterNumber, ContentGuide? guide = null)
        {
            var matchedChapterIds = new List<string>();

            var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
            await volumeService.InitializeAsync();
            var volumeNumbers = volumeService.GetAllVolumeDesigns()
                .Select(v => v.VolumeNumber)
                .Where(v => v > 0)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            foreach (var vol in volumeNumbers)
            {
                var candidateId = ChapterParserHelper.BuildChapterId(vol, int.Parse(chapterNumber));
                if (ChapterExistsInGuide(guide, candidateId))
                {
                    matchedChapterIds.Add(candidateId);
                }
            }

            if (matchedChapterIds.Count == 1)
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章唯一匹配: {matchedChapterIds[0]}");
                return matchedChapterIds[0];
            }
            else if (matchedChapterIds.Count > 1)
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章存在多卷匹配: {string.Join(", ", matchedChapterIds)}，需要用户指定卷号");
                return null;
            }
            else
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章未找到任何匹配");
                return null;
            }
        }

        private static string CleanChapterReferences(string userText)
        {
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                userText,
                @"@(?:续写|重写|chapter|rewrite|continue)[:：\s]*\S+",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? userText : cleaned;
        }

        private static bool ChapterExistsInGuide(ContentGuide? guide, string chapterId)
        {
            if (guide?.Chapters == null) return false;
            if (guide.Chapters.TryGetValue(chapterId, out var entry))
            {
                return !string.IsNullOrEmpty(entry.Title) || entry.ChapterNumber > 0;
            }
            return false;
        }

        #endregion

        #region 快捷面板逻辑

        private void RefreshSuggestedActions()
        {
            SuggestedActions.Clear();

            if (string.IsNullOrWhiteSpace(_lastSentUserText))
            {
                OnPropertyChanged(nameof(HasSuggestedActions));
                return;
            }

            if (!IsExplicitChapterGenerationRequest(_lastSentUserText)
                && !ChapterDirectiveParser.HasContinueDirective(_lastSentUserText)
                && !ChapterDirectiveParser.HasRewriteDirective(_lastSentUserText))
            {
                OnPropertyChanged(nameof(HasSuggestedActions));
                return;
            }

            if (_lastExecutedMode == ChatMode.Plan)
            {
                var range = ChapterParserHelper.ParseChapterRange(_lastSentUserText);
                int nextStart = -1;
                if (range.HasValue && range.Value.end > 0)
                {
                    nextStart = range.Value.end + 1;
                }
                else
                {
                    var ranges = ChapterParserHelper.ParseChapterRanges(_lastSentUserText);
                    if (ranges != null && ranges.Count > 0)
                        nextStart = ranges.Max(r => r.end) + 1;
                }

                if (nextStart < 1)
                {
                    var singleCh = ChapterParserHelper.ExtractChapterNumber(_lastSentUserText);
                    if (singleCh > 0) nextStart = singleCh + 1;
                }

                if (nextStart < 1)
                {
                    var lastId = CurrentChapterTracker.CurrentChapterId;
                    if (!string.IsNullOrEmpty(lastId))
                    {
                        var (_, lastCh) = ChapterParserHelper.ParseChapterIdOrDefault(lastId);
                        if (lastCh > 0) nextStart = lastCh + 1;
                    }
                }

                if (nextStart < 1) nextStart = 1;

                var volNum = ChapterParserHelper.ExtractVolumeNumber(_lastSentUserText);
                _planContinueDisplayPrefix = volNum > 0 ? $"生成第{volNum}卷第" : "生成第";
                _planContinueStartNum = nextStart;
                _hasPlanContinueAction = true;
                OnPropertyChanged(nameof(HasPlanContinueAction));
                OnPropertyChanged(nameof(PlanContinueDisplayPrefix));
                OnPropertyChanged(nameof(PlanContinueStartNum));
            }
            else if (_lastExecutedMode == ChatMode.Agent)
            {
                var lastChapterId = CurrentChapterTracker.CurrentChapterId;
                if (string.IsNullOrEmpty(lastChapterId))
                {
                    OnPropertyChanged(nameof(HasSuggestedActions));
                    return;
                }

                var (lastVol, lastChNum) = ChapterParserHelper.ParseChapterIdOrDefault(lastChapterId);
                var nextChNum = lastChNum > 0 ? lastChNum + 1 : -1;

                _agentContinueLabel = nextChNum > 0
                    ? (lastVol > 0 ? $"▶ 继续生成第{lastVol}卷第{nextChNum}章" : $"▶ 继续生成第{nextChNum}章")
                    : string.Empty;
                _agentContinueText = nextChNum > 0
                    ? (lastVol > 0 ? $"生成第{lastVol}卷第{nextChNum}章" : $"生成第{nextChNum}章")
                    : string.Empty;

                _agentRewriteLabel = $"↺ 重写 {lastChapterId}";
                _agentRewriteText = $"@重写:{lastChapterId}";

                _hasAgentActions = true;
                OnPropertyChanged(nameof(HasAgentActions));
                OnPropertyChanged(nameof(HasAgentContinue));
                OnPropertyChanged(nameof(AgentContinueLabel));
                OnPropertyChanged(nameof(AgentRewriteLabel));
            }

            OnPropertyChanged(nameof(HasSuggestedActions));
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

    public class QuickActionItem
    {
        public string Label { get; set; } = string.Empty;
        public ICommand Command { get; set; } = null!;
    }
}
