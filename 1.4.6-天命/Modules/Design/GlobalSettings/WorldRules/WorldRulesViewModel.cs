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
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Modules.Design.GlobalSettings.WorldRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

namespace TM.Modules.Design.GlobalSettings.WorldRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class WorldRulesViewModel : DataManagementViewModelBase<WorldRulesData, WorldRulesCategory, WorldRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;

        public WorldRulesViewModel(IPromptRepository promptRepository, ContextService contextService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
        }
        private string _formName = string.Empty;
        private string _formIcon = "🌍";
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

        private string _formOneLineSummary = string.Empty;
        private string _formPowerSystem = string.Empty;
        private string _formCosmology = string.Empty;
        private string _formSpecialLaws = string.Empty;
        private string _formHardRules = string.Empty;
        private string _formSoftRules = string.Empty;

        public string FormOneLineSummary { get => _formOneLineSummary; set { _formOneLineSummary = value; OnPropertyChanged(); } }
        public string FormPowerSystem { get => _formPowerSystem; set { _formPowerSystem = value; OnPropertyChanged(); } }
        public string FormCosmology { get => _formCosmology; set { _formCosmology = value; OnPropertyChanged(); } }
        public string FormSpecialLaws { get => _formSpecialLaws; set { _formSpecialLaws = value; OnPropertyChanged(); } }
        public string FormHardRules { get => _formHardRules; set { _formHardRules = value; OnPropertyChanged(); } }
        public string FormSoftRules { get => _formSoftRules; set { _formSoftRules = value; OnPropertyChanged(); } }

        private string _formAncientEra = string.Empty;
        private string _formKeyEvents = string.Empty;
        private string _formModernHistory = string.Empty;
        private string _formStatusQuo = string.Empty;

        public string FormAncientEra { get => _formAncientEra; set { _formAncientEra = value; OnPropertyChanged(); } }
        public string FormKeyEvents { get => _formKeyEvents; set { _formKeyEvents = value; OnPropertyChanged(); } }
        public string FormModernHistory { get => _formModernHistory; set { _formModernHistory = value; OnPropertyChanged(); } }
        public string FormStatusQuo { get => _formStatusQuo; set { _formStatusQuo = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        protected override string DefaultDataIcon => "🌍";

        protected override WorldRulesData? CreateNewData(string? categoryName = null)
        {
            return new WorldRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新世界观规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllWorldRules();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateWorldRule(_currentEditingData);
        }

        protected override List<WorldRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<WorldRulesData> GetAllDataItems() => Service.GetAllWorldRules();

        protected override string GetDataCategory(WorldRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(WorldRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = "🌍",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(WorldRulesData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OneLineSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.PowerSystem.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.HardRules.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: WorldRulesData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: WorldRulesCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorldRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(WorldRulesData data)
        {
            FormName = data.Name;
            FormIcon = "🌍";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormOneLineSummary = data.OneLineSummary;
            FormPowerSystem = data.PowerSystem;
            FormCosmology = data.Cosmology;
            FormSpecialLaws = data.SpecialLaws;

            FormHardRules = data.HardRules;
            FormSoftRules = data.SoftRules;

            FormAncientEra = data.AncientEra;
            FormKeyEvents = data.KeyEvents;
            FormModernHistory = data.ModernHistory;
            FormStatusQuo = data.StatusQuo;

        }

        private void LoadCategoryToForm(WorldRulesCategory category)
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
            FormOneLineSummary = string.Empty;
            FormPowerSystem = string.Empty;
            FormCosmology = string.Empty;
            FormSpecialLaws = string.Empty;

            FormHardRules = string.Empty;
            FormSoftRules = string.Empty;

            FormAncientEra = string.Empty;
            FormKeyEvents = string.Empty;
            FormModernHistory = string.Empty;
            FormStatusQuo = string.Empty;
        }

        protected override string NewItemTypeName => "世界观规则";
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
                TM.App.Log($"[WorldRulesViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[WorldRulesViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或世界观规则");
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

            var newCategory = new WorldRulesCategory
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
            await Service.AddWorldRuleAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"世界观规则『{newData.Name}』已创建");
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
            await Service.UpdateWorldRuleAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"世界观规则『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(WorldRulesData data)
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

            data.OneLineSummary = FormOneLineSummary;
            data.PowerSystem = FormPowerSystem;
            data.Cosmology = FormCosmology;
            data.SpecialLaws = FormSpecialLaws;

            data.HardRules = FormHardRules;
            data.SoftRules = FormSoftRules;

            data.AncientEra = FormAncientEra;
            data.KeyEvents = FormKeyEvents;
            data.ModernHistory = FormModernHistory;
            data.StatusQuo = FormStatusQuo;
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有世界观规则也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllWorldRules()
                            .Where(d => d.Category == categoryName)
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteWorldRule(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个世界观规则");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除世界观规则『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteWorldRule(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"世界观规则『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或世界观规则");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorldRulesViewModel] 删除失败: {ex.Message}");
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
                MessagePrefix = "世界观设计",
                ProgressMessage = "正在设计世界观规则...",
                CompleteMessage = "世界观设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["一句话简介"] = v => FormOneLineSummary = v,
                    ["力量体系"] = v => FormPowerSystem = v,
                    ["宇宙观"] = v => FormCosmology = v,
                    ["特殊法则"] = v => FormSpecialLaws = v,

                    ["硬规则"] = v => FormHardRules = v,
                    ["软规则"] = v => FormSoftRules = v,

                    ["创世古代纪元"] = v => FormAncientEra = v,
                    ["关键历史事件"] = v => FormKeyEvents = v,
                    ["近代史"] = v => FormModernHistory = v,
                    ["故事开始前现状"] = v => FormStatusQuo = v,
                },
                OutputFieldGetters = new()
                {
                    ["一句话简介"] = () => FormOneLineSummary,
                    ["力量体系"] = () => FormPowerSystem,
                    ["宇宙观"] = () => FormCosmology,
                    ["特殊法则"] = () => FormSpecialLaws,

                    ["硬规则"] = () => FormHardRules,
                    ["软规则"] = () => FormSoftRules,

                    ["创世古代纪元"] = () => FormAncientEra,
                    ["关键历史事件"] = () => FormKeyEvents,
                    ["近代史"] = () => FormModernHistory,
                    ["故事开始前现状"] = () => FormStatusQuo,
                },
                ContextProvider = async () =>
                {
                    var sb = new System.Text.StringBuilder();
                    var baseContext = await _contextService.GetWorldviewContextStringAsync();
                    if (!string.IsNullOrWhiteSpace(baseContext))
                    {
                        sb.AppendLine(baseContext);
                        sb.AppendLine();
                    }

                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「硬规则」「软规则」「关键历史事件」如有多条，请在字符串内用换行分条。");
                    sb.AppendLine("2. 避免编造未在上下文中出现的专有名词；如需新增，请在对应字段中先定义其含义。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                BatchFieldKeyMap = new()
                {
                    ["一句话简介"] = "OneLineSummary",
                    ["力量体系"] = "PowerSystem",
                    ["宇宙观"] = "Cosmology",
                    ["特殊法则"] = "SpecialLaws",

                    ["硬规则"] = "HardRules",
                    ["软规则"] = "SoftRules",

                    ["创世古代纪元"] = "AncientEra",
                    ["关键历史事件"] = "KeyEvents",
                    ["近代史"] = "ModernHistory",
                    ["故事开始前现状"] = "StatusQuo"
                },
                BatchIndexFields = new() { "Name", "OneLineSummary", "PowerSystem" }
            };
        }

        protected override bool IsBatchGenerationDisabledForCurrentModule() => false;

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllWorldRules().Select(r => r.Name);
        protected override int GetDefaultBatchSize() => 5;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllWorldRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"世界观规则_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[WorldRulesViewModel] 跳过已存在规则: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var data = new WorldRulesData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        OneLineSummary = reader.GetString("OneLineSummary"),
                        PowerSystem = reader.GetString("PowerSystem"),
                        Cosmology = reader.GetString("Cosmology"),
                        SpecialLaws = reader.GetString("SpecialLaws"),
                        HardRules = reader.GetString("HardRules"),
                        SoftRules = reader.GetString("SoftRules"),
                        AncientEra = reader.GetString("AncientEra"),
                        KeyEvents = reader.GetString("KeyEvents"),
                        ModernHistory = reader.GetString("ModernHistory"),
                        StatusQuo = reader.GetString("StatusQuo")
                    };

                    entity["Name"] = name;
                    await Service.AddWorldRuleAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[WorldRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[WorldRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }
    }
}
