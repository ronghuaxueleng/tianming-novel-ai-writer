using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

public partial class PromptManagementViewModel
{
    private ICommand? _addCommand;
    public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
    {
        try
        {
            _currentEditingData = null;
            _currentEditingCategory = null;
            ResetForm();
            ExecuteAddWithCreateMode();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 新建失败: {ex.Message}");
            GlobalToast.Error("新建失败", $"新建失败：{ex.Message}");
        }
    });

    private ICommand? _saveCommand;
    public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
    {
        try
        {
            ExecuteSaveWithCreateEditMode(
                validateForm: ValidateFormCore,
                createCategoryCore: CreateCategoryCore,
                createDataCore: CreateDataCore,
                hasEditingCategory: () => _currentEditingCategory != null,
                hasEditingData: () => _currentEditingData != null,
                updateCategoryCore: UpdateCategoryCore,
                updateDataCore: UpdateDataCore);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 保存失败: {ex.Message}");
            GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
        }
    });

    private ICommand? _deleteCommand;
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
    {
        try
        {
            if (_currentEditingCategory != null)
            {
                var allCategoriesToDelete = new List<string>();
                CollectCategoryAndChildren(_currentEditingCategory.Name, allCategoriesToDelete);

                if (ContainsProtectedCategories(allCategoriesToDelete))
                {
                    GlobalToast.Warning("禁止删除", "1-3级分类不可删除（含联动删除）。");
                    return;
                }

                if (ContainsBuiltInTemplates(allCategoriesToDelete, out var builtInCount))
                {
                    GlobalToast.Warning("禁止删除", $"该分类（含子分类）下包含 {builtInCount} 个内置模板，禁止删除。");
                    return;
                }

                var result = StandardDialog.ShowConfirm(
                    $"确定要删除分类「{_currentEditingCategory.Name}」吗？\n\n注意：该分类及其{allCategoriesToDelete.Count - 1}个子分类下的所有提示词模板也会被删除！",
                    "确认删除"
                );
                if (!result) return;

                int totalDataDeleted = 0;

                var categoryIdLookup = Service.GetAllCategories()
                    .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                var allData = Service.GetAllData();
                foreach (var categoryName in allCategoriesToDelete)
                {
                    categoryIdLookup.TryGetValue(categoryName, out var cId);
                    var dataInCategory = allData
                        .Where(d =>
                            (!string.IsNullOrWhiteSpace(cId) && d.CategoryId == cId) ||
                            (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName))
                        .ToList();

                    foreach (var data in dataInCategory)
                    {
                        if (data.IsBuiltIn)
                        {
                            continue;
                        }
                        Service.DeleteData(data.Id);
                        totalDataDeleted++;
                    }

                    Service.DeleteCategory(categoryName);
                }

                GlobalToast.Success("删除成功",
                    $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个提示词模板");

                _currentEditingCategory = null;
                ResetForm();
                RefreshTreeData();
            }
            else if (_currentEditingData != null)
            {
                if (IsBuiltInTemplate(_currentEditingData))
                {
                    GlobalToast.Warning("禁止删除", "内置模板不可删除。");
                    return;
                }

                var result = StandardDialog.ShowConfirm($"确定要删除提示词模板「{_currentEditingData.Name}」吗？", "确认删除");
                if (!result) return;

                Service.DeleteData(_currentEditingData.Id);
                GlobalToast.Success("删除成功", $"提示词模板「{_currentEditingData.Name}」已删除");

                var deletedCategory = _currentEditingData.Category;

                _currentEditingData = null;
                ResetForm();
                RefreshTreeData();
                EnsureBuiltInDefaultEnabledIfNone(deletedCategory);
            }
            else
            {
                GlobalToast.Warning("删除失败", "请先选择要删除的分类或提示词模板");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 删除失败: {ex.Message}");
            GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
        }
    });
}
