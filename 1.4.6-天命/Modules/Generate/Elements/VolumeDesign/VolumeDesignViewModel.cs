using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Framework.Common.Models;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeDesignViewModel : DataManagementViewModelBase<VolumeDesignData, VolumeDesignCategory, VolumeDesignService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly IWorkScopeService _workScopeService;
        private readonly CharacterRulesService _characterService;
        private readonly FactionRulesService _factionService;
        private readonly LocationRulesService _locationService;

        public VolumeDesignViewModel(IPromptRepository promptRepository, ContextService contextService, IWorkScopeService workScopeService, CharacterRulesService characterService, FactionRulesService factionService, LocationRulesService locationService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _workScopeService = workScopeService;
            _characterService = characterService;
            _factionService = factionService;
            _locationService = locationService;
            RefreshEntityOptions();
        }
        private string _formName = string.Empty;
        private string _formIcon = "📚";
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

        private int _formVolumeNumber;
        private string _formVolumeTitle = string.Empty;
        private string _formVolumeTheme = string.Empty;
        private string _formStageGoal = string.Empty;
        private string _formEstimatedWordCount = string.Empty;
        private int _formStartChapter;
        private int _formEndChapter;

        public int FormVolumeNumber { get => _formVolumeNumber; set { _formVolumeNumber = value; OnPropertyChanged(); } }
        public string FormVolumeTitle { get => _formVolumeTitle; set { _formVolumeTitle = value; OnPropertyChanged(); } }
        public string FormVolumeTheme { get => _formVolumeTheme; set { _formVolumeTheme = value; OnPropertyChanged(); } }
        public string FormStageGoal { get => _formStageGoal; set { _formStageGoal = value; OnPropertyChanged(); } }
        public string FormEstimatedWordCount { get => _formEstimatedWordCount; set { _formEstimatedWordCount = value; OnPropertyChanged(); } }
        public int FormStartChapter { get => _formStartChapter; set { _formStartChapter = value; OnPropertyChanged(); } }
        public int FormEndChapter { get => _formEndChapter; set { _formEndChapter = value; OnPropertyChanged(); } }

        private string _formMainConflict = string.Empty;
        private string _formPressureSource = string.Empty;
        private string _formKeyEvents = string.Empty;
        private string _formOpeningState = string.Empty;
        private string _formEndingState = string.Empty;

        public string FormMainConflict { get => _formMainConflict; set { _formMainConflict = value; OnPropertyChanged(); } }
        public string FormPressureSource { get => _formPressureSource; set { _formPressureSource = value; OnPropertyChanged(); } }
        public string FormKeyEvents { get => _formKeyEvents; set { _formKeyEvents = value; OnPropertyChanged(); } }
        public string FormOpeningState { get => _formOpeningState; set { _formOpeningState = value; OnPropertyChanged(); } }
        public string FormEndingState { get => _formEndingState; set { _formEndingState = value; OnPropertyChanged(); } }

        private string _formChapterAllocationOverview = string.Empty;
        private string _formPlotAllocation = string.Empty;
        private string _formChapterGenerationHints = string.Empty;

        public string FormChapterAllocationOverview { get => _formChapterAllocationOverview; set { _formChapterAllocationOverview = value; OnPropertyChanged(); } }
        public string FormPlotAllocation { get => _formPlotAllocation; set { _formPlotAllocation = value; OnPropertyChanged(); } }
        public string FormChapterGenerationHints { get => _formChapterGenerationHints; set { _formChapterGenerationHints = value; OnPropertyChanged(); } }

        private string _formReferencedCharacterNames = string.Empty;
        private string _formReferencedFactionNames = string.Empty;
        private string _formReferencedLocationNames = string.Empty;

        public string FormReferencedCharacterNames { get => _formReferencedCharacterNames; set { _formReferencedCharacterNames = value; OnPropertyChanged(); } }
        public string FormReferencedFactionNames { get => _formReferencedFactionNames; set { _formReferencedFactionNames = value; OnPropertyChanged(); } }
        public string FormReferencedLocationNames { get => _formReferencedLocationNames; set { _formReferencedLocationNames = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        private List<string> _availableCharacters = new();
        private List<string> _availableFactions = new();
        private List<string> _availableLocations = new();

        public List<string> AvailableCharacters { get => _availableCharacters; private set { _availableCharacters = value; OnPropertyChanged(); } }
        public List<string> AvailableFactions { get => _availableFactions; private set { _availableFactions = value; OnPropertyChanged(); } }
        public List<string> AvailableLocations { get => _availableLocations; private set { _availableLocations = value; OnPropertyChanged(); } }

        private void RefreshEntityOptions()
        {
            var currentScopeId = _workScopeService.CurrentSourceBookId;

            try { AvailableCharacters = _characterService.GetAllCharacterRules().Where(c => c.IsEnabled && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal))).Select(c => c.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载角色列表失败: {ex.Message}"); }

            try { AvailableFactions = _factionService.GetAllFactionRules().Where(f => f.IsEnabled && (string.IsNullOrEmpty(currentScopeId) || string.Equals(f.SourceBookId, currentScopeId, StringComparison.Ordinal))).Select(f => f.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载势力列表失败: {ex.Message}"); }

            try { AvailableLocations = _locationService.GetAllLocationRules().Where(l => l.IsEnabled && (string.IsNullOrEmpty(currentScopeId) || string.Equals(l.SourceBookId, currentScopeId, StringComparison.Ordinal))).Select(l => l.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载地点列表失败: {ex.Message}"); }
        }

        protected override string DefaultDataIcon => "📚";

        protected override VolumeDesignData? CreateNewData(string? categoryName = null)
        {
            var currentScope = _workScopeService.CurrentSourceBookId;
            return new VolumeDesignData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新分卷设计",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                SourceBookId = currentScope,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override async System.Threading.Tasks.Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureServiceInitializedAsync(Service);
            await EnsureServiceInitializedAsync(_characterService);
            await EnsureServiceInitializedAsync(_factionService);
            await EnsureServiceInitializedAsync(_locationService);

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        RefreshEntityOptions();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    RefreshEntityOptions();
                }
            }
            catch
            {
                RefreshEntityOptions();
            }
        }

        protected override async System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var scope = _workScopeService.CurrentSourceBookId;
            FormReferencedCharacterNames = await VolumeResolveNamesAsync(FormReferencedCharacterNames, "character", scope);
            FormReferencedFactionNames   = await VolumeResolveNamesAsync(FormReferencedFactionNames, "faction", scope);
            FormReferencedLocationNames  = await VolumeResolveNamesAsync(FormReferencedLocationNames, "location", scope);
        }

        private System.Threading.Tasks.Task<string> VolumeResolveNamesAsync(string rawNames, string entityType, string? scope)
        {
            if (string.IsNullOrWhiteSpace(rawNames)) return System.Threading.Tasks.Task.FromResult(rawNames);
            var parts = rawNames.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                switch (entityType)
                {
                    case "character":
                        if (_characterService.GetAllCharacterRules().Any(c => c.IsEnabled && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：角色 '{n}' 在上游不存在，已忽略");
                        break;
                    case "faction":
                        if (_factionService.GetAllFactionRules().Any(f => f.IsEnabled && string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：势力 '{n}' 在上游不存在，已忽略");
                        break;
                    case "location":
                        if (_locationService.GetAllLocationRules().Any(l => l.IsEnabled && string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：地点 '{n}' 在上游不存在，已忽略");
                        break;
                }
            }
            return System.Threading.Tasks.Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllVolumeDesigns();

        protected override List<VolumeDesignCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<VolumeDesignData> GetAllDataItems()
            => Service.GetAllVolumeDesigns().OrderBy(v => v.VolumeNumber).ToList();

        protected override string GetDataCategory(VolumeDesignData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(VolumeDesignData data)
        {
            var vol = data.VolumeNumber > 0 ? $"第{data.VolumeNumber}卷" : "未编号";
            var title = string.IsNullOrWhiteSpace(data.VolumeTitle) ? data.Name : data.VolumeTitle;
            return new TreeNodeItem
            {
                Name = $"{vol} {title}",
                Icon = "📚",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(VolumeDesignData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.VolumeTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.StageGoal.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.VolumeTheme.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

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
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(VolumeDesignData data)
        {
            FormName = data.Name;
            FormIcon = "📚";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormVolumeNumber = data.VolumeNumber;
            FormVolumeTitle = data.VolumeTitle;
            FormVolumeTheme = data.VolumeTheme;
            FormStageGoal = data.StageGoal;
            FormEstimatedWordCount = data.EstimatedWordCount;
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
            FormEstimatedWordCount = string.Empty;
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

        protected override string NewItemTypeName => "分卷设计";
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
                    createDataCore: CreateDataCoreAsync,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCoreAsync,
                    updateDataCore: UpdateDataCoreAsync);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", ex.Message);
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
            await Service.AddVolumeDesignAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"分卷『{newData.VolumeTitle}』已创建");
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

        private async System.Threading.Tasks.Task UpdateDataCoreAsync()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            await Service.UpdateVolumeDesignAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"分卷『{_currentEditingData.VolumeTitle}』已更新");
        }

        private void UpdateDataFromForm(VolumeDesignData data)
        {
            data.Name = FormName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.VolumeNumber = FormVolumeNumber;
            data.VolumeTitle = FormVolumeTitle;
            data.VolumeTheme = FormVolumeTheme;
            data.StageGoal = FormStageGoal;
            data.EstimatedWordCount = FormEstimatedWordCount;
            data.StartChapter = FormStartChapter;
            data.EndChapter = FormEndChapter;

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

        private List<string> NormalizeEntityRefList(List<string> rawList, List<string> available)
        {
            if (rawList.Count == 0) return rawList;
            var joined = string.Join("、", rawList);
            var filtered = FilterToCandidatesOrRaw(joined, available);
            return SplitEntityNames(filtered);
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

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllVolumeDesigns()
                            .Where(d => d.Category == categoryName)
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
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return new AIGenerationConfig
            {
                Category = "小说创作者",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "分卷设计",
                ProgressMessage = "正在设计分卷...",
                CompleteMessage = "分卷设计完成",
                InputVariables = new()
                {
                    ["大纲名称"] = () => FormVolumeNumber > 0
                        ? $"第{FormVolumeNumber}卷：{FormVolumeTitle}"
                        : FormVolumeTitle,
                    ["章节标题"] = () => string.Empty,
                    ["场景标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["卷序号"] = v => FormVolumeNumber = SafeParseInt(v),
                    ["卷标题"] = v => FormVolumeTitle = v,
                    ["卷主题"] = v => FormVolumeTheme = v,
                    ["卷阶段目标"] = v => FormStageGoal = v,
                    ["预计字数"] = v => FormEstimatedWordCount = v,

                    ["卷主冲突"] = v => FormMainConflict = v,
                    ["压力来源"] = v => FormPressureSource = v,
                    ["关键转折"] = v => FormKeyEvents = v,
                    ["卷开篇状态"] = v => FormOpeningState = v,
                    ["卷收束状态"] = v => FormEndingState = v,

                    ["章节分配总览"] = v => { if (string.IsNullOrWhiteSpace(FormChapterAllocationOverview)) FormChapterAllocationOverview = v; },
                    ["剧情分配"]     = v => { if (string.IsNullOrWhiteSpace(FormPlotAllocation))            FormPlotAllocation            = v; },
                    ["章节生成提示"] = v => { if (string.IsNullOrWhiteSpace(FormChapterGenerationHints))    FormChapterGenerationHints    = v; },

                    ["出场角色"] = v => FormReferencedCharacterNames = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及势力"] = v => FormReferencedFactionNames = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["涉及地点"] = v => FormReferencedLocationNames = FilterToCandidatesOrRaw(v, AvailableLocations),
                },
                OutputFieldGetters = new()
                {
                    ["卷序号"] = () => FormVolumeNumber.ToString(),
                    ["卷标题"] = () => FormVolumeTitle,
                    ["卷主题"] = () => FormVolumeTheme,
                    ["卷阶段目标"] = () => FormStageGoal,
                    ["预计字数"] = () => FormEstimatedWordCount,

                    ["卷主冲突"] = () => FormMainConflict,
                    ["压力来源"] = () => FormPressureSource,
                    ["关键转折"] = () => FormKeyEvents,
                    ["卷开篇状态"] = () => FormOpeningState,
                    ["卷收束状态"] = () => FormEndingState,

                    ["章节分配总览"] = () => FormChapterAllocationOverview,
                    ["剧情分配"] = () => FormPlotAllocation,
                    ["章节生成提示"] = () => FormChapterGenerationHints,

                    ["出场角色"] = () => FormReferencedCharacterNames,
                    ["涉及势力"] = () => FormReferencedFactionNames,
                    ["涉及地点"] = () => FormReferencedLocationNames,
                },
                ContextProvider = async () =>
                {
                    RefreshEntityOptions();
                    var sb = new System.Text.StringBuilder();
                    var baseContext = await _contextService.GetVolumeDesignContextStringAsync();
                    if (!string.IsNullOrWhiteSpace(baseContext))
                    {
                        sb.AppendLine(baseContext);
                        sb.AppendLine();
                    }

                    int volNum = 0, startChapter = 0, endChapter = 0, targetCount = 0;
                    if (_batchPreCalculatedRanges != null && _batchRangeIndex < _batchPreCalculatedRanges.Count)
                    {
                        var r = _batchPreCalculatedRanges[_batchRangeIndex];
                        volNum = r.VolumeNumber;
                        startChapter = r.StartChapter;
                        endChapter = r.EndChapter;
                        targetCount = r.TargetChapterCount;
                    }
                    else if (FormStartChapter > 0 && FormEndChapter > 0)
                    {
                        volNum = FormVolumeNumber;
                        startChapter = FormStartChapter;
                        endChapter = FormEndChapter;
                        targetCount = FormEndChapter - FormStartChapter + 1;
                    }

                    if (startChapter > 0 && endChapter > 0)
                    {
                        sb.AppendLine($"<section name=\"volume_chapter_range\" locked=\"true\">");
                        sb.AppendLine($"- 本卷为第{volNum}卷，共 {targetCount} 章");
                        sb.AppendLine($"- 章节范围：第 {startChapter} 章 ～ 第 {endChapter} 章");
                        sb.AppendLine($"- 「章节分配总览」「剧情分配」「章节生成提示」中所有章节编号必须严格在此范围内（{startChapter}～{endChapter}），不得使用此范围之外的任何章节编号。");
                        sb.AppendLine("</section>");
                        sb.AppendLine();
                    }

                    if (AvailableCharacters.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选角色", AvailableCharacters, "「出场角色」必须从以下列表中选择，不得编造"));
                    if (AvailableFactions.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选势力", AvailableFactions, "「涉及势力」必须从以下列表中选择，不得编造"));
                    if (AvailableLocations.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选地点", AvailableLocations, "「涉及地点」必须从以下列表中选择，不得编造"));

                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「卷序号」必须为整数（仅数字）。");
                    sb.AppendLine("2. 「预计字数」建议为数字或数字区间，例如：80000 或 80000-100000。");
                    sb.AppendLine("3. 「章节分配总览」「剧情分配」「章节生成提示」如有多条，请在字符串内用换行分条。");
                    sb.AppendLine("4. 「起始章节」「结束章节」「目标章节数」由系统自动分配，无需在JSON中输出这三个字段。");
                    sb.AppendLine("5. 「出场角色」「涉及势力」「涉及地点」必须从上方候选列表中精确选取，以字符串形式输出（不要用JSON数组）。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                SequenceFieldName = "VolumeNumber",
                GetCurrentMaxSequence = (scopeId, categoryName) => Service.GetAllVolumeDesigns()
                    .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Where(c => string.IsNullOrEmpty(scopeId) || string.Equals(c.SourceBookId, scopeId, StringComparison.Ordinal))
                    .Select(c => c.VolumeNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = new()
                {
                    ["卷序号"] = "VolumeNumber",
                    ["卷标题"] = "VolumeTitle",
                    ["卷主题"] = "VolumeTheme",
                    ["卷阶段目标"] = "StageGoal",
                    ["预计字数"] = "EstimatedWordCount",
                    ["卷主冲突"] = "MainConflict",
                    ["压力来源"] = "PressureSource",
                    ["关键转折"] = "KeyEvents",
                    ["卷开篇状态"] = "OpeningState",
                    ["卷收束状态"] = "EndingState",
                    ["章节分配总览"] = "ChapterAllocationOverview",
                    ["剧情分配"] = "PlotAllocation",
                    ["章节生成提示"] = "ChapterGenerationHints",
                    ["出场角色"] = "ReferencedCharacterNames",
                    ["涉及势力"] = "ReferencedFactionNames",
                    ["涉及地点"] = "ReferencedLocationNames",
                },
                BatchIndexFields = new() { "VolumeNumber", "VolumeTheme", "StageGoal" }
            };
        }

        private static int SafeParseInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (int.TryParse(text.Trim(), out var v)) return v;

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out v)) return v;
            return 0;
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prompt)
                && _batchPreCalculatedRanges != null
                && _batchRangeIndex < _batchPreCalculatedRanges.Count)
            {
                var r = _batchPreCalculatedRanges[_batchRangeIndex];
                if (r.StartChapter > 0 && r.EndChapter > 0)
                {
                    var sb = new System.Text.StringBuilder(prompt);
                    sb.AppendLine();
                    sb.AppendLine($"<volume_chapter_range mandatory=\"true\">");
                    sb.AppendLine($"- 本卷为第{r.VolumeNumber}卷，共 {r.TargetChapterCount} 章");
                    sb.AppendLine($"- 章节范围：第 {r.StartChapter} 章 ～ 第 {r.EndChapter} 章");
                    sb.AppendLine($"- 「章节分配总览」「剧情分配」「章节生成提示」中所有章节编号必须严格在此范围内（{r.StartChapter}～{r.EndChapter}），不得使用此范围之外的任何章节编号。");
                    sb.AppendLine("</volume_chapter_range>");
                    return sb.ToString();
                }
            }
            return prompt;
        }

        private List<VolumeChapterRange>? _batchPreCalculatedRanges;
        private int _batchRangeIndex;

        protected override async System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            var currentScopeId = _workScopeService.CurrentSourceBookId;

            PlotRulesService plotRulesService;
            try { plotRulesService = TM.Framework.Common.Services.ServiceLocator.Get<PlotRulesService>(); }
            catch { GlobalToast.Error("服务错误", "无法获取剧情规则服务"); return null; }

            try { await plotRulesService.InitializeAsync(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 初始化剧情规则服务失败: {ex.Message}"); }

            var validVolumes = plotRulesService.GetAllPlotRules()
                .Where(p => p.IsEnabled
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(p.SourceBookId, currentScopeId, StringComparison.Ordinal))
                       && int.TryParse(p.TargetVolume?.Trim(), out var _v) && _v > 0)
                .Select(p => { int.TryParse(p.TargetVolume!.Trim(), out var n); return n; })
                .Distinct().ToList();

            if (validVolumes.Count == 0)
            {
                GlobalToast.Warning("缺少父约束", "请先在剧情规则中填写总卷数，再执行分卷批量生成");
                return null;
            }
            if (validVolumes.Count > 1)
            {
                GlobalToast.Warning("总卷数冲突", $"剧情规则中存在多个不同总卷数（{string.Join("、", validVolumes)}），请先统一后再生成");
                return null;
            }
            var totalVolumes = validVolumes[0];

            OutlineService outlineService;
            try { outlineService = TM.Framework.Common.Services.ServiceLocator.Get<OutlineService>(); }
            catch { GlobalToast.Error("服务错误", "无法获取大纲服务"); return null; }

            try { await outlineService.InitializeAsync(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 初始化大纲服务失败: {ex.Message}"); }

            var validChapters = outlineService.GetAllOutlines()
                .Where(o => o.IsEnabled
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(o.SourceBookId, currentScopeId, StringComparison.Ordinal))
                       && o.TotalChapterCount > 0)
                .Select(o => o.TotalChapterCount)
                .Distinct().ToList();

            if (validChapters.Count == 0)
            {
                GlobalToast.Warning("缺少父约束", "请先在大纲设计中填写总章节数，再执行分卷批量生成");
                return null;
            }
            if (validChapters.Count > 1)
            {
                GlobalToast.Warning("总章节数冲突", $"大纲设计中存在多个不同总章节数（{string.Join("、", validChapters)}），请先统一后再生成");
                return null;
            }
            var totalChapters = validChapters[0];

            if (totalChapters < totalVolumes)
            {
                GlobalToast.Warning("参数无效", $"总章节数({totalChapters})不能少于总卷数({totalVolumes})，请检查大纲设计");
                return null;
            }

            List<VolumeChapterRange> allRanges;
            try
            {
                allRanges = ChapterAllocationHelper.Allocate(totalVolumes, totalChapters);
                foreach (var r in allRanges)
                    TM.App.Log($"[VolumeDesignViewModel] 分配: 第{r.VolumeNumber}卷 {r.StartChapter}-{r.EndChapter} ({r.TargetChapterCount}章)");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("分配失败", $"章节范围计算失败: {ex.Message}");
                return null;
            }

            var existingWithContent = Service.GetAllVolumeDesigns()
                .Where(v => !string.IsNullOrWhiteSpace(v.VolumeTheme)
                       && !string.IsNullOrWhiteSpace(v.MainConflict)
                       && string.Equals(v.Category, categoryName, StringComparison.Ordinal)
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal)))
                .Select(v => v.VolumeNumber)
                .ToHashSet();

            _batchPreCalculatedRanges = allRanges
                .Where(r => !existingWithContent.Contains(r.VolumeNumber))
                .ToList();
            _batchRangeIndex = 0;

            var alreadyCompleted = allRanges.Count - _batchPreCalculatedRanges.Count;
            if (_batchPreCalculatedRanges.Count == 0)
            {
                GlobalToast.Info("已全部完成", $"本分类 {totalVolumes} 卷均已有AI内容，无需重新生成");
                _batchPreCalculatedRanges = null;
                return null;
            }

            TM.App.Log($"[VolumeDesignViewModel] 批量配置: 总卷数={totalVolumes}, 总章节数={totalChapters}, 待生成={_batchPreCalculatedRanges.Count}");

            string confirmMessage;
            if (alreadyCompleted > 0)
            {
                confirmMessage = $"即将对「{categoryName}」继续执行 AI 批量重建分卷设计：\n\n" +
                                 $"• 分卷数量：共 {totalVolumes} 卷\n" +
                                 $"• 已完成：{alreadyCompleted} 卷（跳过）\n" +
                                 $"• 待生成：{_batchPreCalculatedRanges.Count} 卷\n\n" +
                                 $"确认继续生成？";
            }
            else
            {
                confirmMessage = $"即将对「{categoryName}」执行 AI 批量重建分卷设计：\n\n" +
                                 $"• 分卷数量：共 {totalVolumes} 卷\n" +
                                 $"• 总章节数：{totalChapters} 章\n" +
                                 $"• 超出卷数的旧分卷数据将被自动清理\n\n" +
                                 $"确认开始生成？";
            }
            if (!StandardDialog.ShowConfirm(confirmMessage, "AI 批量生成确认"))
            {
                _batchPreCalculatedRanges = null;
                return null;
            }

            var config = new BatchGenerationConfig { CategoryName = categoryName, TotalCount = _batchPreCalculatedRanges.Count, BatchSize = 1 };
            return config;
        }

        protected override async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            if (_batchPreCalculatedRanges == null || _batchPreCalculatedRanges.Count == 0) return;

            var currentScopeId = _workScopeService.CurrentSourceBookId;
            var totalVolumes = _batchPreCalculatedRanges.Count;
            var tail = Service.GetAllVolumeDesigns()
                .Where(v => v.VolumeNumber > totalVolumes
                       && string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal)
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal)))
                .ToList();

            foreach (var v in tail)
            {
                Service.DeleteVolumeDesign(v.Id);
                TM.App.Log($"[VolumeDesignViewModel] 清尾: 删除第{v.VolumeNumber}卷（超出本次总卷数{totalVolumes}）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[VolumeDesignViewModel] 清尾完成: 删除 {tail.Count} 个旧分卷");

            if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllVolumeDesigns()
                    .Where(v => string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal)
                           && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal))
                           && string.IsNullOrWhiteSpace(v.VolumeTheme)
                           && string.IsNullOrWhiteSpace(v.MainConflict))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteVolumeDesign(shell.Id);
                    TM.App.Log($"[VolumeDesignViewModel] 取消清理: 删除空壳第{shell.VolumeNumber}卷");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳分卷，下次批量生成会按需续接");
            }
            else
            {
            foreach (var range in _batchPreCalculatedRanges)
            {
                var existing = Service.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.VolumeNumber == range.VolumeNumber
                                   && string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal)
                                   && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                if (existing != null)
                {
                    existing.StartChapter = range.StartChapter;
                    existing.EndChapter = range.EndChapter;
                    existing.TargetChapterCount = range.TargetChapterCount;
                    await Service.UpdateVolumeDesignAsync(existing);
                    continue;
                }

                var data = new VolumeDesignData
                {
                    Id = ShortIdGenerator.New("D"),
                    Name = $"第{range.VolumeNumber}卷",
                    Category = config.CategoryName,
                    IsEnabled = true,
                    SourceBookId = currentScopeId,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    VolumeNumber = range.VolumeNumber,
                    VolumeTitle = $"第{range.VolumeNumber}卷",
                    StartChapter = range.StartChapter,
                    EndChapter = range.EndChapter,
                    TargetChapterCount = range.TargetChapterCount,
                };
                await Service.AddVolumeDesignAsync(data);
                TM.App.Log($"[VolumeDesignViewModel] 补齐缺卷: 第{range.VolumeNumber}卷 {range.StartChapter}-{range.EndChapter}");
            }

            _batchPreCalculatedRanges = null;
            }

            RefreshTreeData();
        }

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var currentScopeId = _workScopeService.CurrentSourceBookId;

            foreach (var entity in entities)
            {
                try
                {
                    VolumeChapterRange? range = null;
                    if (_batchPreCalculatedRanges != null && _batchRangeIndex < _batchPreCalculatedRanges.Count)
                    {
                        range = _batchPreCalculatedRanges[_batchRangeIndex];
                        _batchRangeIndex++;
                    }

                    var volumeNumber = range?.VolumeNumber ?? _batchRangeIndex;
                    entity["VolumeNumber"] = volumeNumber;
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);

                    var existing = Service.GetAllVolumeDesigns()
                        .FirstOrDefault(v => v.VolumeNumber == volumeNumber
                                       && string.Equals(v.Category, categoryName, StringComparison.Ordinal)
                                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                    if (existing != null)
                    {
                        var aiName = reader.GetString("Name");
                        if (!string.IsNullOrWhiteSpace(aiName)) existing.Name = aiName;

                        var volumeTitle = reader.GetString("VolumeTitle");
                        if (!string.IsNullOrWhiteSpace(volumeTitle)) existing.VolumeTitle = volumeTitle;
                        var volumeTheme = reader.GetString("VolumeTheme");
                        if (!string.IsNullOrWhiteSpace(volumeTheme)) existing.VolumeTheme = volumeTheme;
                        var stageGoal = reader.GetString("StageGoal");
                        if (!string.IsNullOrWhiteSpace(stageGoal)) existing.StageGoal = stageGoal;
                        var estimatedWordCount = reader.GetString("EstimatedWordCount");
                        if (!string.IsNullOrWhiteSpace(estimatedWordCount)) existing.EstimatedWordCount = estimatedWordCount;
                        var mainConflict = reader.GetString("MainConflict");
                        if (!string.IsNullOrWhiteSpace(mainConflict)) existing.MainConflict = mainConflict;
                        var pressureSource = reader.GetString("PressureSource");
                        if (!string.IsNullOrWhiteSpace(pressureSource)) existing.PressureSource = pressureSource;
                        var keyEvents = reader.GetString("KeyEvents");
                        if (!string.IsNullOrWhiteSpace(keyEvents)) existing.KeyEvents = keyEvents;
                        var openingState = reader.GetString("OpeningState");
                        if (!string.IsNullOrWhiteSpace(openingState)) existing.OpeningState = openingState;
                        var endingState = reader.GetString("EndingState");
                        if (!string.IsNullOrWhiteSpace(endingState)) existing.EndingState = endingState;
                        var chapterAllocationOverview = reader.GetString("ChapterAllocationOverview");
                        if (!string.IsNullOrWhiteSpace(chapterAllocationOverview)) existing.ChapterAllocationOverview = chapterAllocationOverview;
                        var plotAllocation = reader.GetString("PlotAllocation");
                        if (!string.IsNullOrWhiteSpace(plotAllocation)) existing.PlotAllocation = plotAllocation;
                        var chapterGenerationHints = reader.GetString("ChapterGenerationHints");
                        if (!string.IsNullOrWhiteSpace(chapterGenerationHints)) existing.ChapterGenerationHints = chapterGenerationHints;
                        var batchScope = _workScopeService.CurrentSourceBookId;
                        var refCharsRaw = string.Join("、", reader.GetStringList("ReferencedCharacterNames"));
                        var refCharsResolved = await VolumeResolveNamesAsync(refCharsRaw, "character", batchScope);
                        var refCharacters = SplitEntityNames(refCharsResolved);
                        if (refCharacters.Count > 0) existing.ReferencedCharacterNames = refCharacters;
                        var refFacsRaw = string.Join("、", reader.GetStringList("ReferencedFactionNames"));
                        var refFacsResolved = await VolumeResolveNamesAsync(refFacsRaw, "faction", batchScope);
                        var refFactions = SplitEntityNames(refFacsResolved);
                        if (refFactions.Count > 0) existing.ReferencedFactionNames = refFactions;
                        var refLocsRaw = string.Join("、", reader.GetStringList("ReferencedLocationNames"));
                        var refLocsResolved = await VolumeResolveNamesAsync(refLocsRaw, "location", batchScope);
                        var refLocations = SplitEntityNames(refLocsResolved);
                        if (refLocations.Count > 0) existing.ReferencedLocationNames = refLocations;

                        if (range != null)
                        {
                            existing.StartChapter = range.StartChapter;
                            existing.EndChapter = range.EndChapter;
                            existing.TargetChapterCount = range.TargetChapterCount;
                        }
                        existing.DependencyModuleVersions = versionSnapshot ?? new();
                        await Service.UpdateVolumeDesignAsync(existing);
                        TM.App.Log($"[VolumeDesignViewModel] Upsert更新: 第{volumeNumber}卷 {existing.StartChapter}-{existing.EndChapter}");
                    }
                    else
                    {
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name)) name = $"第{volumeNumber}卷";
                        var data = new VolumeDesignData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = name,
                            Category = categoryName,
                            IsEnabled = true,
                            SourceBookId = currentScopeId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            VolumeNumber = volumeNumber,
                            VolumeTitle = reader.GetString("VolumeTitle"),
                            VolumeTheme = reader.GetString("VolumeTheme"),
                            StageGoal = reader.GetString("StageGoal"),
                            EstimatedWordCount = reader.GetString("EstimatedWordCount"),
                            StartChapter = range?.StartChapter ?? 0,
                            EndChapter = range?.EndChapter ?? 0,
                            TargetChapterCount = range?.TargetChapterCount ?? 0,
                            MainConflict = reader.GetString("MainConflict"),
                            PressureSource = reader.GetString("PressureSource"),
                            KeyEvents = reader.GetString("KeyEvents"),
                            OpeningState = reader.GetString("OpeningState"),
                            EndingState = reader.GetString("EndingState"),
                            ChapterAllocationOverview = reader.GetString("ChapterAllocationOverview"),
                            PlotAllocation = reader.GetString("PlotAllocation"),
                            ChapterGenerationHints = reader.GetString("ChapterGenerationHints"),
                            ReferencedCharacterNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedCharacterNames")), "character", currentScopeId)),
                            ReferencedFactionNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedFactionNames")), "faction", currentScopeId)),
                            ReferencedLocationNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedLocationNames")), "location", currentScopeId)),
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };
                        await Service.AddVolumeDesignAsync(data);
                        TM.App.Log($"[VolumeDesignViewModel] Upsert新建: 第{volumeNumber}卷 {data.StartChapter}-{data.EndChapter}");
                    }

                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[VolumeDesignViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[VolumeDesignViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        protected override string GetModuleNameForVersionTracking() => "VolumeDesign";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateVolumeDesign(_currentEditingData);
        }
    }
}
