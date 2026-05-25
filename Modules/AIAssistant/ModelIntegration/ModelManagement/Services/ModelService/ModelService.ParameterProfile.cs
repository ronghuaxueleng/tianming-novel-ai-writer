using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

public partial class ModelService
{

    private class ProviderData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Icon")] public string Icon { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LogoPath")] public string? LogoPath { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ApiEndpoint")] public string ApiEndpoint { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ModelsEndpoint")] public string ModelsEndpoint { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ChatEndpoint")] public string? ChatEndpoint { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndpointVerifiedAt")] public DateTime? EndpointVerifiedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndpointSignature")] public string? EndpointSignature { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RequiresApiKey")] public bool RequiresApiKey { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Order")] public int Order { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DefaultProfileId")] public string? DefaultProfileId { get; set; }
    }

    private class ParameterProfile
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Temperature")] public double Temperature { get; set; } = 0.7;
        [System.Text.Json.Serialization.JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("TopP")] public double TopP { get; set; } = 1.0;
        [System.Text.Json.Serialization.JsonPropertyName("FrequencyPenalty")] public double FrequencyPenalty { get; set; } = 0.1;
        [System.Text.Json.Serialization.JsonPropertyName("PresencePenalty")] public double PresencePenalty { get; set; } = 0.0;
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitRPM")] public int RateLimitRPM { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitTPM")] public int RateLimitTPM { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("MaxConcurrency")] public int MaxConcurrency { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("Seed")] public string Seed { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("StopSequences")] public string StopSequences { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("RetryCount")] public int RetryCount { get; set; } = 3;
        [System.Text.Json.Serialization.JsonPropertyName("TimeoutSeconds")] public int TimeoutSeconds { get; set; } = 30;
        [System.Text.Json.Serialization.JsonPropertyName("ReasoningEffort")] public string ReasoningEffort { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ThinkingEnabled")] public bool? ThinkingEnabled { get; set; }
    }

    private class ParameterOverrideRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Temperature")] public double? Temperature { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("MaxTokens")] public int? MaxTokens { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("TopP")] public double? TopP { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("FrequencyPenalty")] public double? FrequencyPenalty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PresencePenalty")] public double? PresencePenalty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitRPM")] public int? RateLimitRPM { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitTPM")] public int? RateLimitTPM { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("MaxConcurrency")] public int? MaxConcurrency { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Seed")] public string? Seed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("StopSequences")] public string? StopSequences { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RetryCount")] public int? RetryCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("TimeoutSeconds")] public int? TimeoutSeconds { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ReasoningEffort")] public string? ReasoningEffort { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ThinkingEnabled")] public bool? ThinkingEnabled { get; set; }
    }

    private class SlimModelRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Robot";
        [System.Text.Json.Serialization.JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ModelName")] public string ModelName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ApiEndpoint")] public string ApiEndpoint { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ApiKey")] public string ApiKey { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsActive")] public bool IsActive { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ProviderName")] public string ProviderName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ModelVersion")] public string ModelVersion { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ContextLength")] public string ContextLength { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("TrainingDataCutoff")] public string TrainingDataCutoff { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("InputPrice")] public string InputPrice { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("OutputPrice")] public string OutputPrice { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SupportedFeatures")] public string SupportedFeatures { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BatchTier")] public string BatchTier { get; set; } = "64K";

        [System.Text.Json.Serialization.JsonPropertyName("AutoDisabledBySystem")] public bool AutoDisabledBySystem { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("SupportsReasoningEffort")] public bool SupportsReasoningEffort { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportedEffortLevels")] public List<string>? SupportedEffortLevels { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsThinking")] public bool SupportsThinking { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ThinkingPassthrough")] public bool? ThinkingPassthrough { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsVision")] public bool SupportsVision { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsImageGeneration")] public bool SupportsImageGeneration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsTools")] public bool SupportsTools { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("CapabilitiesDetected")] public bool CapabilitiesDetected { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("SupportsLongContext")] public bool SupportsLongContext { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EnableLongContext")] public bool? EnableLongContext { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LongContextWindow")] public int LongContextWindow { get; set; } = 1_000_000;
    }

    private async Task LoadParameterProfilesAsync()
    {
        _parameterProfiles.Clear();

        try
        {
            if (File.Exists(_parameterProfilesFilePath))
            {
                var json = await File.ReadAllTextAsync(_parameterProfilesFilePath).ConfigureAwait(false);
                var profiles = JsonSerializer.Deserialize<List<ParameterProfile>>(json, JsonHelper.Default);
                if (profiles != null)
                {
                    foreach (var profile in profiles)
                    {
                        if (string.IsNullOrWhiteSpace(profile.Id))
                            continue;

                        _parameterProfiles[profile.Id] = profile;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 加载参数模板失败: {ex.Message}");
        }

        if (!_parameterProfiles.ContainsKey(DefaultProfileId))
        {
            _parameterProfiles[DefaultProfileId] = CreateDefaultProfile();
        }

        try
        {
            if (!File.Exists(_parameterProfilesFilePath))
            {
                var profilesToSave = _parameterProfiles.Values.ToList();
                var jsonOut = JsonSerializer.Serialize(profilesToSave, JsonHelper.Default);
                var tmpP0 = _parameterProfilesFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpP0, jsonOut).ConfigureAwait(false);
                File.Move(tmpP0, _parameterProfilesFilePath, overwrite: true);
                TM.App.Log($"[ModelService] 初始化参数模板文件: {_parameterProfilesFilePath}");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 初始化参数模板文件失败: {ex.Message}");
        }
    }

    private static ParameterProfile CreateDefaultProfile()
    {
        return new ParameterProfile
        {
            Id = DefaultProfileId,
            Name = "默认参数",
            Temperature = 0.7,
            MaxTokens = 0,
            TopP = 1.0,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.0,
            RateLimitRPM = 0,
            RateLimitTPM = 0,
            MaxConcurrency = 5,
            Seed = string.Empty,
            StopSequences = string.Empty,
            RetryCount = 3,
            TimeoutSeconds = 30,
            ReasoningEffort = string.Empty
        };
    }

    private ParameterProfile GetProfileForProvider(AIProviderCategory provider)
    {
        var key = GetProviderKey(provider);

        if (_providerDefaultProfileIds.TryGetValue(key, out var profileId))
        {
            if (!string.IsNullOrWhiteSpace(profileId) && _parameterProfiles.TryGetValue(profileId, out var profile))
            {
                return profile;
            }
        }

        if (_parameterProfiles.TryGetValue(DefaultProfileId, out var defaultProfile))
        {
            return defaultProfile;
        }

        if (_parameterProfiles.Count > 0)
        {
            return _parameterProfiles.Values.First();
        }

        return CreateDefaultProfile();
    }

    private static void ApplyProfileToData(ParameterProfile profile, UserConfigurationData data)
    {
        data.Temperature = profile.Temperature;
        data.MaxTokens = profile.MaxTokens;
        data.TopP = profile.TopP;
        data.FrequencyPenalty = profile.FrequencyPenalty;
        data.PresencePenalty = profile.PresencePenalty;
        data.RateLimitRPM = profile.RateLimitRPM;
        data.RateLimitTPM = profile.RateLimitTPM;
        data.MaxConcurrency = profile.MaxConcurrency;
        data.Seed = profile.Seed;
        data.StopSequences = profile.StopSequences;
        data.RetryCount = profile.RetryCount;
        data.TimeoutSeconds = profile.TimeoutSeconds;
        data.ReasoningEffort = profile.ReasoningEffort;
        data.ThinkingEnabled = profile.ThinkingEnabled;
    }

    private static void ApplyOverridesToData(ParameterOverrideRecord overrides, UserConfigurationData data)
    {
        if (overrides.Temperature.HasValue) data.Temperature = overrides.Temperature.Value;
        if (overrides.MaxTokens.HasValue) data.MaxTokens = overrides.MaxTokens.Value;
        if (overrides.TopP.HasValue) data.TopP = overrides.TopP.Value;
        if (overrides.FrequencyPenalty.HasValue) data.FrequencyPenalty = overrides.FrequencyPenalty.Value;
        if (overrides.PresencePenalty.HasValue) data.PresencePenalty = overrides.PresencePenalty.Value;
        if (overrides.RateLimitRPM.HasValue) data.RateLimitRPM = overrides.RateLimitRPM.Value;
        if (overrides.RateLimitTPM.HasValue) data.RateLimitTPM = overrides.RateLimitTPM.Value;
        if (overrides.MaxConcurrency.HasValue) data.MaxConcurrency = overrides.MaxConcurrency.Value;
        if (overrides.Seed != null) data.Seed = overrides.Seed;
        if (overrides.StopSequences != null) data.StopSequences = overrides.StopSequences;
        if (overrides.RetryCount.HasValue) data.RetryCount = overrides.RetryCount.Value;
        if (overrides.TimeoutSeconds.HasValue) data.TimeoutSeconds = overrides.TimeoutSeconds.Value;
        if (overrides.ReasoningEffort != null) data.ReasoningEffort = overrides.ReasoningEffort;
        if (overrides.ThinkingEnabled.HasValue) data.ThinkingEnabled = overrides.ThinkingEnabled;
    }

    private static SlimModelRecord CreateSlimFromData(UserConfigurationData data)
    {
        return new SlimModelRecord
        {
            Id = data.Id,
            Name = data.Name,
            Icon = data.Icon,
            Category = data.Category,
            CategoryId = data.CategoryId,
            IsEnabled = data.IsEnabled,
            CreatedTime = data.CreatedTime,
            ModifiedTime = data.ModifiedTime,
            Description = data.Description,
            ModelName = data.ModelName,
            ApiEndpoint = data.ApiEndpoint,
            ApiKey = string.Empty,
            IsActive = data.IsActive,
            ProviderName = data.ProviderName,
            ModelVersion = data.ModelVersion,
            ContextLength = data.ContextLength,
            TrainingDataCutoff = data.TrainingDataCutoff,
            InputPrice = data.InputPrice,
            OutputPrice = data.OutputPrice,
            SupportedFeatures = data.SupportedFeatures,
            BatchTier = data.BatchTier,
            AutoDisabledBySystem = data.AutoDisabledBySystem,
            SupportsReasoningEffort = data.SupportsReasoningEffort,
            SupportedEffortLevels = data.SupportedEffortLevels,
            SupportsThinking = data.SupportsThinking,
            ThinkingPassthrough = data.ThinkingPassthrough,
            SupportsVision = data.SupportsVision,
            SupportsImageGeneration = data.SupportsImageGeneration,
            SupportsTools = data.SupportsTools,
            SupportsStreaming = data.SupportsStreaming,
            CapabilitiesDetected = data.CapabilitiesDetected,
            SupportsLongContext = data.SupportsLongContext,
            EnableLongContext = data.EnableLongContext,
            LongContextWindow = data.LongContextWindow > 0 ? data.LongContextWindow : 1_000_000
        };
    }

    private static UserConfigurationData CreateFromSlim(
        SlimModelRecord slim,
        ParameterProfile profile,
        Dictionary<string, ParameterOverrideRecord> overrides)
    {
        var data = new UserConfigurationData
        {
            Id = slim.Id,
            Name = slim.Name,
            Icon = slim.Icon,
            Category = slim.Category,
            CategoryId = slim.CategoryId,
            IsEnabled = slim.IsEnabled,
            CreatedTime = slim.CreatedTime,
            ModifiedTime = slim.ModifiedTime,
            Description = slim.Description,
            ModelName = slim.ModelName,
            ApiEndpoint = slim.ApiEndpoint,
            ApiKey = string.Empty,
            IsActive = slim.IsActive,
            ProviderName = slim.ProviderName,
            ModelVersion = slim.ModelVersion,
            ContextLength = slim.ContextLength,
            TrainingDataCutoff = slim.TrainingDataCutoff,
            InputPrice = slim.InputPrice,
            OutputPrice = slim.OutputPrice,
            SupportedFeatures = slim.SupportedFeatures,
            BatchTier = slim.BatchTier,
            AutoDisabledBySystem = slim.AutoDisabledBySystem,
            SupportsReasoningEffort = slim.SupportsReasoningEffort,
            SupportedEffortLevels = slim.SupportedEffortLevels,
            SupportsThinking = slim.SupportsThinking,
            ThinkingPassthrough = slim.ThinkingPassthrough,
            SupportsVision = slim.SupportsVision,
            SupportsImageGeneration = slim.SupportsImageGeneration,
            SupportsTools = slim.SupportsTools,
            SupportsStreaming = slim.SupportsStreaming,
            CapabilitiesDetected = slim.CapabilitiesDetected,
            SupportsLongContext = slim.SupportsLongContext,
            EnableLongContext = slim.EnableLongContext,
            LongContextWindow = slim.LongContextWindow > 0 ? slim.LongContextWindow : 1_000_000
        };

        ApplyProfileToData(profile, data);

        if (overrides.TryGetValue(slim.Id, out var ov) && ov != null)
        {
            ApplyOverridesToData(ov, data);
        }

        return data;
    }

    private static ParameterOverrideRecord? BuildOverridesFromData(ParameterProfile profile, UserConfigurationData data)
    {
        var ov = new ParameterOverrideRecord();
        var has = false;

        if (data.Temperature != profile.Temperature) { ov.Temperature = data.Temperature; has = true; }
        if (data.MaxTokens != profile.MaxTokens) { ov.MaxTokens = data.MaxTokens; has = true; }
        if (data.TopP != profile.TopP) { ov.TopP = data.TopP; has = true; }
        if (data.FrequencyPenalty != profile.FrequencyPenalty) { ov.FrequencyPenalty = data.FrequencyPenalty; has = true; }
        if (data.PresencePenalty != profile.PresencePenalty) { ov.PresencePenalty = data.PresencePenalty; has = true; }
        if (data.RateLimitRPM != profile.RateLimitRPM) { ov.RateLimitRPM = data.RateLimitRPM; has = true; }
        if (data.RateLimitTPM != profile.RateLimitTPM) { ov.RateLimitTPM = data.RateLimitTPM; has = true; }
        if (data.MaxConcurrency != profile.MaxConcurrency) { ov.MaxConcurrency = data.MaxConcurrency; has = true; }
        if (!string.Equals(data.Seed, profile.Seed, StringComparison.Ordinal)) { ov.Seed = data.Seed; has = true; }
        if (!string.Equals(data.StopSequences, profile.StopSequences, StringComparison.Ordinal)) { ov.StopSequences = data.StopSequences; has = true; }
        if (data.RetryCount != profile.RetryCount) { ov.RetryCount = data.RetryCount; has = true; }
        if (data.TimeoutSeconds != profile.TimeoutSeconds) { ov.TimeoutSeconds = data.TimeoutSeconds; has = true; }
        if (!string.Equals(data.ReasoningEffort, profile.ReasoningEffort, StringComparison.Ordinal)) { ov.ReasoningEffort = data.ReasoningEffort; has = true; }
        if (data.ThinkingEnabled != profile.ThinkingEnabled) { ov.ThinkingEnabled = data.ThinkingEnabled; has = true; }

        return has ? ov : null;
    }

    public List<ParameterProfileDto> GetAllParameterProfilesForUI()
    {
        var list = new List<ParameterProfileDto>();

        foreach (var profile in _parameterProfiles.Values)
        {
            list.Add(new ParameterProfileDto
            {
                Id = profile.Id,
                Name = profile.Name,
                Temperature = profile.Temperature,
                MaxTokens = profile.MaxTokens,
                TopP = profile.TopP,
                FrequencyPenalty = profile.FrequencyPenalty,
                PresencePenalty = profile.PresencePenalty,
                RateLimitRPM = profile.RateLimitRPM,
                RateLimitTPM = profile.RateLimitTPM,
                MaxConcurrency = profile.MaxConcurrency,
                Seed = profile.Seed,
                StopSequences = profile.StopSequences,
                RetryCount = profile.RetryCount,
                TimeoutSeconds = profile.TimeoutSeconds,
                ReasoningEffort = profile.ReasoningEffort,
                ThinkingEnabled = profile.ThinkingEnabled
            });
        }

        return list
            .OrderBy(p => p.Id == DefaultProfileId ? 0 : 1)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public void SaveParameterProfilesFromUI(IEnumerable<ParameterProfileDto> profilesFromUi)
    {
        if (profilesFromUi == null)
            return;

        _parameterProfiles.Clear();

        foreach (var dto in profilesFromUi)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
                continue;

            _parameterProfiles[dto.Id] = new ParameterProfile
            {
                Id = dto.Id,
                Name = dto.Name,
                Temperature = dto.Temperature,
                MaxTokens = dto.MaxTokens,
                TopP = dto.TopP,
                FrequencyPenalty = dto.FrequencyPenalty,
                PresencePenalty = dto.PresencePenalty,
                RateLimitRPM = dto.RateLimitRPM,
                RateLimitTPM = dto.RateLimitTPM,
                MaxConcurrency = dto.MaxConcurrency,
                Seed = dto.Seed,
                StopSequences = dto.StopSequences,
                RetryCount = dto.RetryCount,
                TimeoutSeconds = dto.TimeoutSeconds,
                ReasoningEffort = dto.ReasoningEffort,
                ThinkingEnabled = dto.ThinkingEnabled
            };
        }

        if (!_parameterProfiles.ContainsKey(DefaultProfileId))
        {
            _parameterProfiles[DefaultProfileId] = CreateDefaultProfile();
        }

        var snapshot = _parameterProfiles.Values.ToList();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && dispatcher.CheckAccess())
            _ = System.Threading.Tasks.Task.Run(async () => await WriteParameterProfilesCoreAsync(snapshot).ConfigureAwait(false));
        else
            _ = WriteParameterProfilesCoreAsync(snapshot);
    }

    private async Task WriteParameterProfilesCoreAsync(List<ParameterProfile> profiles)
    {
        try
        {
            var jsonOut = JsonSerializer.Serialize(profiles, JsonHelper.Default);
            var tmpP = _parameterProfilesFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmpP, jsonOut).ConfigureAwait(false);
            File.Move(tmpP, _parameterProfilesFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 保存参数模板失败: {ex.Message}");
        }
    }

    public string? GetDefaultProfileIdForProvider(AIProviderCategory provider)
    {
        if (provider == null)
            return null;

        var key = GetProviderKey(provider);

        if (_providerDefaultProfileIds.TryGetValue(key, out var profileId))
        {
            return profileId;
        }

        return DefaultProfileId;
    }

    public async Task SetDefaultProfileIdForProviderAsync(AIProviderCategory provider, string? profileId)
    {
        if (provider == null)
            return;

        var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");

        if (!File.Exists(providersFile))
        {
            TM.App.Log("[ModelService] providers.json不存在，无法更新默认参数模板");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(providersFile).ConfigureAwait(false);
            var providers = JsonSerializer.Deserialize<List<ProviderData>>(json, JsonHelper.Default) ?? new List<ProviderData>();

            var target = providers.FirstOrDefault(p =>
                (!string.IsNullOrWhiteSpace(p.Id) && p.Id == provider.Id) ||
                (p.Name == provider.Name && p.Category == (provider.ParentCategory ?? string.Empty)));

            if (target == null)
            {
                TM.App.Log($"[ModelService] 未在providers.json中找到供应商 '{provider.Name}'，无法更新默认模板");
                return;
            }

            target.DefaultProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId;

            var jsonOut = JsonSerializer.Serialize(providers, JsonHelper.Default);
            var tmpPp = providersFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmpPp, jsonOut).ConfigureAwait(false);
            File.Move(tmpPp, providersFile, overwrite: true);

            var key = GetProviderKey(provider);
            var finalProfileId = target.DefaultProfileId;
            if (string.IsNullOrWhiteSpace(finalProfileId) || !_parameterProfiles.ContainsKey(finalProfileId))
            {
                finalProfileId = DefaultProfileId;
            }

            _providerDefaultProfileIds[key] = finalProfileId;
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 更新供应商默认参数模板失败: {ex.Message}");
        }
    }

    public void ApplyProfileToAllModelsForProvider(AIProviderCategory provider, string? profileId)
    {
        if (provider == null)
            return;

        ParameterProfile profile;

        if (!string.IsNullOrWhiteSpace(profileId) && _parameterProfiles.TryGetValue(profileId, out var found))
        {
            profile = found;
        }
        else
        {
            profile = GetProfileForProvider(provider);
        }

        var models = GetModelsForProvider(provider).ToList();

        foreach (var data in models)
        {
            ApplyProfileToData(profile, data);
        }

        SaveModelsForProvider(provider, models);
    }
}
