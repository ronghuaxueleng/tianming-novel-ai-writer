using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Modules.Design.Templates.CreativeMaterials.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Framework.UI.Workspace.Services.Spec;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CreativeMaterialsViewModel : DataManagementViewModelBase<CreativeMaterialData, CreativeMaterialCategory, CreativeMaterialsService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly IFocusContextService _focusContextService;
        private readonly BookAnalysisService _bookAnalysisService;
        private readonly IWorkScopeService _workScopeService;
        private readonly SpecLoader _specLoader;
        private string _formName = string.Empty;
        private string _formIcon = "📝";
        private string _formStatus = "已启用";
        private string _formCategory = string.Empty;

        public string FormName { get => _formName; set { _formName = value; OnPropertyChanged(); } }
        public string FormIcon { get => _formIcon; set { _formIcon = value; OnPropertyChanged(); } }
        public string FormStatus { get => _formStatus; set { _formStatus = value; OnPropertyChanged(); } }

        public string FormCategory
        {
            get => _formCategory;
            set
            {
                if (_formCategory != value)
                {
                    _formCategory = value;
                    OnPropertyChanged();
                    OnCategoryValueChanged(_formCategory);
                }
            }
        }

        private async Task SyncScopeFromSelectionAsync(string? sourceBookId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceBookId))
                {
                    await _workScopeService.ClearScopeAsync();
                    TM.App.Log("[CreativeMaterialsViewModel] 已清空全局 CurrentSourceBookId");
                    return;
                }

                await _workScopeService.SetCurrentScopeAsync(sourceBookId);
                TM.App.Log($"[CreativeMaterialsViewModel] 已设置全局 CurrentSourceBookId: {sourceBookId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] 设置 CurrentSourceBookId 失败: {ex.Message}");
            }
        }

        private string _formSourceBookId = string.Empty;
        private string _formSourceBookName = string.Empty;
        private string _formGenre = string.Empty;
        private string _formOverallIdea = string.Empty;

        private bool _genreManuallySet;
        private bool _suppressGenreManualMark;

        public string FormSourceBookId
        {
            get => _formSourceBookId;
            set
            {
                if (_formSourceBookId != value)
                {
                    _formSourceBookId = value;
                    OnPropertyChanged();
                    UpdateSourceBookName();
                    _ = SyncScopeFromSelectionAsync(_formSourceBookId);
                }
            }
        }
        public string FormSourceBookName { get => _formSourceBookName; set { _formSourceBookName = value; OnPropertyChanged(); } }
        public string FormGenre
        {
            get => _formGenre;
            set
            {
                if (_formGenre != value)
                {
                    _formGenre = value;
                    if (!_suppressGenreManualMark)
                    {
                        _genreManuallySet = !string.IsNullOrWhiteSpace(_formGenre);
                    }
                    OnPropertyChanged();
                }
            }
        }
        public string FormOverallIdea { get => _formOverallIdea; set { _formOverallIdea = value; OnPropertyChanged(); } }

        private string _formWorldBuildingMethod = string.Empty;
        private string _formPowerSystemDesign = string.Empty;
        private string _formEnvironmentDescription = string.Empty;
        private string _formFactionDesign = string.Empty;
        private string _formWorldviewHighlights = string.Empty;

        public string FormWorldBuildingMethod { get => _formWorldBuildingMethod; set { _formWorldBuildingMethod = value; OnPropertyChanged(); } }
        public string FormPowerSystemDesign { get => _formPowerSystemDesign; set { _formPowerSystemDesign = value; OnPropertyChanged(); } }
        public string FormEnvironmentDescription { get => _formEnvironmentDescription; set { _formEnvironmentDescription = value; OnPropertyChanged(); } }
        public string FormFactionDesign { get => _formFactionDesign; set { _formFactionDesign = value; OnPropertyChanged(); } }
        public string FormWorldviewHighlights { get => _formWorldviewHighlights; set { _formWorldviewHighlights = value; OnPropertyChanged(); } }

        private string _formProtagonistDesign = string.Empty;
        private string _formSupportingRoles = string.Empty;
        private string _formCharacterRelations = string.Empty;
        private string _formGoldenFingerDesign = string.Empty;
        private string _formCharacterHighlights = string.Empty;

        public string FormProtagonistDesign { get => _formProtagonistDesign; set { _formProtagonistDesign = value; OnPropertyChanged(); } }
        public string FormSupportingRoles { get => _formSupportingRoles; set { _formSupportingRoles = value; OnPropertyChanged(); } }
        public string FormCharacterRelations { get => _formCharacterRelations; set { _formCharacterRelations = value; OnPropertyChanged(); } }
        public string FormGoldenFingerDesign { get => _formGoldenFingerDesign; set { _formGoldenFingerDesign = value; OnPropertyChanged(); } }
        public string FormCharacterHighlights { get => _formCharacterHighlights; set { _formCharacterHighlights = value; OnPropertyChanged(); } }

        private string _formPlotStructure = string.Empty;
        private string _formConflictDesign = string.Empty;
        private string _formClimaxArrangement = string.Empty;
        private string _formForeshadowingTechnique = string.Empty;
        private string _formPlotHighlights = string.Empty;

        public string FormPlotStructure { get => _formPlotStructure; set { _formPlotStructure = value; OnPropertyChanged(); } }
        public string FormConflictDesign { get => _formConflictDesign; set { _formConflictDesign = value; OnPropertyChanged(); } }
        public string FormClimaxArrangement { get => _formClimaxArrangement; set { _formClimaxArrangement = value; OnPropertyChanged(); } }
        public string FormForeshadowingTechnique { get => _formForeshadowingTechnique; set { _formForeshadowingTechnique = value; OnPropertyChanged(); } }
        public string FormPlotHighlights { get => _formPlotHighlights; set { _formPlotHighlights = value; OnPropertyChanged(); } }

        private List<BookAnalysisOption> _bookOptions = new();
        public List<BookAnalysisOption> BookOptions { get => _bookOptions; set { _bookOptions = value; OnPropertyChanged(); } }

        private List<GenreInfo> _genreOptions = new();
        public List<GenreInfo> GenreOptions { get => _genreOptions; set { _genreOptions = value; OnPropertyChanged(); } }

        private void UpdateSourceBookName()
        {
            var book = BookOptions.FirstOrDefault(b => b.Id == FormSourceBookId);
            FormSourceBookName = book?.Name ?? string.Empty;

            if (!_genreManuallySet)
            {
                var genre = book?.Genre;
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    _suppressGenreManualMark = true;
                    FormGenre = genre;
                    _suppressGenreManualMark = false;
                }
            }
        }

        private void LoadBookOptions()
        {
            var analyses = _bookAnalysisService.GetAllAnalysis();
            BookOptions = analyses
                .Select(b => new BookAnalysisOption { Id = b.Id, Name = b.Name, Author = b.Author, Genre = b.Genre })
                .ToList();

            GenreOptions = LoadGenresFromSpec();
        }

        public void RefreshBookOptions() => LoadBookOptions();

        private List<GenreInfo> LoadGenresFromSpec()
        {
            try
            {
                var specTemplates = _promptRepository.GetAllTemplates()
                    .Where(t => t.Tags != null && t.Tags.Contains("Spec") && !string.IsNullOrWhiteSpace(t.Category))
                    .ToList();

                if (specTemplates.Count == 0)
                {
                    TM.App.Log("[CreativeMaterials] 未找到任何Spec模板，题材下拉为空");
                    return new List<GenreInfo>();
                }

                return specTemplates.Select(t => new GenreInfo
                {
                    Name = t.Category,
                    Icon = t.Icon,
                    Description = ExtractShortDescription(t.Description),
                    Elements = ExtractElements(t.SystemPrompt),
                    Avoidances = ExtractAvoidances(t.SystemPrompt),
                }).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterials] 读取Spec题材失败: {ex.Message}");
                return new List<GenreInfo>();
            }
        }

        private static string ExtractShortDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var idx = description.IndexOf('，');
            return idx > 0 && idx < description.Length - 1 ? description[(idx + 1)..] : description;
        }

        private static string ExtractElements(string? systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt)) return string.Empty;
            const string key = "【必须包含】";
            var start = systemPrompt.IndexOf(key);
            if (start < 0) return string.Empty;
            start += key.Length;
            var end = systemPrompt.IndexOf('\n', start);
            var raw = end > start ? systemPrompt[start..end] : systemPrompt[start..];
            return raw.Trim().Replace(",", "、");
        }

        private static string ExtractAvoidances(string? systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt)) return string.Empty;
            const string key = "【必须避免】";
            var start = systemPrompt.IndexOf(key);
            if (start < 0) return string.Empty;
            start += key.Length;
            var end = systemPrompt.IndexOf('\n', start);
            var raw = end > start ? systemPrompt[start..end] : systemPrompt[start..];
            return raw.Trim().Replace(",", "、");
        }

        private GenreInfo? FindGenreInfo(string genreName)
        {
            if (string.IsNullOrWhiteSpace(genreName)) return null;
            return GenreOptions.FirstOrDefault(g => string.Equals(g.Name, genreName, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> StatusOptions { get; } = new()
        {
            "已禁用", "已启用"
        };

        protected override string DefaultDataIcon => "💡";

        protected override int GetMaxCategoryCount() => 1;
        protected override int GetMaxDataCountPerCategory() => 1;
        protected override string GetCategoryLimitMessage()
            => "创作模板仅支持系统内置唯一分类，不允许新建分类。";
        protected override string GetDataLimitMessage()
            => "当前素材库已有创作模板，请先删除旧模板，再构建新的创作模板。";

        public CreativeMaterialsViewModel(IPromptRepository promptRepository, IFocusContextService focusContextService, BookAnalysisService bookAnalysisService, IWorkScopeService workScopeService, SpecLoader specLoader)
        {
            _promptRepository = promptRepository;
            _focusContextService = focusContextService;
            _bookAnalysisService = bookAnalysisService;
            _workScopeService = workScopeService;
            _specLoader = specLoader;
            LoadBookOptions();
        }

        protected override CreativeMaterialData? CreateNewData(string? categoryName = null)
        {
            return new CreativeMaterialData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新素材",
                Category = categoryName ?? string.Empty,
                Icon = DefaultDataIcon,
                IsEnabled = true,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllMaterials();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateMaterial(_currentEditingData);
        }

        protected override List<CreativeMaterialCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<CreativeMaterialData> GetAllDataItems() => Service.GetAllMaterials();

        protected override string GetDataCategory(CreativeMaterialData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(CreativeMaterialData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = data.Icon,
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(CreativeMaterialData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SourceBookName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Genre.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OverallIdea.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.WorldBuildingMethod.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.PowerSystemDesign.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.EnvironmentDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.FactionDesign.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.WorldviewHighlights.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ProtagonistDesign.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SupportingRoles.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.CharacterRelations.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.GoldenFingerDesign.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.CharacterHighlights.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.PlotStructure.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ConflictDesign.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ClimaxArrangement.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ForeshadowingTechnique.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.PlotHighlights.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

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
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(CreativeMaterialData data)
        {
            FormName = data.Name;
            FormIcon = data.Icon;
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;
            FormSourceBookId = data.SourceBookId ?? string.Empty;
            FormSourceBookName = data.SourceBookName;
            FormGenre = data.Genre;
            FormOverallIdea = data.OverallIdea;
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
                if (!string.IsNullOrWhiteSpace(existing.SourceBookId))
                    FormSourceBookId = existing.SourceBookId;
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
            FormSourceBookId = string.Empty;
            FormSourceBookName = string.Empty;
            FormGenre = string.Empty;
            _genreManuallySet = false;
            FormOverallIdea = string.Empty;
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

        protected override string NewItemTypeName => "素材";
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
                GlobalToast.Error("新建失败", ex.Message);
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
                GlobalToast.Error("保存失败", ex.Message);
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

        private async System.Threading.Tasks.Task CreateCategoryCoreAsync()
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

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
        }

        private async System.Threading.Tasks.Task CreateMaterialCoreAsync()
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

        private async System.Threading.Tasks.Task UpdateCategoryCoreAsync()
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

        private async System.Threading.Tasks.Task UpdateMaterialCoreAsync()
        {
            if (_currentEditingData == null) return;

            await UpdateDataFromForm(_currentEditingData);
            await Service.UpdateMaterialAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"素材『{_currentEditingData.Name}』已更新");
            await SyncSpecWithGenreAsync(FormGenre);
        }

        private async Task UpdateDataFromForm(CreativeMaterialData data)
        {
            var newIsEnabled = (FormStatus == "已启用");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckScopeBeforeEnable(FormSourceBookId, FormName))
                {
                    FormStatus = "已禁用";
                    return;
                }
            }

            data.Name = FormName;
            data.Icon = GetDataIconForSave(FormIcon);
            data.Category = FormCategory;
            data.IsEnabled = newIsEnabled;
            data.ModifiedTime = DateTime.Now;
            data.SourceBookId = FormSourceBookId;
            data.SourceBookName = FormSourceBookName;
            data.Genre = FormGenre;
            data.OverallIdea = FormOverallIdea;
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

            await SyncScopeFromSelectionAsync(FormSourceBookId);
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
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllMaterials()
                            .Where(m => m.Category == categoryName)
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
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        private TM.Framework.Common.ViewModels.AIGenerationConfig? _cachedConfig;
        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return _cachedConfig ??= new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "素材设计师",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "生成素材",
                ProgressMessage = "正在生成三维度素材，请稍候...",
                CompleteMessage = "AI素材已生成，请查看并编辑",
                InputVariables = new()
                {
                    ["素材名称"] = () => FormName,
                    ["题材类型"] = () => FindGenreInfo(FormGenre)?.ToPromptString() ?? FormGenre,
                    ["来源拆书"] = () => FormSourceBookName,
                },
                OutputFields = new()
                {
                    ["整体构思"] = v => FormOverallIdea = v,
                    ["世界观素材-构建手法"] = v => FormWorldBuildingMethod = v,
                    ["世界观素材-力量体系"] = v => FormPowerSystemDesign = v,
                    ["世界观素材-环境描写"] = v => FormEnvironmentDescription = v,
                    ["世界观素材-势力设计"] = v => FormFactionDesign = v,
                    ["世界观素材-亮点"] = v => FormWorldviewHighlights = v,
                    ["角色素材-主角塑造"] = v => FormProtagonistDesign = v,
                    ["角色素材-配角设计"] = v => FormSupportingRoles = v,
                    ["角色素材-人物关系"] = v => FormCharacterRelations = v,
                    ["角色素材-金手指"] = v => FormGoldenFingerDesign = v,
                    ["角色素材-角色亮点"] = v => FormCharacterHighlights = v,
                    ["剧情素材-情节结构"] = v => FormPlotStructure = v,
                    ["剧情素材-冲突设计"] = v => FormConflictDesign = v,
                    ["剧情素材-高潮布局"] = v => FormClimaxArrangement = v,
                    ["剧情素材-伏笔设计"] = v => FormForeshadowingTechnique = v,
                    ["剧情素材-剧情亮点"] = v => FormPlotHighlights = v,
                },
                OutputFieldGetters = new()
                {
                    ["整体构思"] = () => FormOverallIdea,
                    ["世界观素材-构建手法"] = () => FormWorldBuildingMethod,
                    ["世界观素材-力量体系"] = () => FormPowerSystemDesign,
                    ["世界观素材-环境描写"] = () => FormEnvironmentDescription,
                    ["世界观素材-势力设计"] = () => FormFactionDesign,
                    ["世界观素材-亮点"] = () => FormWorldviewHighlights,
                    ["角色素材-主角塑造"] = () => FormProtagonistDesign,
                    ["角色素材-配角设计"] = () => FormSupportingRoles,
                    ["角色素材-人物关系"] = () => FormCharacterRelations,
                    ["角色素材-金手指"] = () => FormGoldenFingerDesign,
                    ["角色素材-角色亮点"] = () => FormCharacterHighlights,
                    ["剧情素材-情节结构"] = () => FormPlotStructure,
                    ["剧情素材-冲突设计"] = () => FormConflictDesign,
                    ["剧情素材-高潮布局"] = () => FormClimaxArrangement,
                    ["剧情素材-伏笔设计"] = () => FormForeshadowingTechnique,
                    ["剧情素材-剧情亮点"] = () => FormPlotHighlights,
                },
                ContextProvider = async () =>
                {
                    var sb = new System.Text.StringBuilder();

                    var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();
                    if (string.IsNullOrEmpty(currentSourceBookId))
                    {
                        sb.AppendLine("<notice>");
                        sb.AppendLine("请先在智能拆书模块创建并保存拆书条目，Scope 将自动绑定。");
                        sb.AppendLine("</notice>");
                        return sb.ToString();
                    }

                    var focusId = _currentEditingData?.Id ?? string.Empty;
                    var context = await _focusContextService.GetDesignContextAsync(focusId, "Templates", currentSourceBookId);

                    if (!string.IsNullOrWhiteSpace(FormGenre) && _promptRepository != null)
                    {
                        var specTemplates = _promptRepository.GetTemplatesByCategory(FormGenre);
                        var specTemplate = specTemplates?
                            .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                            .OrderByDescending(t => t.IsDefault)
                            .ThenByDescending(t => t.IsBuiltIn)
                            .FirstOrDefault();
                        if (specTemplate != null)
                        {
                            sb.AppendLine("<genre_spec priority=\"highest\" source=\"prompt_library\">");
                            sb.AppendLine(ExtractDesignSpec(specTemplate.SystemPrompt));
                            sb.AppendLine();
                            sb.AppendLine("以上规格约束具有最高优先级。后续拆书数据仅供借鉴写作技法，所有设计内容的题材风格、世界观元素、避免事项必须严格遵守此规格。");
                            sb.AppendLine("</genre_spec>");
                            sb.AppendLine();
                        }
                    }

                    if (!string.IsNullOrEmpty(context.GlobalSummary?.StorySummary))
                    {
                        sb.AppendLine("<section name=\"story_theme\">");
                        sb.AppendLine(context.GlobalSummary.StorySummary);
                        sb.AppendLine("</section>");
                        sb.AppendLine();
                    }

                    var sourceBookId = !string.IsNullOrEmpty(currentSourceBookId) ? currentSourceBookId : FormSourceBookId;
                    var sourceBook = _bookAnalysisService.GetAllAnalysis().FirstOrDefault(b => b.Id == sourceBookId);
                    if (sourceBook != null)
                    {
                        sb.AppendLine("<section name=\"source_book\">");
                        sb.AppendLine($"条目: {sourceBook.Name}");
                        sb.AppendLine();

                        if (!string.IsNullOrWhiteSpace(sourceBook.WorldBuildingMethod)
                            || !string.IsNullOrWhiteSpace(sourceBook.PowerSystemDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.EnvironmentDescription)
                            || !string.IsNullOrWhiteSpace(sourceBook.FactionDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.WorldviewHighlights))
                        {
                            sb.AppendLine("<section name=\"worldview_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.WorldBuildingMethod))
                                sb.AppendLine(sourceBook.WorldBuildingMethod);
                            if (!string.IsNullOrWhiteSpace(sourceBook.PowerSystemDesign))
                                sb.AppendLine(sourceBook.PowerSystemDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.EnvironmentDescription))
                                sb.AppendLine(sourceBook.EnvironmentDescription);
                            if (!string.IsNullOrWhiteSpace(sourceBook.FactionDesign))
                                sb.AppendLine(sourceBook.FactionDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.WorldviewHighlights))
                                sb.AppendLine(sourceBook.WorldviewHighlights);
                            sb.AppendLine("</section>");
                            sb.AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(sourceBook.ProtagonistDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.SupportingRoles)
                            || !string.IsNullOrWhiteSpace(sourceBook.CharacterRelations)
                            || !string.IsNullOrWhiteSpace(sourceBook.GoldenFingerDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.CharacterHighlights))
                        {
                            sb.AppendLine("<section name=\"character_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.ProtagonistDesign))
                                sb.AppendLine(sourceBook.ProtagonistDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.SupportingRoles))
                                sb.AppendLine(sourceBook.SupportingRoles);
                            if (!string.IsNullOrWhiteSpace(sourceBook.CharacterRelations))
                                sb.AppendLine(sourceBook.CharacterRelations);
                            if (!string.IsNullOrWhiteSpace(sourceBook.GoldenFingerDesign))
                                sb.AppendLine(sourceBook.GoldenFingerDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.CharacterHighlights))
                                sb.AppendLine(sourceBook.CharacterHighlights);
                            sb.AppendLine("</section>");
                            sb.AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(sourceBook.PlotStructure)
                            || !string.IsNullOrWhiteSpace(sourceBook.ConflictDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.ClimaxArrangement)
                            || !string.IsNullOrWhiteSpace(sourceBook.ForeshadowingTechnique)
                            || !string.IsNullOrWhiteSpace(sourceBook.PlotHighlights))
                        {
                            sb.AppendLine("<section name=\"plot_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.PlotStructure))
                                sb.AppendLine(sourceBook.PlotStructure);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ConflictDesign))
                                sb.AppendLine(sourceBook.ConflictDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ClimaxArrangement))
                                sb.AppendLine(sourceBook.ClimaxArrangement);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ForeshadowingTechnique))
                                sb.AppendLine(sourceBook.ForeshadowingTechnique);
                            if (!string.IsNullOrWhiteSpace(sourceBook.PlotHighlights))
                                sb.AppendLine(sourceBook.PlotHighlights);
                            sb.AppendLine("</section>");
                        }

                        sb.AppendLine("</section>");
                    }

                    sb.AppendLine();
                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「角色素材-主角塑造」第一行必须使用固定格式：主角姓名：XXX（仅一个姓名，不要附加解释）。");
                    sb.AppendLine("2. 「角色素材-配角设计」中出现的配角尽量给出明确姓名，并保持前后一致。");
                    sb.AppendLine("3. 「角色素材-人物关系」「剧情素材」等字段涉及角色引用时，优先使用已出现的主角/配角姓名，避免出现无名或临时新角色。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                BatchFieldKeyMap = new()
                {
                    ["素材名称"] = "Name",
                    ["整体构思"] = "OverallIdea",
                    ["世界观素材-构建手法"] = "WorldBuildingMethod",
                    ["世界观素材-力量体系"] = "PowerSystemDesign",
                    ["世界观素材-环境描写"] = "EnvironmentDescription",
                    ["世界观素材-势力设计"] = "FactionDesign",
                    ["世界观素材-亮点"] = "WorldviewHighlights",
                    ["角色素材-主角塑造"] = "ProtagonistDesign",
                    ["角色素材-配角设计"] = "SupportingRoles",
                    ["角色素材-人物关系"] = "CharacterRelations",
                    ["角色素材-金手指"] = "GoldenFingerDesign",
                    ["角色素材-角色亮点"] = "CharacterHighlights",
                    ["剧情素材-情节结构"] = "PlotStructure",
                    ["剧情素材-冲突设计"] = "ConflictDesign",
                    ["剧情素材-高潮布局"] = "ClimaxArrangement",
                    ["剧情素材-伏笔设计"] = "ForeshadowingTechnique",
                    ["剧情素材-剧情亮点"] = "PlotHighlights",
                },
                BatchIndexFields = new() { "Name", "OverallIdea", "WorldBuildingMethod" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllMaterials().Select(r => r.Name);

        protected override bool SupportsBatch(TreeNodeItem categoryNode) => false;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllMaterials().Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"素材_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[CreativeMaterialsViewModel] 跳过已存在素材: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var data = new CreativeMaterialData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        Icon = DefaultDataIcon,
                        IsEnabled = true,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now,
                        SourceBookId = FormSourceBookId,
                        SourceBookName = FormSourceBookName,
                        Genre = FormGenre,
                        OverallIdea = reader.GetString("OverallIdea"),
                        WorldBuildingMethod = reader.GetString("WorldBuildingMethod"),
                        PowerSystemDesign = reader.GetString("PowerSystemDesign"),
                        EnvironmentDescription = reader.GetString("EnvironmentDescription"),
                        FactionDesign = reader.GetString("FactionDesign"),
                        WorldviewHighlights = reader.GetString("WorldviewHighlights"),
                        ProtagonistDesign = reader.GetString("ProtagonistDesign"),
                        SupportingRoles = reader.GetString("SupportingRoles"),
                        CharacterRelations = reader.GetString("CharacterRelations"),
                        GoldenFingerDesign = reader.GetString("GoldenFingerDesign"),
                        CharacterHighlights = reader.GetString("CharacterHighlights"),
                        PlotStructure = reader.GetString("PlotStructure"),
                        ConflictDesign = reader.GetString("ConflictDesign"),
                        ClimaxArrangement = reader.GetString("ClimaxArrangement"),
                        ForeshadowingTechnique = reader.GetString("ForeshadowingTechnique"),
                        PlotHighlights = reader.GetString("PlotHighlights")
                    };

                    entity["Name"] = name;
                    await Service.AddMaterialAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            if (result.Count > 0)
            {
                var genreToSync = FormGenre;
                if (string.IsNullOrWhiteSpace(genreToSync))
                {
                    var categoryNames = CollectCategoryAndChildrenNames(categoryName);
                    genreToSync = Service.GetAllMaterials()
                        .Where(m => categoryNames.Contains(m.Category) && m.IsEnabled && !string.IsNullOrWhiteSpace(m.Genre))
                        .OrderByDescending(m => m.ModifiedTime)
                        .FirstOrDefault()?.Genre ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(genreToSync))
                        TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: FormGenre为空，从子分类继承题材 → {genreToSync}");
                }
                if (!string.IsNullOrWhiteSpace(genreToSync))
                    await SyncSpecWithGenreAsync(genreToSync);
            }
            return result;
        }

        protected override void OnAfterDeleteAll(int deletedCount)
        {
            if (deletedCount > 0)
                _ = ClearSpecAsync();
        }

        private static readonly HashSet<string> _designExcludedTags = new()
        {
            "目标字数", "段落长度", "对话比例", "节奏要求", "叙述视角"
        };

        private static string ExtractDesignSpec(string systemPrompt)
        {
            var lines = systemPrompt.Split('\n');
            return string.Join("\n", lines.Where(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^【([^】]+)】");
                return !m.Success || !_designExcludedTags.Contains(m.Groups[1].Value);
            }));
        }

        private async Task SyncSpecWithGenreAsync(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre) || _promptRepository == null) return;
            try
            {
                var specTemplates = _promptRepository.GetTemplatesByCategory(genre);
                var specTemplate = specTemplates?
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .FirstOrDefault();
                if (specTemplate == null) return;

                var spec = ParseSpecFromTemplate(specTemplate.SystemPrompt, specTemplate.Name);
                await _specLoader.SaveProjectSpecAsync(spec);
                _specLoader.InvalidateCache();
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 已同步为题材: {genre} → {specTemplate.Name}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 同步失败: {ex.Message}");
            }
        }

        private async Task ClearSpecAsync()
        {
            try
            {
                var current = await _specLoader.LoadProjectSpecAsync() ?? new CreativeSpec();
                current.TemplateName = null;
                await _specLoader.SaveProjectSpecAsync(current);
                _specLoader.InvalidateCache();
                TM.App.Log("[CreativeMaterialsViewModel] Spec 已清空模板选择（创作模板已删除）");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 清空失败: {ex.Message}");
            }
        }

        private static CreativeSpec ParseSpecFromTemplate(string systemPrompt, string templateName)
        {
            var spec = new CreativeSpec { TemplateName = templateName };
            foreach (var line in systemPrompt.Split('\n'))
            {
                if (line.Contains("【写作风格】")) spec.WritingStyle = ExtractTagValue(line, "【写作风格】");
                else if (line.Contains("【叙述视角】")) spec.Pov = ExtractTagValue(line, "【叙述视角】");
                else if (line.Contains("【情感基调】")) spec.Tone = ExtractTagValue(line, "【情感基调】");
                else if (line.Contains("【目标字数】")) spec.TargetWordCount = ParseTagInt(ExtractTagValue(line, "【目标字数】"));
                else if (line.Contains("【段落长度】")) spec.ParagraphLength = ParseTagInt(ExtractTagValue(line, "【段落长度】"));
                else if (line.Contains("【对话比例】")) spec.DialogueRatio = ParseTagPercent(ExtractTagValue(line, "【对话比例】"));
                else if (line.Contains("【必须包含】")) spec.MustInclude = ExtractTagValue(line, "【必须包含】")?.Split(',', '，', '、');
                else if (line.Contains("【必须避免】")) spec.MustAvoid = ExtractTagValue(line, "【必须避免】")?.Split(',', '，', '、');
            }
            return spec;
        }

        private static string? ExtractTagValue(string line, string tag)
        {
            var idx = line.IndexOf(tag);
            return idx >= 0 ? line.Substring(idx + tag.Length).Trim() : null;
        }

        private static int? ParseTagInt(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var numStr = new string(value.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(numStr, out var r) ? r : null;
        }

        private static double? ParseTagPercent(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var numStr = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (double.TryParse(numStr, out var r)) return r > 1 ? r / 100.0 : r;
            return null;
        }
    }

    public class BookAnalysisOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(Author) ? Name : $"{Name} ← {Author}";
    }

    public class GenreInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Elements { get; set; } = string.Empty;
        public string Avoidances { get; set; } = string.Empty;

        public string ToPromptString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Name}（{Description}，核心元素：{Elements}");
            if (!string.IsNullOrWhiteSpace(Avoidances))
                sb.Append($"，必须避免：{Avoidances}");
            sb.Append('）');
            return sb.ToString();
        }
    }
}
