using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    public partial class ShortStoryBlueprintViewModel
    {

        private void SyncBlueprintsFromTotalChapters()
        {
            if (!int.TryParse(_formTotalChapters?.Trim(), out var n) || n <= 0)
                return;

            if (ChapterBlueprints.Count != n)
            {
                var newList = ChapterBlueprints.Take(n).ToList();
                while (newList.Count < n)
                {
                    var idx = newList.Count + 1;
                    newList.Add(new ShortStoryChapterBlueprintVM { ChapterIndex = idx, Title = $"第{idx}章" });
                }
                ChapterBlueprints.ReplaceAll(newList);
            }

            if (_currentChapterIndex >= ChapterBlueprints.Count)
                _currentChapterIndex = Math.Max(0, ChapterBlueprints.Count - 1);

            OnPropertyChanged(nameof(CurrentChapterBlueprint));
            OnPropertyChanged(nameof(CurrentChapterLabel));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
        }

        private void UpdateAIGenerateEnabledState()
        {
            var hasEditingContext = IsCreateMode || _currentEditingData != null || _currentEditingCategory != null;
            var isValidCount = int.TryParse(_formTotalChapters?.Trim(), out var n) && n > 0;
            var hasSourceBook = !string.IsNullOrWhiteSpace(FormBookAnalysisId);
            IsAIGenerateEnabled = hasEditingContext && isValidCount && hasSourceBook;
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: ShortStoryBlueprintData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                    UpdateAIGenerateEnabledState();
                }
                else if (param is TreeNodeItem { Tag: ShortStoryBlueprintCategory category })
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
                    UpdateAIGenerateEnabledState();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ShortStoryBlueprintViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(ShortStoryBlueprintData data)
        {
            FormName = data.Name;
            FormIcon = data.Icon;
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;
            _genreManuallySet = true;
            var matchingBook = BookOptions.FirstOrDefault(b => b.Name == data.SourceBookName);
            FormBookAnalysisId = matchingBook?.Id ?? string.Empty;
            if (matchingBook == null)
                FormSourceBookName = data.SourceBookName;
            _suppressGenreManualMark = true;
            FormGenre = data.Genre;
            _suppressGenreManualMark = false;
            _genreManuallySet = !string.IsNullOrWhiteSpace(data.Genre);
            FormSynopsis = data.Synopsis;
            _formTotalChapters = data.TotalChapters;
            OnPropertyChanged(nameof(FormTotalChapters));
            FormWordsPerChapter = data.WordsPerChapter;
            FormToneGuide = data.ToneGuide;

            ChapterBlueprints.ReplaceAll(data.ChapterBlueprints
                .OrderBy(c => c.ChapterIndex)
                .Select(ch => new ShortStoryChapterBlueprintVM
                {
                    ChapterIndex = ch.ChapterIndex,
                    Title = ch.Title,
                    KeyEvents = ch.KeyEvents,
                    Characters = ch.Characters,
                    EndingNote = ch.EndingNote,
                    TargetWordCount = ch.TargetWordCount
                }).ToList());

            _currentChapterIndex = 0;
            OnPropertyChanged(nameof(CurrentChapterBlueprint));
            OnPropertyChanged(nameof(CurrentChapterLabel));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(IsTotalChaptersLocked));
        }

        private void LoadCategoryToForm(ShortStoryBlueprintCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.ParentCategory ?? string.Empty;
            ClearBusinessFields();
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = DefaultDataIcon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ClearBusinessFields();
        }

        private void ClearBusinessFields()
        {
            FormBookAnalysisId = string.Empty;
            _genreManuallySet = false;
            FormGenre = string.Empty;
            FormSynopsis = string.Empty;
            _formTotalChapters = string.Empty;
            OnPropertyChanged(nameof(FormTotalChapters));
            FormWordsPerChapter = string.Empty;
            FormToneGuide = string.Empty;
            ChapterBlueprints.Clear();
            _currentChapterIndex = 0;
            OnPropertyChanged(nameof(CurrentChapterBlueprint));
            OnPropertyChanged(nameof(CurrentChapterLabel));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(IsTotalChaptersLocked));
        }

        private void UpdateDataFromForm(ShortStoryBlueprintData data)
        {
            data.Name = FormName;
            data.Icon = GetDataIconForSave(FormIcon);
            data.Category = FormCategory;
            data.IsEnabled = FormStatus == "已启用";
            data.ModifiedTime = DateTime.Now;
            data.SourceBookName = FormSourceBookName;
            data.Genre = FormGenre;
            data.Synopsis = FormSynopsis;
            data.TotalChapters = FormTotalChapters;
            data.WordsPerChapter = FormWordsPerChapter;
            data.ToneGuide = FormToneGuide;
            data.ChapterBlueprints = ChapterBlueprints.Select(vm => new ShortStoryChapterBlueprint
            {
                ChapterIndex = vm.ChapterIndex,
                Title = vm.Title,
                KeyEvents = vm.KeyEvents,
                Characters = vm.Characters,
                EndingNote = vm.EndingNote,
                TargetWordCount = vm.TargetWordCount
            }).ToList();
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
                IsAIGenerateEnabled = false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ShortStoryBlueprintViewModel] 新建失败: {ex.Message}");
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
                    createDataCore: CreateBlueprintCoreAsync,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCoreAsync,
                    updateDataCore: UpdateBlueprintCoreAsync);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ShortStoryBlueprintViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
            }
        });

        private bool ValidateFormCore()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                GlobalToast.Warning("保存失败", "请输入蓝图名称");
                return false;
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或蓝图");
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

            var newCategory = new ShortStoryBlueprintCategory
            {
                Id = ShortIdGenerator.New("C"),
                Name = FormName,
                Icon = GetCategoryIconForSave(FormIcon),
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

            await SyncSpecWithGenreAsync(FormGenre);

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
        }

        private async Task CreateBlueprintCoreAsync()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            await Service.AddBlueprintAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"蓝图『{newData.Name}』已创建");
            await SyncSpecWithGenreAsync(FormGenre);
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

            await SyncSpecWithGenreAsync(FormGenre);
        }

        private async Task UpdateBlueprintCoreAsync()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            await Service.UpdateBlueprintAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"蓝图『{_currentEditingData.Name}』已更新");
            await SyncSpecWithGenreAsync(FormGenre);
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                if (_currentEditingCategory != null)
                {
                    var allCategoriesToDelete = CollectCategoryAndChildrenNames(_currentEditingCategory.Name);

                    if (allCategoriesToDelete.Any(name => Service.IsCategoryBuiltIn(name)))
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除。");
                        return;
                    }

                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n该分类及其子分类下的所有蓝图也会被删除！",
                        "确认删除");
                    if (!result) return;

                    Service.CascadeDeleteCategory(_currentEditingCategory.Name);
                    GlobalToast.Success("删除成功", $"分类『{_currentEditingCategory.Name}』及其数据已删除");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除蓝图『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteBlueprint(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"蓝图『{_currentEditingData.Name}』已删除");

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
                TM.App.Log($"[ShortStoryBlueprintViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

        private ICommand? _prevChapterCommand;
        public ICommand PrevChapterCommand => _prevChapterCommand ??= new RelayCommand(
            _ => CurrentChapterIndex--,
            () => CanGoPrev);

        private ICommand? _nextChapterCommand;
        public ICommand NextChapterCommand => _nextChapterCommand ??= new RelayCommand(
            _ => CurrentChapterIndex++,
            () => CanGoNext);

        private ICommand? _goToChapterCommand;
        public ICommand GoToChapterCommand => _goToChapterCommand ??= new RelayCommand(param =>
        {
            if (param is int idx) CurrentChapterIndex = idx;
            else if (param is string s && int.TryParse(s, out var n)) CurrentChapterIndex = n;
        });
    }
}
