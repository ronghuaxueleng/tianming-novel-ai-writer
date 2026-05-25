using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.Security;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{

    private List<AICategory> _categories = new();
    private List<AIProvider> _providers = new();
    private List<AIModel> _models = new();
    private List<UserConfiguration> _userConfigurations = new();
    private readonly object _userConfigurationsLock = new();
    private readonly SemaphoreSlim _userConfigurationsSaveLock = new(1, 1);

    public event EventHandler? ConfigurationsChanged;

    private readonly HashSet<string> _compatibilityFallbackModels = new(StringComparer.OrdinalIgnoreCase);

    private static string BaseDeveloperMessage => SemanticKernel.Prompts.PromptLibrary.GetDeveloperPrompt();

    private readonly string _libraryPath;
    private readonly string _configurationsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    private sealed class BusinessSessionState
    {
        public string Key { get; }
        public ChatHistory History { get; }
        public bool IsInitialized { get; set; }
        public bool IsDirty { get; set; }
        public SemaphoreSlim Gate { get; }

        public BusinessSessionState(string key)
        {
            Key = key;
            History = new ChatHistory();
            Gate = new SemaphoreSlim(1, 1);
        }
    }

    private readonly Dictionary<string, BusinessSessionState> _businessSessions = new(StringComparer.Ordinal);
    private string? _lastDirtyBusinessSessionKey;

    public AIService()
    {
        _libraryPath = StoragePathHelper.GetFilePath("Services", "AI", "Library");
        _configurationsPath = StoragePathHelper.GetFilePath("Services", "AI", "Configurations");

        _jsonOptions = JsonHelper.CnDefault;

        LoadLibrary();
        LoadUserConfigurations();

        TM.App.Log("[AIService] AI核心服务已初始化");
    }

    public bool HasDirtyBusinessSession(string businessSessionKey)
    {
        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            return false;
        }

        lock (_businessSessions)
        {
            return _businessSessions.TryGetValue(businessSessionKey, out var state) && state.IsDirty;
        }
    }

    public bool HasDirtyBusinessSessionsByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        var prefixWithUnderscore = prefix + "_";
        lock (_businessSessions)
        {
            foreach (var kv in _businessSessions)
            {
                if (!kv.Value.IsDirty)
                {
                    continue;
                }

                var key = kv.Key;
                if (string.Equals(key, prefix, StringComparison.Ordinal)
                    || key.StartsWith(prefixWithUnderscore, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetDirtyBusinessSessionKey(out string businessSessionKey)
    {
        businessSessionKey = string.Empty;

        lock (_businessSessions)
        {
            if (string.IsNullOrWhiteSpace(_lastDirtyBusinessSessionKey))
            {
                return false;
            }

            if (_businessSessions.TryGetValue(_lastDirtyBusinessSessionKey, out var state) && state.IsDirty)
            {
                businessSessionKey = _lastDirtyBusinessSessionKey;
                return true;
            }

            _lastDirtyBusinessSessionKey = null;
            return false;
        }
    }

    public void EndBusinessSession(string businessSessionKey)
    {
        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            return;
        }

        BusinessSessionState? removed = null;
        lock (_businessSessions)
        {
            if (_businessSessions.TryGetValue(businessSessionKey, out removed))
            {
                _businessSessions.Remove(businessSessionKey);
                if (string.Equals(_lastDirtyBusinessSessionKey, businessSessionKey, StringComparison.Ordinal))
                {
                    _lastDirtyBusinessSessionKey = null;
                }
            }
        }
        if (removed != null)
        {
            removed.IsDirty = false;
            removed.IsInitialized = false;
        }
    }

    public void ClearAllBusinessSessions()
    {
        lock (_businessSessions)
        {
            _businessSessions.Clear();
            _lastDirtyBusinessSessionKey = null;
        }
        TM.App.Log("[AIService] 已清理所有业务会话");
    }

    public void EndBusinessSessionsByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;

        var prefixWithUnderscore = prefix + "_";
        List<BusinessSessionState> removed = new();

        lock (_businessSessions)
        {
            var keysToRemove = _businessSessions.Keys
                .Where(k => string.Equals(k, prefix, StringComparison.Ordinal) ||
                            k.StartsWith(prefixWithUnderscore, StringComparison.Ordinal))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_businessSessions.TryGetValue(key, out var state))
                {
                    _businessSessions.Remove(key);
                    removed.Add(state);
                }
            }

            if (_lastDirtyBusinessSessionKey != null &&
                (string.Equals(_lastDirtyBusinessSessionKey, prefix, StringComparison.Ordinal) ||
                 _lastDirtyBusinessSessionKey.StartsWith(prefixWithUnderscore, StringComparison.Ordinal)))
            {
                _lastDirtyBusinessSessionKey = null;
            }
        }

        foreach (var state in removed)
        {
            state.IsDirty = false;
            state.IsInitialized = false;
        }

        if (removed.Count > 0)
            TM.App.Log($"[AIService] 已清理业务会话（前缀={prefix}）：{removed.Count} 个");
    }

    public async System.Threading.Tasks.Task<GenerationResult> GenerateInBusinessSessionAsync(
        string businessSessionKey,
        Func<System.Threading.Tasks.Task<string>>? initialContextProvider,
        string userPrompt,
        System.Threading.CancellationToken ct,
        bool isNavigationGuarded = true)
        => await GenerateInBusinessSessionAsync(businessSessionKey, initialContextProvider, userPrompt, null, ct, isNavigationGuarded);

    public async System.Threading.Tasks.Task<GenerationResult> GenerateInBusinessSessionAsync(
        string businessSessionKey,
        Func<System.Threading.Tasks.Task<string>>? initialContextProvider,
        string userPrompt,
        IProgress<string>? progress,
        System.Threading.CancellationToken ct,
        bool isNavigationGuarded = true)
    {
        var result = new GenerationResult();

        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            result.Success = false;
            result.ErrorMessage = "BusinessSessionKey为空";
            return result;
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var activeConfig = GetActiveConfiguration();
            if (activeConfig == null)
            {
                result.Success = false;
                result.ErrorMessage = "当前没有激活的AI模型";
                return result;
            }

            var model = GetModelById(activeConfig.ModelId);
            if (model == null)
            {
                TM.App.Log($"[AIService] 模型库未收录 {activeConfig.ModelId}，将作为自定义模型继续调用");
            }

            BusinessSessionState state;
            lock (_businessSessions)
            {
                if (!_businessSessions.TryGetValue(businessSessionKey, out state!))
                {
                    state = new BusinessSessionState(businessSessionKey);
                    _businessSessions[businessSessionKey] = state;
                }
            }

            await state.Gate.WaitAsync(ct);
            try
            {
                state.IsDirty = true;
                if (isNavigationGuarded)
                    _lastDirtyBusinessSessionKey = businessSessionKey;

                if (!state.IsInitialized)
                {
                    var developerPrompt = GetEffectiveDeveloperMessage(activeConfig) ?? string.Empty;
                    var initialContext = string.Empty;
                    if (initialContextProvider != null)
                    {
                        initialContext = await initialContextProvider();
                    }

                    var systemText = string.IsNullOrWhiteSpace(initialContext)
                        ? developerPrompt
                        : developerPrompt + "\n\n" + initialContext;

                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        state.History.AddSystemMessage(systemText);
                    }

                    state.IsInitialized = true;
                }

                var sk = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
                var text = await sk.GenerateWithChatHistoryAsync(state.History, userPrompt, progress, ct);

                if (text.StartsWith("[会话终止]", StringComparison.Ordinal))
                {
                    EndBusinessSession(businessSessionKey);
                    result.Success = false;
                    result.ErrorMessage = text;
                    return result;
                }

                if (text.StartsWith("[错误]") || text.StartsWith("[已取消]"))
                {
                    result.Success = false;
                    result.ErrorMessage = text;
                    return result;
                }

                result.Success = true;
                result.Content = text;
                return result;
            }
            finally
            {
                state.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] GenerateInBusinessSessionAsync 调用失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = $"AI生成失败：{ex.Message}";
            return result;
        }
    }

    #region 库加载

    private void LoadLibrary()
    {
        var newCategories = new List<AICategory>();
        var newProviders  = new List<AIProvider>();
        var newModels     = new List<AIModel>();

        try
        {
            var categoriesFile = Path.Combine(_libraryPath, "categories.json");
            if (File.Exists(categoriesFile))
            {
                var json = File.ReadAllText(categoriesFile);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith("["))
                {
                    newCategories = JsonSerializer.Deserialize<List<AICategory>>(json, _jsonOptions) ?? new List<AICategory>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json, _jsonOptions);
                    newCategories = wrapper?.Categories ?? new List<AICategory>();
                }

                TM.App.Log($"[AIService] 加载分类: {newCategories.Count}个");
            }

            var providersFile = Path.Combine(_libraryPath, "providers.json");
            if (File.Exists(providersFile))
            {
                var json = File.ReadAllText(providersFile);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith("["))
                {
                    newProviders = JsonSerializer.Deserialize<List<AIProvider>>(json, _jsonOptions) ?? new List<AIProvider>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<ProviderWrapper>(json, _jsonOptions);
                    newProviders = wrapper?.Providers ?? new List<AIProvider>();
                }

                TM.App.Log($"[AIService] 加载供应商: {newProviders.Count}个");
            }

            var modelsFile = Path.Combine(_libraryPath, "models.json");
            if (File.Exists(modelsFile))
            {
                var json = File.ReadAllText(modelsFile);
                var trimmed = json.TrimStart();

                if (trimmed.StartsWith("["))
                {
                    newModels = JsonSerializer.Deserialize<List<AIModel>>(json, _jsonOptions) ?? new List<AIModel>();
                }
                else
                {
                    var wrapper = JsonSerializer.Deserialize<ModelWrapper>(json, _jsonOptions);
                    newModels = wrapper?.Models ?? new List<AIModel>();
                }

                TM.App.Log($"[AIService] 从 models.json 加载模型: {newModels.Count}个");
            }
            else
            {
                var providerModelsPath = Path.Combine(_libraryPath, "ProviderModels");
                if (Directory.Exists(providerModelsPath))
                {
                    var modelFiles = Directory.GetFiles(providerModelsPath, "*.models.json");
                    foreach (var file in modelFiles)
                    {
                        try
                        {
                            var json = File.ReadAllText(file);
                            var models = JsonSerializer.Deserialize<List<AIModel>>(json, _jsonOptions);
                            if (models != null && models.Count > 0)
                            {
                                newModels.AddRange(models);
                            }
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[AIService] 加载 {Path.GetFileName(file)} 失败: {ex.Message}");
                        }
                    }
                    TM.App.Log($"[AIService] 从 ProviderModels 目录加载模型: {newModels.Count}个（来自 {modelFiles.Length} 个文件）");
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 加载模型库失败: {ex.Message}");
        }

        _categories = newCategories;
        _providers  = newProviders;
        _models     = newModels;

        try
        {
            FillModelCapabilities();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 填充模型协议能力失败: {ex.Message}");
        }
    }

    public void ReloadLibrary()
    {
        LoadLibrary();
    }

    private void LoadUserConfigurations()
    {
        try
        {
            Directory.CreateDirectory(_configurationsPath);
            var configFile = Path.Combine(_configurationsPath, "user_configurations.json");

            if (File.Exists(configFile))
            {
                var json = File.ReadAllText(configFile);
                var wrapper = JsonSerializer.Deserialize<ConfigurationWrapper>(json, _jsonOptions);
                var loaded = wrapper?.Configurations ?? new List<UserConfiguration>();
                lock (_userConfigurationsLock)
                    _userConfigurations = loaded;
                TM.App.Log($"[AIService] 加载用户配置: {loaded.Count}个");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 加载用户配置失败: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task LoadUserConfigurationsAsync()
    {
        try
        {
            Directory.CreateDirectory(_configurationsPath);
            var configFile = Path.Combine(_configurationsPath, "user_configurations.json");

            if (File.Exists(configFile))
            {
                var json = await File.ReadAllTextAsync(configFile);
                var wrapper = JsonSerializer.Deserialize<ConfigurationWrapper>(json, _jsonOptions);
                var loaded = wrapper?.Configurations ?? new List<UserConfiguration>();
                lock (_userConfigurationsLock)
                    _userConfigurations = loaded;
                TM.App.Log($"[AIService] 异步加载用户配置: {loaded.Count}个");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 异步加载用户配置失败: {ex.Message}");
        }
    }

    private async void SaveUserConfigurations()
    {
        var acquired = false;
        try
        {
            await _userConfigurationsSaveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            Directory.CreateDirectory(_configurationsPath);
            var configFile = Path.Combine(_configurationsPath, "user_configurations.json");

            UserConfiguration[] snapshot;
            lock (_userConfigurationsLock)
                snapshot = _userConfigurations.ToArray();

            var wrapper = new ConfigurationWrapper
            {
                Configurations = snapshot
                    .Select(CloneForStorage)
                    .ToList()
            };
            var json = JsonSerializer.Serialize(wrapper, _jsonOptions);

            var tmp = configFile + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, configFile, overwrite: true);

            TM.App.Log($"[AIService] 保存用户配置: {snapshot.Length}个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 保存用户配置失败: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _userConfigurationsSaveLock.Release();
        }
    }

    private static UserConfiguration CloneForStorage(UserConfiguration config)
    {
        return new UserConfiguration
        {
            Id = config.Id,
            Name = config.Name,
            ProviderId = config.ProviderId,
            ModelId = config.ModelId,
            CustomEndpoint = config.CustomEndpoint,
            Temperature = config.Temperature,
            MaxTokens = config.MaxTokens,
            ContextWindow = config.ContextWindow,
            IsActive = config.IsActive,
            IsEnabled = config.IsEnabled,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            DeveloperMessage = config.DeveloperMessage
        };
    }

    #endregion

    #region 公共API - 库查询

    public IReadOnlyList<AICategory> GetAllCategories() => _categories.OrderBy(c => c.Order).ToList();

    public IReadOnlyList<AIProvider> GetAllProviders() => _providers.OrderBy(p => p.Order).ToList();

    public List<AIProvider> GetProvidersByCategory(string categoryId)
    {
        return _providers.Where(p => p.Category == categoryId).OrderBy(p => p.Order).ToList();
    }

    public IReadOnlyList<AIModel> GetAllModels() => _models.OrderBy(m => m.Order).ToList();

    public IReadOnlyList<AIModel> GetModelsByProvider(string providerId)
    {
        return _models.Where(m => m.ProviderId == providerId).OrderBy(m => m.Order).ToList();
    }

    public AIProvider? GetProviderById(string providerId)
    {
        return _providers.FirstOrDefault(p => p.Id == providerId);
    }

    public AIModel? GetModelById(string modelId)
    {
        var model = _models.FirstOrDefault(m => m.Id == modelId);
        if (model != null)
            return model;

        return _models.FirstOrDefault(m => m.Name == modelId);
    }

    public bool IsCompatibilityFallbackEnabled(string providerId, string modelId)
    {
        var key = BuildCompatibilityKey(providerId, modelId);
        return _compatibilityFallbackModels.Contains(key);
    }

    public void EnableCompatibilityFallback(string providerId, string modelId)
    {
        var key = BuildCompatibilityKey(providerId, modelId);
        if (string.IsNullOrWhiteSpace(key))
            return;

        _compatibilityFallbackModels.Add(key);
    }

    private static string BuildCompatibilityKey(string providerId, string modelId)
    {
        var p = providerId ?? string.Empty;
        var m = modelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(p) && string.IsNullOrWhiteSpace(m))
            return string.Empty;

        return p.ToLowerInvariant() + "|" + m.ToLowerInvariant();
    }

    public static string GetEffectiveDeveloperMessage(UserConfiguration? config)
    {
        if (config == null)
        {
            return BaseDeveloperMessage;
        }

        if (string.IsNullOrWhiteSpace(config.DeveloperMessage))
        {
            return BaseDeveloperMessage;
        }

        return BaseDeveloperMessage + "\n\n" + config.DeveloperMessage;
    }

    #endregion

    #region 内部辅助方法 - 模型协议能力填充

    private void FillModelCapabilities()
    {
        if (_models == null || _models.Count == 0 || _providers == null || _providers.Count == 0)
            return;

        try
        {
            var capabilitiesRoot = StoragePathHelper.GetFilePath("Services", "AI", "Capabilities");
            var rulesFile = Path.Combine(capabilitiesRoot, "model-capabilities.json");

            if (!File.Exists(rulesFile))
            {
                TM.App.Log("[AIService] 未找到模型能力配置文件 model-capabilities.json，跳过能力填充");
                return;
            }

            var json = File.ReadAllText(rulesFile);
            var ruleSet = JsonSerializer.Deserialize<ModelCapabilityRuleSet>(json, _jsonOptions);
            var rules = ruleSet?.Rules ?? new List<ModelCapabilityRule>();

            if (rules.Count == 0)
            {
                TM.App.Log("[AIService] 模型能力配置为空，跳过能力填充");
                return;
            }

            var providerMap = new Dictionary<string, AIProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _providers)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) && !providerMap.ContainsKey(p.Id))
                {
                    providerMap[p.Id] = p;
                }
            }

            foreach (var model in _models)
            {
                if (string.IsNullOrWhiteSpace(model.ProviderId))
                    continue;

                if (!providerMap.TryGetValue(model.ProviderId, out var provider))
                    continue;

                foreach (var rule in rules)
                {
                    if (!RuleMatches(rule, provider, model))
                        continue;

                    if (rule.SupportsArrayContent.HasValue)
                        model.SupportsArrayContent = rule.SupportsArrayContent.Value;

                    if (rule.SupportsDeveloperMessage.HasValue)
                        model.SupportsDeveloperMessage = rule.SupportsDeveloperMessage.Value;

                    if (rule.SupportsStreamOptions.HasValue)
                        model.SupportsStreamOptions = rule.SupportsStreamOptions.Value;

                    if (rule.SupportsServiceTier.HasValue)
                        model.SupportsServiceTier = rule.SupportsServiceTier.Value;

                    if (rule.SupportsEnableThinking.HasValue)
                        model.SupportsEnableThinking = rule.SupportsEnableThinking.Value;

                    if (!string.IsNullOrWhiteSpace(rule.ServiceTier))
                        model.ServiceTier = rule.ServiceTier;
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] 解析模型能力配置失败: {ex.Message}");
        }
    }

    private static bool RuleMatches(ModelCapabilityRule rule, AIProvider provider, AIModel model)
    {
        if (rule == null)
            return false;

        var providerId = provider.Id ?? string.Empty;
        var providerName = (provider.Name ?? string.Empty).ToLowerInvariant();
        var endpoint = (provider.ApiEndpoint ?? string.Empty).ToLowerInvariant();
        var modelId = model.Id ?? string.Empty;
        var modelName = (model.Name ?? string.Empty).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(rule.ProviderId) &&
            !string.Equals(rule.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ProviderNameContains) &&
            !providerName.Contains(rule.ProviderNameContains.ToLowerInvariant()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.EndpointContains) &&
            !endpoint.Contains(rule.EndpointContains.ToLowerInvariant()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ModelId) &&
            !string.Equals(rule.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ModelNameContains) &&
            !modelName.Contains(rule.ModelNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    #endregion

    #region 公共API - 用户配置管理

    public IReadOnlyList<UserConfiguration> GetAllConfigurations()
    {
        lock (_userConfigurationsLock)
            return _userConfigurations.ToList();
    }

    public UserConfiguration? GetActiveConfiguration()
    {
        lock (_userConfigurationsLock)
            return _userConfigurations.FirstOrDefault(c => c.IsActive);
    }

    public void AddConfiguration(UserConfiguration config)
    {
        UserConfiguration? existing;
        lock (_userConfigurationsLock)
            existing = _userConfigurations.FirstOrDefault(c =>
                string.Equals(c.ProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.ModelId, config.ModelId, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            config.Id = existing.Id;
            config.CreatedAt = existing.CreatedAt;
            UpdateConfiguration(config);
            return;
        }

        config.Id = ShortIdGenerator.New("D");
        config.CreatedAt = DateTime.Now;
        config.UpdatedAt = DateTime.Now;
        lock (_userConfigurationsLock)
            _userConfigurations.Add(config);
        SaveUserConfigurations();
        TM.App.Log($"[AIService] 添加配置: {config.Name}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateConfiguration(UserConfiguration config)
    {
        int index;
        lock (_userConfigurationsLock)
            index = _userConfigurations.FindIndex(c => c.Id == config.Id);
        if (index >= 0)
        {
            config.UpdatedAt = DateTime.Now;
            lock (_userConfigurationsLock)
                _userConfigurations[index] = config;
            SaveUserConfigurations();
            TM.App.Log($"[AIService] 更新配置: {config.Name}");
            ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void DeleteConfiguration(string configId)
    {
        UserConfiguration? config;
        lock (_userConfigurationsLock)
            config = _userConfigurations.FirstOrDefault(c => c.Id == configId);
        if (config != null)
        {
            lock (_userConfigurationsLock)
                _userConfigurations.Remove(config);
            SaveUserConfigurations();
            TM.App.Log($"[AIService] 删除配置: {config.Name}");
            ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetActiveConfiguration(string configId)
    {
        lock (_userConfigurationsLock)
        {
            foreach (var config in _userConfigurations)
                config.IsActive = config.Id == configId;
        }
        SaveUserConfigurations();
        TM.App.Log($"[AIService] 激活配置: {configId}");
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
    }

    void IAIConfigurationService.SetActiveConfiguration(UserConfiguration configuration)
    {
        if (configuration == null || string.IsNullOrWhiteSpace(configuration.Id))
        {
            return;
        }

        SetActiveConfiguration(configuration.Id);
    }

    #endregion

    #region 公共API - 统一生成入口

    public async System.Threading.Tasks.Task<GenerationResult> GenerateAsync(string prompt)
    {
        return await GenerateAsync(prompt, System.Threading.CancellationToken.None);
    }

    System.Threading.Tasks.Task<GenerationResult> IAITextGenerationService.GenerateAsync(string prompt, System.Threading.CancellationToken ct)
    {
        return GenerateAsync(prompt, ct);
    }

    public async System.Threading.Tasks.Task<GenerationResult> GenerateAsync(string prompt, System.Threading.CancellationToken ct)
    {
        var result = new GenerationResult();

        try
        {
            ct.ThrowIfCancellationRequested();

            var activeConfig = GetActiveConfiguration();
            if (activeConfig == null)
            {
                const string configureMessage = "当前没有激活的AI模型，请前往“智能助手 > 模型管理”完成配置后重试。";
                TM.App.Log("[AIService] 未检测到激活的AI模型配置");

                result.Success = false;
                result.ErrorMessage = configureMessage;
                return result;
            }

            if (string.IsNullOrWhiteSpace(activeConfig.ProviderId) || string.IsNullOrWhiteSpace(activeConfig.ModelId))
            {
                TM.App.Log($"[AIService] 激活配置缺少 ProviderId 或 ModelId: {activeConfig.Id}");
                result.Success = false;
                result.ErrorMessage = "AI配置缺少供应商或模型信息，请检查模型设置。";
                return result;
            }

            var provider = GetProviderById(activeConfig.ProviderId);
            if (provider == null)
            {
                TM.App.Log($"[AIService] 未找到供应商: {activeConfig.ProviderId}");
                result.Success = false;
                result.ErrorMessage = $"未找到供应商: {activeConfig.ProviderId}";
                return result;
            }

            var model = GetModelById(activeConfig.ModelId);
            var modelDisplayName = model?.Name ?? activeConfig.ModelId;
            if (model == null)
            {
                TM.App.Log($"[AIService] 模型库未收录 {activeConfig.ModelId}，将作为自定义模型继续调用");
            }

            TM.App.Log($"[AIService] 使用激活配置 {activeConfig.Name} 调用模型 {modelDisplayName} 生成内容");

            try
            {
                var skService = ServiceLocator.Get<SemanticKernel.SKChatService>();

                var developerPrompt = GetEffectiveDeveloperMessage(activeConfig) ?? string.Empty;

                var text = await skService.GenerateOneShotAsync(
                    developerPrompt,
                    prompt,
                    ct);

                if (text.StartsWith("[错误]") || text.StartsWith("[已取消]"))
                {
                    result.Success = false;
                    result.ErrorMessage = text;
                    return result;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    result.Success = false;
                    result.ErrorMessage = "AI未返回任何内容，请稍后重试";
                    return result;
                }

                result.Success = true;
                result.Content = text;
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AIService] SK调用失败: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] GenerateAsync 调用失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = $"AI生成失败：{ex.Message}";
            return result;
        }
    }

    public class GenerationResult
    {
        public string Content { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    #endregion

    #region 内部数据包装类

    private class CategoryWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Categories")] public List<AICategory> Categories { get; set; } = new();
    }

    private class ProviderWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Providers")] public List<AIProvider> Providers { get; set; } = new();
    }

    private class ModelWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Models")] public List<AIModel> Models { get; set; } = new();
    }

    private class ConfigurationWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("Configurations")] public List<UserConfiguration> Configurations { get; set; } = new();
    }

    private class ModelCapabilityRuleSet
    {
        [System.Text.Json.Serialization.JsonPropertyName("Rules")] public List<ModelCapabilityRule> Rules { get; set; } = new();
    }

    private class ModelCapabilityRule
    {
        [System.Text.Json.Serialization.JsonPropertyName("ProviderId")] public string? ProviderId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ProviderNameContains")] public string? ProviderNameContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndpointContains")] public string? EndpointContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ModelId")] public string? ModelId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ModelNameContains")] public string? ModelNameContains { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsArrayContent")] public bool? SupportsArrayContent { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsDeveloperMessage")] public bool? SupportsDeveloperMessage { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsStreamOptions")] public bool? SupportsStreamOptions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsServiceTier")] public bool? SupportsServiceTier { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SupportsEnableThinking")] public bool? SupportsEnableThinking { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ServiceTier")] public string? ServiceTier { get; set; }
    }

    #endregion
}
