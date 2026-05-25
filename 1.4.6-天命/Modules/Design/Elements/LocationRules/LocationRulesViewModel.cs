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
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

namespace TM.Modules.Design.Elements.LocationRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LocationRulesViewModel : DataManagementViewModelBase<LocationRulesData, LocationRulesCategory, LocationRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly FactionRulesService _factionRulesService;
        private string _formName = string.Empty;
        private string _formIcon = "📍";
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

        private string _formLocationType = string.Empty;
        private string _formDescription = string.Empty;
        private string _formScale = string.Empty;

        public string FormLocationType { get => _formLocationType; set { _formLocationType = value; OnPropertyChanged(); } }
        public string FormDescription { get => _formDescription; set { _formDescription = value; OnPropertyChanged(); } }
        public string FormScale { get => _formScale; set { _formScale = value; OnPropertyChanged(); } }

        private string _formTerrain = string.Empty;
        private string _formClimate = string.Empty;
        private string _formLandmarks = string.Empty;
        private string _formResources = string.Empty;

        public string FormTerrain { get => _formTerrain; set { _formTerrain = value; OnPropertyChanged(); } }
        public string FormClimate { get => _formClimate; set { _formClimate = value; OnPropertyChanged(); } }
        public string FormLandmarks { get => _formLandmarks; set { _formLandmarks = value; OnPropertyChanged(); } }
        public string FormResources { get => _formResources; set { _formResources = value; OnPropertyChanged(); } }

        private string _formHistoricalSignificance = string.Empty;
        private string _formDangers = string.Empty;
        private string _formFactionId = string.Empty;

        public string FormHistoricalSignificance { get => _formHistoricalSignificance; set { _formHistoricalSignificance = value; OnPropertyChanged(); } }
        public string FormDangers { get => _formDangers; set { _formDangers = value; OnPropertyChanged(); } }
        public string FormFactionId { get => _formFactionId; set { _formFactionId = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
        public List<string> LocationTypeOptions { get; } = new() { "", "区域/大陆", "城市", "地点/秘境" };

        private List<string> _availableFactionNames = new();

        private Dictionary<string, string> _factionIdToName = new();
        private Dictionary<string, string> _factionNameToId = new(StringComparer.OrdinalIgnoreCase);

        public List<string> AvailableFactions
        {
            get => _availableFactionNames;
            set { _availableFactionNames = value; OnPropertyChanged(); }
        }

        public LocationRulesViewModel(IPromptRepository promptRepository, ContextService contextService, FactionRulesService factionRulesService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _factionRulesService = factionRulesService;
            RefreshRelationshipOptions();
        }

        private void RefreshRelationshipOptions()
        {
            _factionIdToName = new();
            _factionNameToId = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var factionList = _factionRulesService.GetAllFactionRules()
                    .Where(f => f.IsEnabled)
                    .ToList();

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
                TM.App.Log($"[LocationRulesViewModel] 加载势力列表失败: {ex.Message}");
                AvailableFactions = new List<string>();
            }
        }

        private string IdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_factionIdToName.TryGetValue(idOrName, out var name)) return name;
            if (_factionNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string NameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var normalized = EntityNameNormalizeHelper.StripBracketAnnotation(name).Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

            if (string.Equals(normalized, "无", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "暂无", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "空", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "无所属", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "不适用", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "NA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "-", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (_factionNameToId.TryGetValue(normalized, out var id)) return id;
            if (_factionNameToId.TryGetValue(name.Trim(), out id)) return id;
            if (ShortIdGenerator.IsLikelyId(normalized)) return normalized;

            foreach (var kv in _factionNameToId)
            {
                var candidateName = kv.Key;
                if (string.IsNullOrWhiteSpace(candidateName)) continue;
                if (EntityNameNormalizeHelper.NameExistsInContent(candidateName, normalized) ||
                    EntityNameNormalizeHelper.NameExistsInContent(normalized, candidateName) ||
                    normalized.Contains(candidateName, StringComparison.OrdinalIgnoreCase) ||
                    candidateName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            TM.App.Log($"[LocationRulesViewModel] 未匹配到名称: {name}");
            return string.Empty;
        }

        protected override string DefaultDataIcon => "📍";

        protected override LocationRulesData? CreateNewData(string? categoryName = null)
        {
            return new LocationRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新位置规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormFactionId)) return System.Threading.Tasks.Task.CompletedTask;

            var displayName = FormFactionId.Trim();
            if (ShortIdGenerator.IsLikelyId(displayName)) return System.Threading.Tasks.Task.CompletedTask;

            var nullWords = new[] { "无", "暂无", "空", "无所属", "不适用", "N/A", "NA", "None", "-", "/", "null" };
            if (nullWords.Any(w => string.Equals(displayName, w, StringComparison.OrdinalIgnoreCase)))
            {
                FormFactionId = string.Empty;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            var existing = _factionRulesService.GetAllFactionRules()
                .FirstOrDefault(f => f.IsEnabled &&
                    string.Equals(f.Name, displayName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!_factionNameToId.ContainsKey(existing.Name))
                {
                    _factionIdToName[existing.Id] = existing.Name;
                    _factionNameToId[existing.Name] = existing.Id;
                }
                FormFactionId = existing.Name;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            TM.App.Log($"[LocationRulesViewModel] 实体引用：势力 '{displayName}' 在上游不存在，已忽略");
            FormFactionId = string.Empty;
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
            await EnsureServiceInitializedAsync(_factionRulesService);

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

        protected override int ClearAllDataItems() => Service.ClearAllLocationRules();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateLocationRule(_currentEditingData);
        }

        protected override List<LocationRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<LocationRulesData> GetAllDataItems() => Service.GetAllLocationRules();

        protected override string GetDataCategory(LocationRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(LocationRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = "📍",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(LocationRulesData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.LocationType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: LocationRulesData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    RefreshRelationshipOptions();
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: LocationRulesCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    RefreshRelationshipOptions();
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocationRulesViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(LocationRulesData data)
        {
            FormName = data.Name;
            FormIcon = "📍";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormLocationType = data.LocationType;
            FormDescription = data.Description;
            FormScale = data.Scale;

            FormTerrain = data.Terrain;
            FormClimate = data.Climate;
            FormLandmarks = string.Join("\n", data.Landmarks);
            FormResources = string.Join("\n", data.Resources);

            FormHistoricalSignificance = data.HistoricalSignificance;
            FormDangers = string.Join("\n", data.Dangers);
            FormFactionId = IdToName(data.FactionId ?? string.Empty);
        }

        private void LoadCategoryToForm(LocationRulesCategory category)
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
            FormLocationType = string.Empty;
            FormDescription = string.Empty;
            FormScale = string.Empty;

            FormTerrain = string.Empty;
            FormClimate = string.Empty;
            FormLandmarks = string.Empty;
            FormResources = string.Empty;

            FormHistoricalSignificance = string.Empty;
            FormDangers = string.Empty;
            FormFactionId = string.Empty;
        }

        protected override string NewItemTypeName => "位置规则";
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
                TM.App.Log($"[LocationRulesViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[LocationRulesViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或位置规则");
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

            var newCategory = new LocationRulesCategory
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
            await Service.AddLocationRuleAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"位置规则『{newData.Name}』已创建");
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
            await Service.UpdateLocationRuleAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"位置规则『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(LocationRulesData data)
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

            data.LocationType = FormLocationType;
            data.Description = FormDescription;
            data.Scale = FormScale;

            data.Terrain = FormTerrain;
            data.Climate = FormClimate;
            data.Landmarks = FormLandmarks.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            data.Resources = FormResources.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

            data.HistoricalSignificance = FormHistoricalSignificance;
            data.Dangers = FormDangers.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var factionId = NameToId(FormFactionId);
            data.FactionId = string.IsNullOrWhiteSpace(factionId) ? null : factionId;
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有位置规则也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllLocationRules()
                            .Where(d => d.Category == categoryName)
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteLocationRule(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个位置规则");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除位置规则『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteLocationRule(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"位置规则『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或位置规则");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocationRulesViewModel] 删除失败: {ex.Message}");
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
                MessagePrefix = "位置设计",
                ProgressMessage = "正在设计位置规则...",
                CompleteMessage = "位置设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["位置类型"] = v => FormLocationType = EntityNameNormalizeHelper.FilterToCandidate(v, LocationTypeOptions),
                    ["位置描述"] = v => FormDescription = v,
                    ["规模范围"] = v => FormScale = v,
                    ["地形环境"] = v => FormTerrain = v,
                    ["气候特征"] = v => FormClimate = v,
                    ["标志地标"] = v => FormLandmarks = v,
                    ["特产资源"] = v => FormResources = v,
                    ["历史意义"] = v => FormHistoricalSignificance = v,
                    ["危险禁忌"] = v => FormDangers = v,
                    ["所属势力"] = v => FormFactionId = FilterToCandidateOrRaw(v, AvailableFactions),
                },
                OutputFieldGetters = new()
                {
                    ["位置类型"] = () => FormLocationType,
                    ["位置描述"] = () => FormDescription,
                    ["规模范围"] = () => FormScale,
                    ["地形环境"] = () => FormTerrain,
                    ["气候特征"] = () => FormClimate,
                    ["标志地标"] = () => FormLandmarks,
                    ["特产资源"] = () => FormResources,
                    ["历史意义"] = () => FormHistoricalSignificance,
                    ["危险禁忌"] = () => FormDangers,
                    ["所属势力"] = () => FormFactionId,
                },
                ContextProvider = async () => await GetLocationContextAsync(),
                BatchFieldKeyMap = new()
                {
                    ["位置类型"] = "LocationType",
                    ["位置描述"] = "Description",
                    ["规模范围"] = "Scale",
                    ["地形环境"] = "Terrain",
                    ["气候特征"] = "Climate",
                    ["标志地标"] = "Landmarks",
                    ["特产资源"] = "Resources",
                    ["历史意义"] = "HistoricalSignificance",
                    ["危险禁忌"] = "Dangers",
                    ["所属势力"] = "FactionId"
                },
                BatchIndexFields = new() { "Name", "LocationType", "Description" }
            };
        }

        protected override ModuleNormalizationConfig? GetNormalizationConfig()
        {
            return new ModuleNormalizationConfig
            {
                ModuleName = nameof(LocationRulesViewModel),
                Rules = new List<FieldNormalizationRule>
                {
                    new()
                    {
                        FieldName = "LocationType",
                        Type = NormalizationType.StaticOptions,
                        StaticOptions = LocationTypeOptions.Where(o => !string.IsNullOrWhiteSpace(o)).ToList(),
                        DefaultValue = LocationTypeOptions.FirstOrDefault(o => string.Equals(o, "地点/秘境", StringComparison.Ordinal))
                                       ?? LocationTypeOptions.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))
                                       ?? string.Empty,
                        AllowEmpty = true
                    }
                }
            };
        }

        private async System.Threading.Tasks.Task<string> GetLocationContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetLocationContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }

            var availableFacs = AvailableFactions.Where(f => !string.IsNullOrEmpty(f)).ToList();
            if (availableFacs.Any())
            {
                sb.AppendLine("<section name=\"available_factions\">");
                sb.AppendLine("所属势力必须从以下列表中选择");
                sb.AppendLine(string.Join("、", availableFacs));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<section name=\"location_type_options\">");
            sb.AppendLine(string.Join("、", LocationTypeOptions.Where(o => !string.IsNullOrEmpty(o))));
            sb.AppendLine("</section>");
            sb.AppendLine();

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「位置类型」必须从上方 location_type_options 列表中选择。");
            sb.AppendLine("2. 「所属势力」请填写势力名称（不是ID/GUID）；有则填写，无则填写「暂无」。");
            sb.AppendLine("3. 「标志地标」「特产资源」「危险禁忌」如有多项，请在字符串内用换行分条；无则填写「暂无」。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            var otherLocations = Service.GetAllLocationRules()
                .Where(l => l.Id != _currentEditingData?.Id && l.IsEnabled)
                .ToList();

            if (otherLocations.Any())
            {
                sb.AppendLine("<section name=\"other_locations\">");
                foreach (var location in otherLocations)
                {
                    sb.AppendLine($"- **{location.Name}**（{location.LocationType}）：{location.Description}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllLocationRules().Select(r => r.Name);
        protected override int GetDefaultBatchSize() => 15;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllLocationRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"位置_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[LocationRulesViewModel] 跳过已存在地点: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var locationType = NormalizeFieldValue("LocationType", reader.GetString("LocationType"));
                    var factionName = reader.GetString("FactionId");
                    var factionId = NameToId(factionName);
                    if (!string.IsNullOrWhiteSpace(factionName)
                        && string.IsNullOrWhiteSpace(factionId)
                        && !ShortIdGenerator.IsLikelyId(factionName))
                    {
                        TM.App.Log($"[LocationRulesViewModel] 实体引用（批量）：势力 '{factionName.Trim()}' 在上游不存在，已忽略");
                        factionId = string.Empty;
                    }

                    var data = new LocationRulesData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        LocationType = locationType,
                        Description = reader.GetString("Description"),
                        Scale = reader.GetString("Scale"),
                        Terrain = reader.GetString("Terrain"),
                        Climate = reader.GetString("Climate"),
                        Landmarks = reader.GetStringList("Landmarks"),
                        Resources = reader.GetStringList("Resources"),
                        HistoricalSignificance = reader.GetString("HistoricalSignificance"),
                        Dangers = reader.GetStringList("Dangers"),
                        FactionId = factionId
                    };

                    entity["Name"] = name;
                    entity["LocationType"] = locationType;
                    await Service.AddLocationRuleAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LocationRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[LocationRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }
    }
}
