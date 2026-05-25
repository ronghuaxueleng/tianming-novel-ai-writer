using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Controls;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

public partial class PromptManagementViewModel
{

    private bool ContainsProtectedCategories(IEnumerable<string> categoryNames)
    {
        if (categoryNames == null) return false;

        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var name in categoryNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (lookup.TryGetValue(name, out var category) && IsProtectedCategory(category))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProtectedCategory(PromptCategory category)
    {
        if (category.IsBuiltIn && category.Level <= 3)
        {
            return true;
        }

        return false;
    }

    private bool ContainsBuiltInTemplates(IEnumerable<string> categoryNames, out int builtInCount)
    {
        builtInCount = 0;
        if (categoryNames == null) return false;

        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
        if (set.Count == 0) return false;

        builtInCount = Service.GetAllData().Count(d => IsBuiltInTemplate(d) && set.Contains(d.Category));
        return builtInCount > 0;
    }

    private static bool IsBuiltInTemplate(PromptTemplateData data)
    {
        return data.IsBuiltIn;
    }

    private static bool IsUnifiedMutexCategory(string? categoryName)
        => !string.IsNullOrWhiteSpace(categoryName) && UnifiedMutexCategories.Contains(categoryName);

    private void EnforceUnifiedCategoryMutex(string categoryName, string enabledTemplateId)
    {
        if (!IsUnifiedMutexCategory(categoryName))
        {
            return;
        }

        foreach (var t in Service.GetTemplatesByCategory(categoryName))
        {
            t.IsEnabled = string.Equals(t.Id, enabledTemplateId, StringComparison.Ordinal);
        }
    }

    private void ApplyUnifiedBuiltInDefaults(IEnumerable<string> categoryNames)
    {
        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        foreach (var category in UnifiedMutexCategories)
        {
            if (!set.Contains(category))
            {
                continue;
            }

            var templates = Service.GetTemplatesByCategory(category).ToList();
            if (templates.Count == 0)
            {
                continue;
            }

            var selected = templates
                .Where(t => t.IsBuiltIn)
                .MaxBy(t => t.IsDefault)
                ?? templates.MaxBy(t => t.IsDefault)
                ?? templates.FirstOrDefault();

            if (selected == null)
            {
                continue;
            }

            EnforceUnifiedCategoryMutex(category, selected.Id);
            Service.UpdateData(selected);
        }
    }

    private bool IsAllEnabledInCategories(List<string> categoryNames)
    {
        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        var categories = Service.GetAllCategories();
        var data = GetAllDataItems();

        var allCategoriesEnabled = categories.Where(c => set.Contains(c.Name)).All(c => c.IsEnabled);
        var allDataEnabled = data.Where(d => set.Contains(GetDataCategory(d))).All(d => GetDataIsEnabled(d));

        return allCategoriesEnabled && allDataEnabled;
    }

    private PromptCategory? TryGetBulkToggleRootCategory()
    {
        return GetBulkToggleCurrentCategory();
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

    private new void OnNodeDoubleClick(TreeNodeItem? node)
    {
        if (node == null) return;

        try
        {
            if (node.Tag is PromptCategory category)
            {
                _currentEditingCategory = category;
                _currentEditingData = null;
                if (category.IsBuiltIn)
                {
                    ResetForm();
                    EnterEditMode();
                }
                else
                {
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            else if (node.Tag is PromptTemplateData data)
            {
                _currentEditingData = data;
                _currentEditingCategory = null;
                LoadDataToForm(data);
                EnterEditMode();
                OnDataItemLoaded();
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 加载节点失败: {ex.Message}");
            GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
        }
    }

    private void LoadCategoryToForm(PromptCategory category)
    {
        FormName = category.Name;
        FormIcon = category.Icon;
        FormStatus = "分类";

        _suppressCategoryValueChanged = true;
        try
        {
            FormCategory = category.ParentCategory ?? string.Empty;
        }
        finally
        {
            _suppressCategoryValueChanged = false;
        }

        SyncCategorySelectionDisplay(FormCategory);

        FormSystemPrompt = string.Empty;
        FormUserTemplate = string.Empty;
        FormVariables = string.Empty;
        FormTags = string.Empty;
        FormDescription = string.Empty;
        FormIsBuiltIn = false;
        FormIsDefault = false;
    }

    private void LoadDataToForm(PromptTemplateData data)
    {
        FormName = data.Name;
        FormIcon = data.Icon;
        FormStatus = data.IsEnabled ? "已启用" : "已禁用";

        _suppressCategoryValueChanged = true;
        try
        {
            FormCategory = data.Category;
        }
        finally
        {
            _suppressCategoryValueChanged = false;
        }

        SyncCategorySelectionDisplay(FormCategory);

        FormSystemPrompt = data.SystemPrompt;
        FormUserTemplate = data.UserTemplate;
        FormVariables = data.Variables;
        FormTags = data.Tags;
        FormDescription = data.Description;
        FormIsBuiltIn = data.IsBuiltIn;
        FormIsDefault = data.IsDefault;
    }

    private void SyncCategorySelectionDisplay(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            SelectedCategoryTreePath = "主页导航";
            SelectedCategoryTreeIcon = IconHelper.TryGet("Icon.Home");
            return;
        }

        var category = Service.GetAllCategories().FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));
        SelectedCategoryTreeIcon = IconHelper.TryGet(category?.Icon) ?? IconHelper.TryGet("Icon.Folder");

        var chain = BuildCategoryChain(categoryName);
        SelectedCategoryTreePath = chain.Count == 0
            ? $"主页导航 > {categoryName}"
            : $"主页导航 > {string.Join(" > ", chain)}";
    }

    private List<string> BuildCategoryChain(string categoryName)
    {
        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new List<string>();
        var current = categoryName;

        while (!string.IsNullOrWhiteSpace(current) && lookup.TryGetValue(current, out var cat))
        {
            result.Add(cat.Name);
            current = cat.ParentCategory ?? string.Empty;
        }

        result.Reverse();
        return result;
    }

    private void UpdateDataFromForm(PromptTemplateData data)
    {
        data.Name = FormName;
        data.Icon = GetDataIconForSave(FormIcon);
        data.Category = FormCategory;
        data.IsEnabled = FormStatus == "已启用";
        data.ModifiedTime = DateTime.Now;

        data.SystemPrompt = FormSystemPrompt;
        data.UserTemplate = FormUserTemplate;
        data.Variables = FormVariables;
        data.Tags = FormTags;
        data.Description = FormDescription;
        data.IsDefault = FormIsDefault;
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormIcon = "Icon.Note";
        FormStatus = "已启用";
        FormCategory = string.Empty;

        FormSystemPrompt = string.Empty;
        FormUserTemplate = string.Empty;
        FormVariables = string.Empty;
        FormTags = string.Empty;
        FormDescription = string.Empty;
        FormIsBuiltIn = false;
        FormIsDefault = false;
    }
}
