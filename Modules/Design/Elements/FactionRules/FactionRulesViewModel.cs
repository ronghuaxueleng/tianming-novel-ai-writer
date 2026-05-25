using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Services.Modules.ProjectData.Metadata;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.Design.Elements.FactionRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class FactionRulesViewModel : DataManagementViewModelBase<FactionRulesData, FactionRulesCategory, FactionRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterRulesService;
        private string _formName = string.Empty;
        private string _formIcon = "Icon.Institution";
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

        private string _formFactionType = string.Empty;
        private string _formGoal = string.Empty;
        private string _formStrengthTerritory = string.Empty;

        public string FormFactionType { get => _formFactionType; set { _formFactionType = value; OnPropertyChanged(); } }
        public string FormGoal { get => _formGoal; set { _formGoal = value; OnPropertyChanged(); } }
        public string FormStrengthTerritory { get => _formStrengthTerritory; set { _formStrengthTerritory = value; OnPropertyChanged(); } }

        private string _formLeader = string.Empty;
        private string _formCoreMembers = string.Empty;
        private string _formMemberTraits = string.Empty;

        public string FormLeader { get => _formLeader; set { _formLeader = value; OnPropertyChanged(); } }
        public string FormCoreMembers { get => _formCoreMembers; set { _formCoreMembers = value; OnPropertyChanged(); } }
        public string FormMemberTraits { get => _formMemberTraits; set { _formMemberTraits = value; OnPropertyChanged(); } }

        private string _formAllies = string.Empty;
        private string _formEnemies = string.Empty;
        private string _formNeutralCompetitors = string.Empty;

        public string FormAllies { get => _formAllies; set { _formAllies = value; OnPropertyChanged(); } }
        public string FormEnemies { get => _formEnemies; set { _formEnemies = value; OnPropertyChanged(); } }
        public string FormNeutralCompetitors { get => _formNeutralCompetitors; set { _formNeutralCompetitors = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
        public List<string> FactionTypeOptions { get; } = new() { "", "宗门/教派", "王国/帝国", "家族/世家", "商盟/行会", "军事组织", "秘密组织", "部落/氏族" };

        private List<string> _availableCharacterNames = new();
        private List<string> _availableFactionNames = new();

        private Dictionary<string, string> _characterIdToName = new();
        private Dictionary<string, string> _characterNameToId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _factionIdToName = new();
        private Dictionary<string, string> _factionNameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly TM.Framework.Common.Services.LazyListCache<CharacterRulesData> _allCharsCache = new();
        private readonly TM.Framework.Common.Services.LazyListCache<FactionRulesData> _allFactionsCache = new();

        public List<string> AvailableCharacters
        {
            get => _availableCharacterNames;
            set { _availableCharacterNames = value; OnPropertyChanged(); }
        }

        public List<string> AvailableFactions
        {
            get => _availableFactionNames;
            set { _availableFactionNames = value; OnPropertyChanged(); }
        }

        public FactionRulesViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterRulesService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterRulesService = characterRulesService;
        }

        protected override void OnAfterInitializeRefresh()
        {
            RefreshRelationshipOptions();
        }

        private void InvalidateRelationshipCache() { _allCharsCache.Invalidate(); _allFactionsCache.Invalidate(); }

        private void RefreshRelationshipOptions()
        {
            _characterIdToName = new();
            _characterNameToId = new(StringComparer.OrdinalIgnoreCase);
            _factionIdToName = new();
            _factionNameToId = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var characterList = _allCharsCache.Get(() => _characterRulesService.GetAllCharacterRules()
                    .Where(c => c.IsEnabled)
                    .ToList());
                var names = new List<string>();
                foreach (var c in characterList)
                {
                    names.Add(c.Name);
                    _characterIdToName[c.Id] = c.Name;
                    _characterNameToId[c.Name] = c.Id;
                }
                AvailableCharacters = names;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactionRulesViewModel] 加载角色列表失败: {ex.Message}");
                AvailableCharacters = new List<string>();
            }

            try
            {
                var allFactions = _allFactionsCache.Get(() => Service.GetAllFactionRules()
                    .Where(f => f.IsEnabled)
                    .ToList());

                var currentId = _currentEditingData?.Id;
                var factionList = string.IsNullOrWhiteSpace(currentId)
                    ? allFactions
                    : allFactions.Where(f => f.Id != currentId).ToList();

                var names = new List<string>();
                foreach (var f in factionList)
                {
                    names.Add(f.Name);
                    _factionIdToName[f.Id] = f.Name;
                    _factionNameToId[f.Name] = f.Id;
                }
                AvailableFactions = names;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactionRulesViewModel] 加载势力列表失败: {ex.Message}");
                AvailableFactions = new List<string>();
            }
        }

        private string IdToName(string idOrName, Dictionary<string, string> idToNameMap, Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (idToNameMap.TryGetValue(idOrName, out var name)) return name;
            if (nameToIdMap.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string IdsToNames(string idsOrNames, Dictionary<string, string> idToNameMap, Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(idsOrNames)) return string.Empty;
            var items = idsOrNames.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => IdToName(s, idToNameMap, nameToIdMap))
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join("、", items);
        }

        private string NameToId(string name, Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (nameToIdMap.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            TM.App.Log($"[FactionRulesViewModel] 未匹配到名称: {name}");
            return string.Empty;
        }

        private string NamesToIds(string names, Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(names)) return string.Empty;
            var items = names.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => NameToId(s, nameToIdMap))
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join("、", items);
        }

        protected override string DefaultDataIcon => "Icon.Institution";

        protected override FactionRulesData? CreateNewData(string? categoryName = null)
        {
            return new FactionRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新势力规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override async System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            FormLeader = await ResolveOrCreateCharacterNameAsync(FormLeader);
            FormCoreMembers = await ResolveOrCreateCharacterNamesAsync(FormCoreMembers);

            var dbCandidates = new HashSet<string>(
                Service.GetAllFactionRules()
                    .Where(f => f.IsEnabled)
                    .Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);

            FormAllies = ResolveFactionNamesInScope(FormAllies, FormName, dbCandidates);
            FormEnemies = ResolveFactionNamesInScope(FormEnemies, FormName, dbCandidates);
            FormNeutralCompetitors = ResolveFactionNamesInScope(FormNeutralCompetitors, FormName, dbCandidates);
        }

        private System.Threading.Tasks.Task<string> ResolveOrCreateCharacterNameAsync(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return System.Threading.Tasks.Task.FromResult(rawName);
            var name = rawName.Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(name)) return System.Threading.Tasks.Task.FromResult(string.Empty);

            if (ShortIdGenerator.IsLikelyId(name) && _characterIdToName.ContainsKey(name))
                return System.Threading.Tasks.Task.FromResult(name);
            var all = _characterRulesService.GetAllCharacterRules().Where(c => c.IsEnabled).ToList();
            if (all.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                return System.Threading.Tasks.Task.FromResult(name);
            TM.App.Log($"[FactionRulesViewModel] 实体引用：'{name}' 在上游不存在，已忽略");
            return System.Threading.Tasks.Task.FromResult(string.Empty);
        }

        private async System.Threading.Tasks.Task<string> ResolveOrCreateCharacterNamesAsync(string rawNames)
        {
            if (string.IsNullOrWhiteSpace(rawNames)) return rawNames;
            var parts = rawNames.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts) resolved.Add(await ResolveOrCreateCharacterNameAsync(n));
            return string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string ResolveFactionNamesInScope(
            string? rawNames,
            string? selfName,
            HashSet<string> candidatesByName)
        {
            if (string.IsNullOrWhiteSpace(rawNames)) return string.Empty;
            var parts = rawNames.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (!string.IsNullOrWhiteSpace(selfName) && string.Equals(n, selfName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ShortIdGenerator.IsLikelyId(n) && _factionIdToName.ContainsKey(n))
                {
                    resolved.Add(n);
                    continue;
                }
                if (candidatesByName.Contains(n))
                {
                    resolved.Add(n);
                    continue;
                }
                TM.App.Log($"[FactionRulesViewModel] 实体引用：势力 '{n}' 不在候选集合中，已忽略");
            }
            return string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        protected override async System.Threading.Tasks.Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(
                EnsureServiceInitializedAsync(Service),
                EnsureServiceInitializedAsync(_characterRulesService));

            try
            {
                InvalidateRelationshipCache();
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
                InvalidateRelationshipCache();
                RefreshRelationshipOptions();
            }
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllFactionRules();

        protected override string GetModuleNameForVersionTracking() => "FactionRules";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateFactionRule(_currentEditingData);
        }

        protected override List<FactionRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<FactionRulesData> GetAllDataItems() => Service.GetAllFactionRules();

        protected override string GetDataCategory(FactionRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(FactionRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = IconHelper.Get("Icon.Institution"),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(FactionRulesData data)
        {
            return new[] { data.FactionType, data.Goal };
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: FactionRulesData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    RefreshRelationshipOptions();
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: FactionRulesCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    RefreshRelationshipOptions();
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
                TM.App.Log($"[FactionRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(FactionRulesData data)
        {
            FormName = data.Name;
            FormIcon = "Icon.Institution";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormFactionType = data.FactionType;
            FormGoal = data.Goal;
            FormStrengthTerritory = data.StrengthTerritory;

            FormLeader = IdToName(data.Leader, _characterIdToName, _characterNameToId);
            FormCoreMembers = IdsToNames(data.CoreMembers, _characterIdToName, _characterNameToId);
            FormMemberTraits = data.MemberTraits;

            FormAllies = IdsToNames(data.Allies, _factionIdToName, _factionNameToId);
            FormEnemies = IdsToNames(data.Enemies, _factionIdToName, _factionNameToId);
            FormNeutralCompetitors = IdsToNames(data.NeutralCompetitors, _factionIdToName, _factionNameToId);
        }

        private void LoadCategoryToForm(FactionRulesCategory category)
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
            FormFactionType = string.Empty;
            FormGoal = string.Empty;
            FormStrengthTerritory = string.Empty;

            FormLeader = string.Empty;
            FormCoreMembers = string.Empty;
            FormMemberTraits = string.Empty;

            FormAllies = string.Empty;
            FormEnemies = string.Empty;
            FormNeutralCompetitors = string.Empty;
        }

        protected override string NewItemTypeName => "势力规则";
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
                TM.App.Log($"[FactionRulesViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[FactionRulesViewModel] 保存失败: {ex.Message}");
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

            var unmatchedLeader = EntityNameNormalizeHelper.GetUnmatchedNames(FormLeader, AvailableCharacters);
            var unmatchedCoreMembers = EntityNameNormalizeHelper.GetUnmatchedNames(FormCoreMembers, AvailableCharacters);
            var unmatchedAllies = EntityNameNormalizeHelper.GetUnmatchedNames(FormAllies, AvailableFactions);
            var unmatchedEnemies = EntityNameNormalizeHelper.GetUnmatchedNames(FormEnemies, AvailableFactions);
            var unmatchedNeutral = EntityNameNormalizeHelper.GetUnmatchedNames(FormNeutralCompetitors, AvailableFactions);

            if (unmatchedLeader.Count > 0 || unmatchedCoreMembers.Count > 0 || unmatchedAllies.Count > 0 || unmatchedEnemies.Count > 0 || unmatchedNeutral.Count > 0)
            {
                var parts = new List<string>();
                if (unmatchedLeader.Count > 0)
                    parts.Add($"领袖: {string.Join("、", unmatchedLeader)}");
                if (unmatchedCoreMembers.Count > 0)
                    parts.Add($"核心成员: {string.Join("、", unmatchedCoreMembers)}");
                if (unmatchedAllies.Count > 0)
                    parts.Add($"盟友势力: {string.Join("、", unmatchedAllies)}");
                if (unmatchedEnemies.Count > 0)
                    parts.Add($"敌对势力: {string.Join("、", unmatchedEnemies)}");
                if (unmatchedNeutral.Count > 0)
                    parts.Add($"中立竞争: {string.Join("、", unmatchedNeutral)}");

                GlobalToast.Warning("断链预警", $"以下名称未在当前候选列表中找到，可能导致上下文变弱：{string.Join("；", parts)}");
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或势力规则");
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

            var newCategory = new FactionRulesCategory
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
            await Service.AddFactionRuleAsync(newData);
            _currentEditingData = newData;
            InvalidateRelationshipCache();
            GlobalToast.Success("保存成功", $"势力规则『{newData.Name}』已创建");
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
            await Service.UpdateFactionRuleAsync(_currentEditingData);
            InvalidateRelationshipCache();
            GlobalToast.Success("保存成功", $"势力规则『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(FactionRulesData data)
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

            data.FactionType = FormFactionType;
            data.Goal = FormGoal;
            data.StrengthTerritory = FormStrengthTerritory;

            data.Leader = NameToId(FormLeader, _characterNameToId);
            data.CoreMembers = NamesToIds(FormCoreMembers, _characterNameToId);
            data.MemberTraits = FormMemberTraits;

            data.Allies = NamesToIds(FormAllies, _factionNameToId);
            data.Enemies = NamesToIds(FormEnemies, _factionNameToId);
            data.NeutralCompetitors = NamesToIds(FormNeutralCompetitors, _factionNameToId);
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有势力规则也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    var categoryIdLookup = Service.GetAllCategories()
                        .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        categoryIdLookup.TryGetValue(categoryName, out var cId);
                        var dataInCategory = Service.GetAllFactionRules()
                            .Where(d =>
                                (!string.IsNullOrWhiteSpace(cId) && d.CategoryId == cId) ||
                                (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName))
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteFactionRule(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个势力规则");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除势力规则『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteFactionRule(_currentEditingData.Id);
                    InvalidateRelationshipCache();
                    GlobalToast.Success("删除成功", $"势力规则『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或势力规则");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactionRulesViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "小说设计师",
                ActiveModuleHint = "势力规则",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "势力设计",
                ProgressMessage = "正在设计势力规则...",
                CompleteMessage = "势力设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["势力类型"] = v => FormFactionType = EntityNameNormalizeHelper.FilterToCandidate(v, FactionTypeOptions),
                    ["理念目标"] = v => FormGoal = v,
                    ["实力地盘"] = v => FormStrengthTerritory = v,
                    ["领袖"] = v => FormLeader = FilterToCandidateOrRaw(v, AvailableCharacters),
                    ["核心成员"] = v => FormCoreMembers = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["成员特征"] = v => FormMemberTraits = v,
                    ["盟友势力"] = v => FormAllies = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["敌对势力"] = v => FormEnemies = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["中立竞争"] = v => FormNeutralCompetitors = FilterToCandidatesOrRaw(v, AvailableFactions),
                },
                OutputFieldGetters = new()
                {
                    ["势力类型"] = () => FormFactionType,
                    ["理念目标"] = () => FormGoal,
                    ["实力地盘"] = () => FormStrengthTerritory,
                    ["领袖"] = () => FormLeader,
                    ["核心成员"] = () => FormCoreMembers,
                    ["成员特征"] = () => FormMemberTraits,
                    ["盟友势力"] = () => FormAllies,
                    ["敌对势力"] = () => FormEnemies,
                    ["中立竞争"] = () => FormNeutralCompetitors,
                },
                ContextProvider = async () => await GetFactionContextAsync(),
                BatchFieldKeyMap = CreateBatchFieldKeyMap(),
                BatchIndexFields = new() { "Name", "FactionType", "Goal", "Leader" }
            };
        }

        public static Dictionary<string, string> CreateBatchFieldKeyMap()
            => EntityFieldMeta.GetFieldKeyMap("factions");

        private async System.Threading.Tasks.Task<string> GetFactionContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetFactionContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }

            var availableChars = AvailableCharacters.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (availableChars.Count > 0)
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选角色",
                    candidates: availableChars,
                    fieldHint: "领袖/核心成员必须从以下列表中选择"));
            }

            var availableFactions = AvailableFactions.Where(f => !string.IsNullOrEmpty(f)).ToList();
            if (availableFactions.Count > 0)
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选势力",
                    candidates: availableFactions,
                    fieldHint: "盟友/敌对/中立必须从以下列表中选择"));
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「领袖」「核心成员」只填写角色姓名，禁止附带描述、职位或其他说明性文字。");
            sb.AppendLine("2. 「理念目标」「实力地盘」「成员特征」等长文本字段如有多条，请在字符串内用换行分条。");
            sb.AppendLine($"3. 「势力类型」必须从以下选项中选择（双斜线表示跨题材等价类型，根据上下文世界观选择最匹配的一项）：{string.Join("、", FactionTypeOptions.Where(o => !string.IsNullOrWhiteSpace(o)))}");
            sb.AppendLine("4. 批量生成时，同一种势力类型不得超过总数的 1/3，确保类型分布多元化。");
            sb.AppendLine("5. 「Name」后缀应与「势力类型」保持风格一致（参考：宗门/教派→宗/派/教/门；王国/帝国→国/朝/邦；家族/世家→家/氏/阀；商盟/行会→盟/商行/局；军事组织→军/营/旅/卫；秘密组织→阁/楼/堂；部落/氏族→部/族/寨），批量生成时同一后缀字不得在 Name 中出现超过总数的 1/3。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            sb.AppendLine("<relationship_constraints mandatory=\"true\">");
            sb.AppendLine("批量生成时，各势力的「盟友势力」「敌对势力」「中立竞争」可以互相引用同批次中的其他势力名称。");
            sb.AppendLine("每个势力的三项对外关系字段都必须填写：有则填写势力名称，无则填写「暂无」。");
            sb.AppendLine("当本次批量生成数量>=3时：关系网络要求至少 2个势力之间存在盟友关系，且至少 2个势力之间存在敌对关系。");
            sb.AppendLine("</relationship_constraints>");
            sb.AppendLine();

            return sb.ToString();
        }

        protected override ModuleNormalizationConfig? GetNormalizationConfig()
        {
            return new ModuleNormalizationConfig
            {
                ModuleName = nameof(FactionRulesViewModel),
                Rules = new List<FieldNormalizationRule>
                {
                    new()
                    {
                        FieldName = "FactionType",
                        Type = NormalizationType.StaticOptions,
                        StaticOptions = FactionTypeOptions.Where(o => !string.IsNullOrWhiteSpace(o)).ToList(),
                        DefaultValue = FactionTypeOptions.FirstOrDefault(o => string.Equals(o, "宗门/教派", StringComparison.Ordinal))
                                       ?? FactionTypeOptions.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))
                                       ?? string.Empty,
                        AllowEmpty = true
                    }
                }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllFactionRules().Select(r => r.Name);
        protected override int GetBaseBatchSize() => 10;
        protected override int GetBatchSize64K() => 12;
        protected override int GetBatchSize128K() => 15;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllFactionRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var batchFactionNames = entities
                .Select(e => new TM.Framework.Common.Services.BatchEntityReader(e).GetString("Name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            var allFactionCandidates = dbNames
                .Concat(batchFactionNames)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (batchFactionNames.Count >= 3)
            {
                var suffixGroups = batchFactionNames
                    .Where(n => n.Length >= 1)
                    .GroupBy(n => n[n.Length - 1])
                    .OrderByDescending(g => g.Count());
                var dominant = suffixGroups.FirstOrDefault();
                if (dominant != null && dominant.Count() * 3 > batchFactionNames.Count)
                {
                    TM.App.Log($"[FactionRulesViewModel] 命名同质化警告：后缀「{dominant.Key}」出现 {dominant.Count()}/{batchFactionNames.Count} 次（超过1/3），命名多样性不足");
                }
            }

            Service.BeginBatchSave();
            try
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name))
                            name = $"势力_{DateTime.Now:HHmmss}_{result.Count + 1}";

                        var baseName = name;

                        if (dbNames.Contains(baseName))
                        {
                            TM.App.Log($"[FactionRulesViewModel] 跳过已存在势力: {baseName}");
                            continue;
                        }

                        int suffix = 1;
                        while (batchNames.Contains(name))
                        {
                            name = $"{baseName}_{suffix++}";
                        }
                        batchNames.Add(name);
                        dbNames.Add(name);

                        var leader = await ResolveOrCreateCharacterNameAsync(reader.GetString("Leader"));
                        var allies = ResolveFactionNamesInScope(reader.GetString("Allies"), name, allFactionCandidates);
                        var enemies = ResolveFactionNamesInScope(reader.GetString("Enemies"), name, allFactionCandidates);
                        var neutral = ResolveFactionNamesInScope(reader.GetString("NeutralCompetitors"), name, allFactionCandidates);
                        var data = new FactionRulesData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = name,
                            Category = categoryName,
                            IsEnabled = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            FactionType = NormalizeFieldValue("FactionType", reader.GetString("FactionType")),
                            Goal = reader.GetString("Goal"),
                            StrengthTerritory = reader.GetString("StrengthTerritory"),
                            Leader = leader,
                            CoreMembers = await ResolveOrCreateCharacterNamesAsync(reader.GetString("CoreMembers")),
                            MemberTraits = reader.GetString("MemberTraits"),
                            Allies = allies,
                            Enemies = enemies,
                            NeutralCompetitors = neutral,
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };

                        entity["Name"] = name;
                        entity["Leader"] = leader;
                        await Service.AddFactionRuleAsync(data);
                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[FactionRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[FactionRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }
    }
}
