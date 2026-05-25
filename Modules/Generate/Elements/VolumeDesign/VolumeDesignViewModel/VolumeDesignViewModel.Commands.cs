using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.VersionTracking;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    public partial class VolumeDesignViewModel
    {
        protected override string NewItemTypeName => "分卷设计";

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: VolumeDesignData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: VolumeDesignCategory category })
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
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(VolumeDesignData data)
        {
            FormName = data.Name;
            FormIcon = "Icon.Books";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormVolumeNumber = data.VolumeNumber;
            FormVolumeTitle = data.VolumeTitle;
            FormVolumeTheme = data.VolumeTheme;
            FormStageGoal = data.StageGoal;
            FormStartChapter = data.StartChapter;
            FormEndChapter = data.EndChapter;

            FormMainConflict = data.MainConflict;
            FormPressureSource = data.PressureSource;
            FormKeyEvents = data.KeyEvents;
            FormOpeningState = data.OpeningState;
            FormEndingState = data.EndingState;

            FormChapterAllocationOverview = data.ChapterAllocationOverview;
            FormPlotAllocation = data.PlotAllocation;
            FormChapterGenerationHints = data.ChapterGenerationHints;

            FormReferencedCharacterNames = string.Join("、", data.ReferencedCharacterNames);
            FormReferencedFactionNames = string.Join("、", data.ReferencedFactionNames);
            FormReferencedLocationNames = string.Join("、", data.ReferencedLocationNames);
        }

        private void LoadCategoryToForm(VolumeDesignCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ResetBusinessFields();
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = DefaultDataIcon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ResetBusinessFields();
        }

        private void ResetBusinessFields()
        {
            FormVolumeNumber = 0;
            FormVolumeTitle = string.Empty;
            FormVolumeTheme = string.Empty;
            FormStageGoal = string.Empty;
            FormStartChapter = 0;
            FormEndChapter = 0;

            FormMainConflict = string.Empty;
            FormPressureSource = string.Empty;
            FormKeyEvents = string.Empty;
            FormOpeningState = string.Empty;
            FormEndingState = string.Empty;

            FormChapterAllocationOverview = string.Empty;
            FormPlotAllocation = string.Empty;
            FormChapterGenerationHints = string.Empty;

            FormReferencedCharacterNames = string.Empty;
            FormReferencedFactionNames = string.Empty;
            FormReferencedLocationNames = string.Empty;
        }

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
                TM.App.Log($"[VolumeDesignViewModel] 新建失败: {ex.Message}");
                GlobalToast.Error("新建失败", $"新建失败：{ex.Message}");
            }
        });

        private ICommand? _saveCommand;
        public ICommand SaveCommand => _saveCommand ??= new AsyncRelayCommand(async () =>
        {
            try
            {
                await ExecuteSaveWithCreateEditModeAsync(
                    validateForm: ValidateFormCore,
                    createCategoryCore: CreateCategoryCoreAsync,
                    createDataCore: CreateDataCoreAsync,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCoreAsync,
                    updateDataCore: UpdateDataCoreAsync);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
            }
        });

        private bool ValidateFormCore()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                GlobalToast.Warning("保存失败", "请输入名称");
                return false;
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或分卷设计");
                return false;
            }

            return true;
        }

        private async Task CreateCategoryCoreAsync()
        {
            var parentCategoryName = string.Empty;
            var level = 1;

            if (!string.IsNullOrWhiteSpace(FormCategory))
            {
                parentCategoryName = FormCategory;
                var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
                level = parentCategory != null ? parentCategory.Level + 1 : 1;
            }

            var categoryIcon = GetCategoryIconForSave(FormIcon);

            var newCategory = new VolumeDesignCategory
            {
                Id = ShortIdGenerator.New("C"),
                Name = FormName,
                Icon = categoryIcon,
                ParentCategory = parentCategoryName,
                Level = level,
                Order = Service.GetAllCategories().Count + 1
            };

            if (!await Service.AddCategoryAsync(newCategory))
            {
                GlobalToast.Warning("创建失败", "分类名已存在，请改名");
                return;
            }

            string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
            GlobalToast.Success("保存成功", $"{levelDesc}『{newCategory.Name}』已创建");

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
        }

        private static Dictionary<string, int> TryGetVolumeVersionSnapshot()
        {
            try { return ServiceLocator.Get<VersionTrackingService>().GetDependencySnapshot("VolumeDesign"); }
            catch { return new Dictionary<string, int>(); }
        }

        private async Task CreateDataCoreAsync()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            newData.DependencyModuleVersions = TryGetVolumeVersionSnapshot();
            await Service.AddVolumeDesignAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"分卷『{newData.VolumeTitle}』已创建");
        }

        private async Task UpdateCategoryCoreAsync()
        {
            if (_currentEditingCategory == null) return;

            var oldName = _currentEditingCategory.Name;
            _currentEditingCategory.Name = FormName;
            _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
            if (!await Service.UpdateCategoryAsync(_currentEditingCategory))
            {
                _currentEditingCategory.Name = oldName;
                GlobalToast.Warning("保存失败", "分类名已存在，请改名");
                return;
            }
            GlobalToast.Success("保存成功", $"分类『{_currentEditingCategory.Name}』已更新");
        }

        private async Task UpdateDataCoreAsync()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            _currentEditingData.DependencyModuleVersions = TryGetVolumeVersionSnapshot();
            await Service.UpdateVolumeDesignAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"分卷『{_currentEditingData.VolumeTitle}』已更新");
        }

        private void UpdateDataFromForm(VolumeDesignData data)
        {
            data.Name = FormName;
            data.Category = FormCategory;
            var _catMatch = Service.GetAllCategories().FirstOrDefault(c => string.Equals(c.Name, FormCategory, StringComparison.Ordinal));
            if (_catMatch != null) data.CategoryId = _catMatch.Id;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.VolumeNumber = FormVolumeNumber;
            data.VolumeTitle = FormVolumeTitle;
            data.VolumeTheme = FormVolumeTheme;
            data.StageGoal = FormStageGoal;
            data.StartChapter = FormStartChapter;
            data.EndChapter = FormEndChapter;
            data.TargetChapterCount = (FormStartChapter > 0 && FormEndChapter >= FormStartChapter)
                ? FormEndChapter - FormStartChapter + 1
                : 0;

            data.MainConflict = FormMainConflict;
            data.PressureSource = FormPressureSource;
            data.KeyEvents = FormKeyEvents;
            data.OpeningState = FormOpeningState;
            data.EndingState = FormEndingState;

            data.ChapterAllocationOverview = FormChapterAllocationOverview;
            data.PlotAllocation = FormPlotAllocation;
            data.ChapterGenerationHints = FormChapterGenerationHints;

            data.ReferencedCharacterNames = SplitEntityNames(FormReferencedCharacterNames);
            data.ReferencedFactionNames = SplitEntityNames(FormReferencedFactionNames);
            data.ReferencedLocationNames = SplitEntityNames(FormReferencedLocationNames);
        }

        private static List<string> SplitEntityNames(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text
                .Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "无", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                if (_currentEditingCategory != null)
                {
                    var allCategoriesToDelete = CollectCategoryAndChildrenNames(_currentEditingCategory.Name);

                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有分卷设计也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    var categoryIdLookup = Service.GetAllCategories()
                        .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        categoryIdLookup.TryGetValue(categoryName, out var cId);
                        var dataInCategory = Service.GetAllVolumeDesigns()
                            .Where(d =>
                                (!string.IsNullOrWhiteSpace(cId) && d.CategoryId == cId) ||
                                (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName))
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteVolumeDesign(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个分卷设计");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分卷设计『{_currentEditingData.VolumeTitle}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteVolumeDesign(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"分卷设计『{_currentEditingData.VolumeTitle}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或分卷设计");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });
    }
}
