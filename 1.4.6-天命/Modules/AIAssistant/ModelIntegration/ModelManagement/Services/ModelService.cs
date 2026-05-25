using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.Security;
using TM.Framework.Common.Services;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Services.Framework.AI.Core;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

public class ModelService : ModuleServiceBase<AIProviderCategory, UserConfigurationData>
{
    public event EventHandler? ConfigurationsChanged;

    private void RaiseConfigurationsChanged()
    {
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

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

    protected override System.Threading.Tasks.Task OnInitializedAsync()
    {
        LoadParameterProfiles();
        SyncKeyPoolsToRotationService();
        EnsureProvidersFileExists();
        LoadProvidersFromJson();
        DataItems.Clear();
        LoadAllProviderModels();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void LoadAllProviderModels()
    {
        try
        {
            var providers = GetAllCategories()
                .Where(c => c.Level == 2)
                .ToList();

            foreach (var provider in providers)
            {
                var models = GetModelsForProvider(provider);
                if (models.Count > 0)
                {
                    TM.App.Log($"[ModelService] 启动时加载供应商 '{provider.Name}' 模型 {models.Count} 条");
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
                _keyStateHandlerRegistered = true;
            }

            foreach (var cat in Categories)
            {
                if (cat.Level != 2) continue;
                if (string.IsNullOrWhiteSpace(cat.Id)) continue;
                if (cat.ApiKeys == null || cat.ApiKeys.Count == 0) continue;

                rotation.UpdateKeyPool(cat.Id, cat.ApiKeys);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 同步密钥池到轮询服务失败: {ex.Message}");
        }
    }

    private void OnKeyStatePersistRequired(string providerId)
    {
        try
        {
            SaveAllCategories();
            TM.App.Log($"[ModelService] 密钥状态变更，已保存 categories.json: providerId={providerId}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 保存密钥状态失败: {ex.Message}");
        }
    }

    private void LoadParameterProfiles()
    {
        _parameterProfiles.Clear();

        try
        {
            if (File.Exists(_parameterProfilesFilePath))
            {
                var json = File.ReadAllText(_parameterProfilesFilePath);
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
                File.WriteAllText(tmpP0, jsonOut);
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
            MaxTokens = 4096,
            TopP = 1.0,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.0,
            RateLimitRPM = 0,
            RateLimitTPM = 0,
            MaxConcurrency = 5,
            Seed = string.Empty,
            StopSequences = string.Empty,
            RetryCount = 3,
            TimeoutSeconds = 30,
            EnableStreaming = true
        };
    }

    protected override int OnBeforeDeleteData(string dataId)
    {
        return DataItems.RemoveAll(d => d.Id == dataId);
    }

    protected override List<AIProviderCategory> CreateDefaultCategories()
    {
        return new List<AIProviderCategory>();
    }

    private void EnsureProvidersFileExists()
    {
        var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");
        if (!File.Exists(providersFile))
        {
            SyncProvidersFromCategories();
        }
    }

    public void SyncProvidersFromCategories()
    {
        try
        {
            var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");

            var existingProviders = new List<ProviderData>();
            if (File.Exists(providersFile))
            {
                try
                {
                    var existingJson = File.ReadAllText(providersFile);
                    existingProviders = JsonSerializer.Deserialize<List<ProviderData>>(existingJson, JsonHelper.Default)
                                        ?? new List<ProviderData>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ModelService] 读取现有providers.json失败: {ex.Message}");
                    existingProviders = new List<ProviderData>();
                }
            }

            var existingMap = existingProviders.ToDictionary(
                p => !string.IsNullOrWhiteSpace(p.Id)
                    ? p.Id
                    : $"{p.Category}::{p.Name}",
                p => p,
                StringComparer.Ordinal);

            var providerCategories = GetAllCategories()
                .Where(c => c.Level == 2)
                .ToList();

            var providers = providerCategories
                .Select(c =>
                {
                    var key = !string.IsNullOrWhiteSpace(c.Id)
                        ? c.Id
                        : $"{c.ParentCategory ?? string.Empty}::{c.Name}";

                    existingMap.TryGetValue(key, out var old);

                    return new ProviderData
                    {
                        Id = c.Id ?? string.Empty,
                        Name = c.Name ?? string.Empty,
                        Icon = c.Icon ?? string.Empty,
                        LogoPath = c.LogoPath,
                        Category = c.ParentCategory ?? string.Empty,
                        ApiEndpoint = c.ApiEndpoint ?? string.Empty,
                        ModelsEndpoint = c.ModelsEndpoint ?? string.Empty,
                        ChatEndpoint = c.ChatEndpoint,
                        EndpointVerifiedAt = c.EndpointVerifiedAt,
                        EndpointSignature = c.EndpointSignature,
                        RequiresApiKey = c.RequiresApiKey,
                        SupportsStreaming = c.SupportsStreaming,
                        Description = c.Description ?? string.Empty,
                        Order = c.Order,
                        DefaultProfileId = old?.DefaultProfileId ?? DefaultProfileId
                    };
                })
                .ToList();

            var jsonOut = JsonSerializer.Serialize(providers, JsonHelper.Default);
            var tmpPf = providersFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmpPf, jsonOut);
            File.Move(tmpPf, providersFile, overwrite: true);

            TM.App.Log($"[ModelService] 同步providers.json: {providers.Count}个供应商");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 同步providers.json失败: {ex.Message}");
        }
    }

    private void LoadProvidersFromJson()
    {
        _providerDefaultProfileIds.Clear();

        try
        {
            var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");

            if (!File.Exists(providersFile))
            {
                TM.App.Log("[ModelService] providers.json不存在");
                return;
            }

            var json = File.ReadAllText(providersFile);
            var providers = JsonSerializer.Deserialize<List<ProviderData>>(json, JsonHelper.Default);

            if (providers == null || providers.Count == 0)
            {
                TM.App.Log("[ModelService] 未加载到供应商数据");
                return;
            }

            var allCategories = GetAllCategories();

            foreach (var provider in providers)
            {
                var category = allCategories
                    .FirstOrDefault(c =>
                        c.Level == 2 &&
                        (c.Id == provider.Id ||
                         (!string.IsNullOrWhiteSpace(provider.Category) &&
                          c.Name == provider.Name &&
                          c.ParentCategory == provider.Category)));

                if (category == null)
                {
                    TM.App.Log($"[ModelService] providers.json中的供应商 '{provider.Name}' 未在categories中找到对应节点");
                    continue;
                }

                var key = GetProviderKey(category);
                var profileId = provider.DefaultProfileId;
                if (string.IsNullOrWhiteSpace(profileId) || !_parameterProfiles.ContainsKey(profileId))
                {
                    profileId = DefaultProfileId;
                }
                _providerDefaultProfileIds[key] = profileId;
            }

            TM.App.Log($"[ModelService] 加载供应商: {providers.Count}个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 加载供应商失败: {ex.Message}");
        }
    }

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
        [System.Text.Json.Serialization.JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 4096;
        [System.Text.Json.Serialization.JsonPropertyName("TopP")] public double TopP { get; set; } = 1.0;
        [System.Text.Json.Serialization.JsonPropertyName("FrequencyPenalty")] public double FrequencyPenalty { get; set; } = 0.0;
        [System.Text.Json.Serialization.JsonPropertyName("PresencePenalty")] public double PresencePenalty { get; set; } = 0.0;
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitRPM")] public int RateLimitRPM { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("RateLimitTPM")] public int RateLimitTPM { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("MaxConcurrency")] public int MaxConcurrency { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("Seed")] public string Seed { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("StopSequences")] public string StopSequences { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("RetryCount")] public int RetryCount { get; set; } = 3;
        [System.Text.Json.Serialization.JsonPropertyName("TimeoutSeconds")] public int TimeoutSeconds { get; set; } = 30;
        [System.Text.Json.Serialization.JsonPropertyName("EnableStreaming")] public bool EnableStreaming { get; set; } = true;
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
        [System.Text.Json.Serialization.JsonPropertyName("EnableStreaming")] public bool? EnableStreaming { get; set; }
    }

    private class SlimModelRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Icon")] public string Icon { get; set; } = "🤖";
        [System.Text.Json.Serialization.JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
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
    }

    private string GetProviderKey(AIProviderCategory provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.Id))
            return provider.Id;

        var raw = $"{provider.ParentCategory ?? string.Empty}::{provider.Name}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = sha.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
    }

    private string GetProviderModelsFilePath(string providerKey)
    {
        var fileName = $"provider-{providerKey}.models.json";
        return StoragePathHelper.GetFilePath("Services", "AI/Library/ProviderModels", fileName);
    }

    private string GetProviderOverridesFilePath(string providerKey)
    {
        var fileName = $"provider-{providerKey}.overrides.json";
        return StoragePathHelper.GetFilePath("Services", "AI/Library/ProviderModels", fileName);
    }

    private void EnsureProviderModelsDirectory()
    {
        var dir = Path.GetDirectoryName(GetProviderModelsFilePath("dummy"));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
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
        data.EnableStreaming = profile.EnableStreaming;
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
        if (!string.IsNullOrEmpty(overrides.Seed)) data.Seed = overrides.Seed;
        if (!string.IsNullOrEmpty(overrides.StopSequences)) data.StopSequences = overrides.StopSequences;

        if (overrides.RetryCount.HasValue) data.RetryCount = overrides.RetryCount.Value;
        if (overrides.TimeoutSeconds.HasValue) data.TimeoutSeconds = overrides.TimeoutSeconds.Value;
        if (overrides.EnableStreaming.HasValue) data.EnableStreaming = overrides.EnableStreaming.Value;
    }

    private static SlimModelRecord CreateSlimFromData(UserConfigurationData data)
    {
        return new SlimModelRecord
        {
            Id = data.Id,
            Name = data.Name,
            Icon = data.Icon,
            Category = data.Category,
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
            SupportedFeatures = data.SupportedFeatures
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
            SupportedFeatures = slim.SupportedFeatures
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
        if (data.EnableStreaming != profile.EnableStreaming) { ov.EnableStreaming = data.EnableStreaming; has = true; }

        return has ? ov : null;
    }

    public IReadOnlyList<UserConfigurationData> GetModelsForProvider(AIProviderCategory provider)
    {
        var key = GetProviderKey(provider);

        if (_providerModelsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = new List<UserConfigurationData>();

        try
        {
            var modelsFile = GetProviderModelsFilePath(key);
            var overridesFile = GetProviderOverridesFilePath(key);

            var slimList = new List<SlimModelRecord>();
            if (File.Exists(modelsFile))
            {
                var json = File.ReadAllText(modelsFile);
                slimList = JsonSerializer.Deserialize<List<SlimModelRecord>>(json, JsonHelper.Default) ?? new List<SlimModelRecord>();
            }

            var overrides = new Dictionary<string, ParameterOverrideRecord>();
            if (File.Exists(overridesFile))
            {
                var json = File.ReadAllText(overridesFile);
                overrides = JsonSerializer.Deserialize<Dictionary<string, ParameterOverrideRecord>>(json, JsonHelper.Default)
                            ?? new Dictionary<string, ParameterOverrideRecord>();
            }

            var profile = GetProfileForProvider(provider);

            var existingIds = new HashSet<string>(DataItems.Select(d => d.Id));

            foreach (var slim in slimList)
            {
                var data = CreateFromSlim(slim, profile, overrides);
                result.Add(data);

                if (!existingIds.Contains(data.Id))
                {
                    DataItems.Add(data);
                    existingIds.Add(data.Id);
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 加载供应商 '{provider.Name}' 模型失败: {ex.Message}");
        }

        _providerModelsCache[key] = result;
        TM.App.Log($"[ModelService] 懒加载供应商 '{provider.Name}' 模型 {result.Count} 条");

        return result;
    }

    public void SaveModelsForProvider(AIProviderCategory provider, IEnumerable<UserConfigurationData> models)
    {
        var key = GetProviderKey(provider);
        var list = models.ToList();

        _providerModelsCache[key] = list;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && dispatcher.CheckAccess())
        {
            lock (_saveModelQueueLock)
            {
                if (!_saveModelVersionByKey.ContainsKey(key)) _saveModelVersionByKey[key] = 0;
                var version = ++_saveModelVersionByKey[key];
                var prev = _saveModelQueueByKey.TryGetValue(key, out var t) ? t : System.Threading.Tasks.Task.CompletedTask;
                _saveModelQueueByKey[key] = prev.ContinueWith(async _ =>
                {
                    await System.Threading.Tasks.Task.Delay(30).ConfigureAwait(false);
                    bool shouldWrite;
                    lock (_saveModelQueueLock)
                    {
                        shouldWrite = !_saveModelVersionByKey.TryGetValue(key, out var cur) || cur == version;
                    }
                    if (!shouldWrite) return;
                    WriteProviderModelsCore(key, provider.Name, list);
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }
        }
        else
        {
            WriteProviderModelsCore(key, provider.Name, list);
        }
    }

    private void WriteProviderModelsCore(string key, string providerName, List<UserConfigurationData> list)
    {
        var modelsFile = GetProviderModelsFilePath(key);
        var overridesFile = GetProviderOverridesFilePath(key);

        if (list.Count == 0)
        {
            try
            {
                if (File.Exists(modelsFile))
                {
                    File.Delete(modelsFile);
                    TM.App.Log($"[ModelService] 供应商 '{providerName}' 无模型，已删除文件 {modelsFile}");
                }
                if (File.Exists(overridesFile)) File.Delete(overridesFile);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModelService] 删除空模型文件 '{modelsFile}' 失败: {ex.Message}");
            }
            return;
        }

        EnsureProviderModelsDirectory();

        try
        {
            var provider = GetAllCategories().FirstOrDefault(c => GetProviderKey(c) == key);
            var profile = provider != null ? GetProfileForProvider(provider) : null;

            var slimList = new List<SlimModelRecord>();
            var overrides = new Dictionary<string, ParameterOverrideRecord>();

            foreach (var data in list)
            {
                slimList.Add(CreateSlimFromData(data));
                if (profile != null)
                {
                    var ov = BuildOverridesFromData(profile, data);
                    if (ov != null) overrides[data.Id] = ov;
                }
            }

            var jsonModels = JsonSerializer.Serialize(slimList, JsonHelper.Default);
            var tmpM = modelsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmpM, jsonModels);
            File.Move(tmpM, modelsFile, overwrite: true);

            if (overrides.Count > 0)
            {
                var jsonOverrides = JsonSerializer.Serialize(overrides, JsonHelper.Default);
                var tmpOv = overridesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmpOv, jsonOverrides);
                File.Move(tmpOv, overridesFile, overwrite: true);
            }
            else if (File.Exists(overridesFile))
            {
                File.Delete(overridesFile);
            }

            TM.App.Log($"[ModelService] 保存供应商 '{providerName}' 模型 {list.Count} 条 -> {modelsFile}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 保存供应商 '{providerName}' 模型失败: {ex.Message}");
        }
    }

    public void EnsureModelsLoadedForCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return;

        var provider = GetAllCategories()
            .FirstOrDefault(c => c.Name == categoryName && c.Level == 2);

        if (provider == null)
            return;

        GetModelsForProvider(provider);
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
                EnableStreaming = profile.EnableStreaming
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
                EnableStreaming = dto.EnableStreaming
            };
        }

        if (!_parameterProfiles.ContainsKey(DefaultProfileId))
        {
            _parameterProfiles[DefaultProfileId] = CreateDefaultProfile();
        }

        var snapshot = _parameterProfiles.Values.ToList();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && dispatcher.CheckAccess())
            _ = System.Threading.Tasks.Task.Run(() => WriteParameterProfilesCore(snapshot));
        else
            WriteParameterProfilesCore(snapshot);
    }

    private void WriteParameterProfilesCore(List<ParameterProfile> profiles)
    {
        try
        {
            var jsonOut = JsonSerializer.Serialize(profiles, JsonHelper.Default);
            var tmpP = _parameterProfilesFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmpP, jsonOut);
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

    public void SetDefaultProfileIdForProvider(AIProviderCategory provider, string? profileId)
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
            var json = File.ReadAllText(providersFile);
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
            File.WriteAllText(tmpPp, jsonOut);
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

    public void AddConfiguration(UserConfigurationData data)
    {
        if (data == null) return;
        if (string.IsNullOrWhiteSpace(data.Id))
        {
            data.Id = ShortIdGenerator.New("D");
        }
        data.CreatedTime = DateTime.Now;
        data.ModifiedTime = DateTime.Now;

        var provider = GetAllCategories()
            .FirstOrDefault(c => c.Level == 2 && (
                (!string.IsNullOrWhiteSpace(data.CategoryId) && string.Equals(c.Id, data.CategoryId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(c.Name, data.Category, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name, data.ProviderName, StringComparison.OrdinalIgnoreCase)));

        if (provider == null)
        {
            TM.App.Log($"[ModelService] AddConfiguration 跳过：找不到供应商分类 '{data.Category}'");
            return;
        }

        data.Category = provider.Name;
        data.CategoryId = provider.Id;
        if (string.IsNullOrWhiteSpace(data.ProviderName))
        {
            data.ProviderName = provider.Name;
        }

        var models = GetModelsForProvider(provider).ToList();
        models.Add(data);
        SaveModelsForProvider(provider, models);

        var existingIndex = DataItems.FindIndex(d => d.Id == data.Id);
        if (existingIndex < 0)
        {
            DataItems.Add(data);
        }

        RaiseConfigurationsChanged();
    }

    public int AddConfigurationsBatch(IEnumerable<UserConfigurationData> dataList, string categoryName)
    {
        if (dataList == null || string.IsNullOrWhiteSpace(categoryName)) return 0;

        var provider = GetAllCategories().FirstOrDefault(c => c.Name == categoryName && c.Level == 2);
        if (provider == null)
        {
            TM.App.Log($"[ModelService] AddConfigurationsBatch 跳过：找不到供应商分类 '{categoryName}'");
            return 0;
        }

        var existingModels = GetModelsForProvider(provider).ToList();
        var existingModelNames = new HashSet<string>(existingModels.Select(m => m.ModelName));

        int addedCount = 0;
        var now = DateTime.Now;

        foreach (var data in dataList)
        {
            if (existingModelNames.Contains(data.ModelName))
                continue;

            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedTime = now;
            data.ModifiedTime = now;

            existingModels.Add(data);
            DataItems.Add(data);
            existingModelNames.Add(data.ModelName);
            addedCount++;
        }

        if (addedCount > 0)
        {
            SaveModelsForProvider(provider, existingModels);
            TM.App.Log($"[ModelService] 批量添加完成: {addedCount}个配置已写入");
            RaiseConfigurationsChanged();
        }

        return addedCount;
    }

    public void UpdateConfiguration(UserConfigurationData data)
    {
        if (data == null) return;
        data.ModifiedTime = DateTime.Now;

        var provider = GetAllCategories().FirstOrDefault(c => c.Level == 2 && (
            (!string.IsNullOrWhiteSpace(data.CategoryId) && string.Equals(c.Id, data.CategoryId, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(c.Name, data.Category, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name, data.ProviderName, StringComparison.OrdinalIgnoreCase)));
        if (provider == null)
        {
            TM.App.Log($"[ModelService] UpdateConfiguration 跳过：找不到供应商分类 '{data.Category}'");
            return;
        }

        data.Category = provider.Name;
        data.CategoryId = provider.Id;
        if (string.IsNullOrWhiteSpace(data.ProviderName))
        {
            data.ProviderName = provider.Name;
        }

        var models = GetModelsForProvider(provider).ToList();
        var index = models.FindIndex(m => m.Id == data.Id);
        if (index >= 0)
        {
            models[index] = data;
        }
        else
        {
            models.Add(data);
        }

        SaveModelsForProvider(provider, models);

        var globalIndex = DataItems.FindIndex(d => d.Id == data.Id);
        if (globalIndex >= 0)
        {
            DataItems[globalIndex] = data;
        }

        RaiseConfigurationsChanged();
    }

    public void DeleteConfiguration(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var existing = DataItems.FirstOrDefault(d => d.Id == id);
        if (existing == null)
            return;

        var provider = GetAllCategories().FirstOrDefault(c => c.Name == existing.Category && c.Level == 2);
        if (provider != null)
        {
            var models = GetModelsForProvider(provider).ToList();
            models.RemoveAll(m => m.Id == id);
            SaveModelsForProvider(provider, models);
        }

        DataItems.RemoveAll(d => d.Id == id);
        RaiseConfigurationsChanged();
    }

    public int DeleteConfigurationsByCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return 0;

        var provider = GetAllCategories().FirstOrDefault(c => c.Name == categoryName && c.Level == 2);
        if (provider == null) return 0;

        var models = GetModelsForProvider(provider).ToList();
        int count = models.Count;

        models.Clear();
        SaveModelsForProvider(provider, models);

        var idKey = GetProviderKey(provider);
        var hashKey = ComputeHashProviderKey(provider.ParentCategory ?? string.Empty, provider.Name);
        ForceDeleteProviderFiles(idKey);
        ForceDeleteProviderFiles(hashKey);

        int memoryCount = DataItems.RemoveAll(d => d.Category == categoryName);
        int totalRemoved = count + memoryCount;

        TM.App.Log($"[ModelService] 批量删除分类配置: {categoryName}, 文件={count}条, 内存残留={memoryCount}条, idKey={idKey}, hashKey={hashKey}");
        if (totalRemoved > 0)
        {
            RaiseConfigurationsChanged();
        }

        return count;
    }

    public void CleanupOrphanedProviderFiles()
    {
        try
        {
            var validKeys = GetAllCategories()
                .Where(c => c.Level == 2)
                .SelectMany(c =>
                {
                    var idK = GetProviderKey(c);
                    var hashK = ComputeHashProviderKey(c.ParentCategory ?? string.Empty, c.Name);
                    return new[] { idK, hashK };
                })
                .ToHashSet(StringComparer.Ordinal);

            var dir = Path.GetDirectoryName(GetProviderModelsFilePath("dummy"));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            int cleaned = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "provider-*.json"))
            {
                var fileName = Path.GetFileName(file);
                var withoutExt = Path.GetFileNameWithoutExtension(fileName);
                var withoutSecondExt = Path.GetFileNameWithoutExtension(withoutExt);
                var key = withoutSecondExt.StartsWith("provider-") ? withoutSecondExt[9..] : null;

                if (key != null && !validKeys.Contains(key))
                {
                    try
                    {
                        File.Delete(file);
                        _providerModelsCache.Remove(key);
                        cleaned++;
                        TM.App.Log($"[ModelService] 孤立文件已清理: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ModelService] 清理孤立文件失败 {fileName}: {ex.Message}");
                    }
                }
            }

            if (cleaned > 0)
                TM.App.Log($"[ModelService] 孤立文件清理完成，共清理 {cleaned} 个文件");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] CleanupOrphanedProviderFiles 失败（非致命）: {ex.Message}");
        }
    }

    private static string ComputeHashProviderKey(string parentCategory, string name)
    {
        var raw = $"{parentCategory}::{name}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = sha.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
    }

    private void ForceDeleteProviderFiles(string key)
    {
        try
        {
            var modelsFile = GetProviderModelsFilePath(key);
            var overridesFile = GetProviderOverridesFilePath(key);
            if (File.Exists(modelsFile)) File.Delete(modelsFile);
            if (File.Exists(overridesFile)) File.Delete(overridesFile);
            _providerModelsCache.Remove(key);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] ForceDeleteProviderFiles key={key} 失败（非致命）: {ex.Message}");
        }
    }

    protected override (int categoriesDeleted, int dataDeleted) CascadeDeleteCategoryNames(List<string> categoryNames)
    {
        var nameSet = new HashSet<string>(categoryNames, StringComparer.Ordinal);

        foreach (var cat in GetAllCategories().Where(c => c.Level == 2 && nameSet.Contains(c.Name) && !c.IsBuiltIn))
        {
            var idKey = GetProviderKey(cat);
            var hashKey = ComputeHashProviderKey(cat.ParentCategory ?? string.Empty, cat.Name);
            ForceDeleteProviderFiles(idKey);
            ForceDeleteProviderFiles(hashKey);
        }

        var result = base.CascadeDeleteCategoryNames(categoryNames);

        CleanupOrphanedProviderFiles();
        _providerModelsCache.Clear();

        return result;
    }

    public int ClearAllConfigurations() => ClearAllData();
}

public class ParameterProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public double TopP { get; set; } = 1.0;
    public double FrequencyPenalty { get; set; } = 0.0;
    public double PresencePenalty { get; set; } = 0.0;
    public int RateLimitRPM { get; set; } = 0;
    public int RateLimitTPM { get; set; } = 0;
    public int MaxConcurrency { get; set; } = 5;
    public string Seed { get; set; } = string.Empty;
    public string StopSequences { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableStreaming { get; set; } = true;
}
