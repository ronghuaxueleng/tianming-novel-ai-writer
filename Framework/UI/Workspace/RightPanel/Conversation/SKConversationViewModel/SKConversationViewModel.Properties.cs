using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        private double _cachedContextPercent;
        private int _cachedContextTokens;
        private int _cachedContextWindow;

        private UserConfiguration? _cachedActiveConfig;
        private IReadOnlyList<WritingEndpointItem>? _cachedEndpointConfigs;

        private System.Windows.Threading.DispatcherTimer? _contextRefreshTimer;

        private static SolidColorBrush? _brushDanger;
        private static SolidColorBrush? _brushWarningHigh;
        private static SolidColorBrush? _brushWarningMedium;
        private static SolidColorBrush? _brushSuccess;
        private static SolidColorBrush? _brushTextDisabled;

        internal static void InvalidateThemeBrushCache()
        {
            _brushDanger = null;
            _brushWarningHigh = null;
            _brushWarningMedium = null;
            _brushSuccess = null;
            _brushTextDisabled = null;
        }

        private static SolidColorBrush GetThemeBrush(string key, ref SolidColorBrush? cache, Color fallback)
        {
            if (cache != null) return cache;
            var found = Application.Current?.TryFindResource(key) as SolidColorBrush;
            if (found != null)
            {
                cache = found;
            }
            else
            {
                var fb = new SolidColorBrush(fallback); fb.Freeze();
                cache = fb;
            }
            return cache;
        }

        #region 属性

        public RangeObservableCollection<UIMessageItem> Messages { get; } = new();

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
                ScheduleContextRefresh();
                _ = UpdateChapterHintAsync();
            }
        }

        private void ScheduleContextRefresh()
        {
            if (_contextRefreshTimer == null)
            {
                _contextRefreshTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _contextRefreshTimer.Tick += (_, _) => { _contextRefreshTimer.Stop(); RefreshContextUsage(); };
            }
            _contextRefreshTimer.Stop();
            _contextRefreshTimer.Start();
        }

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
            _hintDebounceCts?.Dispose();
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

                    if (_prebuiltChapterId != chapterId)
                    {
                        _prebuiltContextCts?.Cancel();
                        _prebuiltChapterId = null;
                        _prebuiltContextPrompt = null;
                        var prebuildCts = new CancellationTokenSource();
                        _prebuiltContextCts = prebuildCts;
                        var capturedId = chapterId;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var bridge = ServiceLocator.Get<TM.Framework.UI.Workspace.Services.ChapterGenerationBridge>();
                                var prompt = await bridge.GetGenerationPromptAsync(capturedId);
                                if (!prebuildCts.Token.IsCancellationRequested && !string.IsNullOrWhiteSpace(prompt))
                                {
                                    _prebuiltChapterId = capturedId;
                                    _prebuiltContextPrompt = prompt;
                                }
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex) { TM.App.Log($"[SKConversationVM] 预构建上下文失败: {ex.Message}"); }
                        }, prebuildCts.Token);
                    }
                }
                else
                {
                    ResolvedChapterHint = string.Empty;
                    _prebuiltChapterId = null;
                    _prebuiltContextPrompt = null;
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
                ChatMode.Edit => "Edit",
                ChatMode.Agent => "Agent",
                ChatMode.Plan => "Plan",
                ChatMode.Channel => "Channel",
                ChatMode.Business => "Business",
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
                    TM.Framework.Common.Services.MemoryOptimizationService.SetGeneratingState(true);
                    try { ServiceLocator.Get<TM.Framework.Common.Services.MemoryOptimizationService>().NotifyUserActivity(); } catch { }
                    _hasPlanContinueAction = false;
                    _hasAgentActions = false;
                    _hasEditPreviewActions = false;
                    _pendingPreviewId = null;
                    _pendingFilePreviewIds.Clear();
                    PlanContinueEndText = string.Empty;
                    OnPropertyChanged(nameof(HasPlanContinueAction));
                    OnPropertyChanged(nameof(HasAgentActions));
                    OnPropertyChanged(nameof(HasEditPreviewActions));
                    OnPropertyChanged(nameof(HasSuggestedActions));
                }
                else if (wasGenerating && !value)
                {
                    ShowTodoOverlay = false;

                    TM.Framework.Common.Services.MemoryOptimizationService.SetGeneratingState(false);
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

                        ShowEditPreviewPanelIfPending();
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

        public bool HasSuggestedActions => _hasPlanContinueAction || _hasAgentActions || _hasBlueprintSessionActions || _hasEditPreviewActions;

        public bool HasPlanContinueAction => _hasPlanContinueAction;
        public bool HasAgentActions => _hasAgentActions;
        public bool HasAgentContinue => !string.IsNullOrEmpty(_agentContinueLabel);
        public bool HasEditPreviewActions => _hasEditPreviewActions;
        public bool HasBlueprintSessionActions => _hasBlueprintSessionActions;
        public string ImitationProgressLabel => _blueprintProgressLabel;
        public string BlueprintNextLabel => _blueprintNextLabel;
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

        public SolidColorBrush CurrentModeActiveColor
        {
            get => _lastRunStatus switch
            {
                "Running" => GetThemeBrush("SuccessColor", ref _brushSuccess, Color.FromRgb(0x22, 0xC5, 0x5E)),
                "Failed" => GetThemeBrush("DangerColor", ref _brushDanger, Color.FromRgb(0xDC, 0x26, 0x26)),
                _ => GetThemeBrush("TextDisabled", ref _brushTextDisabled, Color.FromRgb(0x9C, 0xA3, 0xAF))
            };
        }

        public RangeObservableCollection<UserConfiguration> ModelConfigurations { get; } = new();

        public UserConfiguration? ActiveConfiguration
        {
            get => _cachedActiveConfig;
            set
            {
                if (_isRefreshingConfigs) return;

                if (value != null)
                {
                    _isSavingQuickConfig = true;
                    try
                    {
                        _aiService.SetActiveConfiguration(value.Id);
                    }
                    finally { _isSavingQuickConfig = false; }
                    RefreshCachedActiveConfig();
                    _cachedEndpointConfigs = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveConfigurationId));
                    OnPropertyChanged(nameof(WritingEndpointConfigs));
                    OnPropertyChanged(nameof(ShowThinkingToggle));
                    OnPropertyChanged(nameof(ShowEffortDropdown));
                    OnPropertyChanged(nameof(QuickThinkingEnabled));
                    OnPropertyChanged(nameof(AvailableThinkingEfforts));
                    OnPropertyChanged(nameof(QuickReasoningEffort));
                    OnPropertyChanged(nameof(ShowLongContextSwitch));
                    OnPropertyChanged(nameof(EnableLongContext));
                    RefreshContextUsage();
                }
            }
        }

        public string? ActiveConfigurationId
        {
            get => _cachedActiveConfig?.Id;
            set
            {
                if (_isRefreshingConfigs) return;
                if (string.IsNullOrWhiteSpace(value)) return;
                if (string.Equals(_cachedActiveConfig?.Id, value, StringComparison.Ordinal)) return;
                var cfg = ModelConfigurations.FirstOrDefault(c => c.Id == value);
                if (cfg != null) ActiveConfiguration = cfg;
            }
        }

        private void RefreshCachedActiveConfig()
        {
            var active = _aiService.GetActiveConfiguration();
            _cachedActiveConfig = active == null ? null : ModelConfigurations.FirstOrDefault(c => c.Id == active.Id);
        }

        private static bool IsReasoningEffortModel(string? modelId, string? providerId)
        {
            var r = CapabilityServices.DefaultResolver.Resolve(
                providerId ?? string.Empty,
                modelId ?? string.Empty);
            return r.Reasoning.SupportsReasoningEffort;
        }

        private static bool IsOpenRouterReasoningModel(string? modelId, string? providerId)
        {
            var r = CapabilityServices.DefaultResolver.Resolve(
                providerId ?? string.Empty,
                modelId ?? string.Empty,
                endpoint: "https://openrouter.ai/api/v1");
            return r.Reasoning.SupportsReasoningEffort;
        }

        private static bool IsThinkingModel(string? modelId, string? providerId)
        {
            var r = CapabilityServices.DefaultResolver.Resolve(
                providerId ?? string.Empty,
                modelId ?? string.Empty);
            return r.Thinking.SupportsThinking;
        }

        private static bool IsNeitherParamModel(string? modelId, string? providerId)
            => TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsNeitherParamModel(modelId, providerId);

        private TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models.UserConfigurationData? GetModelDataForConfig(UserConfiguration? c)
        {
            if (c == null) return null;
            try
            {
                var allData = _modelService.GetAllData();
                return allData.FirstOrDefault(d =>
                    string.Equals(d.CategoryId, c.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.ModelName, c.ModelId, StringComparison.OrdinalIgnoreCase))
                    ?? allData.FirstOrDefault(d =>
                        string.Equals(d.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { TM.App.Log($"[SKConversationViewModel] GetModelDataForConfig 异常: {ex.Message}"); return null; }
        }

        public bool ShowLongContextSwitch
        {
            get
            {
                var c = QuickParamEffectiveConfig;
                if (c == null) return false;
                if (!c.SupportsLongContext) return false;
                if (TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.IsUnsupportedParam(
                        c.ProviderId, c.CustomEndpoint, c.ModelId, "long_context"))
                    return false;
                return true;
            }
        }

        public bool? EnableLongContext
        {
            get => QuickParamEffectiveConfig?.EnableLongContext;
            set
            {
                var c = QuickParamEffectiveConfig;
                if (c == null) return;
                if (c.EnableLongContext == value) return;
                SetQuickEnableLongContext(value);
                OnPropertyChanged();
                RefreshContextUsage();
                var human = value == true
                    ? "已启用，上限提升至 1M"
                    : "已恢复默认（按模型基线窗口）";
                GlobalToast.Info("1M 上下文", human);
            }
        }

        public bool ShowThinkingToggle
        {
            get
            {
                var c = QuickParamEffectiveConfig;
                if (c == null) return false;

                if (TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.IsAnyThinkingParamUnsupported(
                        c.ProviderId, c.CustomEndpoint, c.ModelId))
                {
                    return false;
                }

                var hint = BuildHintFor(c);
                var resolved = CapabilityServices.DefaultResolver.Resolve(c.ProviderId, c.ModelId, c.CustomEndpoint, hint);
                return resolved.HasThinkingToggle;
            }
        }

        public bool ShowEffortDropdown
        {
            get
            {
                var c = QuickParamEffectiveConfig;
                if (c == null) return false;

                if (TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.IsAnyThinkingParamUnsupported(
                        c.ProviderId, c.CustomEndpoint, c.ModelId))
                {
                    return false;
                }

                var hint = BuildHintFor(c);
                var resolved = CapabilityServices.DefaultResolver.Resolve(c.ProviderId, c.ModelId, c.CustomEndpoint, hint);
                return resolved.HasThinkingToggle && resolved.HasEffortLevels;
            }
        }

        public IReadOnlyList<EffortOption> AvailableThinkingEfforts
        {
            get
            {
                var c = QuickParamEffectiveConfig;
                if (c == null) return Array.Empty<EffortOption>();

                var hint = BuildHintFor(c);
                var resolved = CapabilityServices.DefaultResolver.Resolve(c.ProviderId, c.ModelId, c.CustomEndpoint, hint);
                if (resolved.RequestParameterMode == RequestParameterMode.None) return Array.Empty<EffortOption>();

                return EffortOption.BuildList(resolved.Reasoning.SupportedEffortLevels);
            }
        }

        public IReadOnlyList<ThinkingStateOption> AvailableThinkingStates { get; } = ThinkingStateOption.All;

        public bool? QuickThinkingEnabled
        {
            get => QuickParamEffectiveConfig?.ThinkingEnabled;
            set
            {
                SetQuickThinkingEnabled(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowEffortDropdown));
            }
        }

        public string QuickReasoningEffort
        {
            get => EffortConstants.Normalize(QuickParamEffectiveConfig?.ReasoningEffort);
            set
            {
                var normalized = EffortConstants.Normalize(value);
                SetQuickReasoningEffort(normalized);
                OnPropertyChanged();
            }
        }

        private static UserCapabilityHint BuildHintFor(UserConfiguration c)
        {
            return new UserCapabilityHint
            {
                ReasoningEffort = c.ReasoningEffort,
                ThinkingEnabled = c.ThinkingEnabled,
                CapabilitiesDetected = c.CapabilitiesDetected,
                SupportsReasoningEffort = c.CapabilitiesDetected ? c.SupportsReasoningEffort : (bool?)null,
                SupportsThinking = c.CapabilitiesDetected ? c.SupportsThinking : (bool?)null,
                SupportedEffortLevels = c.SupportedEffortLevels?.Count > 0
                    ? c.SupportedEffortLevels
                    : null,
            };
        }

        public WritingEndpointItem? QuickParamSelectedEndpoint
        {
            get => _quickParamSelectedEndpoint;
            set
            {
                _quickParamSelectedEndpoint = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowThinkingToggle));
                OnPropertyChanged(nameof(ShowEffortDropdown));
                OnPropertyChanged(nameof(QuickThinkingEnabled));
                OnPropertyChanged(nameof(AvailableThinkingEfforts));
                OnPropertyChanged(nameof(QuickReasoningEffort));
                OnPropertyChanged(nameof(ShowLongContextSwitch));
                OnPropertyChanged(nameof(EnableLongContext));
            }
        }

        public UserConfiguration? QuickParamEffectiveConfig =>
            _quickParamSelectedEndpoint?.Config ?? ActiveConfiguration;

        public IReadOnlyList<WritingEndpointItem> WritingEndpointConfigs
        {
            get => _cachedEndpointConfigs ??= BuildEndpointConfigs();
        }

        private IReadOnlyList<WritingEndpointItem> BuildEndpointConfigs()
        {
            var result = new List<WritingEndpointItem>();
            var chat = ActiveConfiguration;
            var backup = WritingBackupChatConfiguration;
            var polish = WritingPolishConfiguration;
            if (chat != null)
                result.Add(new WritingEndpointItem("主", chat));
            if (backup != null && result.All(x => x.Config.Id != backup.Id))
                result.Add(new WritingEndpointItem("备用", backup));
            if (polish != null && result.All(x => x.Config.Id != polish.Id))
                result.Add(new WritingEndpointItem("润色", polish));
            return result;
        }

        public string ContextUsageDetailLine1
        {
            get
            {
                if (_cachedContextWindow <= 0)
                {
                    return ActiveConfiguration == null ? "未选择模型" : "上下文未知";
                }
                var windowText = _chatService.IsContextWindowReal()
                    ? FormatTokenCount(_cachedContextWindow)
                    : $"≈{FormatTokenCount(_cachedContextWindow)}";
                return $"{FormatTokenCount(_cachedContextTokens)} / {windowText}";
            }
        }

        public string ContextUsageStatusText
        {
            get
            {
                if (_cachedContextPercent >= 95) return "⚠ 即将自动压缩对话";
                if (_cachedContextPercent >= 80) return "⚠ 接近上下文上限";
                if (_cachedContextPercent >= 60) return "注意：上下文使用较多";
                return string.Empty;
            }
        }

        public SolidColorBrush ContextUsageColor
        {
            get
            {
                var percent = _cachedContextPercent;
                if (percent >= 95) return GetThemeBrush("DangerColor", ref _brushDanger, Color.FromRgb(0xDC, 0x26, 0x26));
                if (percent >= 80) return GetThemeBrush("WarningColor", ref _brushWarningHigh, Color.FromRgb(0xF5, 0x9E, 0x0B));
                if (percent >= 60) return GetThemeBrush("WarningColor", ref _brushWarningMedium, Color.FromRgb(0xEA, 0xB3, 0x08));
                return GetThemeBrush("SuccessColor", ref _brushSuccess, Color.FromRgb(0x22, 0xC5, 0x5E));
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
    }

}
