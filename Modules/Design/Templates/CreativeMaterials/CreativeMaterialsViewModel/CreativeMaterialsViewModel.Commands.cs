using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    public partial class CreativeMaterialsViewModel
    {
        protected override string NewItemTypeName => "素材";

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: CreativeMaterialData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: CreativeMaterialCategory category })
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
                TM.App.Log($"[CreativeMaterialsViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private async void LoadDataToForm(CreativeMaterialData data)
        {
            try
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
                FormGenre = data.Genre;
                FormOverallIdea = data.OverallIdea;
                FormNovelSynopsis = data.NovelSynopsis;
                _formGoldenChapterModeText = (await TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.LoadAsync()) ? "黄金三章" : "不启用";
                OnPropertyChanged(nameof(FormGoldenChapterModeText));
                FormWorldBuildingMethod = data.WorldBuildingMethod;
                FormPowerSystemDesign = data.PowerSystemDesign;
                FormEnvironmentDescription = data.EnvironmentDescription;
                FormFactionDesign = data.FactionDesign;
                FormWorldviewHighlights = data.WorldviewHighlights;
                FormProtagonistDesign = data.ProtagonistDesign;
                FormSupportingRoles = data.SupportingRoles;
                FormCharacterRelations = data.CharacterRelations;
                FormGoldenFingerDesign = data.GoldenFingerDesign;
                FormCharacterHighlights = data.CharacterHighlights;
                FormPlotStructure = data.PlotStructure;
                FormConflictDesign = data.ConflictDesign;
                FormClimaxArrangement = data.ClimaxArrangement;
                FormForeshadowingTechnique = data.ForeshadowingTechnique;
                FormPlotHighlights = data.PlotHighlights;
            }
            catch (Exception ex) { TM.App.Log($"[CreativeMaterials] 加载素材数据失败: {ex.Message}"); }
        }

        private void LoadCategoryToForm(CreativeMaterialCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.ParentCategory ?? string.Empty;
            ClearBusinessFields();

            var categoryNames = CollectCategoryAndChildrenNames(category.Name);
            var existing = Service.GetAllMaterials()
                .Where(m => categoryNames.Contains(m.Category) && m.IsEnabled && !string.IsNullOrWhiteSpace(m.Genre))
                .OrderByDescending(m => m.ModifiedTime)
                .FirstOrDefault();
            if (existing != null)
            {
                FormGenre = existing.Genre;
            }
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
            FormSourceBookName = string.Empty;
            FormGenre = string.Empty;
            FormSelectedGenres.Clear();
            _genreManuallySet = false;
            FormOverallIdea = string.Empty;
            FormNovelSynopsis = string.Empty;
            FormWorldBuildingMethod = string.Empty;
            FormPowerSystemDesign = string.Empty;
            FormEnvironmentDescription = string.Empty;
            FormFactionDesign = string.Empty;
            FormWorldviewHighlights = string.Empty;
            FormProtagonistDesign = string.Empty;
            FormSupportingRoles = string.Empty;
            FormCharacterRelations = string.Empty;
            FormGoldenFingerDesign = string.Empty;
            FormCharacterHighlights = string.Empty;
            FormPlotStructure = string.Empty;
            FormConflictDesign = string.Empty;
            FormClimaxArrangement = string.Empty;
            FormForeshadowingTechnique = string.Empty;
            FormPlotHighlights = string.Empty;
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
                TM.App.Log($"[CreativeMaterialsViewModel] 新建失败: {ex.Message}");
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
                    createDataCore: CreateMaterialCoreAsync,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCoreAsync,
                    updateDataCore: UpdateMaterialCoreAsync);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
            }
        });

        private bool ValidateFormCore()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                GlobalToast.Warning("保存失败", "请输入素材名称");
                return false;
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或素材");
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

            var newCategory = new CreativeMaterialCategory
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

            await SyncSpecWithGenreAsync(FormGenre);

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
        }

        private async Task CreateMaterialCoreAsync()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            await UpdateDataFromForm(newData);
            await Service.AddMaterialAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"素材『{newData.Name}』已创建");
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

        private async Task UpdateMaterialCoreAsync()
        {
            if (_currentEditingData == null) return;

            await UpdateDataFromForm(_currentEditingData);
            await Service.UpdateMaterialAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"素材『{_currentEditingData.Name}』已更新");
            await SyncSpecWithGenreAsync(FormGenre);
        }

        private Task UpdateDataFromForm(CreativeMaterialData data)
        {
            var newIsEnabled = (FormStatus == "已启用");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckBeforeEnable(null, FormName))
                {
                    FormStatus = "已禁用";
                    return Task.CompletedTask;
                }
            }

            data.Name = FormName;
            data.Icon = GetDataIconForSave(FormIcon);
            data.Category = FormCategory;
            data.IsEnabled = newIsEnabled;
            data.ModifiedTime = DateTime.Now;
            data.SourceBookName = FormSourceBookName;
            data.Genre = FormGenre;
            data.OverallIdea = FormOverallIdea;
            data.NovelSynopsis = FormNovelSynopsis;
            data.WorldBuildingMethod = FormWorldBuildingMethod;
            data.PowerSystemDesign = FormPowerSystemDesign;
            data.EnvironmentDescription = FormEnvironmentDescription;
            data.FactionDesign = FormFactionDesign;
            data.WorldviewHighlights = FormWorldviewHighlights;
            data.ProtagonistDesign = FormProtagonistDesign;
            data.SupportingRoles = FormSupportingRoles;
            data.CharacterRelations = FormCharacterRelations;
            data.GoldenFingerDesign = FormGoldenFingerDesign;
            data.CharacterHighlights = FormCharacterHighlights;
            data.PlotStructure = FormPlotStructure;
            data.ConflictDesign = FormConflictDesign;
            data.ClimaxArrangement = FormClimaxArrangement;
            data.ForeshadowingTechnique = FormForeshadowingTechnique;
            data.PlotHighlights = FormPlotHighlights;

            return Task.CompletedTask;
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
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除（含联动删除）。");
                        return;
                    }

                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有素材也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    int totalCategoryDeleted = 0;
                    var categoryIdLookup = Service.GetAllCategories()
                        .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        categoryIdLookup.TryGetValue(categoryName, out var cId);
                        var dataInCategory = Service.GetAllMaterials()
                            .Where(m =>
                                (!string.IsNullOrWhiteSpace(cId) && m.CategoryId == cId) ||
                                (string.IsNullOrWhiteSpace(m.CategoryId) && m.Category == categoryName))
                            .ToList();

                        foreach (var material in dataInCategory)
                        {
                            Service.DeleteMaterial(material.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                        if (!Service.IsCategoryBuiltIn(categoryName))
                        {
                            totalCategoryDeleted++;
                        }
                    }

                    if (totalCategoryDeleted == 0)
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除。");
                        return;
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {totalCategoryDeleted} 个分类及其 {totalDataDeleted} 个素材");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除素材『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteMaterial(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"素材『{_currentEditingData.Name}』已删除");
                    if (Service.GetAllMaterials().Count == 0)
                        _ = ClearSpecAsync();

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或素材");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });
    }
}
