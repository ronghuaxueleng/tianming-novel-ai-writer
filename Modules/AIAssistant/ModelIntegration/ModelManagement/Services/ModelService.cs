using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Services.Framework.AI.Core;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class ModelService : ModuleServiceBase<AIProviderCategory, UserConfigurationData>
{
    public event EventHandler? ConfigurationsChanged;

    private void RaiseConfigurationsChanged()
    {
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void LogIfPublicProviderId(string? providerId, string message)
    {
        if (AIProviderCategoryLogHelper.IsTianmingPrivateId(providerId)) return;
        TM.App.Log(message);
    }

    private static string GetProviderDisplayNameForUi(AIProviderCategory? provider, string providerName)
        => provider.IsTianmingPrivate() ? "内置服务商" : providerName;

    private readonly Dictionary<string, List<UserConfigurationData>> _providerModelsCache = new();

    private readonly Dictionary<string, System.Threading.Tasks.Task> _saveModelQueueByKey = new();
    private readonly Dictionary<string, int> _saveModelVersionByKey = new();
    private readonly object _saveModelQueueLock = new();

    private readonly Dictionary<string, ParameterProfile> _parameterProfiles = new();

    private readonly Dictionary<string, string> _providerDefaultProfileIds = new();

    private readonly string _categoriesFilePath;
    private readonly string _configDataFilePath;
    private readonly string _providerModelsRoot;
    private readonly string _parameterProfilesFilePath;

    private const string DefaultProfileId = "default";

    public ModelService()
        : base(
            modulePath: "AIAssistant/ModelIntegration/ModelManagement",
            categoriesFileName: "categories.json",
            dataFileName: "user_configurations.json",
            delayDataLoading: true)
    {
        _categoriesFilePath = StoragePathHelper.GetFilePath("Services", "AI/Library", "categories.json");
        _configDataFilePath = StoragePathHelper.GetFilePath("Services", "AI/Library", "user_configurations.json");
        _providerModelsRoot = StoragePathHelper.GetFilePath("Services", "AI/Library", "ProviderModels");
        _parameterProfilesFilePath = StoragePathHelper.GetFilePath("Services", "AI/Library", "parameter-profiles.json");

        OverrideCategoriesFile(_categoriesFilePath);
        OverrideBuiltInCategoriesFile(StoragePathHelper.GetFilePath("Services", "AI/Library", "built_in_categories.json"));
        OverrideDataFile(_configDataFilePath);

        SetStorageStrategy(new SingleFileStorage<UserConfigurationData>(_configDataFilePath));
    }

    protected override string GetBuiltInCategoriesResourceMarker()
    {
        return ".Services.Framework.AI.Library.Resources.built_in_categories.json";
    }

    protected override async System.Threading.Tasks.Task OnInitializedAsync()
    {
        await LoadParameterProfilesAsync().ConfigureAwait(false);
        SyncKeyPoolsToRotationService();
        await EnsureProvidersFileExistsAsync().ConfigureAwait(false);
        await LoadProvidersFromJsonAsync().ConfigureAwait(false);
        _providerModelsCache.Clear();
        DataItems.Clear();
        await LoadAllProviderModelsAsync().ConfigureAwait(false);
        ReconcileKeyExhaustedProviders();
        SubscribeRuntimeCapabilityFeedback();
        RaiseConfigurationsChanged();
    }

    private bool _runtimeFeedbackSubscribed;

    private void SubscribeRuntimeCapabilityFeedback()
    {
        if (_runtimeFeedbackSubscribed) return;
        _runtimeFeedbackSubscribed = true;
        TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.UnsupportedParamMarked +=
            OnRuntimeUnsupportedParamMarked;
    }

    private void OnRuntimeUnsupportedParamMarked(string? providerId, string? endpoint, string modelId, string paramName)
    {
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(paramName))
            return;

        try
        {
            var p = paramName.ToLowerInvariant();
            bool affectReasoning = p == "reasoning_effort" || p == "reasoning"
                                 || p == "include_reasoning" || p == "reasoning_max_tokens"
                                 || p == "reasoning.effort"
                                 || p == "effort";
            bool affectThinking = p.Contains("thinking") || p == "enable_thinking" || p == "budget_tokens";
            bool affectTools = p == "tools" || p == "tool_choice"
                            || p == "functions" || p == "function_call";
            bool affectStreaming = p == "stream" || p == "stream_options" || p == "streaming";
            bool affectLongContext = p == "long_context";

            if (!affectReasoning && !affectThinking && !affectTools && !affectStreaming && !affectLongContext)
                return;

            var matches = DataItems.Where(d =>
                string.Equals(d.ModelName, modelId, StringComparison.OrdinalIgnoreCase) &&
                ((!string.IsNullOrWhiteSpace(providerId) && string.Equals(d.CategoryId, providerId, StringComparison.OrdinalIgnoreCase))
                 || (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(d.ApiEndpoint)
                     && (d.ApiEndpoint.StartsWith(endpoint!, StringComparison.OrdinalIgnoreCase)
                         || endpoint!.StartsWith(d.ApiEndpoint, StringComparison.OrdinalIgnoreCase)))))
                .ToList();

            if (matches.Count == 0) return;

            bool anyChanged = false;
            foreach (var data in matches)
            {
                bool changed = false;
                if (affectReasoning)
                {
                    if (data.SupportsReasoningEffort)
                    {
                        data.SupportsReasoningEffort = false;
                        changed = true;
                    }
                    if (data.SupportedEffortLevels != null)
                    {
                        data.SupportedEffortLevels = null;
                        changed = true;
                    }
                }
                if (affectThinking && data.SupportsThinking)
                {
                    data.SupportsThinking = false;
                    if (data.ThinkingPassthrough != false) data.ThinkingPassthrough = false;
                    changed = true;
                }
                if (affectTools && data.SupportsTools)
                {
                    data.SupportsTools = false;
                    changed = true;
                }
                if (affectStreaming && data.SupportsStreaming)
                {
                    data.SupportsStreaming = false;
                    changed = true;
                }
                if (affectLongContext)
                {
                    if (data.SupportsLongContext) { data.SupportsLongContext = false; changed = true; }
                    if (data.EnableLongContext == true) { data.EnableLongContext = null; changed = true; }
                }

                if (changed)
                {
                    data.CapabilitiesDetected = true;
                    UpdateConfiguration(data);
                    anyChanged = true;
                    LogIfPublicProviderId(data.CategoryId, $"[ModelService] 运行时纠错回写: {data.Name}({data.ModelName}) 参数 '{paramName}' 不被支持");

                    try
                    {
                        var aiSvc = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
                        var config = aiSvc.GetAllConfigurations()
                            .FirstOrDefault(c => string.Equals(c.ModelId, data.ModelName, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(c.ProviderId, data.CategoryId, StringComparison.OrdinalIgnoreCase));
                        if (config != null)
                        {
                            bool cfgDirty = false;
                            if (affectReasoning)
                            {
                                if (config.SupportsReasoningEffort) { config.SupportsReasoningEffort = false; cfgDirty = true; }
                                if (config.SupportedEffortLevels != null) { config.SupportedEffortLevels = null; cfgDirty = true; }
                            }
                            if (affectThinking && config.SupportsThinking) { config.SupportsThinking = false; config.ThinkingPassthrough = false; cfgDirty = true; }
                            if (affectLongContext)
                            {
                                if (config.SupportsLongContext) { config.SupportsLongContext = false; cfgDirty = true; }
                                if (config.EnableLongContext == true) { config.EnableLongContext = null; cfgDirty = true; }
                            }
                            if (!config.CapabilitiesDetected) { config.CapabilitiesDetected = true; cfgDirty = true; }
                            if (cfgDirty)
                                aiSvc.UpdateConfiguration(config);
                        }
                    }
                    catch (Exception syncEx)
                    {
                        LogIfPublicProviderId(data.CategoryId, $"[ModelService] 运行时纠错同步到AIService失败: {syncEx.Message}");
                    }
                }
            }

            if (anyChanged)
                RaiseConfigurationsChanged();
        }
        catch (Exception ex)
        {
            LogIfPublicProviderId(providerId, $"[ModelService] OnRuntimeUnsupportedParamMarked 处理失败: {ex.Message}");
        }
    }
    private void ReconcileKeyExhaustedProviders()
    {
        try
        {
            var exhaustedProviders = Categories
                .Where(c => c.Level == 2 && c.IsKeyExhausted)
                .ToList();

            if (exhaustedProviders.Count == 0) return;

            foreach (var provider in exhaustedProviders)
            {
                var hasActiveKey = provider.ApiKeys?.Any(k => k.IsEnabled && !string.IsNullOrWhiteSpace(k.Key)) == true;
                if (!hasActiveKey)
                {
                    provider.LogIfPublic($"[ModelService] 启动对账: {provider.Name}({provider.Id}) 仍无可用Key，保持禁用");
                    continue;
                }

                provider.IsKeyExhausted = false;

                var modelEnabledCount = 0;
                var models = GetModelsForProvider(provider).ToList();
                foreach (var model in models)
                {
                    if (!model.IsEnabled && model.AutoDisabledBySystem)
                    {
                        model.IsEnabled = true;
                        model.AutoDisabledBySystem = false;
                        modelEnabledCount++;
                    }
                }
                if (modelEnabledCount > 0)
                    SaveModelsForProvider(provider, models);

                var aiService = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
                aiService.EnableConfigurationsByProvider(provider.Id);

                provider.LogIfPublic($"[ModelService] 启动对账: {provider.Name}({provider.Id}) 密钥已恢复，自动启用 {modelEnabledCount} 个模型");
            }

            if (exhaustedProviders.Any(p => !p.IsKeyExhausted))
                SaveAllCategories();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 启动对账失败: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task LoadAllProviderModelsAsync()
    {
        try
        {
            var providers = GetAllCategories()
                .Where(c => c.Level == 2)
                .ToList();

            foreach (var provider in providers)
            {
                var models = await GetModelsForProviderAsync(provider).ConfigureAwait(false);
                if (models.Count > 0)
                {
                    var providerKey = GetProviderKey(provider);
                    if (InfoLogDedup.ShouldLog($"ModelService:StartupLoad:{providerKey}"))
                    {
                        provider.LogIfPublic($"[ModelService] 启动时加载供应商 '{provider.Name}' 模型 {models.Count} 条");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 启动时加载供应商模型失败: {ex.Message}");
        }
    }

    private bool _keyStateHandlerRegistered;

    public void SyncKeyPoolsToRotationService()
    {
        try
        {
            var rotation = ServiceLocator.Get<ApiKeyRotationService>();

            if (!_keyStateHandlerRegistered)
            {
                rotation.KeyStateChanged += OnKeyStatePersistRequired;
                rotation.ProviderExhausted += OnProviderExhausted;
                rotation.ProviderRecovered += OnProviderRecovered;
                _keyStateHandlerRegistered = true;
            }

            foreach (var cat in Categories)
            {
                if (cat.Level != 2) continue;
                if (string.IsNullOrWhiteSpace(cat.Id)) continue;
                PushProviderPoolToRotation(rotation, cat);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 同步密钥池到轮询服务失败: {ex.Message}");
        }
    }

    private void PushProviderPoolToRotation(ApiKeyRotationService rotation, AIProviderCategory provider)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.Id)) return;

        if (provider.ApiKeys == null || provider.ApiKeys.Count == 0)
        {
            rotation.UpdateKeyPool(provider.Id, new List<TM.Services.Framework.AI.Core.ApiKeyEntry>());
            return;
        }

        var decryptedKeys = provider.ApiKeys.Select(k => new TM.Services.Framework.AI.Core.ApiKeyEntry
        {
            Id = k.Id,
            Key = TM.Framework.Common.Helpers.LocalKeyProtector.TryUnprotect(k.Key) ?? k.Key,
            Remark = k.Remark,
            IsEnabled = k.IsEnabled,
            CreatedAt = k.CreatedAt
        }).ToList();
        rotation.UpdateKeyPool(provider.Id, decryptedKeys);
    }

    private void OnKeyStatePersistRequired(string providerId, string keyId)
    {
        try
        {
            var provider = Categories.FirstOrDefault(c =>
                c.Level == 2 && string.Equals(c.Id, providerId, StringComparison.OrdinalIgnoreCase));
            if (provider?.ApiKeys == null)
            {
                LogIfPublicProviderId(providerId, $"[ModelService] 密钥状态变更但未找到 provider: providerId={providerId}, keyId={keyId}");
                return;
            }

            var entry = provider.ApiKeys.FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
            if (entry == null)
            {
                LogIfPublicProviderId(providerId, $"[ModelService] 密钥状态变更但未找到 keyId: providerId={providerId}, keyId={keyId}");
                return;
            }

            if (!entry.IsEnabled)
            {
                return;
            }

            entry.IsEnabled = false;
            SaveAllCategories();

            try
            {
                var rotation = ServiceLocator.Get<ApiKeyRotationService>();
                PushProviderPoolToRotation(rotation, provider);
            }
            catch (Exception syncEx)
            {
                provider.LogIfPublic($"[ModelService] 密钥禁用后收敛 rotation 池失败: {syncEx.Message}");
            }

            LogIfPublicProviderId(providerId, $"[ModelService] 密钥已永久禁用并持久化: providerId={providerId}, keyId={keyId}");
        }
        catch (Exception ex)
        {
            LogIfPublicProviderId(providerId, $"[ModelService] 保存密钥状态失败: {ex.Message}");
        }
    }

    private void OnProviderExhausted(string providerId)
    {
        try
        {
            var provider = Categories.FirstOrDefault(c =>
                c.Level == 2 && string.Equals(c.Id, providerId, StringComparison.OrdinalIgnoreCase));
            var providerName = provider?.Name ?? providerId;
            var providerDisplayName = GetProviderDisplayNameForUi(provider, providerName);

            var modelDisabledCount = 0;
            if (provider != null)
            {
                provider.IsKeyExhausted = true;
                SaveAllCategories();

                var models = GetModelsForProvider(provider).ToList();
                foreach (var model in models)
                {
                    if (model.IsEnabled)
                    {
                        model.IsEnabled = false;
                        model.IsActive = false;
                        model.AutoDisabledBySystem = true;
                        modelDisabledCount++;
                    }
                }
                SaveModelsForProvider(provider, models);
            }

            var aiService = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
            aiService.DisableConfigurationsByProvider(providerId);

            RaiseConfigurationsChanged();
            provider.LogIfPublic(
                $"[ModelService] 服务商所有Key耗尽，已批量禁用模型并触发刷新: {providerName}({providerId}) DisabledModels={modelDisabledCount}");
            Application.Current?.Dispatcher.BeginInvoke(() =>
                GlobalToast.Error($"{providerDisplayName} API Key 全部失效",
                    $"该服务商所有 API Key 均已永久失效，相关模型已自动禁用。\n添加新 Key 后模型将自动恢复启用。"));
        }
        catch (Exception ex)
        {
            LogIfPublicProviderId(providerId, $"[ModelService] 服务商Key耗尽处理失败: {ex.Message}");
        }
    }

    private void OnProviderRecovered(string providerId)
    {
        try
        {
            var provider = Categories.FirstOrDefault(c =>
                c.Level == 2 && string.Equals(c.Id, providerId, StringComparison.OrdinalIgnoreCase));
            var providerName = provider?.Name ?? providerId;
            var providerDisplayName = GetProviderDisplayNameForUi(provider, providerName);

            if (provider == null || !provider.IsKeyExhausted)
            {
                LogIfPublicProviderId(providerId, $"[ModelService] 服务商密钥恢复但未标记 IsKeyExhausted，跳过自动恢复: {providerName}({providerId})");
                return;
            }

            provider.IsKeyExhausted = false;
            SaveAllCategories();

            var modelEnabledCount = 0;
            var models = GetModelsForProvider(provider).ToList();
            foreach (var model in models)
            {
                if (!model.IsEnabled && model.AutoDisabledBySystem)
                {
                    model.IsEnabled = true;
                    model.AutoDisabledBySystem = false;
                    modelEnabledCount++;
                }
            }
            if (modelEnabledCount > 0)
                SaveModelsForProvider(provider, models);

            var aiService = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
            aiService.EnableConfigurationsByProvider(providerId);

            RaiseConfigurationsChanged();
            provider.LogIfPublic(
                $"[ModelService] 服务商密钥恢复，已自动重新启用模型: {providerName}({providerId}) EnabledModels={modelEnabledCount}");
            if (modelEnabledCount > 0)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    GlobalToast.Success($"{providerDisplayName} 密钥已恢复",
                        $"该服务商有新的可用 Key，{modelEnabledCount} 个模型已自动重新启用。"));
            }
        }
        catch (Exception ex)
        {
            LogIfPublicProviderId(providerId, $"[ModelService] 服务商密钥恢复处理失败: {ex.Message}");
        }
    }

    protected override int OnBeforeDeleteData(string dataId)
    {
        return DataItems.RemoveAll(d => d.Id == dataId);
    }

    protected override List<AIProviderCategory> CreateDefaultCategories()
    {
        return new List<AIProviderCategory>();
    }

    private async Task EnsureProvidersFileExistsAsync()
    {
        var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");
        if (!File.Exists(providersFile))
        {
            await SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
        }
    }
}

public class ParameterProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 0;
    public double TopP { get; set; } = 1.0;
    public double FrequencyPenalty { get; set; } = 0.1;
    public double PresencePenalty { get; set; } = 0.0;
    public int RateLimitRPM { get; set; } = 0;
    public int RateLimitTPM { get; set; } = 0;
    public int MaxConcurrency { get; set; } = 5;
    public string Seed { get; set; } = string.Empty;
    public string StopSequences { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public string ReasoningEffort { get; set; } = string.Empty;
    public bool? ThinkingEnabled { get; set; }
}
