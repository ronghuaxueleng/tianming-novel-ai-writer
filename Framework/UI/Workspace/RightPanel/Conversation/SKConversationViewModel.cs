using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.Appearance.ThemeManagement;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.WritingConfig;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class SKConversationViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SKChatService _chatService;
        private readonly PanelCommunicationService _comm;
        private readonly AIService _aiService;
        private readonly TodoExecutionService _todoExecutionService;
        private readonly IGuideContextService _guideContextService;
        private readonly NovelCrawlerService _novelCrawlerService;
        private readonly TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService _modelService;
        private readonly ModelDisableCoordinator _disableCoordinator;
        private readonly WritingSettingsService _writingSettings;
        private readonly WritingApiRouter _writingApiRouter;

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

        private string? _prebuiltChapterId;
        private string? _prebuiltContextPrompt;
        private CancellationTokenSource? _prebuiltContextCts;
        private bool _isGenerating;
        private ChatMode _currentMode = ChatMode.Edit;
        private ChatMode _lastExecutedMode = ChatMode.Edit;
        private string? _pendingModeHint;
        private UIMessageItem? _selectedMessage;
        private string _sessionTitle = "新会话";
        private string _monitorTitle = "Edit";
        private string _monitorSubTitle = "空闲";
        private bool _isMultiSelectMode;

        private string? _pendingContinueSourceId;
        private string? _pendingRewriteTargetId;

        private bool _hasEditPreviewActions;
        private string? _pendingPreviewId;

        private readonly List<string> _pendingFilePreviewIds = new();

        private string? _blueprintSessionId;
        private string _blueprintSessionName = string.Empty;
        private int _blueprintTotalChapters;
        private int _blueprintSavedCount;
        private int _blueprintNextChapterIndex;
        private bool _hasBlueprintSessionActions;
        private string _blueprintProgressLabel = string.Empty;
        private string _blueprintNextLabel = string.Empty;
        private CancellationTokenSource? _blueprintCts;
        private bool _disposed;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();
        private static readonly Regex ChapterIdFromDetailRegex = new(@"章节ID:\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex ChapterReferencePrefixRegex = new(@"@(?:续写|重写|chapter|rewrite|continue)[:：\s]*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private EventHandler? _cfgChangedHandler;
        private EventHandler? _writingSettingsChangedHandler;
        private EventHandler<ThemeChangedEventArgs>? _themeChangedHandler;
        private bool _isSavingQuickConfig;
        private bool _isRefreshingConfigs;
        private int _refreshModelConfigsQueued;
        private WritingEndpointItem? _quickParamSelectedEndpoint;

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

        public SKConversationViewModel(
            SKChatService chatService,
            PanelCommunicationService comm,
            AIService aiService,
            TodoExecutionService todoExecutionService,
            IGuideContextService guideContextService,
            NovelCrawlerService novelCrawlerService,
            TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService modelService,
            ModelDisableCoordinator disableCoordinator)
        {
            _chatService = chatService;
            _comm = comm;
            _aiService = aiService;
            _todoExecutionService = todoExecutionService;
            _guideContextService = guideContextService;
            _novelCrawlerService = novelCrawlerService;
            _modelService = modelService;
            _disableCoordinator = disableCoordinator;
            _writingSettings = ServiceLocator.Get<WritingSettingsService>();
            _writingApiRouter = ServiceLocator.Get<WritingApiRouter>();

            _chatService.CurrentMode = _currentMode;

            SendCommand = new AsyncRelayCommand(async () => await SendMessageAsync(), () => CanSend);
            CancelCommand = new RelayCommand(() => CancelGeneration(), () => IsGenerating);
            NewSessionCommand = new RelayCommand(() => NewSession());
            ClearHistoryCommand = new RelayCommand(() => ClearHistory());
            ClearSessionCommand = new RelayCommand(() => ClearHistory());
            ShowHistoryCommand = new RelayCommand(ShowHistory);

            CopyMessageCommand = new RelayCommand(
                async () => { if (SelectedMessage != null) await CopyMessageAsync(SelectedMessage); },
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
                    OnPropertyChanged(nameof(HasSuggestedActions));
                    QuickFillInputRequested?.Invoke(this, EventArgs.Empty);
                }
            });

            QuickSendCommand = new AsyncRelayCommand(async param =>
            {
                if (param is string text && !string.IsNullOrWhiteSpace(text))
                {
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

            EditConfirmCommand = new AsyncRelayCommand(async _ => await ExecuteEditConfirmAsync());
            EditCancelCommand = new AsyncRelayCommand(async _ => await ExecuteEditCancelAsync());

            _ = RestoreLastSessionAsync();

            RefreshModelConfigurations();

            var cfgService = (TM.Services.Framework.AI.Interfaces.AI.IAIConfigurationService)_aiService;
            _cfgChangedHandler = (s, e) =>
            {
                if (_isSavingQuickConfig) return;
                if (Interlocked.Exchange(ref _refreshModelConfigsQueued, 1) == 1) return;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(() =>
                    {
                        Interlocked.Exchange(ref _refreshModelConfigsQueued, 0);
                        RefreshModelConfigurations();
                    }));
            };
            cfgService.ConfigurationsChanged += _cfgChangedHandler;

            var writingSettingsService = ServiceLocator.Get<WritingSettingsService>();
            _writingSettingsChangedHandler = (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(() =>
                    {
                        OnPropertyChanged(nameof(WritingBackupChatConfigId));
                        OnPropertyChanged(nameof(WritingBackupChatConfiguration));
                        OnPropertyChanged(nameof(WritingPolishConfigId));
                        OnPropertyChanged(nameof(WritingPolishConfiguration));
                        OnPropertyChanged(nameof(IsWritingFallbackActive));
                        _cachedEndpointConfigs = null;
                        OnPropertyChanged(nameof(WritingEndpointConfigs));
                    }));
            };
            writingSettingsService.SettingsChanged += _writingSettingsChangedHandler;

            try
            {
                var themeManager = ServiceLocator.Get<ThemeManager>();
                _themeChangedHandler = (_, _) =>
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(() =>
                        {
                            RefreshContextUsage();
                            OnPropertyChanged(nameof(CurrentModeActiveColor));
                        }));
                };
                themeManager.ThemeChanged += _themeChangedHandler;
            }
            catch { }

            HasDraftConversation = false;

            ExecutionEventHub.Published += OnExecutionEvent;

            _comm.HighlightExecutionRequested += OnHighlightExecutionRequested;

            _comm.SendMessageRequested += OnSendMessageRequested;

            _comm.StartPlanExecutionRequested += OnStartPlanExecutionRequested;

            RefreshContextUsage();

            TM.App.Log("[SKConversationViewModel] 初始化完成");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _hintDebounceCts?.Cancel();
            _hintDebounceCts?.Dispose();
            _hintDebounceCts = null;
            _prebuiltContextCts?.Cancel();
            _prebuiltContextCts?.Dispose();
            _prebuiltContextCts = null;

            if (_contextRefreshTimer != null)
            {
                _contextRefreshTimer.Stop();
                _contextRefreshTimer = null;
            }

            ExecutionEventHub.Published -= OnExecutionEvent;
            TodoPanelViewModel.Dispose();
            _comm.HighlightExecutionRequested -= OnHighlightExecutionRequested;
            _comm.SendMessageRequested -= OnSendMessageRequested;
            _comm.StartPlanExecutionRequested -= OnStartPlanExecutionRequested;

            if (_cfgChangedHandler != null)
            {
                var cfgService = (TM.Services.Framework.AI.Interfaces.AI.IAIConfigurationService)_aiService;
                cfgService.ConfigurationsChanged -= _cfgChangedHandler;
                _cfgChangedHandler = null;
            }
            if (_writingSettingsChangedHandler != null)
            {
                try
                {
                    ServiceLocator.Get<WritingSettingsService>().SettingsChanged -= _writingSettingsChangedHandler;
                }
                catch { }
                _writingSettingsChangedHandler = null;
            }

            if (_themeChangedHandler != null)
            {
                try
                {
                    ServiceLocator.Get<ThemeManager>().ThemeChanged -= _themeChangedHandler;
                }
                catch { }
                _themeChangedHandler = null;
            }
            GC.SuppressFinalize(this);
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

    public class WritingEndpointItem
    {
        public string Label { get; }
        public UserConfiguration Config { get; }
        public string DisplayName => Config.DisplayNameWithPrefix;

        public WritingEndpointItem(string label, UserConfiguration config)
        {
            Label = label;
            Config = config;
        }
    }
}
