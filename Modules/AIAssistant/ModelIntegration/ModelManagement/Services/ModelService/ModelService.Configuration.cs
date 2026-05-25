using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;

public partial class ModelService
{
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

        var provider = GetAllCategories().FirstOrDefault(c => c.Level == 2 && (
            (!string.IsNullOrWhiteSpace(existing.CategoryId)
                && string.Equals(c.Id, existing.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(c.Name, existing.Category, StringComparison.OrdinalIgnoreCase)));
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
                var key = withoutSecondExt.StartsWith("provider-", StringComparison.Ordinal) ? withoutSecondExt[9..] : null;

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
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
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

    public override (int categoriesDeleted, int dataDeleted) CascadeDeleteCategory(string categoryName)
    {
        var root = GetAllCategories().FirstOrDefault(c => c.Name == categoryName);
        if (root != null && root.IsBuiltIn)
        {
            var (catRemoved, dataRemoved) = base.CascadeDeleteCategory(categoryName);

            var idKey = GetProviderKey(root);
            var hashKey = ComputeHashProviderKey(root.ParentCategory ?? string.Empty, root.Name);
            ForceDeleteProviderFiles(idKey);
            ForceDeleteProviderFiles(hashKey);

            return (catRemoved, dataRemoved);
        }

        return CascadeDeleteCategoryNames(CollectCategoryTree(categoryName));
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
