using System;
using System.Linq;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 模型管理

        public void RefreshModelConfigurations()
        {
            _isRefreshingConfigs = true;
            try
            {
                var newConfigs = _aiService.GetAllConfigurations()
                    .Where(c => c.IsEnabled).ToList();
                ModelConfigurations.ReplaceAll(newConfigs);
            }
            finally
            {
                _isRefreshingConfigs = false;
            }
            RefreshCachedActiveConfig();
            _cachedEndpointConfigs = null;
            OnPropertyChanged(nameof(ActiveConfiguration));
            OnPropertyChanged(nameof(ActiveConfigurationId));
            OnPropertyChanged(nameof(ShowThinkingToggle));
            OnPropertyChanged(nameof(ShowEffortDropdown));
            OnPropertyChanged(nameof(QuickThinkingEnabled));
            OnPropertyChanged(nameof(AvailableThinkingEfforts));
            OnPropertyChanged(nameof(QuickReasoningEffort));
            OnPropertyChanged(nameof(ShowLongContextSwitch));
            OnPropertyChanged(nameof(EnableLongContext));
            OnPropertyChanged(nameof(WritingBackupChatConfigId));
            OnPropertyChanged(nameof(WritingBackupChatConfiguration));
            OnPropertyChanged(nameof(WritingPolishConfigId));
            OnPropertyChanged(nameof(WritingPolishConfiguration));
            OnPropertyChanged(nameof(WritingEndpointConfigs));
        }

        public string? WritingBackupChatConfigId
        {
            get
            {
                try { return _writingSettings.GetBackupChatConfigId(); }
                catch { return null; }
            }
            set
            {
                if (_isRefreshingConfigs) return;
                try
                {
                    _writingSettings.Update(s => s.BackupChatConfigId = value);
                    _cachedEndpointConfigs = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WritingBackupChatConfiguration));
                    OnPropertyChanged(nameof(WritingEndpointConfigs));
                }
                catch { }
            }
        }

        public UserConfiguration? WritingBackupChatConfiguration
        {
            get
            {
                var id = WritingBackupChatConfigId;
                return string.IsNullOrWhiteSpace(id) ? null : ModelConfigurations.FirstOrDefault(c => c.Id == id);
            }
            set
            {
                if (_isRefreshingConfigs) return;
                try
                {
                    _writingSettings.Update(s => s.BackupChatConfigId = value?.Id);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WritingBackupChatConfigId));
                }
                catch { }
            }
        }

        public bool IsWritingFallbackActive
        {
            get
            {
                EnsureWritingRouterSubscribed();
                try { return _writingApiRouter.IsUsingBackup; }
                catch { return false; }
            }
        }

        public string? WritingPolishConfigId
        {
            get
            {
                try { return _writingSettings.GetPolishConfigId(); }
                catch { return null; }
            }
            set
            {
                if (_isRefreshingConfigs) return;
                try
                {
                    _writingSettings.Update(s => s.PolishConfigId = value);
                    _cachedEndpointConfigs = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WritingPolishConfiguration));
                    OnPropertyChanged(nameof(WritingEndpointConfigs));
                }
                catch { }
            }
        }

        public UserConfiguration? WritingPolishConfiguration
        {
            get
            {
                var id = WritingPolishConfigId;
                return string.IsNullOrWhiteSpace(id) ? null : ModelConfigurations.FirstOrDefault(c => c.Id == id);
            }
            set
            {
                if (_isRefreshingConfigs) return;
                try
                {
                    _writingSettings.Update(s => s.PolishConfigId = value?.Id);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WritingPolishConfigId));
                }
                catch { }
            }
        }

        private bool _writingRouterSubscribed;
        private void EnsureWritingRouterSubscribed()
        {
            if (_writingRouterSubscribed) return;
            _writingRouterSubscribed = true;
            try
            {
                _writingApiRouter.StatusChanged += (_, _) =>
                    OnPropertyChanged(nameof(IsWritingFallbackActive));
            }
            catch { }
        }

        public void SetQuickReasoningEffort(string value)
        {
            var config = QuickParamEffectiveConfig;
            if (config == null) return;
            value ??= string.Empty;
            if (string.Equals(config.ReasoningEffort, value, StringComparison.OrdinalIgnoreCase)) return;
            config.ReasoningEffort = value;
            _isSavingQuickConfig = true;
            try
            {
                _aiService.UpdateConfiguration(config);
                SyncQuickParamsToModelService(config);
                TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                    config.ProviderId, config.CustomEndpoint, config.ModelId);
                TM.App.Log($"[SKConversationViewModel] 快捷推理强度: {value}, model={config.ModelId}");
            }
            finally { _isSavingQuickConfig = false; }
        }

        public void SetQuickThinkingEnabled(bool? value)
        {
            var config = QuickParamEffectiveConfig;
            if (config == null) return;
            if (config.ThinkingEnabled == value) return;
            config.ThinkingEnabled = value;
            _isSavingQuickConfig = true;
            try
            {
                _aiService.UpdateConfiguration(config);
                SyncQuickParamsToModelService(config);
                TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                    config.ProviderId, config.CustomEndpoint, config.ModelId);
                TM.App.Log($"[SKConversationViewModel] 快捷思考开关: {(value == true ? "Enabled" : "Default")}, model={config.ModelId}");
            }
            finally { _isSavingQuickConfig = false; }
        }

        public void SetQuickEnableLongContext(bool? value)
        {
            var config = QuickParamEffectiveConfig;
            if (config == null) return;
            if (config.EnableLongContext == value) return;
            config.EnableLongContext = value;
            _isSavingQuickConfig = true;
            try
            {
                _aiService.UpdateConfiguration(config);
                SyncQuickParamsToModelService(config);
                TM.App.Log($"[SKConversationViewModel] 快捷 1M 开关: {(value == true ? "Enabled" : "Default")}, model={config.ModelId}");
            }
            finally { _isSavingQuickConfig = false; }
        }

        private void SyncQuickParamsToModelService(UserConfiguration config)
        {
            try
            {
                var allData = _modelService.GetAllData();
                var data = allData.FirstOrDefault(d =>
                    string.Equals(d.CategoryId, config.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.ModelName, config.ModelId, StringComparison.OrdinalIgnoreCase))
                    ?? allData.FirstOrDefault(d =>
                        string.Equals(d.Name, config.Name, StringComparison.OrdinalIgnoreCase));

                if (data == null) return;

                data.ReasoningEffort = config.ReasoningEffort;
                data.ThinkingEnabled = config.ThinkingEnabled;
                data.EnableLongContext = config.EnableLongContext;
                _modelService.UpdateConfiguration(data);
                TM.App.Log($"[SKConversationViewModel] 推理参数已同步回ModelService: model={data.Name}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 同步推理参数到ModelService失败: {ex.Message}");
            }
        }

        public void DeleteModel(UserConfiguration model)
        {
            if (model == null) return;
            try
            {
                var newActive = _disableCoordinator.DisableSingle(model, "SKConversationViewModel");
                if (newActive == null)
                {
                    RefreshModelConfigurations();
                    OnPropertyChanged(nameof(ActiveConfiguration));
                    OnPropertyChanged(nameof(ActiveConfigurationId));
                }
                GlobalToast.Success("已禁用", $"模型 {model.Name} 已禁用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 禁用模型失败: {ex.Message}");
                GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
            }
        }

        public void DisableAllModels()
        {
            var allModels = ModelConfigurations.ToList();
            if (allModels.Count == 0) return;
            try
            {
                var result = _disableCoordinator.DisableBatch(allModels, "SKConversationViewModel");
                RefreshModelConfigurations();
                OnPropertyChanged(nameof(ActiveConfiguration));
                OnPropertyChanged(nameof(ActiveConfigurationId));
                GlobalToast.Success("已全部禁用", $"共禁用 {result.SuccessCount} 个模型，可在模型管理中重新启用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 全部禁用失败: {ex.Message}");
                GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
            }
        }

        public void SetActiveConfiguration(UserConfiguration config)
        {
            _aiService.SetActiveConfiguration(config.Id);
            OnPropertyChanged(nameof(ActiveConfiguration));
            OnPropertyChanged(nameof(ActiveConfigurationId));
            RefreshContextUsage();
        }

        #endregion
    }
}
