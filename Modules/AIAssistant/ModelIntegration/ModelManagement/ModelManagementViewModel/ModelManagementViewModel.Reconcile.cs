using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.SemanticKernel.Discovery;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ModelManagementViewModel
{
    private string? _activeEnableDataId;

    private static bool ShouldKeepReasoningEffortForSync(UserConfigurationData data, string modelId, string providerId, string endpoint)
    {
        if (data.SupportsReasoningEffort) return true;
        if ((endpoint ?? string.Empty).Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
            return ReasoningCapableByName(modelId, providerId);

        var m = (modelId ?? string.Empty).ToLowerInvariant();
        return IsReasoningEffortOnlyModel(m);
    }

    private void UpdateDataCore()
    {
        if (_currentEditingData == null)
            return;

        UpdateDataFromForm(_currentEditingData);
        Service.UpdateConfiguration(_currentEditingData);
        GlobalToast.Success("保存成功", $"模型配置「{_currentEditingData.Name}」已更新");

        if (_currentEditingData.IsEnabled)
        {
            _ = SyncToAIServiceAndActivateAsync(_currentEditingData);
        }
        else
        {
            SyncDataDisabledToAIService(_currentEditingData);
        }

        _ = Task.Run(async () =>
        {
            try { await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogScoped($"[ModelManagement] 重载模型库失败: {ex.Message}"); }
        });
    }

    private void SyncDataDisabledToAIService(UserConfigurationData data)
    {
        if (data == null) return;

        if (string.Equals(_activeEnableDataId, data.Id, StringComparison.Ordinal))
        {
            _activeEnableDataId = null;
        }

        var aiConfigs = _aiConfigurationService.GetAllConfigurations();
        var matching = aiConfigs.FirstOrDefault(c =>
            string.Equals(c.ModelId, data.ModelName, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(c.ProviderId, data.CategoryId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.Name, data.Name, StringComparison.OrdinalIgnoreCase)));
        if (matching != null && matching.IsEnabled)
        {
            _disableCoordinator.DisableSingle(matching, "ModelManagement.SyncDisabled");
        }
    }

    protected override void OnDataEnabledChanged(UserConfigurationData data, bool isEnabled)
    {
        base.OnDataEnabledChanged(data, isEnabled);
        if (isEnabled)
            return;

        data.AutoDisabledBySystem = false;
        Service.UpdateConfiguration(data);
        SyncDataDisabledToAIService(data);
    }

    protected override bool RequiresAsyncEnableVerification => true;

    protected override void OnDataEnableRequested(UserConfigurationData data)
    {
        if (data == null)
            return;

        data.AutoDisabledBySystem = false;
        Service.UpdateConfiguration(data);

        _activeEnableDataId = data.Id;

        _ = SyncToAIServiceAndActivateAsync(data, startFromDisabled: true);
    }

    protected override void OnBulkToggleCompleted(bool newEnabled)
    {
        base.OnBulkToggleCompleted(newEnabled);

        if (!newEnabled)
        {
            _activeEnableDataId = null;
        }

        _suppressAiConfigurationsChanged = true;
        try
        {
            ReconcileAIServiceWithModelService();
        }
        finally
        {
            _suppressAiConfigurationsChanged = false;
        }
        Application.Current?.Dispatcher.InvokeAsync(RefreshEnabledConfigs);
    }

    private void ReconcileAIServiceWithModelService()
    {
        var modelServiceData = Service.GetAllData().ToList();
        var aiConfigs = _aiConfigurationService.GetAllConfigurations();

        var modelLookup = modelServiceData
            .GroupBy(d => d.ModelName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        UserConfiguration? lastModified = null;
        UserConfiguration? activeButDisabled = null;
        var now = DateTime.Now;
        foreach (var aiConfig in aiConfigs)
        {
            UserConfigurationData? msData = null;
            if (modelLookup.TryGetValue(aiConfig.ModelId ?? "", out var candidates))
            {
                msData = candidates.FirstOrDefault(d =>
                    string.Equals(d.CategoryId, aiConfig.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Name, aiConfig.Name, StringComparison.OrdinalIgnoreCase));
            }
            if (msData != null && msData.IsEnabled != aiConfig.IsEnabled)
            {
                aiConfig.IsEnabled = msData.IsEnabled;
                aiConfig.UpdatedAt = now;
                aiConfig.AutoDisabledBySystem = false;
                lastModified = aiConfig;

                if (!aiConfig.IsEnabled && aiConfig.IsActive)
                    activeButDisabled = aiConfig;
            }
        }
        if (activeButDisabled != null)
            _aiConfigurationService.UpdateConfiguration(activeButDisabled);
        else if (lastModified != null)
            _aiConfigurationService.UpdateConfiguration(lastModified);
    }

    private async Task SyncToAIServiceAndActivateAsync(UserConfigurationData data, bool startFromDisabled = false)
    {
        if (data == null)
            return;

        if (!data.IsEnabled && !startFromDisabled)
            return;

        try
        {
            var providers = _aiLibraryService.GetAllProviders();
            var provider = providers.FirstOrDefault(p =>
                string.Equals(p.Name, data.Category, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, data.ProviderName, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                LogScoped($"[ModelManagement] 同步到AIService失败：未找到供应商，Category={data.Category}, ProviderName={data.ProviderName}");
                GlobalToast.Warning("模型未同步到对话", $"未找到供应商：{data.Category}");
                return;
            }

            var providerCategory = Service.GetAllCategories()
                .FirstOrDefault(c => c.Level == 2 && string.Equals(c.Name, data.Category, StringComparison.OrdinalIgnoreCase));

            if (providerCategory == null ||
                string.IsNullOrWhiteSpace(providerCategory.ModelsEndpoint) ||
                string.IsNullOrWhiteSpace(providerCategory.ChatEndpoint))
            {
                var maskEndpointForUnverified = providerCategory.IsTianmingPrivate();
                var modelsEpDisp = maskEndpointForUnverified ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedEndpointLabel : (providerCategory?.ModelsEndpoint ?? string.Empty);
                var chatEpDisp = maskEndpointForUnverified ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedEndpointLabel : (providerCategory?.ChatEndpoint ?? string.Empty);
                LogScoped($"[ModelManagement] 禁止激活：供应商端点未验证，Category={data.Category}, ModelsEndpoint={modelsEpDisp}, ChatEndpoint={chatEpDisp}");
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
                    LogScoped($"[ModelManagement] 同步到AIService失败：模型名称为空，无法构建配置");
                    GlobalToast.Warning("模型未同步到对话", "模型名称为空，无法激活");
                    return;
                }

                modelId = modelName;
                LogScoped($"[ModelManagement] 未在模型库中找到 '{modelName}'，将直接使用自定义模型ID");
            }

            var configs = _aiConfigurationService.GetAllConfigurations();
            var config = configs.FirstOrDefault(c =>
                string.Equals(c.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

            var hasExistingActive = configs.Any(c => c.IsEnabled && c.IsActive);

            int contextWindow = 0;
            if (!string.IsNullOrEmpty(data.ContextLength) && int.TryParse(data.ContextLength, out var parsedContext))
            {
                contextWindow = parsedContext;
            }

            if (contextWindow <= 0)
            {
                contextWindow = model?.ContextWindow > 0 ? model.ContextWindow : 0;
            }

            int safeMaxTokens = data.MaxTokens;
            if (safeMaxTokens < 0)
            {
                safeMaxTokens = 0;
            }

            var ceiling = TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.BuildEffectiveMaxOutputCeiling(
                modelId, providerCategory.ChatEndpoint, provider.Id, contextWindow);
            if (safeMaxTokens > 0 && ceiling > 0 && safeMaxTokens > ceiling)
            {
                safeMaxTokens = ceiling;
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

            if (safeMaxTokens > 0 && safeMaxTokens < 256)
            {
                safeMaxTokens = 256;
            }

            if (safeMaxTokens != data.MaxTokens)
            {
                var originalMaxTokens = data.MaxTokens;
                LogScoped($"[ModelManagement] MaxTokens 已调整: raw={originalMaxTokens}, effective={safeMaxTokens}, contextWindow={contextWindow}, modelMaxOutput={(model?.MaxOutputTokens ?? 0)}");

                data.MaxTokens = safeMaxTokens;
                Service.UpdateConfiguration(data);

                GlobalToast.Info("参数已自动调整", $"MaxTokens: {originalMaxTokens} → {safeMaxTokens}");
            }

            if (config == null)
            {
                var finalEndpoint = providerCategory.ChatEndpoint;
                var keepReasoningEffort = ShouldKeepReasoningEffortForSync(data, modelId, provider.Id, finalEndpoint);
                var effectiveReasoningEffort = keepReasoningEffort ? data.ReasoningEffort : string.Empty;

                config = new UserConfiguration
                {
                    Name = data.Name,
                    ProviderId = provider.Id,
                    ModelId = modelId,
                    CustomEndpoint = finalEndpoint,
                    Temperature = data.Temperature,
                    MaxTokens = safeMaxTokens,
                    FrequencyPenalty = data.FrequencyPenalty,
                    BatchTier = data.BatchTier,
                    ContextWindow = contextWindow,
                    ReasoningEffort = effectiveReasoningEffort,
                    TopP = data.TopP,
                    PresencePenalty = data.PresencePenalty,
                    Seed = data.Seed ?? string.Empty,
                    StopSequences = data.StopSequences ?? string.Empty,
                    RateLimitRPM = data.RateLimitRPM,
                    RateLimitTPM = data.RateLimitTPM,
                    MaxConcurrency = data.MaxConcurrency > 0 ? data.MaxConcurrency : 5,
                    RetryCount = data.RetryCount > 0 ? data.RetryCount : 3,
                    TimeoutSeconds = data.TimeoutSeconds > 0 ? data.TimeoutSeconds : 30,
                    IsActive = false,
                    IsEnabled = data.IsEnabled,
                    ThinkingPassthrough = data.ThinkingPassthrough,
                    SupportsThinking = data.SupportsThinking,
                    SupportsReasoningEffort = data.SupportsReasoningEffort,
                    SupportedEffortLevels = data.SupportedEffortLevels,
                    ThinkingEnabled = data.ThinkingEnabled,
                    SupportsLongContext = data.SupportsLongContext,
                    EnableLongContext = data.EnableLongContext,
                    LongContextWindow = data.LongContextWindow > 0 ? data.LongContextWindow : 1_000_000,
                    CapabilitiesDetected = data.CapabilitiesDetected,
                };

                _aiConfigurationService.AddConfiguration(config);
                TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                    config.ProviderId, config.CustomEndpoint, config.ModelId);
                var ctxWindowText = contextWindow > 0 ? contextWindow.ToString() : "Auto(Family Fallback)";
                var chatEpDispCreate = providerCategory.IsTianmingPrivate() ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedEndpointLabel : (finalEndpoint ?? string.Empty);
                LogScoped($"[ModelManagement] 已为模型创建AIService配置: {config.Name}, ContextWindow={ctxWindowText}, ChatEndpoint={chatEpDispCreate}, IsActive={config.IsActive}");
            }
            else
            {
                var finalEndpoint = providerCategory.ChatEndpoint;
                var keepReasoningEffort = ShouldKeepReasoningEffortForSync(data, modelId, provider.Id, finalEndpoint);
                var effectiveReasoningEffort = keepReasoningEffort ? data.ReasoningEffort : string.Empty;
                var previousProviderId = config.ProviderId;
                var previousEndpoint = config.CustomEndpoint;
                var previousModelId = config.ModelId;
                var reasoningParamsChanged =
                    !string.Equals(config.ReasoningEffort, effectiveReasoningEffort, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(config.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(config.CustomEndpoint, finalEndpoint, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(config.ModelId, modelId, StringComparison.OrdinalIgnoreCase);

                config.Name = data.Name;
                config.ProviderId = provider.Id;
                config.ModelId = modelId;
                config.CustomEndpoint = finalEndpoint;
                config.Temperature = data.Temperature;
                config.MaxTokens = safeMaxTokens;
                config.FrequencyPenalty = data.FrequencyPenalty;
                config.BatchTier = data.BatchTier;
                config.ContextWindow = contextWindow;
                config.ReasoningEffort = effectiveReasoningEffort;
                config.TopP = data.TopP;
                config.PresencePenalty = data.PresencePenalty;
                config.Seed = data.Seed ?? string.Empty;
                config.StopSequences = data.StopSequences ?? string.Empty;
                config.RateLimitRPM = data.RateLimitRPM;
                config.RateLimitTPM = data.RateLimitTPM;
                config.MaxConcurrency = data.MaxConcurrency > 0 ? data.MaxConcurrency : 5;
                config.RetryCount = data.RetryCount > 0 ? data.RetryCount : 3;
                config.TimeoutSeconds = data.TimeoutSeconds > 0 ? data.TimeoutSeconds : 30;
                config.IsEnabled = data.IsEnabled;
                config.ThinkingPassthrough = data.ThinkingPassthrough;
                config.SupportsThinking = data.SupportsThinking;
                config.SupportsReasoningEffort = data.SupportsReasoningEffort;
                config.SupportedEffortLevels = data.SupportedEffortLevels;
                config.ThinkingEnabled = data.ThinkingEnabled;
                config.SupportsLongContext = data.SupportsLongContext;
                config.EnableLongContext = data.EnableLongContext;
                if (data.LongContextWindow > 0)
                    config.LongContextWindow = data.LongContextWindow;
                config.CapabilitiesDetected = data.CapabilitiesDetected;

                _aiConfigurationService.UpdateConfiguration(config);
                if (reasoningParamsChanged)
                {
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                        previousProviderId, previousEndpoint, previousModelId);
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                        config.ProviderId, config.CustomEndpoint, config.ModelId);
                }
                var ctxWindowText = contextWindow > 0 ? contextWindow.ToString() : "Auto(Family Fallback)";
                var chatEpDispUpdate = providerCategory.IsTianmingPrivate() ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedEndpointLabel : (finalEndpoint ?? string.Empty);
                LogScoped($"[ModelManagement] 已更新AIService配置: {config.Name}, ContextWindow={ctxWindowText}, ChatEndpoint={chatEpDispUpdate}, IsActive={config.IsActive}");
            }

            _testConnectionCts?.Cancel();
            _testConnectionCts?.Dispose();
            _testConnectionCts = new CancellationTokenSource();
            var probeToken = _testConnectionCts.Token;

            _testingProgressText = $"正在探测「{data.Name}」的模型参数...";
            IsTestingConnection = true;
            StartTimeoutMonitor(_testConnectionCts);

            ModelCapabilityResult? probeSnapshot = null;
            bool probeCancelled = false;
            string? probeExceptionMsg = null;

            try
            {
                var probeApiKey = providerCategory.ApiKey ?? string.Empty;
                var probeEndpoint = providerCategory.ChatEndpoint;

                bool runtimeRejectedEffort =
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.IsUnsupportedParam(
                        provider.Id, probeEndpoint, modelId, "reasoning_effort")
                    || TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.IsUnsupportedParam(
                        provider.Id, probeEndpoint, modelId, "reasoning");
                bool? knownEffort = data.SupportsReasoningEffort
                    ? true
                    : runtimeRejectedEffort ? false : (bool?)null;
                bool isOfficialGeminiThinking =
                    modelId.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                    && TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsThinkingModel(modelId, provider.Id);
                bool? knownThinking = isOfficialGeminiThinking
                    ? true
                    : data.CapabilitiesDetected ? data.SupportsThinking : (bool?)null;

                bool isDefaultLongContext =
                    TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsDefaultLongContextModel(modelId, provider.Id);
                bool isLongContextNameMatch =
                    TM.Services.Framework.AI.Core.ModelFamilyClassifier.IsLongContextModel(modelId, provider.Id);
                bool? knownLongContext;
                if (isDefaultLongContext)
                {
                    knownLongContext = true;
                }
                else if (data.CapabilitiesDetected && data.SupportsLongContext)
                {
                    knownLongContext = true;
                }
                else if (data.CapabilitiesDetected && !isLongContextNameMatch)
                {
                    knownLongContext = false;
                }
                else
                {
                    knownLongContext = null;
                }

                using (TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.EndpointTestService.BeginPrivateScope(providerCategory.IsTianmingPrivate()))
                {
                    probeSnapshot = await _endpointTestService.ProbeModelCapabilitiesAsync(
                        probeEndpoint, probeApiKey, modelId, probeToken,
                        knownSupportsReasoningEffort: knownEffort,
                        knownSupportsThinking: knownThinking,
                        providerId: provider.Id,
                        knownSupportsLongContext: knownLongContext);
                }

                if (probeSnapshot.SupportsReasoningEffort)
                {
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearUnsupportedParam(
                        provider.Id, config.CustomEndpoint, modelId, "reasoning_effort");
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearUnsupportedParam(
                        provider.Id, config.CustomEndpoint, modelId, "reasoning");
                }

                var thinkingRejected = probeSnapshot.EnableThinkingRejected && probeSnapshot.ClaudeThinkingRejected;
                if (probeSnapshot.SupportsThinking || thinkingRejected)
                    data.SupportsThinking = probeSnapshot.SupportsThinking;
                data.ThinkingPassthrough = probeSnapshot.ThinkingPassthrough;

                if (probeSnapshot.SupportsLongContext)
                {
                    data.SupportsLongContext = true;
                }
                else if (probeSnapshot.LongContextRejected)
                {
                    data.SupportsLongContext = false;
                    data.EnableLongContext = null;
                }

                if (data.SupportsLongContext
                    && int.TryParse(data.ContextLength, out var ctxLenForLong)
                    && ctxLenForLong >= 1_000_000
                    && ctxLenForLong > data.LongContextWindow)
                {
                    data.LongContextWindow = ctxLenForLong;
                }

                if (probeSnapshot.AnyRequestReachedServer && probeSnapshot.FinalChatOk)
                {
                    data.CapabilitiesDetected = true;
                }
                Service.UpdateConfiguration(data);

                var keepReasoningEffort = ShouldKeepReasoningEffortForSync(data, modelId, provider.Id ?? string.Empty, config.CustomEndpoint);
                var effectiveReasoningEffort = keepReasoningEffort ? data.ReasoningEffort : string.Empty;
                var probeReasoningParamsChanged =
                    !string.Equals(config.ReasoningEffort, effectiveReasoningEffort, StringComparison.OrdinalIgnoreCase);
                config.ReasoningEffort = effectiveReasoningEffort;
                config.ThinkingPassthrough = data.ThinkingPassthrough;
                config.SupportsThinking = data.SupportsThinking;
                config.SupportsReasoningEffort = data.SupportsReasoningEffort;
                config.SupportedEffortLevels = data.SupportedEffortLevels;
                config.ThinkingEnabled = data.ThinkingEnabled;
                config.SupportsLongContext = data.SupportsLongContext;
                config.EnableLongContext = data.EnableLongContext;
                if (data.LongContextWindow > 0)
                    config.LongContextWindow = data.LongContextWindow;
                config.CapabilitiesDetected = data.CapabilitiesDetected;
                _aiConfigurationService.UpdateConfiguration(config);
                if (probeReasoningParamsChanged)
                {
                    TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ClearReasoningCaps(
                        config.ProviderId, config.CustomEndpoint, config.ModelId);
                }

                LogScoped($"[ModelManagement] 模型参数探测完成: {modelId}, " +
                    $"Reached={probeSnapshot.AnyRequestReachedServer}, ChatOk={probeSnapshot.FinalChatOk}, " +
                    $"Effort={probeSnapshot.SupportsReasoningEffort}, Thinking={probeSnapshot.SupportsThinking}, " +
                    $"LongContext={probeSnapshot.SupportsLongContext}" +
                    (probeSnapshot.LongContextRejected ? "(rejected)" : string.Empty) + ", " +
                    $"Passthrough={probeSnapshot.ThinkingPassthrough?.ToString() ?? "null"}" +
                    (probeSnapshot.FailureReason != null ? $", FailureReason={probeSnapshot.FailureReason}" : string.Empty));

                if (data.CapabilitiesDetected)
                {
                    var pid = provider.Id;
                    var ep = config.CustomEndpoint;
                    var mid = modelId;
                    if (thinkingRejected)
                    {
                        TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.MarkUnsupportedParam(pid, ep, mid, "enable_thinking");
                        TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.MarkUnsupportedParam(pid, ep, mid, "thinking_budget");
                        TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.MarkUnsupportedParam(pid, ep, mid, "thinking");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                probeCancelled = true;
                LogScoped($"[ModelManagement] 模型参数探测已取消: {modelId}");
            }
            catch (Exception ex)
            {
                probeExceptionMsg = $"探测过程异常：{ex.Message}";
                LogScoped($"[ModelManagement] 模型参数探测异常: {modelId}, {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                IsTestingConnection = false;
            }

            if (probeCancelled)
            {
                if (string.Equals(_activeEnableDataId, data.Id, StringComparison.Ordinal))
                    _activeEnableDataId = null;
                GlobalToast.Warning("启用已取消", $"「{data.Name}」参数探测已取消，模型未被激活。可稍后重试。");
                return;
            }

            bool probeSucceeded = probeSnapshot?.AnyRequestReachedServer == true
                                && probeSnapshot?.FinalChatOk == true;

            if (!probeSucceeded)
            {
                var reason = probeSnapshot?.FailureReason
                             ?? probeExceptionMsg
                             ?? "探测未能收到服务端有效响应";
                LogScoped($"[ModelManagement] 模型启用失败: {modelId}, 原因: {reason}");

                var failEndpointDisplay = providerCategory.IsTianmingPrivate()
                    ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedAllLabel
                    : (!string.IsNullOrWhiteSpace(config?.CustomEndpoint)
                        ? config!.CustomEndpoint
                        : (providerCategory?.ChatEndpoint ?? "（未知）"));
                var failMsg = $"「{data.Name}」启用失败\n\n端点：{failEndpointDisplay}\n\n失败原因：\n{reason}";
                StandardDialog.ShowError(failMsg, "启用失败");

                if (config != null)
                {
                    _disableCoordinator.DisableSingle(config, "ModelManagement.ProbeFailed");
                }
                else if (data.IsEnabled)
                {
                    data.IsEnabled = false;
                    Service.UpdateConfiguration(data);
                }

                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();
                RefreshEnabledConfigs();
                if (string.Equals(_activeEnableDataId, data.Id, StringComparison.Ordinal))
                    _activeEnableDataId = null;
                return;
            }

            if (startFromDisabled)
            {
                if (!string.Equals(_activeEnableDataId, data.Id, StringComparison.Ordinal))
                {
                    LogScoped($"[ModelManagement] 探测期间启用令牌已变更，放弃升级 IsEnabled: {data.Name}({data.Id})");
                    return;
                }
                _activeEnableDataId = null;

                if (!data.IsEnabled)
                {
                    data.IsEnabled = true;
                    Service.UpdateConfiguration(data);
                }
                if (!config.IsEnabled)
                {
                    config.IsEnabled = true;
                    _aiConfigurationService.UpdateConfiguration(config);
                }
                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();
                RefreshEnabledConfigs();
            }

            if (!hasExistingActive)
            {
                _aiConfigurationService.SetActiveConfiguration(config);
            }

            var successMsg = BuildEnableSuccessMessage(data, config);
            StandardDialog.ShowInfo(successMsg, "启用成功");

            if (providerCategory != null
                && !string.IsNullOrWhiteSpace(providerCategory.ModelsEndpoint)
                && TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.ShouldProbeModelsEndpoint(modelId, config.CustomEndpoint, config.ProviderId))
            {
                var bgModelId = modelId;
                var bgEndpoint = providerCategory.ModelsEndpoint;
                var bgApiKey = providerCategory.ApiKey ?? string.Empty;
                var bgData = data;
                var bgConfig = config;
                var bgCurrentCl = contextWindow;
                var bgProviderCategory = providerCategory;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var _bgEpScope = TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services.EndpointTestService.BeginPrivateScope(bgProviderCategory.IsTianmingPrivate());
                        var fetchResult = await _endpointTestService.TestModelsEndpointAsync(
                            new List<string> { bgEndpoint }, bgApiKey);
                        if (!fetchResult.Success) return;

                        var match = fetchResult.Models.FirstOrDefault(m =>
                            string.Equals(m.Id, bgModelId, StringComparison.OrdinalIgnoreCase));
                        if (match == null || (match.ContextLength <= 0 && match.MaxTokens <= 0)) return;

                        if (match.ContextLength > 0 && match.ContextLength > bgCurrentCl)
                        {
                            var freshData = Service.GetAllData()
                                .FirstOrDefault(d => d.Id == bgData.Id);
                            if (freshData != null)
                            {
                                freshData.ContextLength = match.ContextLength.ToString();
                                Service.UpdateConfiguration(freshData);
                            }

                            var freshConfig = _aiConfigurationService.GetAllConfigurations()
                                .FirstOrDefault(c => c.Id == bgConfig.Id);
                            if (freshConfig != null)
                            {
                                freshConfig.ContextWindow = match.ContextLength;
                                _aiConfigurationService.UpdateConfiguration(freshConfig);
                            }

                            TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.RecordDiscoveredContextWindow(
                                bgModelId, match.ContextLength, bgConfig.CustomEndpoint, bgConfig.ProviderId, DiscoverySource.Declared);

                            bgProviderCategory.LogIfPublic($"[ModelManagement] 探测升级 ContextWindow: {bgModelId} {bgCurrentCl} → {match.ContextLength}");
                        }

                        int finalMo = 0;
                        bool isFromApi = false;

                        if (match.MaxTokens > 0)
                        {
                            var isLikelyContextLength = match.ContextLength > 0
                                && match.MaxTokens >= match.ContextLength;

                            if (isLikelyContextLength)
                            {
                                bgProviderCategory.LogIfPublic($"[ModelManagement] MaxTokens({match.MaxTokens}) >= ContextLength({match.ContextLength})，疑似代理误报，跳过 API 值: {bgModelId}");
                            }
                            else
                            {
                                finalMo = match.MaxTokens;
                                isFromApi = true;
                            }
                        }

                        if (finalMo <= 0)
                        {
                            finalMo = TM.Services.Framework.AI.SemanticKernel.SKChatService.GetFamilyMaxOutput(bgModelId);
                            if (finalMo > 0)
                                bgProviderCategory.LogIfPublic($"[ModelManagement] /models 未返回 MaxTokens，回退家族真值: {bgModelId} → {finalMo}");
                        }

                        if (finalMo > 0)
                        {
                            TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.RecordDiscoveredMaxOutput(
                                bgModelId, finalMo, bgConfig.CustomEndpoint, bgConfig.ProviderId,
                                isFromApi ? DiscoverySource.Declared : DiscoverySource.Family);
                            bgProviderCategory.LogIfPublic($"[ModelManagement] {(isFromApi ? "/models 探测" : "家族兜底")}写入 MaxOutput 缓存: {bgModelId} → {finalMo} (source={(isFromApi ? DiscoverySource.Declared : DiscoverySource.Family)})");
                        }
                    }
                    catch (Exception ex)
                    {
                        bgProviderCategory.LogIfPublic($"[ModelManagement] 后台探测失败（非致命）: {bgModelId}: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 同步到AIService失败: {ex.Message}");
            GlobalToast.Error("模型激活失败", $"模型激活失败：{ex.Message}");
        }
    }

    private static string BuildEnableSuccessMessage(UserConfigurationData data, UserConfiguration config)
    {
        var hint = new UserCapabilityHint
        {
            CapabilitiesDetected = data.CapabilitiesDetected,
            SupportsThinking = data.CapabilitiesDetected ? data.SupportsThinking : (bool?)null,
            SupportsReasoningEffort = data.CapabilitiesDetected ? data.SupportsReasoningEffort : (bool?)null,
            SupportedEffortLevels = data.SupportedEffortLevels?.Count > 0 ? data.SupportedEffortLevels : null,
        };
        var resolved = CapabilityServices.DefaultResolver.Resolve(
            config.ProviderId, config.ModelId, config.CustomEndpoint, hint);

        string thinkLine;
        if (!data.SupportsThinking)
            thinkLine = "✗ 不支持";
        else if (resolved.HasThinkingToggle)
            thinkLine = "✓ 支持";
        else
            thinkLine = "✓ 支持（模型自动激活）";

        string effortLine;
        bool hasEffortLevels = resolved.HasThinkingToggle && resolved.HasEffortLevels;
        if (hasEffortLevels)
            effortLine = "✓ 支持";
        else if (data.SupportsThinking && !resolved.HasThinkingToggle)
            effortLine = "✗ 不支持（模型自动控制）";
        else
            effortLine = "✗ 不支持";

        string longCtxLine;
        if (!data.SupportsLongContext)
            longCtxLine = "✗ 不支持";
        else if (ModelFamilyClassifier.IsDefaultLongContextModel(config.ModelId, config.ProviderId))
            longCtxLine = "✓ 支持（模型自动开启）";
        else
            longCtxLine = "✓ 支持";

        var ctxWindow = FormatContextWindowSize(config.ContextWindow);

        return $"「{data.Name}」启用成功\n\n" +
               $"思考能力：{thinkLine}\n" +
               $"推理强度：{effortLine}\n" +
               $"1M 上下文：{longCtxLine}\n" +
               $"上下文窗口：{ctxWindow}";
    }

    private static string FormatContextWindowSize(int tokens)
    {
        if (tokens <= 0) return "—";
        if (tokens >= 1_000_000)
        {
            var m = tokens / 1_000_000.0;
            return Math.Abs(m - Math.Round(m)) < 0.05
                ? $"{Math.Round(m):0}M"
                : $"{m:0.0}M";
        }
        if (tokens >= 1_000) return $"{tokens / 1_000}K";
        return tokens.ToString();
    }

    private void UpdateDataFromForm(UserConfigurationData data)
    {
        data.Name = FormName;
        data.Icon = GetDataIconForSave(FormIcon);
        data.Category = FormCategory;
        data.IsEnabled = FormStatus == "已启用";
        data.AutoDisabledBySystem = false;
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
        data.BatchTier = FormBatchTier;
        data.RateLimitRPM = FormRateLimitRPM;
        data.RateLimitTPM = FormRateLimitTPM;
        data.MaxConcurrency = FormMaxConcurrency;
        data.Seed = FormSeed;
        data.StopSequences = FormStopSequences;

        data.RetryCount = FormRetryCount;
        data.TimeoutSeconds = FormTimeoutSeconds;

        data.ReasoningEffort = FormSupportsReasoningEffort ? FormReasoningEffort : string.Empty;
        data.ThinkingEnabled = (FormSupportsReasoningEffort || FormSupportsThinking) ? FormThinkingEnabled : null;

        data.SupportsReasoningEffort = FormSupportsReasoningEffort;
        data.SupportsThinking = FormSupportsThinking;
        data.SupportsLongContext = FormSupportsLongContext;
        if (!FormSupportsLongContext) data.EnableLongContext = null;
        data.CapabilitiesDetected = true;
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
        FormBatchTier = data.BatchTier;
        FormRateLimitRPM = data.RateLimitRPM;
        FormRateLimitTPM = data.RateLimitTPM;
        FormMaxConcurrency = data.MaxConcurrency;
        FormSeed = data.Seed;
        FormStopSequences = data.StopSequences;

        FormRetryCount = data.RetryCount;
        FormTimeoutSeconds = data.TimeoutSeconds;

        FormReasoningEffort = data.ReasoningEffort;
        FormThinkingEnabled = data.ThinkingEnabled;
        FormSupportsReasoningEffort = data.SupportsReasoningEffort;
        FormSupportsThinking = data.SupportsThinking;
        FormSupportsLongContext = data.SupportsLongContext;

        var provider = Service.GetAllCategories()
            .FirstOrDefault(c => c.Name == data.Category && c.Level == 2);
        SetCurrentProvider(provider);

        OnPropertyChanged(nameof(FormAvailableThinkingEfforts));
        OnPropertyChanged(nameof(FormShowThinkingToggle));
        OnPropertyChanged(nameof(FormShowEffortDropdown));

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
        FormIcon = "Icon.Robot";
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
        FormMaxTokens = 0;
        FormTopP = 1.0;
        FormFrequencyPenalty = 0.1;
        FormPresencePenalty = 0.0;
        FormBatchTier = "64K";
        FormRateLimitRPM = 0;
        FormRateLimitTPM = 0;
        FormMaxConcurrency = 5;
        FormSeed = string.Empty;
        FormStopSequences = string.Empty;

        FormRetryCount = 3;
        FormTimeoutSeconds = 30;

        FormReasoningEffort = string.Empty;
        FormThinkingEnabled = null;
        FormSupportsReasoningEffort = false;
        FormSupportsThinking = false;
        FormSupportsLongContext = false;

        SetCurrentProvider(null);
    }

    private void RefreshFormStateProperties()
    {
        OnPropertyChanged(nameof(IsGlobalParametersAvailable));
        OnPropertyChanged(nameof(IsTab1Enabled));
        OnPropertyChanged(nameof(IsApiConfigEditable));
        OnPropertyChanged(nameof(IsBuiltInCategory));
        OnPropertyChanged(nameof(IsApiActionEnabled));
        OnPropertyChanged(nameof(IsTab2Enabled));
        OnPropertyChanged(nameof(FormReasoningEffortEnabled));
        OnPropertyChanged(nameof(FormThinkingParamsEnabled));
    }
}

