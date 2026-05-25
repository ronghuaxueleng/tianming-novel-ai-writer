using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.ViewModels;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ModelManagementViewModel
{
    private int _suppressTreeRefreshCount = 0;

    private bool _suppressAiConfigurationsChanged;

    private EventHandler? _modelServiceConfigurationsChangedHandler;
    private EventHandler? _aiServiceConfigurationsChangedHandler;
    private EventHandler? _writingRouterStatusChangedHandler;
    private WritingApiRouter? _writingApiRouter;
    private bool _disposed;

    private string _formName = string.Empty;
    private string _formIcon = "Icon.Robot";
    private string _formStatus = "已禁用";
    private string _formCategory = string.Empty;
    private string _formDescription = string.Empty;

    public string FormName
    {
        get => _formName;
        set { _formName = value; OnPropertyChanged(); }
    }

    public string FormIcon
    {
        get => _formIcon;
        set { _formIcon = value; OnPropertyChanged(); }
    }

    public string FormStatus
    {
        get => _formStatus;
        set { _formStatus = value; OnPropertyChanged(); }
    }

    public string FormCategory
    {
        get => _formCategory;
        set
        {
            if (_formCategory != value)
            {
                _formCategory = value;
                OnPropertyChanged();
                _ = OnCategoryValueChangedAsync(_formCategory);
                OnPropertyChanged(nameof(IsApiConfigEditable));
                OnPropertyChanged(nameof(IsBuiltInCategory));
                OnPropertyChanged(nameof(IsApiActionEnabled));
            }
        }
    }

    public string FormDescription
    {
        get => _formDescription;
        set { _formDescription = value; OnPropertyChanged(); }
    }

    private string _formModelName = string.Empty;
    private string _formApiEndpoint = string.Empty;
    private string _formApiKey = string.Empty;
    private bool _formIsActive;

    public string FormModelName
    {
        get => _formModelName;
        set { _formModelName = value; OnPropertyChanged(); }
    }

    public string FormApiEndpoint
    {
        get => _formApiEndpoint;
        set
        {
            if (_formApiEndpoint != value)
            {
                _formApiEndpoint = value;
                OnPropertyChanged();
                CheckEndpointConfigurationChanged();
            }
        }
    }

    public string FormApiKey
    {
        get => _formApiKey;
        set
        {
            if (_formApiKey != value)
            {
                _formApiKey = value;
                OnPropertyChanged();
                CheckEndpointConfigurationChanged();
            }
        }
    }

    public string ApiKeyCountLabel
    {
        get
        {
            var keys = _currentEditingCategory?.ApiKeys;
            if (keys == null) return "点击配置密钥";
            int total = 0, enabled = 0;
            foreach (var k in keys)
            {
                if (!string.IsNullOrWhiteSpace(k.Key))
                {
                    total++;
                    if (k.IsEnabled) enabled++;
                }
            }
            if (total == 0) return "点击配置密钥";
            return $"已配置 {total} 个密钥（{enabled} 个启用）";
        }
    }

    public ICommand OpenApiKeyManagerCommand { get; }

    private void OpenApiKeyManager()
    {
        if (_currentEditingCategory == null) return;

        _currentEditingCategory.ApiKeys ??= new System.Collections.Generic.List<ApiKeyEntry>();

        var providerName = _currentEditingCategory.Name ?? "未知供应商";
        var dialog = new ApiKeyManagerDialog(_currentEditingCategory.ApiKeys, providerName, _currentEditingCategory.Id)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.ResultKeys != null)
        {
            _currentEditingCategory.ApiKeys = dialog.ResultKeys;
            Service.UpdateCategory(_currentEditingCategory);
            Service.SyncKeyPoolsToRotationService();
            FormApiKey = _currentEditingCategory.ApiKey ?? string.Empty;
            OnPropertyChanged(nameof(ApiKeyCountLabel));
            OnPropertyChanged(nameof(ActiveKeyDisplay));

            var count = dialog.ResultKeys.Count;
            var enabled = dialog.ResultKeys.Count(k => k.IsEnabled);
            GlobalToast.Success("密钥已更新", $"共 {count} 个密钥，{enabled} 个启用");
        }
    }

    public bool FormIsActive
    {
        get => _formIsActive;
        set { _formIsActive = value; OnPropertyChanged(); }
    }

    private string _formProviderName = string.Empty;
    private string _formModelVersion = string.Empty;
    private string _formContextLength = string.Empty;
    private string _formTrainingDataCutoff = string.Empty;
    private string _formInputPrice = string.Empty;
    private string _formOutputPrice = string.Empty;
    private string _formSupportedFeatures = string.Empty;

    public string FormProviderName
    {
        get => _formProviderName;
        set { _formProviderName = value; OnPropertyChanged(); }
    }

    public string FormModelVersion
    {
        get => _formModelVersion;
        set { _formModelVersion = value; OnPropertyChanged(); }
    }

    public string FormContextLength
    {
        get => _formContextLength;
        set { _formContextLength = value; OnPropertyChanged(); }
    }

    public string FormTrainingDataCutoff
    {
        get => _formTrainingDataCutoff;
        set { _formTrainingDataCutoff = value; OnPropertyChanged(); }
    }

    public string FormInputPrice
    {
        get => _formInputPrice;
        set { _formInputPrice = value; OnPropertyChanged(); }
    }

    public string FormOutputPrice
    {
        get => _formOutputPrice;
        set { _formOutputPrice = value; OnPropertyChanged(); }
    }

    public string FormSupportedFeatures
    {
        get => _formSupportedFeatures;
        set { _formSupportedFeatures = value; OnPropertyChanged(); }
    }

    private double _formTemperature = 0.7;
    private int _formMaxTokens = 0;
    private double _formTopP = 1.0;
    private double _formFrequencyPenalty = 0.1;
    private double _formPresencePenalty = 0.0;
    private string _formBatchTier = "64K";
    private int _formRateLimitRPM = 0;
    private int _formRateLimitTPM = 0;
    private int _formMaxConcurrency = 5;
    private string _formSeed = string.Empty;
    private string _formStopSequences = string.Empty;

    public double FormTemperature
    {
        get => _formTemperature;
        set { _formTemperature = value; OnPropertyChanged(); }
    }

    public int FormMaxTokens
    {
        get => _formMaxTokens;
        set { _formMaxTokens = value; OnPropertyChanged(); }
    }

    public double FormTopP
    {
        get => _formTopP;
        set { _formTopP = value; OnPropertyChanged(); }
    }

    public double FormFrequencyPenalty
    {
        get => _formFrequencyPenalty;
        set { _formFrequencyPenalty = value; OnPropertyChanged(); }
    }

    public double FormPresencePenalty
    {
        get => _formPresencePenalty;
        set { _formPresencePenalty = value; OnPropertyChanged(); }
    }

    public string FormBatchTier
    {
        get => _formBatchTier;
        set { _formBatchTier = value; OnPropertyChanged(); }
    }

    public record BatchTierOption(string Value, string Display);
    public List<BatchTierOption> BatchTierOptions { get; } = new()
    {
        new("32K",  "MaxOutput 32K · 保守稳定"),
        new("64K",  "MaxOutput 64K · 均衡推荐（默认）"),
        new("128K", "MaxOutput 128K · 高吞吐")
    };

    public int FormRateLimitRPM
    {
        get => _formRateLimitRPM;
        set { _formRateLimitRPM = value; OnPropertyChanged(); }
    }

    public int FormRateLimitTPM
    {
        get => _formRateLimitTPM;
        set { _formRateLimitTPM = value; OnPropertyChanged(); }
    }

    public int FormMaxConcurrency
    {
        get => _formMaxConcurrency;
        set { _formMaxConcurrency = value; OnPropertyChanged(); }
    }

    public string FormSeed
    {
        get => _formSeed;
        set { _formSeed = value; OnPropertyChanged(); }
    }

    public string FormStopSequences
    {
        get => _formStopSequences;
        set { _formStopSequences = value; OnPropertyChanged(); }
    }

    private int _formRetryCount = 3;
    private int _formTimeoutSeconds = 30;

    public int FormRetryCount
    {
        get => _formRetryCount;
        set { _formRetryCount = value; OnPropertyChanged(); }
    }

    public int FormTimeoutSeconds
    {
        get => _formTimeoutSeconds;
        set { _formTimeoutSeconds = value; OnPropertyChanged(); }
    }

    private string _formReasoningEffort = string.Empty;
    private bool? _formThinkingEnabled;
    private bool _formSupportsReasoningEffort;
    private bool _formSupportsThinking;
    private bool _formSupportsLongContext;

    public string FormReasoningEffort
    {
        get => _formReasoningEffort;
        set { _formReasoningEffort = value; OnPropertyChanged(); }
    }

    public bool? FormThinkingEnabled
    {
        get => _formThinkingEnabled;
        set
        {
            _formThinkingEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormShowEffortDropdown));
            OnPropertyChanged(nameof(FormCanSelectEffort));
        }
    }

    public IReadOnlyList<ThinkingStateOption> FormAvailableThinkingStates { get; } = ThinkingStateOption.All;

    public bool FormSupportsReasoningEffort
    {
        get => _formSupportsReasoningEffort;
        set
        {
            _formSupportsReasoningEffort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormReasoningEffortEnabled));
            OnPropertyChanged(nameof(FormAvailableThinkingEfforts));
            OnPropertyChanged(nameof(FormShowThinkingToggle));
            OnPropertyChanged(nameof(FormShowEffortDropdown));
            OnPropertyChanged(nameof(FormCanSelectEffort));
        }
    }

    public bool FormSupportsThinking
    {
        get => _formSupportsThinking;
        set
        {
            _formSupportsThinking = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormThinkingParamsEnabled));
            OnPropertyChanged(nameof(FormAvailableThinkingEfforts));
            OnPropertyChanged(nameof(FormShowThinkingToggle));
            OnPropertyChanged(nameof(FormShowEffortDropdown));
        }
    }

    public bool FormShowThinkingToggle
    {
        get
        {
            var data = _currentEditingData;
            if (data == null) return false;
            var resolved = ResolveFormCapability(data);
            return resolved.HasThinkingToggle || FormSupportsThinking || FormSupportsReasoningEffort;
        }
    }

    public bool FormShowEffortDropdown
    {
        get
        {
            var data = _currentEditingData;
            if (data == null) return false;
            var resolved = ResolveFormCapability(data);
            return (resolved.HasThinkingToggle || FormSupportsThinking || FormSupportsReasoningEffort)
                   && resolved.HasEffortLevels;
        }
    }

    public bool FormCanSelectEffort => FormReasoningEffortEnabled && FormThinkingEnabled == true;

    public IReadOnlyList<EffortOption> FormAvailableThinkingEfforts
    {
        get
        {
            var data = _currentEditingData;
            if (data == null) return Array.Empty<EffortOption>();
            var resolved = ResolveFormCapability(data);
            if (resolved.RequestParameterMode == RequestParameterMode.None) return Array.Empty<EffortOption>();
            return EffortOption.BuildList(resolved.Reasoning.SupportedEffortLevels);
        }
    }

    private ResolvedCapability ResolveFormCapability(UserConfigurationData data)
    {
        var hint = new UserCapabilityHint
        {
            ReasoningEffort = FormReasoningEffort,
            ThinkingEnabled = FormThinkingEnabled,
            CapabilitiesDetected = data.CapabilitiesDetected,
            SupportsReasoningEffort = data.CapabilitiesDetected ? FormSupportsReasoningEffort : (bool?)null,
            SupportsThinking = data.CapabilitiesDetected ? FormSupportsThinking : (bool?)null,
            SupportedEffortLevels = data.SupportedEffortLevels?.Count > 0
                ? data.SupportedEffortLevels
                : null,
        };
        return CapabilityServices.DefaultResolver.Resolve(
            providerId: data.Category,
            modelId: data.ModelName,
            endpoint: data.ApiEndpoint,
            userHint: hint);
    }

    public bool FormSupportsLongContext
    {
        get => _formSupportsLongContext;
        set
        {
            _formSupportsLongContext = value;
            OnPropertyChanged();
        }
    }

    public RangeObservableCollection<ParameterProfileDto> ParameterProfiles { get; } = new();

    private string _selectedProfileId = string.Empty;
    public string SelectedProfileId
    {
        get => _selectedProfileId;
        set
        {
            if (_selectedProfileId == value) return;
            _selectedProfileId = value;
            OnPropertyChanged();

            SelectedProfile = ParameterProfiles.FirstOrDefault(p => p.Id == _selectedProfileId);

            if (_currentProvider != null && !string.IsNullOrWhiteSpace(_selectedProfileId))
            {
                _ = Task.Run(async () =>
                {
                    try { await Service.SetDefaultProfileIdForProviderAsync(_currentProvider, _selectedProfileId).ConfigureAwait(false); }
                    catch (Exception ex) { LogScoped($"[ModelManagement] 设置默认参数模板失败: {ex.Message}"); }
                });
            }
        }
    }

    private ParameterProfileDto? _selectedProfile;
    public ParameterProfileDto? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile == value) return;
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProfile));
        }
    }

    private AIProviderCategory? _currentProvider;
    public string CurrentProviderName => _currentProvider?.Name ?? "未选择供应商";
    public bool HasCurrentProvider => _currentProvider != null;
    public bool HasSelectedProfile => _selectedProfile != null;

    private bool _isAutoFetchMode = true;
    public bool IsAutoFetchMode
    {
        get => _isAutoFetchMode;
        set
        {
            if (_isAutoFetchMode == value) return;
            _isAutoFetchMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualInputMode));
            OnPropertyChanged(nameof(AutoFetchVisibility));
            OnPropertyChanged(nameof(ManualInputVisibility));
        }
    }

    public bool IsManualInputMode
    {
        get => !_isAutoFetchMode;
        set => IsAutoFetchMode = !value;
    }

    public Visibility AutoFetchVisibility => IsAutoFetchMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManualInputVisibility => IsManualInputMode ? Visibility.Visible : Visibility.Collapsed;

    public RangeObservableCollection<ModelInfo> AvailableModels { get; } = new();

    private ModelInfo? _selectedModel;
    public ModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (_selectedModel == value) return;
            _selectedModel = value;
            OnPropertyChanged();
            if (value != null)
            {
                FormModelName = value.Id;
            }
        }
    }

    private string _manualModelName = string.Empty;
    public string ManualModelName
    {
        get => _manualModelName;
        set
        {
            if (_manualModelName == value) return;
            _manualModelName = value;
            OnPropertyChanged();
            if (IsManualInputMode)
            {
                FormModelName = value;
            }
        }
    }

    public bool IsModelComboEnabled => AvailableModels.Count > 0;

    private bool _isApiKeyVisible;
    public bool IsApiKeyVisible
    {
        get => _isApiKeyVisible;
        set
        {
            if (_isApiKeyVisible == value) return;
            _isApiKeyVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApiKeyVisibilityIcon));
            OnPropertyChanged(nameof(ActiveKeyDisplay));
        }
    }

    public string ApiKeyVisibilityIcon => _isApiKeyVisible ? "Icon.Eye" : "Icon.Lock";

    public string ActiveKeyDisplay
    {
        get
        {
            var key = _currentEditingCategory?.ApiKey;
            if (string.IsNullOrWhiteSpace(key)) return "点击配置密钥";
            if (_isApiKeyVisible) return key;
            return new string('*', Math.Min(key.Length, 20));
        }
    }

    public ICommand FetchModelsCommand { get; }
    public ICommand FetchManualModelCommand { get; }
    public ICommand TestApiConnectionCommand { get; }
    public ICommand ToggleApiKeyVisibilityCommand { get; }
    public ICommand RetryWithDropdownCommand { get; }
    public ICommand RetryWithManualCommand { get; }

    private ICommand? _enableSelectedCommand;
    public ICommand EnableSelectedCommand => _enableSelectedCommand ??= new RelayCommand(param =>
    {
        try
        {
            if (param is not TreeNodeItem node)
            {
                return;
            }

            if (node.Tag is not UserConfigurationData data)
            {
                return;
            }

            var providerCategory = Service.GetAllCategories()
                .FirstOrDefault(c => c.Level == 2 && string.Equals(c.Name, data.Category, StringComparison.OrdinalIgnoreCase));

            if (providerCategory == null ||
                string.IsNullOrWhiteSpace(providerCategory.ModelsEndpoint) ||
                string.IsNullOrWhiteSpace(providerCategory.ChatEndpoint))
            {
                StandardDialog.ShowWarning("该供应商端点尚未验证（Models/Chat）。请先在供应商分类中点击「测试连接」完成验证。", "禁止启用");
                return;
            }

            _currentEditingData = data;
            _currentEditingCategory = null;
            LoadDataToForm(data);
            OnDataItemLoaded();
            OnPropertyChanged(nameof(IsGlobalParametersAvailable));
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsApiActionEnabled));
            OnPropertyChanged(nameof(IsTab2Enabled));
            OnPropertyChanged(nameof(FormReasoningEffortEnabled));
            OnPropertyChanged(nameof(FormThinkingParamsEnabled));

            FormStatus = "已启用";

            if (SaveCommand.CanExecute(null))
            {
                SaveCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 启用失败: {ex.Message}");
            GlobalToast.Error("启用失败", $"启用失败：{ex.Message}");
        }
    });

    private readonly IAILibraryService _aiLibraryService;
    private readonly IAIConfigurationService _aiConfigurationService;
    private readonly ProxyService _proxyService;
    private readonly EndpointTestService _endpointTestService;

    private List<Services.ModelInfo>? _lastFetchedModels;

    private bool _isTestingConnection;
    private string _testingProgressText = string.Empty;
    private CancellationTokenSource? _testConnectionCts;

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            _isTestingConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IAIGeneratingState.IsAIGenerating));
            OnPropertyChanged(nameof(IAIGeneratingState.BatchProgressText));
        }
    }

    bool IAIGeneratingState.IsAIGenerating => IsTestingConnection || base.IsAIGenerating;
    string IAIGeneratingState.BatchProgressText => IsTestingConnection ? _testingProgressText : BatchProgressText;

    ICommand IAIGeneratingState.CancelBatchGenerationCommand => new RelayCommand(() =>
    {
        if (IsTestingConnection)
        {
            try { _testConnectionCts?.Cancel(); }
            catch (Exception ex) { LogScoped($"[ModelManagement] 取消测试连接失败: {ex.Message}"); }
            LogScoped("[ModelManagement] 测试连接已请求取消");
        }
        else
        {
            CancelBatchGeneration();
        }
    });

    public bool IsEndpointVerified => _currentEditingCategory?.Level == 2
        && !string.IsNullOrWhiteSpace(_currentEditingCategory?.ModelsEndpoint)
        && !string.IsNullOrWhiteSpace(_currentEditingCategory?.ChatEndpoint);

    public string EndpointVerificationIcon => IsEndpointVerified ? "Icon.CheckCircle" : "Icon.Warning";

    public string EndpointVerificationTooltip => IsEndpointVerified
        ? $"已验证\nModels: {_currentEditingCategory?.ModelsEndpoint}\nChat: {_currentEditingCategory?.ChatEndpoint}\n验证时间: {_currentEditingCategory?.EndpointVerifiedAt:yyyy-MM-dd HH:mm:ss}"
        : "未验证，请点击「测试连接」";

    public bool ShowEndpointVerificationStatus => _currentEditingCategory?.Level == 2;

    public bool IsBuiltInCategory => _currentEditingCategory?.IsBuiltIn == true
        || (_currentEditingData != null && _currentProvider?.IsBuiltIn == true);

    public bool IsTab1Enabled => IsCreateMode || _currentEditingCategory?.Level == 2;

    public bool IsApiConfigEditable
    {
        get
        {
            if (_currentEditingCategory?.Level == 2)
            {
                if (_currentEditingCategory.IsBuiltIn)
                    return false;
                return true;
            }

            if (IsCreateMode)
            {
                if (string.IsNullOrWhiteSpace(FormCategory))
                    return false;

                var parent = Service.GetAllCategories().FirstOrDefault(c => c.Name == FormCategory);
                var level = parent != null ? parent.Level + 1 : 1;
                return level == 2;
            }

            return false;
        }
    }

    public bool IsApiActionEnabled => _currentEditingCategory?.Level == 2 || IsCreateMode;

    public bool IsTab2Enabled => _currentEditingData != null;

    public bool FormReasoningEffortEnabled => _currentEditingData != null && FormSupportsReasoningEffort;

    public bool FormThinkingParamsEnabled => _currentEditingData != null && FormSupportsThinking;

    private static bool ReasoningCapableByName(string? modelId, string? providerId)
        => TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsOpenRouterReasoningModel(modelId, providerId);

    private static bool IsReasoningEffortOnlyModel(string m)
        => TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsReasoningEffortModel(m, null);

    private void RefreshEndpointVerificationStatus()
    {
        OnPropertyChanged(nameof(IsEndpointVerified));
        OnPropertyChanged(nameof(EndpointVerificationIcon));
        OnPropertyChanged(nameof(EndpointVerificationTooltip));
        OnPropertyChanged(nameof(ShowEndpointVerificationStatus));
        OnPropertyChanged(nameof(IsTab1Enabled));
        OnPropertyChanged(nameof(IsApiConfigEditable));
        OnPropertyChanged(nameof(IsBuiltInCategory));
        OnPropertyChanged(nameof(IsApiActionEnabled));
        OnPropertyChanged(nameof(IsTab2Enabled));
        OnPropertyChanged(nameof(ShowChatRetryPanel));
        OnPropertyChanged(nameof(IsChatRetryDropdownEnabled));
        OnPropertyChanged(nameof(IsAutoRetryDropdownEnabled));
        OnPropertyChanged(nameof(IsManualRetryInputEnabled));
        OnPropertyChanged(nameof(ChatRetryModels));
    }

    public bool ShowChatRetryPanel => _chatTestFailed && _lastFetchedModels?.Count > 0;

    public bool IsChatRetryDropdownEnabled => _chatTestFailed && ChatRetryModels.Count > 0;

    public bool IsAutoRetryDropdownEnabled => IsAutoRetryMode && IsChatRetryDropdownEnabled;

    public bool IsManualRetryInputEnabled => IsManualRetryMode && _chatTestFailed;

    private bool _chatTestFailed;

    public RangeObservableCollection<Services.ModelInfo> ChatRetryModels { get; } = new();

    private Services.ModelInfo? _selectedChatRetryModel;
    public Services.ModelInfo? SelectedChatRetryModel
    {
        get => _selectedChatRetryModel;
        set
        {
            if (_selectedChatRetryModel == value) return;
            _selectedChatRetryModel = value;
            OnPropertyChanged();
        }
    }

    private bool _isAutoRetryMode = true;
    public bool IsAutoRetryMode
    {
        get => _isAutoRetryMode;
        set
        {
            if (_isAutoRetryMode == value) return;
            _isAutoRetryMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualRetryMode));
            OnPropertyChanged(nameof(IsAutoRetryDropdownEnabled));
            OnPropertyChanged(nameof(IsManualRetryInputEnabled));
        }
    }

    public bool IsManualRetryMode
    {
        get => !_isAutoRetryMode;
        set => IsAutoRetryMode = !value;
    }

    private string _retryManualModelName = string.Empty;
    public string RetryManualModelName
    {
        get => _retryManualModelName;
        set
        {
            if (_retryManualModelName == value) return;
            _retryManualModelName = value;
            OnPropertyChanged();
        }
    }

    public ICommand RetryChatTestCommand { get; }

    public ModelManagementViewModel(IAILibraryService aiLibraryService, IAIConfigurationService aiConfigurationService, ProxyService proxyService)
    {
        _aiLibraryService = aiLibraryService;
        _aiConfigurationService = aiConfigurationService;
        _proxyService = proxyService;
        _endpointTestService = new EndpointTestService(proxyService);
        _writingSettingsService = ServiceLocator.Get<WritingSettingsService>();
        _disableCoordinator = ServiceLocator.Get<ModelDisableCoordinator>();

        _modelServiceConfigurationsChangedHandler = (_, _) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (_suppressTreeRefreshCount > 0)
                {
                    _suppressTreeRefreshCount--;
                    UpdateBulkToggleState();
                    return;
                }
                ScheduleImmediateRefreshTreeData();
                ForceRebuildCategorySelectionTree();
                UpdateBulkToggleState();
                RefreshEnabledConfigs();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };
        Service.ConfigurationsChanged += _modelServiceConfigurationsChangedHandler;

        _aiServiceConfigurationsChangedHandler = (_, _) =>
        {
            if (_suppressAiConfigurationsChanged) return;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                RefreshEnabledConfigs();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };
        _aiConfigurationService.ConfigurationsChanged += _aiServiceConfigurationsChangedHandler;

        ShowAIGenerateButton = false;

        FetchModelsCommand = new AsyncRelayCommand(FetchModelsAsync);
        FetchManualModelCommand = new AsyncRelayCommand(FetchManualModelAsync);
        TestApiConnectionCommand = new AsyncRelayCommand(TestApiConnectionAsync);

        ToggleApiKeyVisibilityCommand = new RelayCommand(() =>
        {
            if (IsBuiltInCategory) return;
            IsApiKeyVisible = !IsApiKeyVisible;
        });

        RetryChatTestCommand = new AsyncRelayCommand(RetryChatTestAsync);
        RetryWithDropdownCommand = new AsyncRelayCommand(RetryWithDropdownAsync);
        RetryWithManualCommand = new AsyncRelayCommand(RetryWithManualAsync);
        OpenApiKeyManagerCommand = new RelayCommand(() => OpenApiKeyManager());

        LoadParameterProfilesForUI();

        InitDefaultProviderForGlobalParameters();

        TypeOptions.Remove("数据");
        FormType = "分类";

        CleanupCategorySelectionTree();

        ProviderLogoHelper.PreloadInBackground(
            Service.GetAllCategories().Select(c =>
                !string.IsNullOrEmpty(c.LogoPath) ? c.LogoPath
                : c.Level == 1 ? "app.png"
                : ProviderLogoHelper.GetLogoFileName(c.Name)));

        DisableWritingConfigModelCommand = new RelayCommand<UserConfiguration>(model =>
        {
            if (model == null) return;
            var confirm = StandardDialog.ShowConfirm(
                $"确定要禁用模型 \"{model.Name}\" 吗？\n禁用后可在模型管理中重新启用。", "禁用模型");
            if (confirm) DisableWritingConfigModel(model);
        });
        DisableAllWritingConfigModelsCommand = new RelayCommand(DisableAllWritingConfigModels);
        ManualResetFallbackCommand = new RelayCommand(() =>
        {
            if (!StandardDialog.ShowConfirm("确定要立即恢复到主API吗？\n将停止当前备用API的使用，切换回主对话API。", "立即恢复主API"))
                return;
            try
            {
                var router = ServiceLocator.Get<TM.Services.Framework.AI.WritingConfig.WritingApiRouter>();
                router.ManualReset();
                GlobalToast.Success("已恢复主API", "已成功切换回主对话API");
            }
            catch (Exception ex)
            {
                LogScoped($"[ModelManagement] 手动恢复主API失败: {ex.Message}");
                GlobalToast.Error("恢复失败", "切换回主API失败，请重试");
            }
        });
        RefreshEnabledConfigs();
        LoadWritingSettings();

        try
        {
            _writingApiRouter = ServiceLocator.Get<WritingApiRouter>();
            _writingRouterStatusChangedHandler = (_, _) =>
            {
                OnPropertyChanged(nameof(IsUsingBackup));
                OnPropertyChanged(nameof(FallbackStatusText));
            };
            _writingApiRouter.StatusChanged += _writingRouterStatusChangedHandler;
        }
        catch { }

        _writingSettingsChangedHandler = (_, _) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                LoadWritingSettings();
                OnPropertyChanged(nameof(IsUsingBackup));
                OnPropertyChanged(nameof(FallbackStatusText));
            });
        };
        _writingSettingsService.SettingsChanged += _writingSettingsChangedHandler;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_modelServiceConfigurationsChangedHandler != null)
        {
            Service.ConfigurationsChanged -= _modelServiceConfigurationsChangedHandler;
            _modelServiceConfigurationsChangedHandler = null;
        }

        if (_aiServiceConfigurationsChangedHandler != null)
        {
            _aiConfigurationService.ConfigurationsChanged -= _aiServiceConfigurationsChangedHandler;
            _aiServiceConfigurationsChangedHandler = null;
        }

        if (_writingApiRouter != null && _writingRouterStatusChangedHandler != null)
        {
            _writingApiRouter.StatusChanged -= _writingRouterStatusChangedHandler;
            _writingRouterStatusChangedHandler = null;
            _writingApiRouter = null;
        }

        if (_writingSettingsChangedHandler != null)
        {
            _writingSettingsService.SettingsChanged -= _writingSettingsChangedHandler;
            _writingSettingsChangedHandler = null;
        }

        base.Dispose();
    }

    private void CleanupCategorySelectionTree()
    {
        var nodesToRemove = CategorySelectionTree.Where(n => n.Name != "主页导航").ToList();
        foreach (var node in nodesToRemove)
        {
            CategorySelectionTree.Remove(node);
        }
    }

    public bool IsGlobalParametersAvailable =>
        _currentEditingCategory != null && _currentEditingCategory.Level == 2;

}
