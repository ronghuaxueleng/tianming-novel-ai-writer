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
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

namespace TM.Modules.Design.Elements.PlotRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class PlotRulesViewModel : DataManagementViewModelBase<PlotRulesData, PlotRulesCategory, PlotRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterService;
        private readonly LocationRulesService _locationService;
        private string _formName = string.Empty;
        private string _formIcon = "📖";
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

        private string _formTargetVolume = string.Empty;
        private string _formAssignedVolume = string.Empty;
        private string _formOneLineSummary = string.Empty;
        private string _formEventType = string.Empty;
        private string _formStoryPhase = string.Empty;
        private string _formPrerequisitesTrigger = string.Empty;

        public string FormTargetVolume
        {
            get => _formTargetVolume;
            set
            {
                if (_formTargetVolume != value)
                {
                    _formTargetVolume = value;
                    OnPropertyChanged();
                    RefreshAssignedVolumeOptions();
                    CommandManager.InvalidateRequerySuggested();

                    var hasEditingContext = IsCreateMode || _currentEditingData != null || _currentEditingCategory != null;
                    var isValidTotalVolume = int.TryParse(_formTargetVolume?.Trim(), out var n) && n > 0;
                    IsAIGenerateEnabled = hasEditingContext && isValidTotalVolume;
                }
            }
        }
        public string FormAssignedVolume { get => _formAssignedVolume; set { _formAssignedVolume = value; OnPropertyChanged(); } }
        public string FormOneLineSummary { get => _formOneLineSummary; set { _formOneLineSummary = value; OnPropertyChanged(); } }
        public string FormEventType { get => _formEventType; set { _formEventType = value; OnPropertyChanged(); } }
        public string FormStoryPhase { get => _formStoryPhase; set { _formStoryPhase = value; OnPropertyChanged(); } }
        public string FormPrerequisitesTrigger { get => _formPrerequisitesTrigger; set { _formPrerequisitesTrigger = value; OnPropertyChanged(); } }

        private string _formMainCharacters = string.Empty;
        private string _formKeyNpcs = string.Empty;
        private string _formLocation = string.Empty;
        private string _formTimeDuration = string.Empty;

        public string FormMainCharacters { get => _formMainCharacters; set { _formMainCharacters = value; OnPropertyChanged(); } }
        public string FormKeyNpcs { get => _formKeyNpcs; set { _formKeyNpcs = value; OnPropertyChanged(); } }
        public string FormLocation { get => _formLocation; set { _formLocation = value; OnPropertyChanged(); } }
        public string FormTimeDuration { get => _formTimeDuration; set { _formTimeDuration = value; OnPropertyChanged(); } }

        private string _formStepTitle = string.Empty;
        private string _formGoal = string.Empty;
        private string _formConflict = string.Empty;
        private string _formResult = string.Empty;
        private string _formEmotionCurve = string.Empty;

        public string FormStepTitle { get => _formStepTitle; set { _formStepTitle = value; OnPropertyChanged(); } }
        public string FormGoal { get => _formGoal; set { _formGoal = value; OnPropertyChanged(); } }
        public string FormConflict { get => _formConflict; set { _formConflict = value; OnPropertyChanged(); } }
        public string FormResult { get => _formResult; set { _formResult = value; OnPropertyChanged(); } }
        public string FormEmotionCurve { get => _formEmotionCurve; set { _formEmotionCurve = value; OnPropertyChanged(); } }

        private string _formMainPlotPush = string.Empty;
        private string _formCharacterGrowth = string.Empty;
        private string _formWorldReveal = string.Empty;
        private string _formRewardsClues = string.Empty;

        public string FormMainPlotPush { get => _formMainPlotPush; set { _formMainPlotPush = value; OnPropertyChanged(); } }
        public string FormCharacterGrowth { get => _formCharacterGrowth; set { _formCharacterGrowth = value; OnPropertyChanged(); } }
        public string FormWorldReveal { get => _formWorldReveal; set { _formWorldReveal = value; OnPropertyChanged(); } }
        public string FormRewardsClues { get => _formRewardsClues; set { _formRewardsClues = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
        public List<string> EventTypeOptions { get; } = new() { "主线剧情", "卷主线", "支线剧情", "过渡剧情", "伏笔埋设", "伏笔揭示" };

        private List<string> _assignedVolumeOptions = new() { "全局" };
        public List<string> AssignedVolumeOptions
        {
            get => _assignedVolumeOptions;
            private set { _assignedVolumeOptions = value; OnPropertyChanged(); }
        }

        private void RefreshAssignedVolumeOptions()
        {
            var options = new List<string> { "全局" };
            if (int.TryParse(_formTargetVolume?.Trim(), out var n) && n > 0)
            {
                for (int i = 1; i <= n; i++)
                    options.Add($"第{i}卷");
            }
            AssignedVolumeOptions = options;
        }

        private static List<string> BuildAssignedVolumeOptions(string? totalVolume)
        {
            var options = new List<string> { "全局" };
            if (int.TryParse(totalVolume?.Trim(), out var n) && n > 0)
            {
                for (int i = 1; i <= n; i++)
                    options.Add($"第{i}卷");
            }
            return options;
        }

        private List<string> _availableCharacters = new();
        private List<string> _availableLocations = new();
        private Dictionary<string, string> _charIdToName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _charNameToId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _locIdToName  = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _locNameToId  = new(StringComparer.OrdinalIgnoreCase);

        public List<string> AvailableCharacters
        {
            get => _availableCharacters;
            set { _availableCharacters = value; OnPropertyChanged(); }
        }

        public List<string> AvailableLocations
        {
            get => _availableLocations;
            set { _availableLocations = value; OnPropertyChanged(); }
        }

        public PlotRulesViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterService, LocationRulesService locationService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterService = characterService;
            _locationService = locationService;
            RefreshRelationshipOptions();
        }

        private void RefreshRelationshipOptions()
        {
            _charIdToName = new(StringComparer.OrdinalIgnoreCase);
            _charNameToId = new(StringComparer.OrdinalIgnoreCase);
            _locIdToName  = new(StringComparer.OrdinalIgnoreCase);
            _locNameToId  = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var characters = _characterService.GetAllCharacterRules().Where(c => c.IsEnabled).ToList();
                foreach (var c in characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Id))   _charIdToName[c.Id]   = c.Name;
                    if (!string.IsNullOrWhiteSpace(c.Name)) _charNameToId[c.Name] = c.Id;
                }
                AvailableCharacters = characters.Select(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 加载角色列表失败: {ex.Message}");
                AvailableCharacters = new List<string>();
            }

            try
            {
                var locations = _locationService.GetAllLocationRules().Where(l => l.IsEnabled).ToList();
                foreach (var l in locations)
                {
                    if (!string.IsNullOrWhiteSpace(l.Id))   _locIdToName[l.Id]   = l.Name;
                    if (!string.IsNullOrWhiteSpace(l.Name)) _locNameToId[l.Name] = l.Id;
                }
                AvailableLocations = locations.Select(l => l.Name).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 加载位置列表失败: {ex.Message}");
                AvailableLocations = new List<string>();
            }
        }

        private string CharIdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_charIdToName.TryGetValue(idOrName, out var n)) return n;
            if (_charNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string CharIdsToNames(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
            return string.Join("、", ids.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(CharIdToName).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string CharNameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (_charNameToId.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            return string.Empty;
        }

        private string CharNamesToIds(string names)
        {
            if (string.IsNullOrWhiteSpace(names)) return string.Empty;
            return string.Join("、", names.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(CharNameToId).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string LocIdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_locIdToName.TryGetValue(idOrName, out var n)) return n;
            if (_locNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string LocNameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (_locNameToId.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            return string.Empty;
        }

        protected override string DefaultDataIcon => "📖";

        protected override PlotRulesData? CreateNewData(string? categoryName = null)
        {
            return new PlotRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新剧情规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            FormMainCharacters = string.Join("、", (FormMainCharacters ?? string.Empty)
                .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s) && !EntityNameNormalizeHelper.IsIgnoredValue(s))
                .Where(n => ShortIdGenerator.IsLikelyId(n)
                    ? _charIdToName.ContainsKey(n)
                    : AvailableCharacters.Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase))));

            FormKeyNpcs = string.Join("、", (FormKeyNpcs ?? string.Empty)
                .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s) && !EntityNameNormalizeHelper.IsIgnoredValue(s))
                .Where(n => ShortIdGenerator.IsLikelyId(n)
                    ? _charIdToName.ContainsKey(n)
                    : AvailableCharacters.Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase))));

            var loc = (FormLocation ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(loc) || EntityNameNormalizeHelper.IsIgnoredValue(loc))
            {
                FormLocation = string.Empty;
            }
            else if (ShortIdGenerator.IsLikelyId(loc))
            {
                FormLocation = _locIdToName.ContainsKey(loc) ? loc : string.Empty;
            }
            else
            {
                FormLocation = AvailableLocations.Any(l => string.Equals(l, loc, StringComparison.OrdinalIgnoreCase))
                    ? loc
                    : string.Empty;
            }

            return System.Threading.Tasks.Task.CompletedTask;
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
            await EnsureServiceInitializedAsync(_locationService);

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        RefreshRelationshipOptions();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    RefreshRelationshipOptions();
                }
            }
            catch
            {
                RefreshRelationshipOptions();
            }
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllPlotRules();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdatePlotRule(_currentEditingData);
        }

        protected override List<PlotRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<PlotRulesData> GetAllDataItems() => Service.GetAllPlotRules();

        protected override string GetDataCategory(PlotRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(PlotRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = "📖",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(PlotRulesData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OneLineSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.EventType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Goal.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

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
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(PlotRulesData data)
        {
            FormName = data.Name;
            FormIcon = "📖";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormTargetVolume = data.TargetVolume;
            FormAssignedVolume = data.AssignedVolume;
            FormOneLineSummary = data.OneLineSummary;
            FormEventType = data.EventType;
            FormStoryPhase = data.StoryPhase;
            FormPrerequisitesTrigger = data.PrerequisitesTrigger;

            FormMainCharacters = CharIdsToNames(data.MainCharacters);
            FormKeyNpcs        = CharIdsToNames(data.KeyNpcs);
            FormLocation       = LocIdToName(data.Location);
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

        protected override string NewItemTypeName => "剧情规则";
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
                TM.App.Log($"[PlotRulesViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或剧情规则");
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
            await Service.AddPlotRuleAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"剧情规则『{newData.Name}』已创建");
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
            await Service.UpdatePlotRuleAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"剧情规则『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(PlotRulesData data)
        {
            var newIsEnabled = (FormStatus == "已启用");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckScopeBeforeEnable(data.SourceBookId, data.Name))
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
            data.KeyNpcs        = CharNamesToIds(FormKeyNpcs);
            data.Location       = LocNameToId(FormLocation);
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

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllPlotRules()
                            .Where(d => d.Category == categoryName)
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
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "小说设计师",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "剧情设计",
                ProgressMessage = "正在设计剧情规则...",
                CompleteMessage = "剧情设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["总卷数"] = v =>
                    {
                        if (string.IsNullOrWhiteSpace(FormTargetVolume))
                            FormTargetVolume = v;
                    },
                    ["所属卷"] = v => FormAssignedVolume = EntityNameNormalizeHelper.FilterToCandidate(v, AssignedVolumeOptions),
                    ["一句话简介"] = v => FormOneLineSummary = v,
                    ["事件类型"] = v => FormEventType = EntityNameNormalizeHelper.FilterToCandidate(v, EventTypeOptions),
                    ["所属阶段"] = v => FormStoryPhase = v,
                    ["前置条件触发"] = v => FormPrerequisitesTrigger = v,
                    ["主要角色"] = v => FormMainCharacters = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["关键NPC"] = v => FormKeyNpcs = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["地点"] = v => FormLocation = FilterToCandidateOrRaw(v, AvailableLocations),
                    ["时间跨度"] = v => FormTimeDuration = v,
                    ["步骤标题"] = v => FormStepTitle = v,
                    ["目标"] = v => FormGoal = v,
                    ["冲突"] = v => FormConflict = v,
                    ["结果"] = v => FormResult = v,
                    ["情绪曲线"] = v => FormEmotionCurve = v,
                    ["主线推进"] = v => FormMainPlotPush = v,
                    ["角色成长"] = v => FormCharacterGrowth = v,
                    ["世界观揭示"] = v => FormWorldReveal = v,
                    ["奖励线索"] = v => FormRewardsClues = v,
                },
                OutputFieldGetters = new()
                {
                    ["总卷数"] = () => FormTargetVolume,
                    ["所属卷"] = () => FormAssignedVolume,
                    ["一句话简介"] = () => FormOneLineSummary,
                    ["事件类型"] = () => FormEventType,
                    ["所属阶段"] = () => FormStoryPhase,
                    ["前置条件触发"] = () => FormPrerequisitesTrigger,
                    ["主要角色"] = () => FormMainCharacters,
                    ["关键NPC"] = () => FormKeyNpcs,
                    ["地点"] = () => FormLocation,
                    ["时间跨度"] = () => FormTimeDuration,
                    ["步骤标题"] = () => FormStepTitle,
                    ["目标"] = () => FormGoal,
                    ["冲突"] = () => FormConflict,
                    ["结果"] = () => FormResult,
                    ["情绪曲线"] = () => FormEmotionCurve,
                    ["主线推进"] = () => FormMainPlotPush,
                    ["角色成长"] = () => FormCharacterGrowth,
                    ["世界观揭示"] = () => FormWorldReveal,
                    ["奖励线索"] = () => FormRewardsClues,
                },
                ContextProvider = async () => await GetEnhancedPlotContextAsync(),
                BatchFieldKeyMap = new()
                {
                    ["总卷数"] = "TargetVolume",
                    ["所属卷"] = "AssignedVolume",
                    ["一句话简介"] = "OneLineSummary",
                    ["事件类型"] = "EventType",
                    ["所属阶段"] = "StoryPhase",
                    ["前置条件触发"] = "PrerequisitesTrigger",
                    ["主要角色"] = "MainCharacters",
                    ["关键NPC"] = "KeyNpcs",
                    ["地点"] = "Location",
                    ["时间跨度"] = "TimeDuration",
                    ["步骤标题"] = "StepTitle",
                    ["目标"] = "Goal",
                    ["冲突"] = "Conflict",
                    ["结果"] = "Result",
                    ["情绪曲线"] = "EmotionCurve",
                    ["主线推进"] = "MainPlotPush",
                    ["角色成长"] = "CharacterGrowth",
                    ["世界观揭示"] = "WorldReveal",
                    ["奖励线索"] = "RewardsClues"
                },
                BatchIndexFields = new() { "Name", "AssignedVolume", "EventType", "MainCharacters" }
            };
        }

        protected override ModuleNormalizationConfig? GetNormalizationConfig()
        {
            return new ModuleNormalizationConfig
            {
                ModuleName = nameof(PlotRulesViewModel),
                Rules = new List<FieldNormalizationRule>
                {
                    new()
                    {
                        FieldName = "MainCharacters",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableCharacters.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    },
                    new()
                    {
                        FieldName = "KeyNpcs",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableCharacters.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    },
                    new()
                    {
                        FieldName = "Location",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableLocations.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    }
                }
            };
        }

        protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            var isValidTotalVolume = int.TryParse(FormTargetVolume?.Trim(), out var n) && n > 0;
            IsAIGenerateEnabled = hasSelection && isValidTotalVolume;
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (!base.CanExecuteAIGenerate()) return false;

            return int.TryParse(FormTargetVolume?.Trim(), out var n) && n > 0;
        }

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllPlotRules().Select(r => r.Name);
        protected override int GetDefaultBatchSize() => 8;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllPlotRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"剧情_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[PlotRulesViewModel] 跳过已存在剧情: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var assignedVolume = EntityNameNormalizeHelper.FilterToCandidate(
                        reader.GetString("AssignedVolume"),
                        BuildAssignedVolumeOptions(string.IsNullOrWhiteSpace(FormTargetVolume)
                            ? reader.GetString("TargetVolume")
                            : FormTargetVolume));
                    var eventType = EntityNameNormalizeHelper.FilterToCandidate(reader.GetString("EventType"), EventTypeOptions);
                    var mainCharacters = reader.GetString("MainCharacters");
                    var keyNpcs = reader.GetString("KeyNpcs");
                    var location = reader.GetString("Location");
                    var data = new PlotRulesData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        TargetVolume = string.IsNullOrWhiteSpace(FormTargetVolume)
                            ? reader.GetString("TargetVolume")
                            : FormTargetVolume,
                        AssignedVolume = assignedVolume,
                        OneLineSummary = reader.GetString("OneLineSummary"),
                        EventType = eventType,
                        StoryPhase = reader.GetString("StoryPhase"),
                        PrerequisitesTrigger = reader.GetString("PrerequisitesTrigger"),
                        MainCharacters = mainCharacters,
                        KeyNpcs = keyNpcs,
                        Location = location,
                        TimeDuration = reader.GetString("TimeDuration"),
                        StepTitle = reader.GetString("StepTitle"),
                        Goal = reader.GetString("Goal"),
                        Conflict = reader.GetString("Conflict"),
                        Result = reader.GetString("Result"),
                        EmotionCurve = reader.GetString("EmotionCurve"),
                        MainPlotPush = reader.GetString("MainPlotPush"),
                        CharacterGrowth = reader.GetString("CharacterGrowth"),
                        WorldReveal = reader.GetString("WorldReveal"),
                        RewardsClues = reader.GetString("RewardsClues")
                    };

                    entity["Name"] = name;
                    entity["AssignedVolume"] = assignedVolume;
                    entity["EventType"] = eventType;
                    entity["MainCharacters"] = mainCharacters;
                    await Service.AddPlotRuleAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlotRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[PlotRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        private async System.Threading.Tasks.Task<string> GetEnhancedPlotContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetPlotContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }

            var availableChars = AvailableCharacters.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (availableChars.Any())
            {
                sb.AppendLine("<section name=\"available_characters\">");
                sb.AppendLine("主要角色/关键NPC必须从以下列表中选择");
                sb.AppendLine(string.Join("、", availableChars));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            var availableLocs = AvailableLocations.Where(l => !string.IsNullOrEmpty(l)).ToList();
            if (availableLocs.Any())
            {
                sb.AppendLine("<section name=\"available_locations\">");
                sb.AppendLine("地点必须从以下列表中选择");
                sb.AppendLine(string.Join("、", availableLocs));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「主要角色」「关键NPC」为多选字段，请只填写角色名称；如有多名，请在字符串内用换行分条；无则填写「暂无」。");
            sb.AppendLine("2. 「地点」请只填写位置名称；有则填写，无则填写「暂无」。");
            sb.AppendLine("3. 「所属卷」必须为「全局」或「第N卷」。");
            sb.AppendLine($"4. 「事件类型」必须从以下选项中选择：{string.Join("、", EventTypeOptions)}");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            if (int.TryParse(FormTargetVolume?.Trim(), out var totalVolumes) && totalVolumes > 0)
            {
                sb.AppendLine($"<volume_count_constraint count=\"{totalVolumes}\">");
                sb.AppendLine($"批量生成时，每条剧情事件的「所属卷」字段必须填写为「全局」或「第1卷」~「第{totalVolumes}卷」之一，合理分配确保各卷均有覆盖。");
                sb.AppendLine($"事件类型必须从以下选项中选择：主线剧情、卷主线、支线剧情、过渡剧情、伏笔埋设、伏笔揭示");
                sb.AppendLine("</volume_count_constraint>");
                sb.AppendLine();
            }

            var existingPlots = Service.GetAllPlotRules()
                .Where(p => p.IsEnabled && p.Id != _currentEditingData?.Id)
                .Select(p => $"- **{p.Name}**（{p.AssignedVolume}）：{p.OneLineSummary}")
                .ToList();
            if (existingPlots.Any())
            {
                sb.AppendLine("<section name=\"existing_plot_events\">");
                sb.AppendLine("以下剧情事件已存在，批量生成时请避免语义重复，并保持叙事逻辑连贯：");
                sb.AppendLine(string.Join("\n", existingPlots));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            return sb.ToString();
        }

    }
}
