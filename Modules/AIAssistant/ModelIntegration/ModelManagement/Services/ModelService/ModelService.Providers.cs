using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

public partial class ModelService
{
    public async Task SyncProvidersFromCategoriesAsync()
    {
        try
        {
            var providersFile = StoragePathHelper.GetFilePath("Services", "AI/Library", "providers.json");

            var existingProviders = new List<ProviderData>();
            if (File.Exists(providersFile))
            {
                try
                {
                    var existingJson = await File.ReadAllTextAsync(providersFile).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(tmpPf, jsonOut).ConfigureAwait(false);
            File.Move(tmpPf, providersFile, overwrite: true);

            TM.App.Log($"[ModelService] 同步providers.json: {providers.Count}个供应商");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelService] 同步providers.json失败: {ex.Message}");
        }
    }

    private async Task LoadProvidersFromJsonAsync()
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

            var json = await File.ReadAllTextAsync(providersFile).ConfigureAwait(false);
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
                    LogIfPublicProviderId(provider.Id, $"[ModelService] providers.json中的供应商 '{provider.Name}' 未在categories中找到对应节点");
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

    private string GetProviderKey(AIProviderCategory provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.Id))
            return provider.Id;

        var raw = $"{provider.ParentCategory ?? string.Empty}::{provider.Name}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
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

    public IReadOnlyList<UserConfigurationData> GetModelsForProvider(AIProviderCategory provider)
    {
        var key = GetProviderKey(provider);

        if (_providerModelsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        provider.LogIfPublic($"[ModelService] GetModelsForProvider '{provider.Name}' 缓存未命中，fire-and-forget 后台加载");
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Task.Run(async () =>
        {
            try
            {
                if (_providerModelsCache.ContainsKey(key)) return;
                await GetModelsForProviderAsync(provider).ConfigureAwait(false);
                if (dispatcher != null)
                    await dispatcher.InvokeAsync(RaiseConfigurationsChanged);
            }
            catch (Exception ex) { provider.LogIfPublic($"[ModelService] 懒加载失败: {ex.Message}"); }
        }).SafeFireAndForget(ex => provider.LogIfPublic($"[ModelService] 后台任务异常: {ex.Message}"));
        return new List<UserConfigurationData>();
    }

    private async Task<IReadOnlyList<UserConfigurationData>> GetModelsForProviderAsync(AIProviderCategory provider)
    {
        var key = GetProviderKey(provider);
        if (_providerModelsCache.TryGetValue(key, out var cached))
            return cached;

        var result = new List<UserConfigurationData>();
        try
        {
            var modelsFile = GetProviderModelsFilePath(key);
            var overridesFile = GetProviderOverridesFilePath(key);

            var slimList = new List<SlimModelRecord>();
            if (File.Exists(modelsFile))
            {
                var json = await File.ReadAllTextAsync(modelsFile).ConfigureAwait(false);
                slimList = JsonSerializer.Deserialize<List<SlimModelRecord>>(json, JsonHelper.Default) ?? new List<SlimModelRecord>();
            }

            var overrides = new Dictionary<string, ParameterOverrideRecord>();
            if (File.Exists(overridesFile))
            {
                var json = await File.ReadAllTextAsync(overridesFile).ConfigureAwait(false);
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
            if (!provider.IsTianmingPrivate())
                TM.App.Log($"[ModelService] 异步加载供应商 '{provider.Name}' 模型失败: {ex.Message}");
        }

        _providerModelsCache[key] = result;
        if (!provider.IsTianmingPrivate())
            TM.App.Log($"[ModelService] 异步懒加载供应商 '{provider.Name}' 模型 {result.Count} 条");
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
                    await WriteProviderModelsCoreAsync(key, provider.Name, list).ConfigureAwait(false);
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }
        }
        else
        {
            _ = WriteProviderModelsCoreAsync(key, provider.Name, list);
        }
    }

    private async Task WriteProviderModelsCoreAsync(string key, string providerName, List<UserConfigurationData> list)
    {
        var modelsFile = GetProviderModelsFilePath(key);
        var overridesFile = GetProviderOverridesFilePath(key);
        var provider = GetAllCategories().FirstOrDefault(c => GetProviderKey(c) == key);

        if (list.Count == 0)
        {
            try
            {
                if (File.Exists(modelsFile))
                {
                    File.Delete(modelsFile);
                    LogIfPublicProviderId(provider?.Id, $"[ModelService] 供应商 '{providerName}' 无模型，已删除文件 {modelsFile}");
                }
                if (File.Exists(overridesFile)) File.Delete(overridesFile);
            }
            catch (Exception ex)
            {
                LogIfPublicProviderId(provider?.Id, $"[ModelService] 删除空模型文件 '{modelsFile}' 失败: {ex.Message}");
            }
            return;
        }

        EnsureProviderModelsDirectory();

        try
        {
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
            await File.WriteAllTextAsync(tmpM, jsonModels).ConfigureAwait(false);
            File.Move(tmpM, modelsFile, overwrite: true);

            if (overrides.Count > 0)
            {
                var jsonOverrides = JsonSerializer.Serialize(overrides, JsonHelper.Default);
                var tmpOv = overridesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpOv, jsonOverrides).ConfigureAwait(false);
                File.Move(tmpOv, overridesFile, overwrite: true);
            }
            else if (File.Exists(overridesFile))
            {
                File.Delete(overridesFile);
            }

            if (InfoLogDedup.ShouldLog($"ModelService:Save:{key}"))
            {
                LogIfPublicProviderId(provider?.Id, $"[ModelService] 保存供应商 '{providerName}' 模型 {list.Count} 条 -> {modelsFile}");
            }
        }
        catch (Exception ex)
        {
            LogIfPublicProviderId(provider?.Id, $"[ModelService] 保存供应商 '{providerName}' 模型失败: {ex.Message}");
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
}
