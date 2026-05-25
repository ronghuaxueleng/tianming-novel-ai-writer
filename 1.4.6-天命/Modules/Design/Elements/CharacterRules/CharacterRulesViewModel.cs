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
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

namespace TM.Modules.Design.Elements.CharacterRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CharacterRulesViewModel : DataManagementViewModelBase<CharacterRulesData, CharacterRulesCategory, CharacterRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private string _formName = string.Empty;
        private string _formIcon = "👤";
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

        private string _formCharacterType = string.Empty;
        private string _formGender = string.Empty;
        private string _formAge = string.Empty;
        private string _formIdentity = string.Empty;
        private string _formRace = string.Empty;
        private string _formAppearance = string.Empty;

        public string FormCharacterType { get => _formCharacterType; set { _formCharacterType = value; OnPropertyChanged(); } }
        public string FormGender { get => _formGender; set { _formGender = value; OnPropertyChanged(); } }
        public string FormAge { get => _formAge; set { _formAge = value; OnPropertyChanged(); } }
        public string FormIdentity { get => _formIdentity; set { _formIdentity = value; OnPropertyChanged(); } }
        public string FormRace { get => _formRace; set { _formRace = value; OnPropertyChanged(); } }
        public string FormAppearance { get => _formAppearance; set { _formAppearance = value; OnPropertyChanged(); } }

        private string _formTargetCharacterName = string.Empty;
        private string _formRelationshipType = string.Empty;
        private string _formEmotionDynamic = string.Empty;

        public string FormTargetCharacterName { get => _formTargetCharacterName; set { _formTargetCharacterName = value; OnPropertyChanged(); } }
        public string FormRelationshipType { get => _formRelationshipType; set { _formRelationshipType = value; OnPropertyChanged(); } }
        public string FormEmotionDynamic { get => _formEmotionDynamic; set { _formEmotionDynamic = value; OnPropertyChanged(); } }

        private string _formWant = string.Empty;
        private string _formNeed = string.Empty;
        private string _formFlawBelief = string.Empty;
        private string _formGrowthPath = string.Empty;

        public string FormWant { get => _formWant; set { _formWant = value; OnPropertyChanged(); } }
        public string FormNeed { get => _formNeed; set { _formNeed = value; OnPropertyChanged(); } }
        public string FormFlawBelief { get => _formFlawBelief; set { _formFlawBelief = value; OnPropertyChanged(); } }
        public string FormGrowthPath { get => _formGrowthPath; set { _formGrowthPath = value; OnPropertyChanged(); } }

        private string _formCombatSkills = string.Empty;
        private string _formNonCombatSkills = string.Empty;
        private string _formSpecialAbilities = string.Empty;

        public string FormCombatSkills { get => _formCombatSkills; set { _formCombatSkills = value; OnPropertyChanged(); } }
        public string FormNonCombatSkills { get => _formNonCombatSkills; set { _formNonCombatSkills = value; OnPropertyChanged(); } }
        public string FormSpecialAbilities { get => _formSpecialAbilities; set { _formSpecialAbilities = value; OnPropertyChanged(); } }

        private string _formSignatureItems = string.Empty;
        private string _formCommonItems = string.Empty;
        private string _formPersonalAssets = string.Empty;

        public string FormSignatureItems { get => _formSignatureItems; set { _formSignatureItems = value; OnPropertyChanged(); } }
        public string FormCommonItems { get => _formCommonItems; set { _formCommonItems = value; OnPropertyChanged(); } }
        public string FormPersonalAssets { get => _formPersonalAssets; set { _formPersonalAssets = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
        public List<string> CharacterTypeOptions { get; } = new() { "主角", "主要角色", "重要配角", "次要配角", "龙套" };

        private List<string> _availableCharacters = new();
        private Dictionary<string, string> _charIdToName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _charNameToId = new(StringComparer.OrdinalIgnoreCase);

        public List<string> AvailableCharacters
        {
            get => _availableCharacters;
            set { _availableCharacters = value; OnPropertyChanged(); }
        }

        public CharacterRulesViewModel(IPromptRepository promptRepository, ContextService contextService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            RefreshRelationshipOptions();
        }

        private void RefreshRelationshipOptions()
        {
            _charIdToName = new(StringComparer.OrdinalIgnoreCase);
            _charNameToId = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var characters = Service.GetAllCharacterRules()
                    .Where(c => c.IsEnabled && c.Id != _currentEditingData?.Id)
                    .ToList();
                foreach (var c in characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Id))  _charIdToName[c.Id]   = c.Name;
                    if (!string.IsNullOrWhiteSpace(c.Name)) _charNameToId[c.Name] = c.Id;
                }
                AvailableCharacters = characters.Select(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharacterRulesViewModel] 加载角色列表失败: {ex.Message}");
                AvailableCharacters = new List<string>();
            }
        }

        private string IdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_charIdToName.TryGetValue(idOrName, out var name)) return name;
            if (_charNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string NameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (_charNameToId.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            return string.Empty;
        }

        protected override string DefaultDataIcon => "👤";

        protected override CharacterRulesData? CreateNewData(string? categoryName = null)
        {
            return new CharacterRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新角色规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var name = (FormTargetCharacterName ?? string.Empty).Trim();
            var normalized = EntityNameNormalizeHelper.StripBracketAnnotation(name).Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(normalized))
            {
                FormTargetCharacterName = string.Empty;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            if (ShortIdGenerator.IsLikelyId(normalized))
            {
                FormTargetCharacterName = normalized;
                return System.Threading.Tasks.Task.CompletedTask;
            }
            if (!AvailableCharacters.Any(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                TM.App.Log($"[CharacterRulesViewModel] 实体引用：'{normalized}' 在上游不存在，已忽略");
                FormTargetCharacterName = string.Empty;
            }
            else
            {
                FormTargetCharacterName = normalized;
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

        protected override int ClearAllDataItems() => Service.ClearAllCharacterRules();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateCharacterRule(_currentEditingData);
        }

        protected override List<CharacterRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<CharacterRulesData> GetAllDataItems() => Service.GetAllCharacterRules();

        protected override string GetDataCategory(CharacterRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(CharacterRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = "👤",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(CharacterRulesData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Identity.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Race.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Want.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: CharacterRulesData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    RefreshRelationshipOptions();
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: CharacterRulesCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharacterRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(CharacterRulesData data)
        {
            FormName = data.Name;
            FormIcon = "👤";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormCharacterType = data.CharacterType;
            FormGender = data.Gender;
            FormAge = data.Age;
            FormIdentity = data.Identity;
            FormRace = data.Race;
            FormAppearance = data.Appearance;

            FormTargetCharacterName = IdToName(data.TargetCharacterName);
            FormRelationshipType = data.RelationshipType;
            FormEmotionDynamic = data.EmotionDynamic;

            FormWant = data.Want;
            FormNeed = data.Need;
            FormFlawBelief = data.FlawBelief;
            FormGrowthPath = data.GrowthPath;

            FormCombatSkills = data.CombatSkills;
            FormNonCombatSkills = data.NonCombatSkills;
            FormSpecialAbilities = data.SpecialAbilities;

            FormSignatureItems = data.SignatureItems;
            FormCommonItems = data.CommonItems;
            FormPersonalAssets = data.PersonalAssets;
        }

        private void LoadCategoryToForm(CharacterRulesCategory category)
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
            FormCharacterType = string.Empty;
            FormGender = string.Empty;
            FormAge = string.Empty;
            FormIdentity = string.Empty;
            FormRace = string.Empty;
            FormAppearance = string.Empty;

            FormTargetCharacterName = string.Empty;
            FormRelationshipType = string.Empty;
            FormEmotionDynamic = string.Empty;

            FormWant = string.Empty;
            FormNeed = string.Empty;
            FormFlawBelief = string.Empty;
            FormGrowthPath = string.Empty;

            FormCombatSkills = string.Empty;
            FormNonCombatSkills = string.Empty;
            FormSpecialAbilities = string.Empty;

            FormSignatureItems = string.Empty;
            FormCommonItems = string.Empty;
            FormPersonalAssets = string.Empty;
        }

        protected override string NewItemTypeName => "角色规则";
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
                TM.App.Log($"[CharacterRulesViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[CharacterRulesViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或角色规则");
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

            var newCategory = new CharacterRulesCategory
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
            await Service.AddCharacterRuleAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"角色规则『{newData.Name}』已创建");
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
            await Service.UpdateCharacterRuleAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"角色规则『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(CharacterRulesData data)
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

            data.CharacterType = FormCharacterType;
            data.Gender = FormGender;
            data.Age = FormAge;
            data.Identity = FormIdentity;
            data.Race = FormRace;
            data.Appearance = FormAppearance;

            data.TargetCharacterName = NameToId(FormTargetCharacterName);
            data.RelationshipType = FormRelationshipType;
            data.EmotionDynamic = FormEmotionDynamic;

            data.Want = FormWant;
            data.Need = FormNeed;
            data.FlawBelief = FormFlawBelief;
            data.GrowthPath = FormGrowthPath;

            data.CombatSkills = FormCombatSkills;
            data.NonCombatSkills = FormNonCombatSkills;
            data.SpecialAbilities = FormSpecialAbilities;

            data.SignatureItems = FormSignatureItems;
            data.CommonItems = FormCommonItems;
            data.PersonalAssets = FormPersonalAssets;
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有角色规则也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllCharacterRules()
                            .Where(d => d.Category == categoryName)
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteCharacterRule(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个角色规则");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除角色规则『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteCharacterRule(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"角色规则『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或角色规则");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharacterRulesViewModel] 删除失败: {ex.Message}");
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
                MessagePrefix = "角色设计",
                ProgressMessage = "正在设计角色规则...",
                CompleteMessage = "角色设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["角色类型"] = v => FormCharacterType = EntityNameNormalizeHelper.FilterToCandidate(v, CharacterTypeOptions),
                    ["性别"] = v => FormGender = v,
                    ["年龄"] = v => FormAge = v,
                    ["身份"] = v => FormIdentity = v,
                    ["种族"] = v => FormRace = v,
                    ["外貌特征"] = v => FormAppearance = v,
                    ["关联角色姓名"] = v => FormTargetCharacterName = FilterToCandidateOrRaw(v, AvailableCharacters),
                    ["关系类型"] = v => FormRelationshipType = v,
                    ["情感动态"] = v => FormEmotionDynamic = v,
                    ["外在目标"] = v => FormWant = v,
                    ["内在需求"] = v => FormNeed = v,
                    ["致命缺点"] = v => FormFlawBelief = v,
                    ["成长路径"] = v => FormGrowthPath = v,
                    ["战斗技能"] = v => FormCombatSkills = v,
                    ["特殊能力"] = v => FormSpecialAbilities = v,
                    ["非战斗技能"] = v => FormNonCombatSkills = v,
                    ["标志性装备"] = v => FormSignatureItems = v,
                    ["常规装备"] = v => FormCommonItems = v,
                    ["个人资产"] = v => FormPersonalAssets = v,
                },
                OutputFieldGetters = new()
                {
                    ["角色类型"] = () => FormCharacterType,
                    ["性别"] = () => FormGender,
                    ["年龄"] = () => FormAge,
                    ["身份"] = () => FormIdentity,
                    ["种族"] = () => FormRace,
                    ["外貌特征"] = () => FormAppearance,
                    ["关联角色姓名"] = () => FormTargetCharacterName,
                    ["关系类型"] = () => FormRelationshipType,
                    ["情感动态"] = () => FormEmotionDynamic,
                    ["外在目标"] = () => FormWant,
                    ["内在需求"] = () => FormNeed,
                    ["致命缺点"] = () => FormFlawBelief,
                    ["成长路径"] = () => FormGrowthPath,
                    ["战斗技能"] = () => FormCombatSkills,
                    ["特殊能力"] = () => FormSpecialAbilities,
                    ["非战斗技能"] = () => FormNonCombatSkills,
                    ["标志性装备"] = () => FormSignatureItems,
                    ["常规装备"] = () => FormCommonItems,
                    ["个人资产"] = () => FormPersonalAssets,
                },
                ContextProvider = async () => await GetEnhancedCharacterContextAsync(),
                BatchFieldKeyMap = new()
                {
                    ["角色类型"] = "CharacterType",
                    ["性别"] = "Gender",
                    ["年龄"] = "Age",
                    ["身份"] = "Identity",
                    ["种族"] = "Race",
                    ["外貌特征"] = "Appearance",
                    ["关联角色姓名"] = "TargetCharacterName",
                    ["关系类型"] = "RelationshipType",
                    ["情感动态"] = "EmotionDynamic",
                    ["外在目标"] = "Want",
                    ["内在需求"] = "Need",
                    ["致命缺点"] = "FlawBelief",
                    ["成长路径"] = "GrowthPath",
                    ["战斗技能"] = "CombatSkills",
                    ["特殊能力"] = "SpecialAbilities",
                    ["非战斗技能"] = "NonCombatSkills",
                    ["标志性装备"] = "SignatureItems",
                    ["常规装备"] = "CommonItems",
                    ["个人资产"] = "PersonalAssets"
                },
                BatchIndexFields = new() { "Name", "CharacterType", "Identity", "SpecialAbilities" }
            };
        }

        protected override ModuleNormalizationConfig? GetNormalizationConfig()
        {
            return new ModuleNormalizationConfig
            {
                ModuleName = nameof(CharacterRulesViewModel),
                Rules = new List<FieldNormalizationRule>
                {
                    new()
                    {
                        FieldName = "CharacterType",
                        Type = NormalizationType.StaticOptions,
                        StaticOptions = CharacterTypeOptions.ToList(),
                        DefaultValue = CharacterTypeOptions.FirstOrDefault(c => string.Equals(c, "次要配角", StringComparison.Ordinal))
                                       ?? CharacterTypeOptions.FirstOrDefault()
                                       ?? string.Empty
                    },
                    new()
                    {
                        FieldName = "TargetCharacterName",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableCharacters.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    }
                }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllCharacterRules().Select(r => r.Name);
        protected override int GetDefaultBatchSize() => 10;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllCharacterRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allBatchCharacterNames = entities
                .Select(e => new TM.Framework.Common.Services.BatchEntityReader(e).GetString("Name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"角色_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[CharacterRulesViewModel] 跳过已存在角色: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var characterType = NormalizeFieldValue("CharacterType", reader.GetString("CharacterType"));
                    var rawTarget = reader.GetString("TargetCharacterName")?.Trim() ?? string.Empty;
                    var targetCharacterName = string.IsNullOrWhiteSpace(rawTarget)
                        ? string.Empty
                        : (dbNames.Contains(rawTarget) || allBatchCharacterNames.Contains(rawTarget))
                            ? rawTarget
                            : string.Empty;
                    var data = new CharacterRulesData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        CharacterType = characterType,
                        Gender = reader.GetString("Gender"),
                        Age = reader.GetString("Age"),
                        Identity = reader.GetString("Identity"),
                        Race = reader.GetString("Race"),
                        Appearance = reader.GetString("Appearance"),
                        Want = reader.GetString("Want"),
                        Need = reader.GetString("Need"),
                        FlawBelief = reader.GetString("FlawBelief"),
                        GrowthPath = reader.GetString("GrowthPath"),
                        TargetCharacterName = targetCharacterName,
                        RelationshipType = reader.GetString("RelationshipType"),
                        EmotionDynamic = reader.GetString("EmotionDynamic"),
                        CombatSkills = reader.GetString("CombatSkills"),
                        NonCombatSkills = reader.GetString("NonCombatSkills"),
                        SpecialAbilities = reader.GetString("SpecialAbilities"),
                        SignatureItems = reader.GetString("SignatureItems"),
                        CommonItems = reader.GetString("CommonItems"),
                        PersonalAssets = reader.GetString("PersonalAssets")
                    };

                    entity["Name"] = name;
                    entity["CharacterType"] = characterType;
                    await Service.AddCharacterRuleAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[CharacterRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[CharacterRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        private async System.Threading.Tasks.Task<string> GetEnhancedCharacterContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetCharacterContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }
            var availableChars = AvailableCharacters.Where(c => !string.IsNullOrEmpty(c)).ToList();
            sb.AppendLine("<section name=\"available_characters\">");
            if (availableChars.Any())
            {
                sb.AppendLine("已有角色（关联角色姓名可从以下已有角色中选择）：");
                sb.AppendLine(string.Join("、", availableChars));
            }
            sb.AppendLine("本次批量生成的角色之间可以互相关联：「关联角色姓名」可以填写本批次 JSON 数组中其他角色的 Name 值。");
            sb.AppendLine("</section>");
            sb.AppendLine();

            sb.AppendLine("<protagonist_constraint mandatory=\"true\">");
            sb.AppendLine("如果上下文中「角色素材-主角塑造」定义了主角，批量生成时必须优先包含该主角角色（角色类型设为\"主角\"），Name使用素材中的主角姓名，剩余名额再生成配角。");
            sb.AppendLine("</protagonist_constraint>");
            sb.AppendLine();

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「角色类型」必须从以下选项中选择：主角、主要角色、重要配角、次要配角、龙套。");
            sb.AppendLine("2. 「关联角色姓名」填写与该角色关系最重要的单个角色姓名（本批次或已有角色均可）；确实无关联则填「暂无」。");
            sb.AppendLine("3. 「人物弧光」「能力技能」「装备资产」等长文本字段如有多条，请在字符串内用换行分条。");
            sb.AppendLine("4. 「身份」只填写社会身份、职业标签与当前处境，严禁写入修为境界、修炼阶段或任何能力描述；此类信息应填入「战斗技能」或「特殊能力」字段。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            return sb.ToString();
        }

    }
}
