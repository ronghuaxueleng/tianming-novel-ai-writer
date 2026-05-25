using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

public partial class PromptManagementViewModel
{

    protected override string DefaultDataIcon => "Icon.Edit";

    protected override PromptTemplateData? CreateNewData(string? categoryName = null)
    {
        return new PromptTemplateData
        {
            Id = ShortIdGenerator.New("D"),
            Category = categoryName ?? string.Empty,
            Icon = DefaultDataIcon,
            IsEnabled = false,
            IsBuiltIn = false,
            CreatedTime = DateTime.Now,
            ModifiedTime = DateTime.Now
        };
    }

    protected override string? GetCurrentCategoryValue()
    {
        return FormCategory;
    }

    protected override void ApplyCategorySelection(string categoryName)
    {
        FormCategory = categoryName;
    }

    protected override int ClearAllDataItems()
    {
        var deletable = Service.GetAllTemplates()
            .Where(t => !IsBuiltInTemplate(t))
            .ToList();

        foreach (var t in deletable)
        {
            Service.DeleteData(t.Id);
        }

        return deletable.Count;
    }

    protected override List<PromptCategory> GetAllCategoriesFromService()
    {
        return Service.GetAllCategories();
    }

    protected override List<PromptTemplateData> GetAllDataItems()
    {
        return Service.GetAllTemplates().ToList();
    }

    protected override string GetDataCategory(PromptTemplateData data)
    {
        return data.Category;
    }

    protected override TreeNodeItem ConvertToTreeNode(PromptTemplateData data)
    {
        return new TreeNodeItem
        {
            Name = data.Name,
            Icon = IconHelper.TryGet(data.Icon),
            Tag = data,
            ShowChildCount = false
        };
    }

    protected override string[] GetSearchAdditionalFields(PromptTemplateData data)
    {
        return new[] { data.SystemPrompt, data.UserTemplate, data.Tags };
    }

    private bool ValidateFormCore()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            GlobalToast.Warning("保存失败", "请输入名称");
            return false;
        }

        if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
        {
            GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或提示词模板");
            return false;
        }

        return true;
    }

    private void CreateCategoryCore()
    {
        var parentCategoryName = "";
        var level = 1;

        if (!string.IsNullOrWhiteSpace(FormCategory))
        {
            parentCategoryName = FormCategory;
            var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
            level = parentCategory != null ? parentCategory.Level + 1 : 1;
        }

        var categoryIcon = GetCategoryIconForSave(FormIcon);

        var newCategory = new PromptCategory
        {
            Id = ShortIdGenerator.New("C"),
            Name = FormName,
            Icon = categoryIcon,
            ParentCategory = parentCategoryName,
            Level = level,
            Order = Service.GetAllCategories().Count + 1,
            IsBuiltIn = false
        };

        if (!Service.AddCategory(newCategory))
        {
            GlobalToast.Warning("创建失败", "分类名已存在，请改名");
            return;
        }

        string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
        GlobalToast.Success("保存成功", $"{levelDesc}「{newCategory.Name}」已创建");

        _currentEditingCategory = null;
        _currentEditingData = null;
        ResetForm();
        RefreshTreeData();
    }

    private void CreateDataCore()
    {
        if (string.IsNullOrWhiteSpace(FormCategory))
        {
            GlobalToast.Warning("保存失败", "请选择所属分类");
            return;
        }

        var newData = CreateNewData(FormCategory);
        if (newData == null) return;

        UpdateDataFromForm(newData);
        if (newData.IsEnabled)
        {
            EnforceUnifiedCategoryMutex(newData.Category, newData.Id);
        }
        Service.AddData(newData);
        _currentEditingData = newData;
        GlobalToast.Success("保存成功", $"提示词模板「{newData.Name}」已创建");
        RefreshTreeData();
        FocusOnDataItem(newData);
    }

    private void UpdateCategoryCore()
    {
        if (_currentEditingCategory == null)
            return;

        var oldName = _currentEditingCategory.Name;
        _currentEditingCategory.Name = FormName;
        _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
        if (!Service.UpdateCategory(_currentEditingCategory))
        {
            _currentEditingCategory.Name = oldName;
            GlobalToast.Warning("保存失败", "分类名已存在，请改名");
            return;
        }
        GlobalToast.Success("保存成功", $"分类「{_currentEditingCategory.Name}」已更新");
    }

    private void UpdateDataCore()
    {
        if (_currentEditingData == null)
            return;

        if (IsBuiltInTemplate(_currentEditingData))
        {
            GlobalToast.Warning("禁止修改", "内置模板不可修改。");
            return;
        }

        UpdateDataFromForm(_currentEditingData);
        if (_currentEditingData.IsEnabled)
        {
            EnforceUnifiedCategoryMutex(_currentEditingData.Category, _currentEditingData.Id);
        }
        Service.UpdateData(_currentEditingData);
        GlobalToast.Success("保存成功", $"提示词模板「{_currentEditingData.Name}」已更新");
    }

    protected override void OnDataEnabledChanged(PromptTemplateData data, bool isEnabled)
    {
        base.OnDataEnabledChanged(data, isEnabled);

        if (!isEnabled)
        {
            if (IsAutoFallbackCategory(data.Category))
            {
                EnsureBuiltInDefaultEnabledIfNone(data.Category);
            }
            return;
        }

        if (!IsUnifiedMutexCategory(data.Category))
        {
            return;
        }

        EnforceUnifiedCategoryMutex(data.Category, data.Id);
        Service.UpdateData(data);

        if (_currentEditingData?.Id == data.Id)
        {
            FormStatus = "已启用";
        }
    }

    protected override void ExecuteBulkToggle()
    {
        try
        {
            var serviceBase = Service as TM.Framework.Common.Services.ModuleServiceBase<PromptCategory, PromptTemplateData>;
            if (serviceBase == null) return;

            var selectedRoot = TryGetBulkToggleRootCategory();

            List<string> names;
            bool allEnabled;

            if (selectedRoot != null && selectedRoot.Level == 1)
            {
                names = CollectCategoryAndChildrenNames(selectedRoot.Name);
                if (names.Count == 0) { GlobalToast.Warning("提示", "未找到可操作的分类"); return; }
                allEnabled = IsAllEnabledInCategories(names);
            }
            else
            {
                var categories = Service.GetAllCategories();
                names = categories.Select(c => c.Name).ToList();
                if (names.Count == 0) { GlobalToast.Warning("提示", "暂无分类数据"); return; }
                allEnabled = IsAllEnabledInCategories(names);
            }

            var newEnabled = !allEnabled;

            if (newEnabled && !CheckBulkEnableWarning(names))
            {
                return;
            }

            var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
            var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);

            if (newEnabled)
            {
                ApplyUnifiedBuiltInDefaults(names);
            }
            else
            {
                foreach (var category in names)
                {
                    EnsureBuiltInDefaultEnabledIfNone(category);
                }
            }

            RefreshTreeAndCategorySelection();
            UpdateBulkToggleState();

            GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 一键启用/禁用失败: {ex.Message}");
            GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
        }
    }
}
