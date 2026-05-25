using System;
using System.Collections.Generic;
using System.Windows.Input;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;

namespace TM.Modules.Generate.Elements.Blueprint
{
    public partial class BlueprintViewModel
    {
        protected override string NewItemTypeName => "蓝图";
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
                TM.App.Log($"[BlueprintViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[BlueprintViewModel] 保存失败: {ex.Message}");
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

            var unmatchedCharacters = EntityNameNormalizeHelper.GetUnmatchedNames(FormCast, AvailableCharacters);
            var unmatchedLocations = EntityNameNormalizeHelper.GetUnmatchedNames(FormLocations, AvailableLocations);
            var unmatchedFactions = EntityNameNormalizeHelper.GetUnmatchedNames(FormFactions, AvailableFactions);

            if (unmatchedCharacters.Count > 0 || unmatchedLocations.Count > 0 || unmatchedFactions.Count > 0)
            {
                var parts = new List<string>();
                if (unmatchedCharacters.Count > 0)
                    parts.Add($"角色: {string.Join("、", unmatchedCharacters)}");
                if (unmatchedLocations.Count > 0)
                    parts.Add($"地点: {string.Join("、", unmatchedLocations)}");
                if (unmatchedFactions.Count > 0)
                    parts.Add($"势力: {string.Join("、", unmatchedFactions)}");

                GlobalToast.Warning("断链预警", $"以下名称未在当前候选列表中找到，可能导致上下文变弱：{string.Join("；", parts)}");
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或蓝图");
                return false;
            }

            if (!ValidateVolumeConsistency())
            {
                return false;
            }

            return true;
        }

        private bool ValidateVolumeConsistency()
        {
            if (string.IsNullOrWhiteSpace(FormChapterId) || string.IsNullOrWhiteSpace(FormCategory))
                return true;

            var volMatch = VolChIdPartialRegex.Match(FormChapterId);
            if (!volMatch.Success) return true;

            var chapterVolumeNumber = int.Parse(volMatch.Groups[1].Value);

            var categoryVolMatch = CategoryVolNumRegex.Match(FormCategory);
            if (!categoryVolMatch.Success) return true;

            var categoryVolumeNumber = int.Parse(categoryVolMatch.Groups[1].Value);

            if (chapterVolumeNumber != categoryVolumeNumber)
            {
                GlobalToast.Error("卷匹配错误",
                    $"关联章节属于【第{chapterVolumeNumber}卷】，但当前分类是【{FormCategory}】，请修正");
                return false;
            }
            return true;
        }

        private System.Threading.Tasks.Task CreateCategoryCoreAsync()
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），选中任意数据项保存即为全量保存");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task CreateDataCoreAsync()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormChapterId))
            {
                FormChapterId = GetDefaultChapterId();
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            await Service.AddBlueprintAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"蓝图『{newData.SceneTitle}』已创建");
        }

        private System.Threading.Tasks.Task UpdateCategoryCoreAsync()
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），选中任意数据项保存即为全量保存");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task UpdateDataCoreAsync()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            await Service.UpdateBlueprintAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"蓝图『{_currentEditingData.SceneTitle}』已更新");
        }

        private void UpdateDataFromForm(BlueprintData data)
        {
            var cleanedName = CleanBlueprintSceneTitle(FormName);
            data.Name = string.IsNullOrWhiteSpace(cleanedName) ? FormName : cleanedName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.ChapterId = MatchChapterId(FormChapterId);
            data.OneLineStructure = FormOneLineStructure;
            data.PacingCurve = FormPacingCurve;

            data.SceneNumber = FormSceneNumber;
            var cleanedSceneTitle = CleanBlueprintSceneTitle(FormSceneTitle);
            data.SceneTitle = string.IsNullOrWhiteSpace(cleanedSceneTitle) ? FormSceneTitle : cleanedSceneTitle;
            data.PovCharacter = FormPovCharacter;
            data.Opening = FormOpening;
            data.Development = FormDevelopment;
            data.Turning = FormTurning;
            data.Ending = FormEnding;
            data.InfoDrop = FormInfoDrop;

            data.Cast = FormCast;
            data.Locations = FormLocations;
            data.Factions = FormFactions;
            data.ItemsClues = FormItemsClues;
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                if (_currentEditingCategory != null)
                {
                    GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
                    return;
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除蓝图『{_currentEditingData.SceneTitle}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteBlueprint(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"蓝图『{_currentEditingData.SceneTitle}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或蓝图");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

    }
}
