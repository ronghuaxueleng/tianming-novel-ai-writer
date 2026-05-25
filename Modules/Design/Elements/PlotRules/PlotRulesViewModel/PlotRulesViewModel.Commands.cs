using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Modules.Design.Elements.PlotRules
{
    public partial class PlotRulesViewModel
    {
        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: PlotRulesData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: PlotRulesCategory category })
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
                TM.App.Log($"[PlotRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

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
                TM.App.Log($"[PlotRulesViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[PlotRulesViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或剧情规则");
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

            var newCategory = new PlotRulesCategory
            {
                Id = ShortIdGenerator.New("C"),
                Name = FormName,
                Icon = categoryIcon,
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
            await Service.AddPlotRuleAsync(newData);
            _currentEditingData = newData;
            InvalidateRelationshipCache();
            GlobalToast.Success("保存成功", $"剧情规则『{newData.Name}』已创建");
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
            await Service.UpdatePlotRuleAsync(_currentEditingData);
            InvalidateRelationshipCache();
            GlobalToast.Success("保存成功", $"剧情规则『{_currentEditingData.Name}』已更新");
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有剧情规则也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    var categoryIdLookup = Service.GetAllCategories()
                        .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        categoryIdLookup.TryGetValue(categoryName, out var cId);
                        var dataInCategory = Service.GetAllPlotRules()
                            .Where(d =>
                                (!string.IsNullOrWhiteSpace(cId) && d.CategoryId == cId) ||
                                (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName))
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeletePlotRule(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个剧情规则");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除剧情规则『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeletePlotRule(_currentEditingData.Id);
                    InvalidateRelationshipCache();
                    GlobalToast.Success("删除成功", $"剧情规则『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或剧情规则");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(PlotRulesData data)
        {
            FormName = data.Name;
            FormIcon = "Icon.Book";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormTargetVolume = data.TargetVolume;
            FormAssignedVolume = data.AssignedVolume;
            FormOneLineSummary = data.OneLineSummary;
            FormEventType = data.EventType;
            FormStoryPhase = data.StoryPhase;
            FormPrerequisitesTrigger = data.PrerequisitesTrigger;

            FormMainCharacters = CharIdsToNames(data.MainCharacters);
            FormKeyNpcs = CharIdsToNames(data.KeyNpcs);
            FormLocation = LocIdToName(data.Location);
            FormTimeDuration = data.TimeDuration;

            FormStepTitle = data.StepTitle;
            FormGoal = data.Goal;
            FormConflict = data.Conflict;
            FormResult = data.Result;
            FormEmotionCurve = data.EmotionCurve;

            FormMainPlotPush = data.MainPlotPush;
            FormCharacterGrowth = data.CharacterGrowth;
            FormWorldReveal = data.WorldReveal;
            FormRewardsClues = data.RewardsClues;
        }

        private void LoadCategoryToForm(PlotRulesCategory category)
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
            FormTargetVolume = string.Empty;
            FormAssignedVolume = string.Empty;
            FormOneLineSummary = string.Empty;
            FormEventType = string.Empty;
            FormStoryPhase = string.Empty;
            FormPrerequisitesTrigger = string.Empty;

            FormMainCharacters = string.Empty;
            FormKeyNpcs = string.Empty;
            FormLocation = string.Empty;
            FormTimeDuration = string.Empty;

            FormStepTitle = string.Empty;
            FormGoal = string.Empty;
            FormConflict = string.Empty;
            FormResult = string.Empty;
            FormEmotionCurve = string.Empty;

            FormMainPlotPush = string.Empty;
            FormCharacterGrowth = string.Empty;
            FormWorldReveal = string.Empty;
            FormRewardsClues = string.Empty;
        }

        private void UpdateDataFromForm(PlotRulesData data)
        {
            var newIsEnabled = (FormStatus == "已启用");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckBeforeEnable(null, data.Name))
                {
                    FormStatus = "已禁用";
                    return;
                }
            }

            data.Name = FormName;
            data.Category = FormCategory;
            data.IsEnabled = newIsEnabled;
            data.UpdatedAt = DateTime.Now;

            data.TargetVolume = FormTargetVolume;
            data.AssignedVolume = FormAssignedVolume;
            data.OneLineSummary = FormOneLineSummary;
            data.EventType = FormEventType;
            data.StoryPhase = FormStoryPhase;
            data.PrerequisitesTrigger = FormPrerequisitesTrigger;

            data.MainCharacters = CharNamesToIds(FormMainCharacters);
            data.KeyNpcs = CharNamesToIds(FormKeyNpcs);
            data.Location = LocNameToId(FormLocation);
            data.TimeDuration = FormTimeDuration;

            data.StepTitle = FormStepTitle;
            data.Goal = FormGoal;
            data.Conflict = FormConflict;
            data.Result = FormResult;
            data.EmotionCurve = FormEmotionCurve;

            data.MainPlotPush = FormMainPlotPush;
            data.CharacterGrowth = FormCharacterGrowth;
            data.WorldReveal = FormWorldReveal;
            data.RewardsClues = FormRewardsClues;
        }
    }
}
