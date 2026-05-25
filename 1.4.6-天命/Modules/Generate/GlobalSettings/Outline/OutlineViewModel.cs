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
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Modules.Design.Elements.PlotRules.Services;

namespace TM.Modules.Generate.GlobalSettings.Outline
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class OutlineViewModel : DataManagementViewModelBase<OutlineData, OutlineCategory, OutlineService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly PlotRulesService _plotRulesService;

        public OutlineViewModel(IPromptRepository promptRepository, ContextService contextService, PlotRulesService plotRulesService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _plotRulesService = plotRulesService;
        }
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

        private string _formTotalChapterCount = string.Empty;
        private string _formEstimatedWordCount = string.Empty;
        private string _formOneLineOutline = string.Empty;
        private string _formEmotionalTone = string.Empty;
        private string _formPhilosophicalMotif = string.Empty;

        public string FormTotalChapterCount
        {
            get => _formTotalChapterCount;
            set
            {
                if (_formTotalChapterCount != value)
                {
                    _formTotalChapterCount = value;
                    OnPropertyChanged();
                    var hasEditingContext = IsCreateMode || _currentEditingData != null || _currentEditingCategory != null;
                    var isValidTotalChapters = int.TryParse(_formTotalChapterCount?.Trim(), out var n) && n > 0;
                    IsAIGenerateEnabled = hasEditingContext && isValidTotalChapters;
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string FormEstimatedWordCount { get => _formEstimatedWordCount; set { _formEstimatedWordCount = value; OnPropertyChanged(); } }
        public string FormOneLineOutline { get => _formOneLineOutline; set { _formOneLineOutline = value; OnPropertyChanged(); } }
        public string FormEmotionalTone { get => _formEmotionalTone; set { _formEmotionalTone = value; OnPropertyChanged(); } }
        public string FormPhilosophicalMotif { get => _formPhilosophicalMotif; set { _formPhilosophicalMotif = value; OnPropertyChanged(); } }

        private string _formTheme = string.Empty;
        private string _formCoreConflict = string.Empty;
        private string _formEndingState = string.Empty;

        public string FormTheme { get => _formTheme; set { _formTheme = value; OnPropertyChanged(); } }
        public string FormCoreConflict { get => _formCoreConflict; set { _formCoreConflict = value; OnPropertyChanged(); } }
        public string FormEndingState { get => _formEndingState; set { _formEndingState = value; OnPropertyChanged(); } }

        private string _formVolumeDivision = string.Empty;
        private string _formOutlineOverview = string.Empty;

        public string FormVolumeDivision { get => _formVolumeDivision; set { _formVolumeDivision = value; OnPropertyChanged(); } }
        public string FormOutlineOverview { get => _formOutlineOverview; set { _formOutlineOverview = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        protected override string DefaultDataIcon => "📖";

        protected override OutlineData? CreateNewData(string? categoryName = null)
        {
            return new OutlineData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新大纲",
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

        protected override int ClearAllDataItems() => Service.ClearAllOutlines();

        protected override List<OutlineCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<OutlineData> GetAllDataItems() => Service.GetAllOutlines();

        protected override string GetDataCategory(OutlineData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(OutlineData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = "📖",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(OutlineData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OneLineOutline.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.CoreConflict.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Theme.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: OutlineData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: OutlineCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OutlineViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(OutlineData data)
        {
            FormName = data.Name;
            FormIcon = "📖";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormTotalChapterCount = data.TotalChapterCount > 0 ? data.TotalChapterCount.ToString() : string.Empty;
            FormEstimatedWordCount = data.EstimatedWordCount;
            FormOneLineOutline = data.OneLineOutline;
            FormEmotionalTone = data.EmotionalTone;
            FormPhilosophicalMotif = data.PhilosophicalMotif;

            FormTheme = data.Theme;
            FormCoreConflict = data.CoreConflict;
            FormEndingState = data.EndingState;

            FormVolumeDivision = data.VolumeDivision;
            FormOutlineOverview = data.OutlineOverview;
        }

        private void LoadCategoryToForm(OutlineCategory category)
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
            FormTotalChapterCount = string.Empty;
            FormEstimatedWordCount = string.Empty;
            FormOneLineOutline = string.Empty;
            FormEmotionalTone = string.Empty;
            FormPhilosophicalMotif = string.Empty;

            FormTheme = string.Empty;
            FormCoreConflict = string.Empty;
            FormEndingState = string.Empty;

            FormVolumeDivision = string.Empty;
            FormOutlineOverview = string.Empty;
        }

        protected override string NewItemTypeName => "大纲";
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
                TM.App.Log($"[OutlineViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[OutlineViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或大纲");
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

            var newCategory = new OutlineCategory
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
            await Service.AddOutlineAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"大纲『{newData.Name}』已创建");
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
            await Service.UpdateOutlineAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"大纲『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(OutlineData data)
        {
            data.Name = FormName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.TotalChapterCount = int.TryParse(FormTotalChapterCount?.Trim(), out var tc) ? tc : 0;
            data.EstimatedWordCount = FormEstimatedWordCount;
            data.OneLineOutline = FormOneLineOutline;
            data.EmotionalTone = FormEmotionalTone;
            data.PhilosophicalMotif = FormPhilosophicalMotif;

            data.Theme = FormTheme;
            data.CoreConflict = FormCoreConflict;
            data.EndingState = FormEndingState;

            data.VolumeDivision = FormVolumeDivision;
            data.OutlineOverview = FormOutlineOverview;
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
                        $"确定要删除分类『{_currentEditingCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有大纲也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllOutlines()
                            .Where(d => d.Category == categoryName)
                            .ToList();

                        foreach (var item in dataInCategory)
                        {
                            Service.DeleteOutline(item.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个大纲");

                    _currentEditingCategory = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除大纲『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteOutline(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"大纲『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或大纲");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OutlineViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "小说创作者",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "大纲创作",
                ProgressMessage = "正在创作战略大纲...",
                CompleteMessage = "大纲创作完成",
                InputVariables = new()
                {
                    ["大纲名称"] = () => FormName,
                    ["章节标题"] = () => string.Empty,
                    ["场景标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["总章节数"] = v => { if (string.IsNullOrWhiteSpace(FormTotalChapterCount)) FormTotalChapterCount = v; },
                    ["预计总字数"] = v => FormEstimatedWordCount = v,
                    ["一句话大纲"] = v => FormOneLineOutline = v,
                    ["情感基调"] = v => FormEmotionalTone = v,
                    ["哲学母题"] = v => FormPhilosophicalMotif = v,
                    ["主题思想"] = v => FormTheme = v,
                    ["核心冲突"] = v => FormCoreConflict = v,
                    ["结局/目标状态"] = v => FormEndingState = v,
                    ["卷/幕划分"] = v => { if (string.IsNullOrWhiteSpace(FormVolumeDivision)) FormVolumeDivision = v; },
                    ["大纲总览"] = v => { if (string.IsNullOrWhiteSpace(FormOutlineOverview))  FormOutlineOverview  = v; },
                },
                OutputFieldGetters = new()
                {
                    ["总章节数"] = () => FormTotalChapterCount,
                    ["预计总字数"] = () => FormEstimatedWordCount,
                    ["一句话大纲"] = () => FormOneLineOutline,
                    ["情感基调"] = () => FormEmotionalTone,
                    ["哲学母题"] = () => FormPhilosophicalMotif,
                    ["主题思想"] = () => FormTheme,
                    ["核心冲突"] = () => FormCoreConflict,
                    ["结局/目标状态"] = () => FormEndingState,
                    ["卷/幕划分"] = () => FormVolumeDivision,
                    ["大纲总览"] = () => FormOutlineOverview,
                },
                ContextProvider = async () => await GetEnhancedOutlineContextAsync(),
                BatchFieldKeyMap = new()
                {
                    ["总章节数"] = "TotalChapterCount",
                    ["预计总字数"] = "EstimatedWordCount",
                    ["一句话大纲"] = "OneLineOutline",
                    ["情感基调"] = "EmotionalTone",
                    ["哲学母题"] = "PhilosophicalMotif",
                    ["主题思想"] = "Theme",
                    ["核心冲突"] = "CoreConflict",
                    ["结局/目标状态"] = "EndingState",
                    ["卷/幕划分"] = "VolumeDivision",
                    ["大纲总览"] = "OutlineOverview"
                },
                BatchIndexFields = new() { "Name", "OneLineOutline", "Theme" }
            };
        }

        private async System.Threading.Tasks.Task<string> GetEnhancedOutlineContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetOutlineContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }

            try
            {
                var volumeList = await _contextService.GetVolumeDesignListAsync();
                if (!string.IsNullOrWhiteSpace(volumeList) && volumeList.Contains("<item"))
                {
                    sb.AppendLine("<volume_structure_reference mandatory=\"true\">");
                    sb.AppendLine("以下是用户已规划的分卷结构，大纲总览必须与此结构完全一致，不得另起一套章节规划：");
                    sb.AppendLine(volumeList);
                    sb.AppendLine("</volume_structure_reference>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OutlineViewModel] 读取分卷结构失败: {ex.Message}");
            }

            if (int.TryParse(FormTotalChapterCount?.Trim(), out var userTotalChapters) && userTotalChapters > 0)
            {
                sb.AppendLine($"<chapter_count_constraint count=\"{userTotalChapters}\" mandatory=\"true\">");
                sb.AppendLine($"用户已设定全书总章节数为{userTotalChapters}章。各卷章节数之和必须严格等于{userTotalChapters}，不得使用'约X章'等模糊表达。");
                sb.AppendLine("</chapter_count_constraint>");
                sb.AppendLine();
            }

            try
            {
                await _plotRulesService.InitializeAsync();
                var plotRules = _plotRulesService.GetAllPlotRules()
                    .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.TargetVolume))
                    .ToList();

                var totalVolumes = plotRules
                    .Select(p => { int.TryParse(p.TargetVolume?.Trim(), out var n); return n; })
                    .Where(n => n > 0)
                    .DefaultIfEmpty(0)
                    .Max();

                if (totalVolumes > 0)
                {
                    sb.AppendLine($"<volume_count_constraint count=\"{totalVolumes}\">");
                    sb.AppendLine($"剧情规则已设定全书共{totalVolumes}卷，卷/幕划分必须严格按照{totalVolumes}卷来规划，不得多于或少于此数。");
                    sb.AppendLine("</volume_count_constraint>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OutlineViewModel] 读取总卷数失败: {ex.Message}");
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「总章节数」必须为整数（仅数字），代表全书计划写的总章节数量。");
            sb.AppendLine("2. 「卷/幕划分」「大纲总览」如有多条，请在字符串内用换行分条。");
            sb.AppendLine("3. 若已给出总卷数约束，卷/幕划分必须与总卷数一致，不得多于或少于。");
            sb.AppendLine("4. 避免编造未在上下文中出现的专有名词；如需新增，请在对应字段中先定义其含义。");
            sb.AppendLine("5. 若 volume_structure_reference 中已有各卷章节范围，大纲总览叙述的章节分配必须与之完全一致，不得另行分配章节数量。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            return sb.ToString();
        }

        protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            var isValidTotalChapters = int.TryParse(FormTotalChapterCount?.Trim(), out var n) && n > 0;
            IsAIGenerateEnabled = hasSelection && isValidTotalChapters;
        }

        protected override bool IsBatchGenerationDisabledForCurrentModule() => true;

        protected override bool CanExecuteAIGenerate()
        {
            if (!base.CanExecuteAIGenerate()) return false;
            return int.TryParse(FormTotalChapterCount?.Trim(), out var n) && n > 0;
        }

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllOutlines().Select(r => r.Name);
        protected override int GetDefaultBatchSize() => 1;
        protected override int GetDefaultTotalCount() => 3;

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllOutlines().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in entities)
            {
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    var name = reader.GetString("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"大纲_{DateTime.Now:HHmmss}_{result.Count + 1}";

                    var baseName = name;

                    if (dbNames.Contains(baseName))
                    {
                        TM.App.Log($"[OutlineViewModel] 跳过已存在大纲: {baseName}");
                        continue;
                    }

                    int suffix = 1;
                    while (batchNames.Contains(name))
                    {
                        name = $"{baseName}_{suffix++}";
                    }
                    batchNames.Add(name);
                    dbNames.Add(name);

                    var data = new OutlineData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = name,
                        Category = categoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        TotalChapterCount = int.TryParse(FormTotalChapterCount?.Trim(), out var userTc) && userTc > 0
                            ? userTc
                            : reader.GetInt("TotalChapterCount"),
                        EstimatedWordCount = string.IsNullOrWhiteSpace(FormEstimatedWordCount)
                            ? reader.GetString("EstimatedWordCount")
                            : FormEstimatedWordCount,
                        OneLineOutline = reader.GetString("OneLineOutline"),
                        EmotionalTone = reader.GetString("EmotionalTone"),
                        PhilosophicalMotif = reader.GetString("PhilosophicalMotif"),
                        Theme = reader.GetString("Theme"),
                        CoreConflict = reader.GetString("CoreConflict"),
                        EndingState = reader.GetString("EndingState"),
                        VolumeDivision = reader.GetString("VolumeDivision"),
                        OutlineOverview = reader.GetString("OutlineOverview"),
                        DependencyModuleVersions = versionSnapshot ?? new()
                    };

                    entity["Name"] = name;
                    await Service.AddOutlineAsync(data);
                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[OutlineViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[OutlineViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        protected override string GetModuleNameForVersionTracking() => "Outline";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateOutline(_currentEditingData);
        }
    }
}
