using System;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ModelManagementViewModel
{

    public ICommand DisableWritingConfigModelCommand { get; private set; } = null!;
    public ICommand DisableAllWritingConfigModelsCommand { get; private set; } = null!;
    public ICommand ManualResetFallbackCommand { get; private set; } = null!;

    private readonly WritingSettingsService _writingSettingsService;
    private readonly ModelDisableCoordinator _disableCoordinator;
    private EventHandler? _writingSettingsChangedHandler;
    private bool _isLoadingSettings;

    private int _currentTabIndex;
    public int CurrentTabIndex
    {
        get => _currentTabIndex;
        set
        {
            if (_currentTabIndex == value) return;
            _currentTabIndex = value;
            OnPropertyChanged();
        }
    }

    public RangeObservableCollection<UserConfiguration> EnabledConfigs { get; } = new();

    private string? _selectedChatConfigId;
    private string? _selectedBackupChatConfigId;
    private UserConfiguration? _selectedChatConfig;
    private UserConfiguration? _selectedBackupChatConfig;
    public string? SelectedChatConfigId
    {
        get => _selectedChatConfigId;
        set
        {
            if (_selectedChatConfigId == value) return;
            _selectedChatConfigId = value;
            OnPropertyChanged();

            if (_isLoadingSettings || string.IsNullOrWhiteSpace(value)) return;
            var target = EnabledConfigs.FirstOrDefault(c => c.Id == value);
            if (target != null)
                _aiConfigurationService.SetActiveConfiguration(target);
        }
    }

    public UserConfiguration? SelectedChatConfig
    {
        get => _selectedChatConfig;
        set
        {
            if (ReferenceEquals(_selectedChatConfig, value)) return;
            _selectedChatConfig = value;
            _selectedChatConfigId = value?.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedChatConfigId));

            if (_isLoadingSettings || value == null) return;
            _aiConfigurationService.SetActiveConfiguration(value);
        }
    }

    private string? _selectedPolishConfigId;
    private UserConfiguration? _selectedPolishConfig;

    public string? SelectedBackupChatConfigId
    {
        get => _selectedBackupChatConfigId;
        set
        {
            if (_selectedBackupChatConfigId == value) return;
            _selectedBackupChatConfigId = value;
            OnPropertyChanged();
            if (!_isLoadingSettings) SaveWritingSettings();
        }
    }

    public UserConfiguration? SelectedBackupChatConfig
    {
        get => _selectedBackupChatConfig;
        set
        {
            if (ReferenceEquals(_selectedBackupChatConfig, value)) return;
            _selectedBackupChatConfig = value;
            _selectedBackupChatConfigId = value?.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedBackupChatConfigId));
            if (!_isLoadingSettings) SaveWritingSettings();
        }
    }

    public bool IsUsingBackup
    {
        get
        {
            try { return _writingApiRouter?.IsUsingBackup ?? false; }
            catch { return false; }
        }
    }

    public string FallbackStatusText
    {
        get
        {
            try
            {
                if (_writingApiRouter == null) return string.Empty;
                if (_writingApiRouter.IsUsingBackup)
                    return $"[注意] 备用API生效中（切换于 {_writingApiRouter.BackupActivatedAt?.ToString("HH:mm:ss")}）";
                return "主对话API正常";
            }
            catch { return string.Empty; }
        }
    }
    public string? SelectedPolishConfigId
    {
        get => _selectedPolishConfigId;
        set
        {
            if (_selectedPolishConfigId == value) return;
            _selectedPolishConfigId = value;
            OnPropertyChanged();
            if (!_isLoadingSettings) SaveWritingSettings();
        }
    }

    public UserConfiguration? SelectedPolishConfig
    {
        get => _selectedPolishConfig;
        set
        {
            if (ReferenceEquals(_selectedPolishConfig, value)) return;
            _selectedPolishConfig = value;
            _selectedPolishConfigId = value?.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPolishConfigId));
            if (!_isLoadingSettings) SaveWritingSettings();
        }
    }

    private void RefreshEnabledConfigs()
    {
        _isLoadingSettings = true;
        try
        {
            var allCfg = _aiConfigurationService.GetAllConfigurations();
            var enabled = allCfg.Where(c => c.IsEnabled).ToList();
            EnabledConfigs.ReplaceAll(enabled);
        }
        finally
        {
            _isLoadingSettings = false;
        }
        _writingSettingsService.NormalizeAgainstAvailableIds(EnabledConfigs.Select(c => c.Id));
        LoadWritingSettings();
    }

    private void LoadWritingSettings()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = _writingSettingsService.Settings;

            var chatId = _aiConfigurationService.GetActiveConfiguration()?.Id;
            _selectedChatConfigId = !string.IsNullOrWhiteSpace(chatId) && EnabledConfigs.Any(c => c.Id == chatId) ? chatId : null;
            _selectedChatConfig = !string.IsNullOrWhiteSpace(_selectedChatConfigId) ? EnabledConfigs.FirstOrDefault(c => c.Id == _selectedChatConfigId) : null;
            OnPropertyChanged(nameof(SelectedChatConfigId));
            OnPropertyChanged(nameof(SelectedChatConfig));

            var backupId = settings.BackupChatConfigId;
            _selectedBackupChatConfigId = !string.IsNullOrWhiteSpace(backupId) && EnabledConfigs.Any(c => c.Id == backupId) ? backupId : null;
            _selectedBackupChatConfig = !string.IsNullOrWhiteSpace(_selectedBackupChatConfigId) ? EnabledConfigs.FirstOrDefault(c => c.Id == _selectedBackupChatConfigId) : null;
            OnPropertyChanged(nameof(SelectedBackupChatConfigId));
            OnPropertyChanged(nameof(SelectedBackupChatConfig));

            var polishId = settings.PolishConfigId;
            _selectedPolishConfigId = !string.IsNullOrWhiteSpace(polishId) && EnabledConfigs.Any(c => c.Id == polishId) ? polishId : null;
            _selectedPolishConfig = !string.IsNullOrWhiteSpace(_selectedPolishConfigId) ? EnabledConfigs.FirstOrDefault(c => c.Id == _selectedPolishConfigId) : null;
            OnPropertyChanged(nameof(SelectedPolishConfigId));
            OnPropertyChanged(nameof(SelectedPolishConfig));
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveWritingSettings()
    {
        _writingSettingsService.Update(settings =>
        {
            settings.BackupChatConfigId = _selectedBackupChatConfigId;
            settings.PolishConfigId = _selectedPolishConfigId;
        });
    }

    public void DisableWritingConfigModel(UserConfiguration model)
    {
        if (model == null) return;
        try
        {
            _disableCoordinator.DisableSingle(model, "ModelManagementViewModel");
            RefreshTreeAndCategorySelection();
            UpdateBulkToggleState();
            RefreshEnabledConfigs();
            GlobalToast.Success("已禁用", $"模型 {model.Name} 已禁用");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagementViewModel] 禁用模型失败: {ex.Message}");
            GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
        }
    }

    public void DisableAllWritingConfigModels()
    {
        var models = EnabledConfigs.ToList();
        if (models.Count == 0) return;
        var confirm = StandardDialog.ShowConfirm(
            $"确定要禁用当前列表中全部 {models.Count} 个模型吗？\n禁用后可在模型管理中逐个重新启用。", "全部禁用");
        if (!confirm) return;
        var result = _disableCoordinator.DisableBatch(models, "ModelManagementViewModel");
        RefreshTreeAndCategorySelection();
        UpdateBulkToggleState();
        RefreshEnabledConfigs();
        GlobalToast.Success("已全部禁用", $"共禁用 {result.SuccessCount} 个模型，可在模型管理中重新启用");
    }

    public class ModelInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int MaxTokens { get; set; }
        public int ContextLength { get; set; }
        public string Provider { get; set; } = string.Empty;
        public bool SupportsReasoningEffort { get; set; }
        public System.Collections.Generic.List<string>? SupportedEffortLevels { get; set; }
        public bool SupportsThinking { get; set; }
        public bool SupportsVision { get; set; }
        public bool SupportsImageGeneration { get; set; }
        public bool SupportsTools { get; set; }
        public bool SupportsStreaming { get; set; }
        public bool CapabilitiesDetected { get; set; }
    }

    private void LoadParameterProfilesForUI()
    {
        var profiles = Service.GetAllParameterProfilesForUI();
        ParameterProfiles.ReplaceAll(profiles.ToList());

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
            LogScoped($"[ModelManagement] 初始化全局参数默认供应商失败: {ex.Message}");
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
            var oldChatEndpointForCache = _currentEditingCategory.ChatEndpoint;
            using (EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate()))
            {
                EndpointTestService.InvalidateProbeCache(oldChatEndpointForCache);
            }

            try
            {
                var providerId = _currentEditingCategory.Id;
                var providerName = _currentEditingCategory.Name;
                var affectedModels = Service.GetAllData()
                    .Where(d => string.Equals(d.CategoryId, providerId, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(d.Category, providerName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var aiSvcForSync = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
                foreach (var data in affectedModels)
                {
                    if (!string.IsNullOrWhiteSpace(data.ModelName))
                    {
                        TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearDiscoveredLimits(
                            data.ModelName, oldChatEndpointForCache, providerId);
                    }

                    if (data.CapabilitiesDetected)
                    {
                        data.CapabilitiesDetected = false;
                        data.ThinkingPassthrough = null;
                        Service.UpdateConfiguration(data);

                        try
                        {
                            var config = aiSvcForSync.GetAllConfigurations()
                                .FirstOrDefault(c => string.Equals(c.ModelId, data.ModelName, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(c.ProviderId, data.CategoryId, StringComparison.OrdinalIgnoreCase));
                            if (config != null && (config.CapabilitiesDetected || config.ThinkingPassthrough != null))
                            {
                                config.CapabilitiesDetected = false;
                                config.ThinkingPassthrough = null;
                                aiSvcForSync.UpdateConfiguration(config);
                            }
                        }
                        catch (Exception syncEx)
                        {
                            LogScoped($"[ModelManagement] 端点签名变化同步AIService失败: {syncEx.Message}");
                        }
                    }
                }

                if (affectedModels.Count > 0)
                    LogScoped($"[ModelManagement] 端点签名变化已清空能力快照: provider={providerName}, models={affectedModels.Count}");
            }
            catch (Exception ex)
            {
                LogScoped($"[ModelManagement] 清空能力快照失败: {ex.Message}");
            }

            _currentEditingCategory.ModelsEndpoint = null;
            _currentEditingCategory.ChatEndpoint = null;
            _currentEditingCategory.EndpointVerifiedAt = null;
            _currentEditingCategory.EndpointSignature = newSignature;

            Service.SaveAllCategories();

            LogScoped($"[ModelManagement] 端点配置变更，已清空验证状态并持久化: OldSignature={oldSignature}, NewSignature={newSignature}");
            GlobalToast.Warning("配置已变更", "端点或密钥已修改，请重新测试连接");
        }
    }

}
