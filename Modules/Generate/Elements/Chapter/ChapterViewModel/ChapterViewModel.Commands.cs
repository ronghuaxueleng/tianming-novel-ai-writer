using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;

namespace TM.Modules.Generate.Elements.Chapter
{
    public partial class ChapterViewModel
    {
        private static readonly Regex ChPunctuationOnlyRegex = new(@"[\p{P}\p{S}\s]+", RegexOptions.Compiled);

        protected override string DefaultDataIcon => "Icon.Chapter";
        protected override string NewItemTypeName => "章节";

        protected override ChapterData? CreateNewData(string? categoryName = null)
        {
            return new ChapterData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新章节",
                Category = categoryName ?? string.Empty,
                Volume = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
            FormVolume = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllChapters();

        protected override List<ChapterCategory> GetAllCategoriesFromService()
        {
            return Service.GetAllCategories();
        }

        protected override List<ChapterData> GetAllDataItems()
            => Service.GetAllChapters().OrderBy(c => c.ChapterNumber).ToList();

        protected override string GetDataCategory(ChapterData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(ChapterData data)
        {
            var title = NormalizeChapterTitle(data.ChapterTitle);
            return new TreeNodeItem
            {
                Name = $"第{data.ChapterNumber}章 {title}",
                Icon = IconHelper.Get("Icon.Chapter"),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(ChapterData data)
        {
            return new[] { data.ChapterTitle, data.MainGoal, data.ChapterTheme };
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: ChapterData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: ChapterCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(ChapterData data)
        {
            FormName = data.Name;
            FormIcon = "Icon.Document";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormChapterTitle = data.ChapterTitle;
            FormChapterNumber = data.ChapterNumber;
            FormVolume = data.Volume;
            FormChapterTheme = data.ChapterTheme;
            FormReaderExperienceGoal = data.ReaderExperienceGoal;
            FormMainGoal = data.MainGoal;

            FormResistanceSource = data.ResistanceSource;
            FormKeyTurn = data.KeyTurn;
            FormHook = data.Hook;

            FormWorldInfoDrop = data.WorldInfoDrop;
            FormCharacterArcProgress = data.CharacterArcProgress;
            FormMainPlotProgress = data.MainPlotProgress;
            FormForeshadowing = data.Foreshadowing;

            FormReferencedCharacterNames = ToCommaSeparated(data.ReferencedCharacterNames);
            FormReferencedFactionNames = ToCommaSeparated(data.ReferencedFactionNames);
            FormReferencedLocationNames = ToCommaSeparated(data.ReferencedLocationNames);
            RefreshEntityPool(data.Category);
        }

        private void LoadCategoryToForm(ChapterCategory category)
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
            FormChapterTitle = string.Empty;
            FormChapterNumber = 0;
            FormVolume = string.Empty;
            FormChapterTheme = string.Empty;
            FormReaderExperienceGoal = string.Empty;
            FormMainGoal = string.Empty;

            FormResistanceSource = string.Empty;
            FormKeyTurn = string.Empty;
            FormHook = string.Empty;

            FormWorldInfoDrop = string.Empty;
            FormCharacterArcProgress = string.Empty;
            FormMainPlotProgress = string.Empty;
            FormForeshadowing = string.Empty;

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
                TM.App.Log($"[ChapterViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[ChapterViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或章节");
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

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            await Service.AddChapterAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"章节『{newData.ChapterTitle}』已创建");
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
            await Service.UpdateChapterAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"章节『{_currentEditingData.ChapterTitle}』已更新");
        }

        private void UpdateDataFromForm(ChapterData data)
        {
            data.Name = FormName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.ChapterTitle = NormalizeChapterTitle(FormChapterTitle);
            data.ChapterNumber = FormChapterNumber;
            data.Volume = FormCategory;
            data.ChapterTheme = FormChapterTheme;
            data.ReaderExperienceGoal = FormReaderExperienceGoal;
            data.MainGoal = FormMainGoal;

            data.ResistanceSource = FormResistanceSource;
            data.KeyTurn = FormKeyTurn;
            data.Hook = FormHook;

            data.WorldInfoDrop = FormWorldInfoDrop;
            data.CharacterArcProgress = FormCharacterArcProgress;
            data.MainPlotProgress = FormMainPlotProgress;
            data.Foreshadowing = FormForeshadowing;

            data.ReferencedCharacterNames = FromCommaSeparated(FormReferencedCharacterNames);
            data.ReferencedFactionNames = FromCommaSeparated(FormReferencedFactionNames);
            data.ReferencedLocationNames = FromCommaSeparated(FormReferencedLocationNames);
        }

        protected override System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var (chars, locs, facs) = NormalizeChapterReferences(
                FormCategory,
                FormReferencedCharacterNames,
                FormReferencedLocationNames,
                FormReferencedFactionNames);

            FormReferencedCharacterNames = chars;
            FormReferencedLocationNames = locs;
            FormReferencedFactionNames = facs;

            return System.Threading.Tasks.Task.CompletedTask;
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
                        $"确定要删除章节『{_currentEditingData.ChapterTitle}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteChapter(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"章节『{_currentEditingData.ChapterTitle}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或章节");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

        private static string NormalizeChapterTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = ChNormLeadingPrefixRegex.Replace(t, string.Empty);
            t = ChNormArabicRegex.Replace(t, string.Empty);
            t = ChNormChineseRegex.Replace(t, string.Empty);
            return t.Trim();
        }

        private static bool HasRealTitleContent(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var t = title.Trim();
            t = ChNormArabicRegex.Replace(t, string.Empty);
            t = ChNormChineseRegex.Replace(t, string.Empty);
            if (string.IsNullOrWhiteSpace(t)) return false;
            return !string.IsNullOrWhiteSpace(ChPunctuationOnlyRegex.Replace(t, string.Empty));
        }

        protected override void OnTreeAfterAction(string? action)
        {
            if (action == "Reorder")
            {
                return;
            }

            base.OnTreeAfterAction(action);
        }

        protected override string GetModuleNameForVersionTracking() => "Chapter";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateChapter(_currentEditingData);
        }
    }
}
