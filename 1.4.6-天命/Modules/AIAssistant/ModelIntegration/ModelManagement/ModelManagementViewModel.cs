using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class ModelManagementViewModel : DataManagementViewModelBase<UserConfigurationData, AIProviderCategory, ModelService>
{
    private int _suppressTreeRefreshCount = 0;

    private string _formName = string.Empty;
    private string _formIcon = "🤖";
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
                OnCategoryValueChanged(_formCategory);
                OnPropertyChanged(nameof(IsApiConfigEditable));
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
            var count = _currentEditingCategory?.ApiKeys?.Count(k => !string.IsNullOrWhiteSpace(k.Key)) ?? 0;
            if (count == 0) return "点击配置密钥";
            var enabled = _currentEditingCategory!.ApiKeys!.Count(k => k.IsEnabled && !string.IsNullOrWhiteSpace(k.Key));
            return $"已配置 {count} 个密钥（{enabled} 个启用）";
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
    private int _formMaxTokens = 4096;
    private double _formTopP = 1.0;
    private double _formFrequencyPenalty = 0.0;
    private double _formPresencePenalty = 0.0;
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
    private bool _formEnableStreaming = true;

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

    public bool FormEnableStreaming
    {
        get => _formEnableStreaming;
        set { _formEnableStreaming = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ParameterProfileDto> ParameterProfiles { get; } = new();

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
                Service.SetDefaultProfileIdForProvider(_currentProvider, _selectedProfileId);
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

    public ObservableCollection<ModelInfo> AvailableModels { get; } = new();

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

    public string ApiKeyVisibilityIcon => _isApiKeyVisible ? "👁" : "🔒";

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
            OnPropertyChanged(nameof(IsTab2Enabled));

            FormStatus = "已启用";

            if (SaveCommand.CanExecute(null))
            {
                SaveCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 启用失败: {ex.Message}");
            GlobalToast.Error("启用失败", ex.Message);
        }
    });

    private readonly IAILibraryService _aiLibraryService;
    private readonly IAIConfigurationService _aiConfigurationService;
    private readonly ProxyService _proxyService;
    private readonly EndpointTestService _endpointTestService;

    private List<Services.ModelInfo>? _lastFetchedModels;

    public bool IsEndpointVerified => _currentEditingCategory?.Level == 2 
        && !string.IsNullOrWhiteSpace(_currentEditingCategory?.ModelsEndpoint)
        && !string.IsNullOrWhiteSpace(_currentEditingCategory?.ChatEndpoint);

    public string EndpointVerificationIcon => IsEndpointVerified ? "✅" : "⚠️";

    public string EndpointVerificationTooltip => IsEndpointVerified 
        ? $"已验证\nModels: {_currentEditingCategory?.ModelsEndpoint}\nChat: {_currentEditingCategory?.ChatEndpoint}\n验证时间: {_currentEditingCategory?.EndpointVerifiedAt:yyyy-MM-dd HH:mm:ss}"
        : "未验证，请点击「测试连接」";

    public bool ShowEndpointVerificationStatus => _currentEditingCategory?.Level == 2;

    public bool IsTab1Enabled => IsCreateMode || _currentEditingCategory?.Level == 2;

    public bool IsApiConfigEditable
    {
        get
        {
            if (_currentEditingCategory?.Level == 2)
                return true;

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

    public bool IsTab2Enabled => _currentEditingData != null;

    private void RefreshEndpointVerificationStatus()
    {
        OnPropertyChanged(nameof(IsEndpointVerified));
        OnPropertyChanged(nameof(EndpointVerificationIcon));
        OnPropertyChanged(nameof(EndpointVerificationTooltip));
        OnPropertyChanged(nameof(ShowEndpointVerificationStatus));
        OnPropertyChanged(nameof(IsTab1Enabled));
        OnPropertyChanged(nameof(IsApiConfigEditable));
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

    public ObservableCollection<Services.ModelInfo> ChatRetryModels { get; } = new();

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

        Service.ConfigurationsChanged += (_, _) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_suppressTreeRefreshCount > 0)
                {
                    _suppressTreeRefreshCount--;
                    UpdateBulkToggleState();
                    return;
                }
                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();
            }));
        };

        ShowAIGenerateButton = false;

        FetchModelsCommand = new AsyncRelayCommand(FetchModelsAsync);
        FetchManualModelCommand = new AsyncRelayCommand(FetchManualModelAsync);
        TestApiConnectionCommand = new AsyncRelayCommand(TestApiConnectionAsync);

        ToggleApiKeyVisibilityCommand = new RelayCommand(() =>
        {
            IsApiKeyVisible = !IsApiKeyVisible;
        });

        RetryChatTestCommand = new AsyncRelayCommand(RetryChatTestAsync);
        RetryWithDropdownCommand = new AsyncRelayCommand(RetryWithDropdownAsync);
        RetryWithManualCommand = new AsyncRelayCommand(RetryWithManualAsync);
        OpenApiKeyManagerCommand = new RelayCommand(() => OpenApiKeyManager());

        LoadParameterProfilesForUI();

        InitDefaultProviderForGlobalParameters();

        TypeOptions.Remove("数据");

        CleanupCategorySelectionTree();

        ProviderLogoHelper.PreloadInBackground(
            Service.GetAllCategories().Select(c => c.LogoPath));
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

    public class ModelInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int MaxTokens { get; set; }
        public int ContextLength { get; set; }
        public string Provider { get; set; } = string.Empty;
    }

    private void LoadParameterProfilesForUI()
    {
        ParameterProfiles.Clear();

        var profiles = Service.GetAllParameterProfilesForUI();
        foreach (var p in profiles)
        {
            ParameterProfiles.Add(p);
        }

        if (ParameterProfiles.Count == 0)
        {
            _selectedProfileId = string.Empty;
            SelectedProfile = null;
            OnPropertyChanged(nameof(SelectedProfileId));
            return;
        }

        string targetId = _selectedProfileId;

        if (_currentProvider != null)
        {
            var providerProfileId = Service.GetDefaultProfileIdForProvider(_currentProvider);
            if (!string.IsNullOrWhiteSpace(providerProfileId))
            {
                targetId = providerProfileId;
            }
        }

        if (string.IsNullOrWhiteSpace(targetId) || !ParameterProfiles.Any(p => p.Id == targetId))
        {
            targetId = ParameterProfiles[0].Id;
        }

        _selectedProfileId = targetId;
        SelectedProfile = ParameterProfiles.FirstOrDefault(p => p.Id == _selectedProfileId);
        OnPropertyChanged(nameof(SelectedProfileId));
    }

    private void InitDefaultProviderForGlobalParameters()
    {
        if (_currentProvider != null)
            return;

        try
        {
            var allCategories = Service.GetAllCategories();
            var firstProvider = allCategories.FirstOrDefault(c => c.Level == 2);

            if (firstProvider != null)
            {
                SetCurrentProvider(firstProvider);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 初始化全局参数默认供应商失败: {ex.Message}");
        }
    }

    private void SetCurrentProvider(AIProviderCategory? provider)
    {
        _currentProvider = provider;
        OnPropertyChanged(nameof(CurrentProviderName));
        OnPropertyChanged(nameof(HasCurrentProvider));

        if (ParameterProfiles.Count == 0)
        {
            _selectedProfileId = string.Empty;
            SelectedProfile = null;
            OnPropertyChanged(nameof(SelectedProfileId));
            return;
        }

        string targetId = _selectedProfileId;

        if (_currentProvider != null)
        {
            var providerProfileId = Service.GetDefaultProfileIdForProvider(_currentProvider);
            if (!string.IsNullOrWhiteSpace(providerProfileId))
            {
                targetId = providerProfileId;
            }
        }

        if (string.IsNullOrWhiteSpace(targetId) || !ParameterProfiles.Any(p => p.Id == targetId))
        {
            targetId = ParameterProfiles[0].Id;
        }

        _selectedProfileId = targetId;
        SelectedProfile = ParameterProfiles.FirstOrDefault(p => p.Id == _selectedProfileId);
        OnPropertyChanged(nameof(SelectedProfileId));
    }

    private bool _isLoadingForm;

    private void CheckEndpointConfigurationChanged()
    {
        if (_isLoadingForm)
            return;

        if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            return;

        var newSignature = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);
        var oldSignature = _currentEditingCategory.EndpointSignature;

        if (!string.IsNullOrWhiteSpace(oldSignature) && oldSignature != newSignature)
        {
            _currentEditingCategory.ModelsEndpoint = null;
            _currentEditingCategory.ChatEndpoint = null;
            _currentEditingCategory.EndpointVerifiedAt = null;
            _currentEditingCategory.EndpointSignature = newSignature;

            TM.App.Log($"[ModelManagement] 端点配置变更，已清空验证状态: OldSignature={oldSignature}, NewSignature={newSignature}");
            GlobalToast.Warning("配置已变更", "端点或密钥已修改，请重新测试连接");
        }
    }

    protected override string DefaultDataIcon => "🤖";

    protected override UserConfigurationData? CreateNewData(string? categoryName = null)
    {
        return new UserConfigurationData
        {
            Id = ShortIdGenerator.New("D"),
            Name = "新模型配置",
            Icon = "🤖",
            Category = categoryName ?? "",
            IsEnabled = false,
            CreatedTime = DateTime.Now,
            ModifiedTime = DateTime.Now
        };
    }

    protected override string? GetCurrentCategoryValue()
    {
        return FormCategory;
    }

    protected override void ApplyCategorySelection(string categoryName)
    {
        FormCategory = categoryName;
    }

    protected override int ClearAllDataItems()
    {
        var count = Service.ClearAllConfigurations();
        try
        {
            foreach (var cfg in _aiConfigurationService.GetAllConfigurations().ToList())
            {
                _aiConfigurationService.DeleteConfiguration(cfg.Id);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 全部删除同步清理对话配置失败: {ex.Message}");
        }
        return count;
    }

    protected override void OnAfterDeleteAll(int deletedCount)
    {
        base.OnAfterDeleteAll(deletedCount);
        _ = Task.Run(() => { Service.SyncProvidersFromCategories(); _aiLibraryService.ReloadLibrary(); });
        _currentEditingData = null;
        _currentEditingCategory = null;
        ResetForm();
        OnPropertyChanged(nameof(IsGlobalParametersAvailable));
        OnPropertyChanged(nameof(IsTab1Enabled));
        OnPropertyChanged(nameof(IsApiConfigEditable));
        OnPropertyChanged(nameof(IsTab2Enabled));
    }

    protected override List<AIProviderCategory> GetAllCategoriesFromService()
    {
        return Service.GetAllCategories();
    }

    protected override void OnTreeDataRefreshed()
    {
        base.OnTreeDataRefreshed();
        CleanupCategorySelectionTree();
    }

    protected override List<UserConfigurationData> GetAllDataItems()
    {
        return Service.GetAllData();
    }

    protected override string GetDataCategory(UserConfigurationData data)
    {
        return data.Category;
    }

    protected override TreeNodeItem ConvertToTreeNode(UserConfigurationData data)
    {
        var provider = Service.GetAllCategories()
            .FirstOrDefault(c => c.Name == data.Category && c.Level == 2);

        System.Windows.Media.ImageSource? logoImage = null;
        string icon = DefaultDataIcon;

        if (!string.IsNullOrWhiteSpace(data.Icon))
        {
            if (data.Icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                logoImage = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(data.Icon, DefaultDataIcon);
                icon = DefaultDataIcon;
            }
            else
            {
                icon = data.Icon;
            }
        }

        if (logoImage == null && !string.IsNullOrWhiteSpace(data.ModelName))
        {
            var modelLogoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(data.ModelName);
            if (!string.IsNullOrEmpty(modelLogoPath))
            {
                logoImage = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(modelLogoPath, DefaultDataIcon);
            }
        }

        if (logoImage == null && provider != null)
        {
            logoImage = GetCategoryLogoImage(provider);
        }

        return new TreeNodeItem
        {
            Name = data.Name,
            Icon = icon,
            Tag = data,
            ShowChildCount = false,
            LogoImage = logoImage
        };
    }

    protected override bool MatchesSearchKeyword(UserConfigurationData data, string keyword)
    {
        return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.ModelName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.ProviderName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    protected override string NewItemTypeName => "模型配置";

    private ICommand? _addCommand;
    public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
    {
        try
        {
            _currentEditingData = null;
            _currentEditingCategory = null;
            ResetForm();
            ExecuteAddWithCreateMode();
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsTab2Enabled));
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 新建失败: {ex.Message}");
            GlobalToast.Error("新建失败", ex.Message);
        }
    });

    private ICommand? _saveCommand;
    public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
    {
        try
        {
            ExecuteSaveWithCreateEditMode(
                validateForm: ValidateFormCore,
                createCategoryCore: CreateCategoryCore,
                createDataCore: CreateDataCore,
                hasEditingCategory: () => _currentEditingCategory != null,
                hasEditingData: () => _currentEditingData != null,
                updateCategoryCore: UpdateCategoryCore,
                updateDataCore: UpdateDataCore);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 保存失败: {ex.Message}");
            GlobalToast.Error("保存失败", ex.Message);
        }
    });

    private ICommand? _deleteCommand;
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(param =>
    {
        try
        {
            if (param is TreeNodeItem node)
            {
                if (node.Tag is AIProviderCategory category)
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                }
                else if (node.Tag is UserConfigurationData data)
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                }
            }

            if (_currentEditingCategory != null)
            {
                if (_currentEditingCategory.IsBuiltIn)
                {
                    GlobalToast.Warning("无法删除", $"「{_currentEditingCategory.Name}」是系统内置分类，不可删除");
                    return;
                }

                var childCount = CollectCategoryAndChildrenNames(_currentEditingCategory.Name).Count - 1;

                var result = StandardDialog.ShowConfirm(
                    $"确定要删除分类「{_currentEditingCategory.Name}」吗？\n\n注意：该分类及其{childCount}个子分类下的所有模型配置也会被删除！",
                    "确认删除"
                );
                if (!result) return;

                try
                {
                    var allNames = CollectCategoryAndChildrenNames(_currentEditingCategory.Name);
                    foreach (var categoryName in allNames)
                    {
                        var provider = _aiLibraryService.GetAllProviders()
                            .FirstOrDefault(p => string.Equals(p.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                        if (provider != null)
                        {
                            foreach (var cfg in _aiConfigurationService.GetAllConfigurations()
                                .Where(c => string.Equals(c.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
                                .ToList())
                            {
                                _aiConfigurationService.DeleteConfiguration(cfg.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ModelManagement] 删除分类同步清理对话配置失败: {ex.Message}");
                }

                var (catDeleted, dataDeleted) = Service.CascadeDeleteCategory(_currentEditingCategory.Name);

                _ = Task.Run(() => { Service.SyncProvidersFromCategories(); _aiLibraryService.ReloadLibrary(); });

                GlobalToast.Success("删除成功", $"已删除 {catDeleted} 个分类及其 {dataDeleted} 个模型配置");

                _currentEditingCategory = null;
                _currentEditingData = null;
                ResetForm();
                RefreshTreeAndCategorySelection();

                OnPropertyChanged(nameof(IsGlobalParametersAvailable));
                OnPropertyChanged(nameof(IsTab1Enabled));
                OnPropertyChanged(nameof(IsApiConfigEditable));
                OnPropertyChanged(nameof(IsTab2Enabled));
            }
            else if (_currentEditingData != null)
            {
                var result = StandardDialog.ShowConfirm($"确定要删除模型配置「{_currentEditingData.Name}」吗？", "确认删除");
                if (!result) return;

                try
                {
                    var providers = _aiLibraryService.GetAllProviders();
                    var provider = providers.FirstOrDefault(p =>
                        string.Equals(p.Name, _currentEditingData.Category, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, _currentEditingData.ProviderName, StringComparison.OrdinalIgnoreCase));

                    if (provider != null)
                    {
                        var models = _aiLibraryService.GetModelsByProvider(provider.Id);
                        var modelName = (_currentEditingData.ModelName ?? string.Empty).Trim();

                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            TM.Services.Framework.AI.Core.AIModel? model = null;
                            if (models.Count > 0)
                            {
                                model = models.FirstOrDefault(m =>
                                    string.Equals(m.Id, modelName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
                            }

                            var modelId = model?.Id ?? modelName;

                            var configToDelete = _aiConfigurationService.GetAllConfigurations()
                                .FirstOrDefault(c =>
                                    string.Equals(c.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(c.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

                            if (configToDelete != null)
                            {
                                _aiConfigurationService.DeleteConfiguration(configToDelete.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ModelManagement] 删除模型配置同步清理对话配置失败: {ex.Message}");
                }

                Service.DeleteConfiguration(_currentEditingData.Id);
                GlobalToast.Success("删除成功", $"模型配置「{_currentEditingData.Name}」已删除");

                _currentEditingData = null;
                ResetForm();
                RefreshTreeAndCategorySelection();

                OnPropertyChanged(nameof(IsGlobalParametersAvailable));
                OnPropertyChanged(nameof(IsTab1Enabled));
                OnPropertyChanged(nameof(IsApiConfigEditable));
                OnPropertyChanged(nameof(IsTab2Enabled));
            }
            else
            {
                GlobalToast.Warning("删除失败", "请先选择要删除的分类或模型配置");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 删除失败: {ex.Message}");
            GlobalToast.Error("删除失败", ex.Message);
        }
    });

    private ICommand? _selectNodeCommand;
    public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
    {
        try
        {
            if (param is TreeNodeItem node)
            {
                if (node.Tag is UserConfigurationData data)
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                    OnPropertyChanged(nameof(IsGlobalParametersAvailable));
                    OnPropertyChanged(nameof(IsTab1Enabled));
                    OnPropertyChanged(nameof(IsApiConfigEditable));
                    OnPropertyChanged(nameof(IsTab2Enabled));
                }
                else if (node.Tag is AIProviderCategory category)
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                    OnPropertyChanged(nameof(IsGlobalParametersAvailable));
                    OnPropertyChanged(nameof(IsTab1Enabled));
                    OnPropertyChanged(nameof(IsApiConfigEditable));
                    OnPropertyChanged(nameof(IsTab2Enabled));
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 选择节点失败: {ex.Message}");
            GlobalToast.Error("选择失败", ex.Message);
        }
    });

    private bool ValidateFormCore()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            GlobalToast.Warning("保存失败", "请输入名称");
            return false;
        }

        if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
        {
            GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或模型配置");
            return false;
        }

        return true;
    }

    private void CreateCategoryCore()
    {
        var parentCategoryName = "";
        var level = 1;

        if (!string.IsNullOrWhiteSpace(FormCategory))
        {
            parentCategoryName = FormCategory;
            var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
            level = parentCategory != null ? parentCategory.Level + 1 : 1;
        }

        var categoryIcon = GetCategoryIconForSave(FormIcon);

        var logoFileName = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(FormName);

        var newCategory = new AIProviderCategory
        {
            Id = ShortIdGenerator.New("C"),
            Name = FormName,
            Icon = categoryIcon,
            LogoPath = logoFileName,
            ParentCategory = parentCategoryName,
            Level = level,
            Order = Service.GetAllCategories().Count + 1,
            Description = FormDescription,
            ApiEndpoint = level == 2 ? FormApiEndpoint : null,
            ApiKey = level == 2 ? FormApiKey : null
        };

        if (!Service.AddCategory(newCategory))
        {
            GlobalToast.Warning("创建失败", "分类名已存在，请改名");
            return;
        }

        _ = Task.Run(() => { Service.SyncProvidersFromCategories(); _aiLibraryService.ReloadLibrary(); });

        string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
        GlobalToast.Success("保存成功", $"{levelDesc}「{newCategory.Name}」已创建");

        _currentEditingCategory = null;
        _currentEditingData = null;
        ResetForm();

        OnPropertyChanged(nameof(IsGlobalParametersAvailable));

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsTab2Enabled));
        }));
    }

    private void CreateDataCore()
    {
        if (string.IsNullOrWhiteSpace(FormCategory))
        {
            GlobalToast.Warning("保存失败", "请选择所属分类");
            return;
        }

        var newData = CreateNewData(FormCategory);
        if (newData == null) return;

        UpdateDataFromForm(newData);
        Service.AddConfiguration(newData);
        _currentEditingData = newData;
        GlobalToast.Success("保存成功", $"模型配置「{newData.Name}」已创建");
        SyncToAIServiceAndActivate(newData);
        _ = Task.Run(_aiLibraryService.ReloadLibrary);

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsTab2Enabled));
        }));
    }

    private void UpdateCategoryCore()
    {
        if (_currentEditingCategory == null)
            return;

        var oldName = _currentEditingCategory.Name;
        _currentEditingCategory.Name = FormName;
        _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
        _currentEditingCategory.Description = FormDescription;

        if (_currentEditingCategory.Level == 2)
        {
            _currentEditingCategory.ApiEndpoint = FormApiEndpoint;
            _currentEditingCategory.ApiKey = FormApiKey;
        }

        if (!Service.UpdateCategory(_currentEditingCategory))
        {
            _currentEditingCategory.Name = oldName;
            GlobalToast.Warning("保存失败", "分类名已存在，请改名");
            return;
        }

        Service.SyncKeyPoolsToRotationService();
        _ = Task.Run(() => { Service.SyncProvidersFromCategories(); _aiLibraryService.ReloadLibrary(); });
        GlobalToast.Success("保存成功", $"分类「{_currentEditingCategory.Name}」已更新");
    }

    private void UpdateDataCore()
    {
        if (_currentEditingData == null)
            return;

        UpdateDataFromForm(_currentEditingData);
        Service.UpdateConfiguration(_currentEditingData);
        GlobalToast.Success("保存成功", $"模型配置「{_currentEditingData.Name}」已更新");
        SyncToAIServiceAndActivate(_currentEditingData);
        _ = Task.Run(_aiLibraryService.ReloadLibrary);
    }

    protected override void OnDataEnabledChanged(UserConfigurationData data, bool isEnabled)
    {
        base.OnDataEnabledChanged(data, isEnabled);
        if (isEnabled)
        {
            SyncToAIServiceAndActivate(data);
        }
    }

    private void SyncToAIServiceAndActivate(UserConfigurationData data)
    {
        if (data == null)
            return;

        if (!data.IsEnabled)
            return;

        try
        {
            var providers = _aiLibraryService.GetAllProviders();
            var provider = providers.FirstOrDefault(p =>
                string.Equals(p.Name, data.Category, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, data.ProviderName, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                TM.App.Log($"[ModelManagement] 同步到AIService失败：未找到供应商，Category={data.Category}, ProviderName={data.ProviderName}");
                GlobalToast.Warning("模型未同步到对话", $"未找到供应商：{data.Category}");
                return;
            }

            var providerCategory = Service.GetAllCategories()
                .FirstOrDefault(c => c.Level == 2 && string.Equals(c.Name, data.Category, StringComparison.OrdinalIgnoreCase));

            if (providerCategory == null ||
                string.IsNullOrWhiteSpace(providerCategory.ModelsEndpoint) ||
                string.IsNullOrWhiteSpace(providerCategory.ChatEndpoint))
            {
                TM.App.Log($"[ModelManagement] 禁止激活：供应商端点未验证，Category={data.Category}, ModelsEndpoint={providerCategory?.ModelsEndpoint}, ChatEndpoint={providerCategory?.ChatEndpoint}");
                GlobalToast.Warning("禁止激活", "该供应商端点尚未验证（Models/Chat），请先在供应商分类中点击「测试连接」");
                return;
            }

            var models = _aiLibraryService.GetModelsByProvider(provider.Id);
            var modelName = (data.ModelName ?? string.Empty).Trim();
            TM.Services.Framework.AI.Core.AIModel? model = null;

            if (!string.IsNullOrEmpty(modelName) && models.Count > 0)
            {
                model = models.FirstOrDefault(m =>
                    string.Equals(m.Id, modelName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
            }

            string modelId;
            if (model != null)
            {
                modelId = model.Id;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    TM.App.Log($"[ModelManagement] 同步到AIService失败：模型名称为空，无法构建配置");
                    GlobalToast.Warning("模型未同步到对话", "模型名称为空，无法激活");
                    return;
                }

                modelId = modelName;
                TM.App.Log($"[ModelManagement] 未在模型库中找到 '{modelName}'，将直接使用自定义模型ID");
            }

            var configs = _aiConfigurationService.GetAllConfigurations();
            var config = configs.FirstOrDefault(c =>
                string.Equals(c.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

            int contextWindow = 0;
            if (!string.IsNullOrEmpty(data.ContextLength) && int.TryParse(data.ContextLength, out var parsedContext))
            {
                contextWindow = parsedContext;
            }

            int safeMaxTokens = data.MaxTokens;
            if (safeMaxTokens <= 0)
            {
                safeMaxTokens = 4096;
            }

            if (model?.MaxOutputTokens > 0 && safeMaxTokens > model.MaxOutputTokens)
            {
                safeMaxTokens = model.MaxOutputTokens;
            }

            if (contextWindow > 0)
            {
                const int safetyMargin = 768;
                var maxAllowedByWindow = contextWindow - safetyMargin;
                if (maxAllowedByWindow < 256)
                {
                    maxAllowedByWindow = 256;
                }

                if (safeMaxTokens > maxAllowedByWindow)
                {
                    safeMaxTokens = maxAllowedByWindow;
                }
            }

            if (safeMaxTokens < 256)
            {
                safeMaxTokens = 256;
            }

            if (safeMaxTokens != data.MaxTokens)
            {
                TM.App.Log($"[ModelManagement] MaxTokens 已调整: raw={data.MaxTokens}, effective={safeMaxTokens}, contextWindow={contextWindow}, modelMaxOutput={(model?.MaxOutputTokens ?? 0)}");

                GlobalToast.Info("参数已自动调整", $"MaxTokens: {data.MaxTokens} → {safeMaxTokens}");
            }

            if (config == null)
            {
                var finalEndpoint = providerCategory.ChatEndpoint;

                config = new UserConfiguration
                {
                    Name = data.Name,
                    ProviderId = provider.Id,
                    ModelId = modelId,
                    CustomEndpoint = finalEndpoint,
                    Temperature = data.Temperature,
                    MaxTokens = safeMaxTokens,
                    ContextWindow = contextWindow,
                    IsActive = true,
                    IsEnabled = true
                };

                _aiConfigurationService.AddConfiguration(config);
                TM.App.Log($"[ModelManagement] 已为模型创建AIService配置并激活: {config.Name}, ContextWindow={contextWindow}, ChatEndpoint={finalEndpoint}");
            }
            else
            {
                var finalEndpoint = providerCategory.ChatEndpoint;

                config.Name = data.Name;
                config.ProviderId = provider.Id;
                config.ModelId = modelId;
                config.CustomEndpoint = finalEndpoint;
                config.Temperature = data.Temperature;
                config.MaxTokens = safeMaxTokens;
                config.ContextWindow = contextWindow;
                config.IsActive = true;
                config.IsEnabled = true;

                _aiConfigurationService.UpdateConfiguration(config);
                TM.App.Log($"[ModelManagement] 已更新AIService配置并激活: {config.Name}, ContextWindow={contextWindow}, ChatEndpoint={finalEndpoint}");
            }

            _aiConfigurationService.SetActiveConfiguration(config);
            GlobalToast.Success("模型已激活", $"当前对话将使用「{data.Name}」");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 同步到AIService失败: {ex.Message}");
            GlobalToast.Error("模型激活失败", ex.Message);
        }
    }

    private void UpdateDataFromForm(UserConfigurationData data)
    {
        data.Name = FormName;
        data.Icon = GetDataIconForSave(FormIcon);
        data.Category = FormCategory;
        data.IsEnabled = FormStatus == "已启用";
        data.Description = FormDescription;
        data.ModifiedTime = DateTime.Now;

        data.ModelName = FormModelName;
        data.ApiEndpoint = FormApiEndpoint;
        data.IsActive = FormIsActive;

        data.ProviderName = FormProviderName;
        data.ModelVersion = FormModelVersion;
        data.ContextLength = FormContextLength;
        data.TrainingDataCutoff = FormTrainingDataCutoff;
        data.InputPrice = FormInputPrice;
        data.OutputPrice = FormOutputPrice;
        data.SupportedFeatures = FormSupportedFeatures;

        data.Temperature = FormTemperature;
        data.MaxTokens = FormMaxTokens;
        data.TopP = FormTopP;
        data.FrequencyPenalty = FormFrequencyPenalty;
        data.PresencePenalty = FormPresencePenalty;
        data.RateLimitRPM = FormRateLimitRPM;
        data.RateLimitTPM = FormRateLimitTPM;
        data.MaxConcurrency = FormMaxConcurrency;
        data.Seed = FormSeed;
        data.StopSequences = FormStopSequences;

        data.RetryCount = FormRetryCount;
        data.TimeoutSeconds = FormTimeoutSeconds;
        data.EnableStreaming = FormEnableStreaming;
    }

    private void LoadDataToForm(UserConfigurationData data)
    {
        FormName = data.Name;
        FormIcon = data.Icon;
        FormStatus = data.IsEnabled ? "已启用" : "已禁用";
        FormCategory = data.Category;
        FormDescription = data.Description;

        FormModelName = data.ModelName;
        FormApiEndpoint = data.ApiEndpoint;
        FormApiKey = Service.GetAllCategories()
            .FirstOrDefault(c => c.Level == 2 && c.Name == data.Category)?.ApiKey ?? string.Empty;
        FormIsActive = data.IsActive;

        FormProviderName = data.ProviderName;
        FormModelVersion = data.ModelVersion;
        FormContextLength = data.ContextLength;
        FormTrainingDataCutoff = data.TrainingDataCutoff;
        FormInputPrice = data.InputPrice;
        FormOutputPrice = data.OutputPrice;
        FormSupportedFeatures = data.SupportedFeatures;

        FormTemperature = data.Temperature;
        FormMaxTokens = data.MaxTokens;
        FormTopP = data.TopP;
        FormFrequencyPenalty = data.FrequencyPenalty;
        FormPresencePenalty = data.PresencePenalty;
        FormRateLimitRPM = data.RateLimitRPM;
        FormRateLimitTPM = data.RateLimitTPM;
        FormMaxConcurrency = data.MaxConcurrency;
        FormSeed = data.Seed;
        FormStopSequences = data.StopSequences;

        FormRetryCount = data.RetryCount;
        FormTimeoutSeconds = data.TimeoutSeconds;
        FormEnableStreaming = data.EnableStreaming;

        var provider = Service.GetAllCategories()
            .FirstOrDefault(c => c.Name == data.Category && c.Level == 2);
        SetCurrentProvider(provider);

        OnDataItemLoaded();
    }

    private void LoadCategoryToForm(AIProviderCategory category)
    {
        _isLoadingForm = true;
        try
        {
        if (category.Level == 1)
        {
            ResetForm();

            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.Name;
            FormDescription = category.Description ?? string.Empty;
            SetCurrentProvider(null);

            _chatTestFailed = false;
            ChatRetryModels.Clear();
            RefreshEndpointVerificationStatus();
            return;
        }

        ResetForm();

        FormName = category.Name;
        FormIcon = category.Icon;
        FormStatus = "已启用";
        FormCategory = category.Name;
        FormDescription = category.Description ?? string.Empty;

        FormApiEndpoint = category.ApiEndpoint ?? string.Empty;
        FormApiKey = category.ApiKey ?? string.Empty;

        SetCurrentProvider(category);

        _chatTestFailed = false;
        ChatRetryModels.Clear();
        RefreshEndpointVerificationStatus();
        _isApiKeyVisible = false;
        OnPropertyChanged(nameof(IsApiKeyVisible));
        OnPropertyChanged(nameof(ApiKeyVisibilityIcon));
        OnPropertyChanged(nameof(ApiKeyCountLabel));
        OnPropertyChanged(nameof(ActiveKeyDisplay));
        }
        finally
        {
            _isLoadingForm = false;
        }
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormIcon = "🤖";
        FormStatus = "已禁用";
        FormCategory = string.Empty;
        FormDescription = string.Empty;

        ResetDataFields();
    }

    private void ResetDataFields()
    {
        FormModelName = string.Empty;
        FormApiEndpoint = string.Empty;
        FormApiKey = string.Empty;
        FormIsActive = false;

        FormProviderName = string.Empty;
        FormModelVersion = string.Empty;
        FormContextLength = string.Empty;
        FormTrainingDataCutoff = string.Empty;
        FormInputPrice = string.Empty;
        FormOutputPrice = string.Empty;
        FormSupportedFeatures = string.Empty;

        FormTemperature = 0.7;
        FormMaxTokens = 4096;
        FormTopP = 1.0;
        FormFrequencyPenalty = 0.0;
        FormPresencePenalty = 0.0;
        FormRateLimitRPM = 0;
        FormRateLimitTPM = 0;
        FormMaxConcurrency = 5;
        FormSeed = string.Empty;
        FormStopSequences = string.Empty;

        FormRetryCount = 3;
        FormTimeoutSeconds = 30;
        FormEnableStreaming = true;

        SetCurrentProvider(null);
    }

    private async Task FetchModelsAsync()
    {
        try
        {
            if (!IsAutoFetchMode)
            {
                StandardDialog.ShowWarning("请切换到「自动获取端点内所有模型」模式", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiEndpoint))
            {
                StandardDialog.ShowWarning("请先输入API端点", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiKey))
            {
                StandardDialog.ShowWarning("请先输入API密钥", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先点击「测试连接」验证端点", "提示");
                return;
            }

            GlobalToast.Info("获取模型", "正在从API获取可用模型列表...");
            TM.App.Log($"[ModelManagement] 开始获取全部模型: Category={FormCategory}, ModelsEndpoint={_currentEditingCategory.ModelsEndpoint}");

            var models = await FetchModelsFromApiAsync(_currentEditingCategory.ModelsEndpoint, FormApiKey);

            if (models == null || models.Count == 0)
            {
                StandardDialog.ShowWarning("未获取到模型列表，请检查API端点和密钥是否正确", "提示");
                return;
            }

            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model);
            }

            OnPropertyChanged(nameof(IsModelComboEnabled));

            TM.App.Log($"[ModelManagement] 获取模型成功: {AvailableModels.Count}个");

            var result = StandardDialog.ShowConfirm(
                $"成功获取到 {models.Count} 个模型，是否要为这些模型批量创建配置？\n\n创建后的配置将显示在「{FormCategory}」分类下。",
                "批量创建配置");

            if (result)
            {
                await BatchCreateConfigurationsAsync(models);
            }
            else
            {
                GlobalToast.Success("获取成功", $"获取到 {AvailableModels.Count} 个可用模型");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 获取模型失败: {ex.Message}");
            StandardDialog.ShowError($"无法获取模型列表：{ex.Message}", "获取失败");
        }
    }

    private async Task FetchManualModelAsync()
    {
        try
        {
            if (!IsManualInputMode)
            {
                StandardDialog.ShowWarning("请切换到「手动获取输入指定模型」模式", "提示");
                return;
            }

            var manualName = ManualModelName?.Trim();
            if (string.IsNullOrWhiteSpace(manualName))
            {
                StandardDialog.ShowWarning("请输入模型名称", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiEndpoint))
            {
                StandardDialog.ShowWarning("请先输入API端点", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiKey))
            {
                StandardDialog.ShowWarning("请先输入API密钥", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先点击「测试连接」验证端点", "提示");
                return;
            }

            TM.App.Log($"[ModelManagement] 手动添加模型: {manualName}");
            GlobalToast.Info("添加模型", $"正在创建模型配置: {manualName}");

            var manualModel = new ModelInfo { Id = manualName, Name = manualName };

            AvailableModels.Clear();
            AvailableModels.Add(manualModel);
            OnPropertyChanged(nameof(IsModelComboEnabled));

            TM.App.Log($"[ModelManagement] 手动模型已就绪: {manualName}");

            var result = StandardDialog.ShowConfirm(
                $"确认添加模型「{manualName}」？\n\n该配置将显示在「{FormCategory}」分类下。\n注意：模型名称须与供应商端点实际支持的 model id 一致。",
                "添加模型");

            if (result)
            {
                await BatchCreateConfigurationsAsync(new List<ModelInfo> { manualModel });
            }
            else
            {
                GlobalToast.Info("已取消", $"取消添加模型: {manualName}");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 手动获取模型失败: {ex.Message}");
            StandardDialog.ShowError($"无法获取模型：{ex.Message}", "获取失败");
        }
    }

    private static readonly string[] FallbackTestModels =
    {
        "gpt-4o-mini", "gpt-3.5-turbo", "gpt-4o", "gpt-4",
        "claude-3-haiku-20240307", "gemini-1.5-flash"
    };

    private async Task TestApiConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FormApiEndpoint))
            {
                StandardDialog.ShowWarning("请先输入API端点", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiKey))
            {
                StandardDialog.ShowWarning("请先输入API密钥", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            _chatTestFailed = false;
            ChatRetryModels.Clear();
            RefreshEndpointVerificationStatus();

            GlobalToast.Info("测试连接", "正在测试端点...");
            TM.App.Log($"[ModelManagement] 开始端点测试: Endpoint={FormApiEndpoint}");

            var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);
            TM.App.Log($"[ModelManagement] 候选URL: {string.Join(", ", candidates)}");

            var modelsResult = await _endpointTestService.TestModelsEndpointAsync(candidates, FormApiKey);

            if (modelsResult.Success)
            {
                TM.App.Log($"[ModelManagement] Models 端点成功: {modelsResult.SuccessfulEndpoint}, 模型数: {modelsResult.Models.Count}");
                _lastFetchedModels = modelsResult.Models;
                _currentEditingCategory.ModelsEndpoint = modelsResult.SuccessfulEndpoint;

                var testModelId = _endpointTestService.SelectTestModel(modelsResult.Models);
                if (string.IsNullOrWhiteSpace(testModelId))
                {
                    StandardDialog.ShowWarning("未找到可用于测试的模型", "测试结果");
                    return;
                }

                GlobalToast.Info("测试连接", $"正在测试 Chat 端点（模型: {testModelId}）...");
                var chatResult = await _endpointTestService.TestChatEndpointAsync(candidates, FormApiKey, testModelId);

                if (!chatResult.Success)
                {
                    TM.App.Log($"[ModelManagement] Chat 端点测试失败: {chatResult.ErrorMessage}");
                    _chatTestFailed = true;
                    ChatRetryModels.Clear();
                    foreach (var m in modelsResult.Models) ChatRetryModels.Add(m);
                    RefreshEndpointVerificationStatus();

                    var saveAnyway = StandardDialog.ShowConfirm(
                        $"Chat 端点测试失败：{chatResult.ErrorMessage}\n\n" +
                        $"但 Models 端点已成功获取 {modelsResult.Models.Count} 个模型，端点地址有效。\n\n" +
                        $"可能原因：测试所用模型需要特定的 API 密钥（如本地中转服务）。\n\n" +
                        $"是否直接保存端点配置？（也可点击\"取消\"后在下方选择其他模型重试）",
                        "Chat 测试失败 - 是否直接保存？");

                    if (saveAnyway)
                    {
                        SaveVerifiedEndpoints(modelsResult.SuccessfulEndpoint, modelsResult.SuccessfulEndpoint, modelsResult.Models.Count);
                    }
                    return;
                }

                SaveVerifiedEndpoints(modelsResult.SuccessfulEndpoint, chatResult.SuccessfulEndpoint, modelsResult.Models.Count);
                return;
            }

            bool isWafBlock = modelsResult.ErrorMessage?.Contains("HTML") == true
                           || modelsResult.ErrorMessage?.Contains("拦截") == true;

            TM.App.Log($"[ModelManagement] Models 端点失败: {modelsResult.ErrorMessage}, WAF={isWafBlock}");

            if (!isWafBlock)
            {
                StandardDialog.ShowWarning($"Models 端点测试失败：{modelsResult.ErrorMessage}", "测试结果");
                return;
            }

            TM.App.Log("[ModelManagement] WAF保护端点，跳过 Chat 原始测试，直接进入强制保存流程");

            TM.App.Log("[ModelManagement] WAF保护端点，强制保存流程");
            var trustSave = StandardDialog.ShowConfirm(
                "端点受 WAF 安全防护，自动验证无法绕过。\n\n" +
                "可能原因：\n" +
                "• 端点受 Cloudflare 等 WAF 保护，需要特定客户端\n" +
                "• API 密钥无效或权限不足\n" +
                "• 端点 URL 格式不正确\n\n" +
                "是否仍然保存此端点配置（信任用户填写的 URL）？\n" +
                "保存后可手动输入模型名称直接使用。",
                "验证失败 - 是否强制保存？");

            if (trustSave)
            {
                var baseUrl = candidates.FirstOrDefault() ?? FormApiEndpoint.TrimEnd('/');
                SaveVerifiedEndpoints(baseUrl, baseUrl, 0);
                GlobalToast.Warning("强制保存", "端点已保存，请手动添加模型名称");
            }
        }
        catch (TaskCanceledException)
        {
            TM.App.Log("[ModelManagement] API连接测试超时");
            StandardDialog.ShowWarning("连接超时，请检查网络或API端点是否正确", "测试结果");
        }
        catch (HttpRequestException ex)
        {
            TM.App.Log($"[ModelManagement] API连接测试网络错误: {ex.Message}");
            StandardDialog.ShowError($"网络错误：{ex.Message}", "连接失败");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] API连接测试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败：{ex.Message}", "错误");
        }
    }

    private void SaveVerifiedEndpoints(string? modelsEndpoint, string? chatEndpoint, int modelCount)
    {
        if (_currentEditingCategory == null) return;

        _currentEditingCategory.ModelsEndpoint = modelsEndpoint ?? string.Empty;
        _currentEditingCategory.ChatEndpoint   = chatEndpoint ?? string.Empty;
        _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
        _currentEditingCategory.EndpointSignature  =
            _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

        Service.UpdateCategory(_currentEditingCategory);
        _ = Task.Run(Service.SyncProvidersFromCategories);

        _chatTestFailed = false;
        ChatRetryModels.Clear();
        RefreshEndpointVerificationStatus();

        TM.App.Log($"[ModelManagement] 端点已保存: Models={modelsEndpoint}, Chat={chatEndpoint}, 模型数={modelCount}");

        if (modelCount > 0)
        {
            var msg = $"✅ 端点测试通过！\n\nModels 端点: {modelsEndpoint}\nChat 端点: {chatEndpoint}\n可用模型数: {modelCount}";
            StandardDialog.ShowInfo(msg, "测试成功");
            GlobalToast.Success("连接成功", "API端点验证通过");
        }
    }

    private async Task RetryChatTestAsync()
    {
        try
        {
            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (SelectedChatRetryModel == null)
            {
                StandardDialog.ShowWarning("请选择一个模型进行测试", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            var testModelId = SelectedChatRetryModel.Id;
            if (string.IsNullOrWhiteSpace(testModelId))
            {
                StandardDialog.ShowWarning("所选模型ID为空，请选择其他模型", "提示");
                return;
            }
            TM.App.Log($"[ModelManagement] 用户选择模型重试 Chat 测试: {testModelId}");
            GlobalToast.Info("重试测试", $"正在测试 Chat 端点（模型: {testModelId}）...\n注意：此测试会消耗极少量 token");

            var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);

            var chatResult = await _endpointTestService.TestChatEndpointAsync(
                candidates, FormApiKey, testModelId);

            if (!chatResult.Success)
            {
                TM.App.Log($"[ModelManagement] Chat 端点重试测试失败: {chatResult.ErrorMessage}");
                StandardDialog.ShowWarning($"Chat 端点测试失败：{chatResult.ErrorMessage}\n\n请尝试选择其他模型。", "测试结果");
                return;
            }

            TM.App.Log($"[ModelManagement] Chat 端点重试测试成功: {chatResult.SuccessfulEndpoint}");

            _currentEditingCategory.ChatEndpoint = chatResult.SuccessfulEndpoint;
            _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
            _currentEditingCategory.EndpointSignature = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

            Service.UpdateCategory(_currentEditingCategory);
            _ = Task.Run(Service.SyncProvidersFromCategories);

            _chatTestFailed = false;
            ChatRetryModels.Clear();
            SelectedChatRetryModel = null;
            RefreshEndpointVerificationStatus();

            var resultMessage = $"✅ 端点测试通过！\n\n" +
                               $"Models 端点: {_currentEditingCategory.ModelsEndpoint}\n" +
                               $"Chat 端点: {chatResult.SuccessfulEndpoint}";

            StandardDialog.ShowInfo(resultMessage, "测试成功");
            GlobalToast.Success("连接成功", "API端点验证通过");
        }
        catch (TaskCanceledException)
        {
            TM.App.Log("[ModelManagement] Chat 重试测试超时");
            StandardDialog.ShowWarning("连接超时，请检查网络", "测试结果");
        }
        catch (HttpRequestException ex)
        {
            TM.App.Log($"[ModelManagement] Chat 重试测试网络错误: {ex.Message}");
            StandardDialog.ShowError($"网络错误：{ex.Message}", "连接失败");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] Chat 重试测试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败：{ex.Message}", "错误");
        }
    }

    private async Task RetryWithDropdownAsync()
    {
        try
        {
            if (!IsAutoRetryMode)
            {
                StandardDialog.ShowWarning("请切换到「获取模型重试」模式", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            if (SelectedChatRetryModel == null || string.IsNullOrWhiteSpace(SelectedChatRetryModel.Id))
            {
                StandardDialog.ShowWarning("请从下拉列表中选择一个模型", "提示");
                return;
            }

            var testModelId = SelectedChatRetryModel.Id;
            await ExecuteChatRetryTestAsync(testModelId);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 下拉重试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败：{ex.Message}", "错误");
        }
    }

    private async Task RetryWithManualAsync()
    {
        try
        {
            if (!IsManualRetryMode)
            {
                StandardDialog.ShowWarning("请切换到「指定模型重试」模式", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            var testModelId = RetryManualModelName?.Trim();
            if (string.IsNullOrWhiteSpace(testModelId))
            {
                StandardDialog.ShowWarning("请输入模型名称", "提示");
                return;
            }

            await ExecuteChatRetryTestAsync(testModelId);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 手动重试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败：{ex.Message}", "错误");
        }
    }

    private async Task ExecuteChatRetryTestAsync(string testModelId)
    {
        TM.App.Log($"[ModelManagement] 重试 Chat 测试: {testModelId}");
        GlobalToast.Info("重试测试", $"正在测试 Chat 端点（模型: {testModelId}）...\n注意：此测试会消耗极少量 token");

        var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);

        var chatResult = await _endpointTestService.TestChatEndpointAsync(
            candidates, FormApiKey, testModelId);

        if (!chatResult.Success)
        {
            TM.App.Log($"[ModelManagement] Chat 端点重试测试失败: {chatResult.ErrorMessage}");
            StandardDialog.ShowWarning($"Chat 端点测试失败：{chatResult.ErrorMessage}\n\n请尝试其他模型。", "测试结果");
            return;
        }

        TM.App.Log($"[ModelManagement] Chat 端点重试测试成功: {chatResult.SuccessfulEndpoint}");

        _currentEditingCategory!.ChatEndpoint = chatResult.SuccessfulEndpoint;
        _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
        _currentEditingCategory.EndpointSignature = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

        Service.UpdateCategory(_currentEditingCategory);
        _ = Task.Run(Service.SyncProvidersFromCategories);

        _chatTestFailed = false;
        ChatRetryModels.Clear();
        SelectedChatRetryModel = null;
        RefreshEndpointVerificationStatus();

        var resultMessage = $"✅ 端点测试通过！\n\n" +
                           $"Models 端点: {_currentEditingCategory.ModelsEndpoint}\n" +
                           $"Chat 端点: {chatResult.SuccessfulEndpoint}";

        StandardDialog.ShowInfo(resultMessage, "测试成功");
        GlobalToast.Success("连接成功", "API端点验证通过");
    }

    private async Task BatchCreateConfigurationsAsync(List<ModelInfo> models)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                StandardDialog.ShowWarning("请先选择所属分类", "提示");
                return;
            }

            var providerCategory = Service.GetAllCategories()
                .FirstOrDefault(c => c.Name == FormCategory && c.Level == 2);
            var providerLogoPath = providerCategory?.LogoPath 
                ?? TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(FormCategory);
            var providerIcon = providerCategory?.Icon ?? FormIcon ?? "🤖";

            GlobalToast.Info("批量创建", $"正在创建 {models.Count} 个模型配置...");

            var categoryName = FormCategory;
            var apiEndpoint = FormApiEndpoint;
            var apiKey = FormApiKey;

            var (configsToAdd, skipCount) = await Task.Run(() =>
            {
                var configs = new List<UserConfigurationData>();
                var existingModelNames = new HashSet<string>(
                    Service.GetAllData()
                        .Where(d => d.Category == categoryName)
                        .Select(d => d.ModelName));
                int skipped = 0;

                foreach (var model in models)
                {
                    if (existingModelNames.Contains(model.Id))
                    {
                        skipped++;
                        continue;
                    }

                    var modelLogoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(model.Id)
                        ?? providerLogoPath;

                    configs.Add(new UserConfigurationData
                    {
                        Name = model.Name,
                        ModelName = model.Id,
                        Icon = modelLogoPath ?? providerIcon,
                        Category = categoryName,
                        Description = model.Description ?? $"模型ID: {model.Id}",
                        IsEnabled = false,
                        ApiEndpoint = apiEndpoint,
                        ApiKey = apiKey,
                        MaxTokens = model.MaxTokens,
                        ContextLength = model.ContextLength > 0 ? model.ContextLength.ToString() : "",
                        Temperature = 0.7,
                        TopP = 1.0
                    });
                }

                return (configs, skipped);
            });

            int successCount = 0;
            if (configsToAdd.Count > 0)
            {
                _suppressTreeRefreshCount++;
                successCount = await Task.Run(() => Service.AddConfigurationsBatch(configsToAdd, categoryName));

                var providerNode = FindProviderNodeInTree(categoryName);
                if (providerNode != null)
                {
                    foreach (var config in configsToAdd)
                    {
                        var childNode = ConvertToTreeNode(config);
                        childNode.Level = providerNode.Level + 1;
                        providerNode.Children.Add(childNode);
                    }
                }
            }

            GlobalToast.Success("批量创建完成",
                $"成功创建 {successCount} 个配置，跳过 {skipCount} 个已存在的配置");
        }
        catch (Exception ex)
        {
            _suppressTreeRefreshCount = 0;
            TM.App.Log($"[ModelManagement] 批量创建配置失败: {ex.Message}");
            StandardDialog.ShowError($"批量创建配置失败：{ex.Message}", "错误");
        }
    }

    private ICommand? _saveProfilesCommand;
    public ICommand SaveProfilesCommand => _saveProfilesCommand ??= new RelayCommand(_ =>
    {
        try
        {
            Service.SaveParameterProfilesFromUI(ParameterProfiles);
            LoadParameterProfilesForUI();
            GlobalToast.Success("保存成功", "参数模板已更新");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 保存参数模板失败: {ex.Message}");
            GlobalToast.Error("保存失败", ex.Message);
        }
    });

    private ICommand? _applyProfileToAllModelsCommand;
    public ICommand ApplyProfileToAllModelsCommand => _applyProfileToAllModelsCommand ??= new RelayCommand(_ =>
    {
        try
        {
            if (_currentProvider == null)
            {
                GlobalToast.Warning("操作无效", "请先选择具体供应商或其模型配置");
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedProfileId))
            {
                GlobalToast.Warning("操作无效", "请先选择要应用的参数模板");
                return;
            }

            var providerName = _currentProvider.Name;
            var profileName = SelectedProfile?.Name ?? SelectedProfileId;

            var confirm = StandardDialog.ShowConfirm(
                $"确定要将参数模板「{profileName}」应用到供应商「{providerName}」下的所有模型吗？\n\n该操作会覆盖这些模型的参数配置字段（Temperature/MaxTokens/TopP等）。",
                "确认批量应用");

            if (!confirm) return;

            Service.ApplyProfileToAllModelsForProvider(_currentProvider, SelectedProfileId);

            if (_currentEditingData != null &&
                _currentProvider != null &&
                _currentEditingData.Category == _currentProvider.Name)
            {
                LoadDataToForm(_currentEditingData);
            }

            GlobalToast.Success("批量应用完成", $"已将模板「{profileName}」应用到供应商「{providerName}」的所有模型。");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 批量应用参数模板失败: {ex.Message}");
            GlobalToast.Error("批量应用失败", ex.Message);
        }
    });

    private async Task<List<ModelInfo>?> FetchModelsFromApiAsync(string apiEndpoint, string apiKey)
    {
        try
        {
            using var httpClient = _proxyService.CreateHttpClient(TimeSpan.FromSeconds(30));

            var url = apiEndpoint.TrimEnd('/') + "/models";
            TM.App.Log($"[ModelManagement] 请求URL: {url}");

            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://cherry-ai.com");
            request.Headers.TryAddWithoutValidation("X-Title", "Cherry Studio");
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");
            request.Headers.TryAddWithoutValidation("sec-ch-ua",
                "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"132\", \"Google Chrome\";v=\"132\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
            request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
            request.Headers.TryAddWithoutValidation("sec-fetch-site", "cross-site");
            request.Headers.TryAddWithoutValidation("Origin", "https://cherry-ai.com");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TM.App.Log($"[ModelManagement] API返回错误: {response.StatusCode}, {errorContent}");
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            TM.App.Log($"[ModelManagement] 收到响应，长度: {jsonContent.Length}字符");

            var trimmed = jsonContent.TrimStart();
            if (trimmed.StartsWith("<", StringComparison.Ordinal)
                || trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                TM.App.Log("[ModelManagement] 响应为 HTML 页面（被安全防护拦截）");
                throw new Exception("端点返回 HTML 页面（被安全防护拦截），请检查 URL 或代理设置");
            }

            var models = ParseOpenAIModelsResponse(jsonContent);

            return models;
        }
        catch (HttpRequestException ex)
        {
            TM.App.Log($"[ModelManagement] 网络请求失败: {ex.Message}");
            throw new Exception($"网络请求失败: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 获取模型API调用失败: {ex.Message}");
            throw;
        }
    }

    private List<ModelInfo> ParseOpenAIModelsResponse(string jsonContent)
    {
        var models = new List<ModelInfo>();

        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            if (root.TryGetProperty("data", out var dataArray))
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProperty))
                    {
                        var modelId = idProperty.GetString();
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            var modelInfo = new ModelInfo
                            {
                                Id = modelId,
                                Name = modelId
                            };

                            if (item.TryGetProperty("name", out var nameProperty))
                            {
                                var name = nameProperty.GetString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    modelInfo.Name = name;
                                }
                            }

                            if (item.TryGetProperty("description", out var descProperty))
                            {
                                modelInfo.Description = descProperty.GetString();
                            }

                            if (item.TryGetProperty("max_tokens", out var maxTokensProperty))
                            {
                                if (maxTokensProperty.ValueKind == JsonValueKind.Number)
                                    modelInfo.MaxTokens = maxTokensProperty.GetInt32();
                            }

                            if (item.TryGetProperty("context_length", out var contextLengthProperty))
                            {
                                if (contextLengthProperty.ValueKind == JsonValueKind.Number)
                                    modelInfo.ContextLength = contextLengthProperty.GetInt32();
                            }
                            else if (item.TryGetProperty("context_window", out var contextWindowProperty))
                            {
                                if (contextWindowProperty.ValueKind == JsonValueKind.Number)
                                    modelInfo.ContextLength = contextWindowProperty.GetInt32();
                            }
                            else if (item.TryGetProperty("max_context_length", out var maxContextProperty))
                            {
                                if (maxContextProperty.ValueKind == JsonValueKind.Number)
                                    modelInfo.ContextLength = maxContextProperty.GetInt32();
                            }

                            if (modelInfo.ContextLength <= 0)
                            {
                                modelInfo.ContextLength = 1000000;
                            }

                            if (modelInfo.MaxTokens <= 0)
                            {
                                modelInfo.MaxTokens = 128000;
                            }

                            models.Add(modelInfo);
                            TM.App.Log($"[ModelManagement] 解析模型: {modelInfo.Id}, ContextLength={modelInfo.ContextLength}");
                        }
                    }
                }
            }

            TM.App.Log($"[ModelManagement] 共解析 {models.Count} 个模型");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 解析JSON失败: {ex.Message}");
            throw new Exception($"解析模型列表失败: {ex.Message}", ex);
        }

        return models;
    }

    protected new void OnCategoryValueChanged(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return;

        Service.EnsureModelsLoadedForCategory(categoryName);

        var provider = Service.GetAllCategories()
            .FirstOrDefault(c => c.Name == categoryName && c.Level == 2);
        SetCurrentProvider(provider);
    }

    private void CollectCategoryAndChildren(string categoryName, List<string> result)
    {
        result.Add(categoryName);

        var childCategories = Service.GetAllCategories()
            .Where(c => c.ParentCategory == categoryName)
            .ToList();

        foreach (var child in childCategories)
        {
            CollectCategoryAndChildren(child.Name, result);
        }
    }

    private TreeNodeItem? FindProviderNodeInTree(string providerName)
    {
        foreach (var rootNode in TreeData)
        {
            var found = FindNodeByName(rootNode, providerName);
            if (found != null)
                return found;
        }
        return null;
    }

    private TreeNodeItem? FindNodeByName(TreeNodeItem node, string name)
    {
        if (node.Name == name)
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeByName(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
