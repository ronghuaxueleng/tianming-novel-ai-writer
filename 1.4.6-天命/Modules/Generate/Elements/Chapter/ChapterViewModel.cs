using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Framework.Common.Models;

namespace TM.Modules.Generate.Elements.Chapter
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterViewModel : DataManagementViewModelBase<ChapterData, ChapterCategory, ChapterService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly IWorkScopeService _workScopeService;

        public ChapterViewModel(IPromptRepository promptRepository, ContextService contextService, VolumeDesignService volumeDesignService, IWorkScopeService workScopeService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _volumeDesignService = volumeDesignService;
            _workScopeService = workScopeService;

            try
            {
                System.Windows.WeakEventManager<VolumeDesignService, EventArgs>.AddHandler(
                    _volumeDesignService,
                    nameof(VolumeDesignService.DataChanged),
                    OnVolumeDataChanged);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 订阅 VolumeDesignService.DataChanged 失败: {ex.Message}");
            }
        }
        private string _formName = string.Empty;
        private string _formIcon = "📑";
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

        private string _formChapterTitle = string.Empty;
        private int _formChapterNumber = 0;
        private string _formVolume = string.Empty;
        private string _formEstimatedWordCount = string.Empty;
        private string _formChapterTheme = string.Empty;
        private string _formReaderExperienceGoal = string.Empty;
        private string _formMainGoal = string.Empty;

        public string FormChapterTitle { get => _formChapterTitle; set { _formChapterTitle = value; OnPropertyChanged(); } }
        public int FormChapterNumber { get => _formChapterNumber; set { _formChapterNumber = value; OnPropertyChanged(); } }
        public string FormVolume { get => _formVolume; set { _formVolume = value; OnPropertyChanged(); } }
        public string FormEstimatedWordCount { get => _formEstimatedWordCount; set { _formEstimatedWordCount = value; OnPropertyChanged(); } }
        public string FormChapterTheme { get => _formChapterTheme; set { _formChapterTheme = value; OnPropertyChanged(); } }
        public string FormReaderExperienceGoal { get => _formReaderExperienceGoal; set { _formReaderExperienceGoal = value; OnPropertyChanged(); } }
        public string FormMainGoal { get => _formMainGoal; set { _formMainGoal = value; OnPropertyChanged(); } }

        private string _formResistanceSource = string.Empty;
        private string _formKeyTurn = string.Empty;
        private string _formHook = string.Empty;

        public string FormResistanceSource { get => _formResistanceSource; set { _formResistanceSource = value; OnPropertyChanged(); } }
        public string FormKeyTurn { get => _formKeyTurn; set { _formKeyTurn = value; OnPropertyChanged(); } }
        public string FormHook { get => _formHook; set { _formHook = value; OnPropertyChanged(); } }

        private string _formWorldInfoDrop = string.Empty;
        private string _formCharacterArcProgress = string.Empty;
        private string _formMainPlotProgress = string.Empty;
        private string _formForeshadowing = string.Empty;

        public string FormWorldInfoDrop { get => _formWorldInfoDrop; set { _formWorldInfoDrop = value; OnPropertyChanged(); } }
        public string FormCharacterArcProgress { get => _formCharacterArcProgress; set { _formCharacterArcProgress = value; OnPropertyChanged(); } }
        public string FormMainPlotProgress { get => _formMainPlotProgress; set { _formMainPlotProgress = value; OnPropertyChanged(); } }
        public string FormForeshadowing { get => _formForeshadowing; set { _formForeshadowing = value; OnPropertyChanged(); } }

        private string _formReferencedCharacterNames = string.Empty;
        private string _formReferencedFactionNames = string.Empty;
        private string _formReferencedLocationNames = string.Empty;

        public string FormReferencedCharacterNames { get => _formReferencedCharacterNames; set { _formReferencedCharacterNames = value; OnPropertyChanged(); } }
        public string FormReferencedFactionNames { get => _formReferencedFactionNames; set { _formReferencedFactionNames = value; OnPropertyChanged(); } }
        public string FormReferencedLocationNames { get => _formReferencedLocationNames; set { _formReferencedLocationNames = value; OnPropertyChanged(); } }

        private static string ToCommaSeparated(List<string> list)
            => list == null ? string.Empty : string.Join("、", list.Where(s => !string.IsNullOrWhiteSpace(s)));

        private static List<string> FromCommaSeparated(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        public System.Collections.ObjectModel.ObservableCollection<string> AvailableCharacters { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> AvailableFactions   { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<string> AvailableLocations  { get; } = new();

        private void RefreshEntityPool(string volumeCategory)
        {
            AvailableCharacters.Clear();
            AvailableFactions.Clear();
            AvailableLocations.Clear();
            if (string.IsNullOrWhiteSpace(volumeCategory)) return;
            try
            {
                var scope = _workScopeService.CurrentSourceBookId;
                var volume = _volumeDesignService.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.IsEnabled
                        && (string.IsNullOrEmpty(scope) || string.Equals(v.SourceBookId, scope, StringComparison.Ordinal))
                        && (string.Equals(v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name, volumeCategory, StringComparison.Ordinal)
                            || string.Equals(v.Name, volumeCategory, StringComparison.Ordinal)));
                if (volume == null) return;
                foreach (var n in volume.ReferencedCharacterNames.Where(s => !string.IsNullOrWhiteSpace(s))) AvailableCharacters.Add(n);
                foreach (var n in volume.ReferencedFactionNames.Where(s => !string.IsNullOrWhiteSpace(s)))   AvailableFactions.Add(n);
                foreach (var n in volume.ReferencedLocationNames.Where(s => !string.IsNullOrWhiteSpace(s)))  AvailableLocations.Add(n);
            }
            catch (Exception ex) { TM.App.Log($"[ChapterViewModel] 刷新实体池失败: {ex.Message}"); }
        }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        protected override string DefaultDataIcon => "📑";

        protected override ChapterData? CreateNewData(string? categoryName = null)
        {
            return new ChapterData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新章节",
                Category = categoryName ?? string.Empty,
                Volume = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
            FormVolume = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllChapters();

        protected override List<ChapterCategory> GetAllCategoriesFromService()
        {
            return Service.GetAllCategories();
        }

        protected override List<ChapterData> GetAllDataItems()
            => Service.GetAllChapters().OrderBy(c => c.ChapterNumber).ToList();

        protected override string GetDataCategory(ChapterData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(ChapterData data)
        {
            var title = NormalizeChapterTitle(data.ChapterTitle);
            return new TreeNodeItem
            {
                Name = $"第{data.ChapterNumber}章 {title}",
                Icon = "📑",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(ChapterData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ChapterTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.MainGoal.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.ChapterTheme.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: ChapterData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: ChapterCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(ChapterData data)
        {
            FormName = data.Name;
            FormIcon = "📑";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormChapterTitle = data.ChapterTitle;
            FormChapterNumber = data.ChapterNumber;
            FormVolume = data.Volume;
            FormEstimatedWordCount = data.EstimatedWordCount;
            FormChapterTheme = data.ChapterTheme;
            FormReaderExperienceGoal = data.ReaderExperienceGoal;
            FormMainGoal = data.MainGoal;

            FormResistanceSource = data.ResistanceSource;
            FormKeyTurn = data.KeyTurn;
            FormHook = data.Hook;

            FormWorldInfoDrop = data.WorldInfoDrop;
            FormCharacterArcProgress = data.CharacterArcProgress;
            FormMainPlotProgress = data.MainPlotProgress;
            FormForeshadowing = data.Foreshadowing;

            FormReferencedCharacterNames = ToCommaSeparated(data.ReferencedCharacterNames);
            FormReferencedFactionNames   = ToCommaSeparated(data.ReferencedFactionNames);
            FormReferencedLocationNames  = ToCommaSeparated(data.ReferencedLocationNames);
            RefreshEntityPool(data.Category);
        }

        private void LoadCategoryToForm(ChapterCategory category)
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
            FormChapterTitle = string.Empty;
            FormChapterNumber = 0;
            FormVolume = string.Empty;
            FormEstimatedWordCount = string.Empty;
            FormChapterTheme = string.Empty;
            FormReaderExperienceGoal = string.Empty;
            FormMainGoal = string.Empty;

            FormResistanceSource = string.Empty;
            FormKeyTurn = string.Empty;
            FormHook = string.Empty;

            FormWorldInfoDrop = string.Empty;
            FormCharacterArcProgress = string.Empty;
            FormMainPlotProgress = string.Empty;
            FormForeshadowing = string.Empty;

            FormReferencedCharacterNames = string.Empty;
            FormReferencedFactionNames   = string.Empty;
            FormReferencedLocationNames  = string.Empty;
        }

        protected override string NewItemTypeName => "章节";
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
                TM.App.Log($"[ChapterViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[ChapterViewModel] 保存失败: {ex.Message}");
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
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或章节");
                return false;
            }

            return true;
        }

        private System.Threading.Tasks.Task CreateCategoryCoreAsync()
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），选中任意数据项保存即为全量保存");
            return System.Threading.Tasks.Task.CompletedTask;
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
            await Service.AddChapterAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"章节『{newData.ChapterTitle}』已创建");
        }

        private System.Threading.Tasks.Task UpdateCategoryCoreAsync()
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），选中任意数据项保存即为全量保存");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task UpdateDataCoreAsync()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            await Service.UpdateChapterAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"章节『{_currentEditingData.ChapterTitle}』已更新");
        }

        private void UpdateDataFromForm(ChapterData data)
        {
            data.Name = FormName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.ChapterTitle = NormalizeChapterTitle(FormChapterTitle);
            data.ChapterNumber = FormChapterNumber;
            data.Volume = FormCategory;
            data.EstimatedWordCount = FormEstimatedWordCount;
            data.ChapterTheme = FormChapterTheme;
            data.ReaderExperienceGoal = FormReaderExperienceGoal;
            data.MainGoal = FormMainGoal;

            data.ResistanceSource = FormResistanceSource;
            data.KeyTurn = FormKeyTurn;
            data.Hook = FormHook;

            data.WorldInfoDrop = FormWorldInfoDrop;
            data.CharacterArcProgress = FormCharacterArcProgress;
            data.MainPlotProgress = FormMainPlotProgress;
            data.Foreshadowing = FormForeshadowing;

            data.ReferencedCharacterNames = FromCommaSeparated(FormReferencedCharacterNames);
            data.ReferencedFactionNames   = FromCommaSeparated(FormReferencedFactionNames);
            data.ReferencedLocationNames  = FromCommaSeparated(FormReferencedLocationNames);
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                if (_currentEditingCategory != null)
                {
                    GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
                    return;
                }
                else if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除章节『{_currentEditingData.ChapterTitle}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteChapter(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"章节『{_currentEditingData.ChapterTitle}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或章节");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 删除失败: {ex.Message}");
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
                MessagePrefix = "章节规划",
                ProgressMessage = "正在规划章节...",
                CompleteMessage = "章节规划完成",
                InputVariables = new()
                {
                    ["章节标题"] = () => FormChapterTitle,
                    ["大纲名称"] = () => string.Empty,
                    ["场景标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["章节标题"] = v => FormChapterTitle = v,
                    ["章节目标"]     = v => { if (string.IsNullOrWhiteSpace(FormMainGoal))             FormMainGoal             = v; },
                    ["章节主题"]     = v => { if (string.IsNullOrWhiteSpace(FormChapterTheme))         FormChapterTheme         = v; },
                    ["读者体验目标"] = v => { if (string.IsNullOrWhiteSpace(FormReaderExperienceGoal)) FormReaderExperienceGoal = v; },
                    ["预估字数"]     = v => FormEstimatedWordCount = v,
                    ["阻力来源"] = v => { if (string.IsNullOrWhiteSpace(FormResistanceSource)) FormResistanceSource = v; },
                    ["关键转折"] = v => { if (string.IsNullOrWhiteSpace(FormKeyTurn))          FormKeyTurn          = v; },
                    ["钩子"]     = v => { if (string.IsNullOrWhiteSpace(FormHook))             FormHook             = v; },
                    ["世界信息释放"] = v => { if (string.IsNullOrWhiteSpace(FormWorldInfoDrop))       FormWorldInfoDrop       = v; },
                    ["角色弧推进"]   = v => { if (string.IsNullOrWhiteSpace(FormCharacterArcProgress)) FormCharacterArcProgress = v; },
                    ["主线推进"]     = v => { if (string.IsNullOrWhiteSpace(FormMainPlotProgress))    FormMainPlotProgress    = v; },
                    ["伏笔"]         = v => { if (string.IsNullOrWhiteSpace(FormForeshadowing))       FormForeshadowing       = v; },
                    ["出场角色"] = v => FormReferencedCharacterNames = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及势力"] = v => FormReferencedFactionNames   = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["涉及地点"] = v => FormReferencedLocationNames  = FilterToCandidatesOrRaw(v, AvailableLocations),
                },
                OutputFieldGetters = new()
                {
                    ["章节标题"] = () => FormChapterTitle,
                    ["章节目标"] = () => FormMainGoal,
                    ["章节主题"] = () => FormChapterTheme,
                    ["读者体验目标"] = () => FormReaderExperienceGoal,
                    ["预估字数"] = () => FormEstimatedWordCount,
                    ["阻力来源"] = () => FormResistanceSource,
                    ["关键转折"] = () => FormKeyTurn,
                    ["钩子"] = () => FormHook,
                    ["世界信息释放"] = () => FormWorldInfoDrop,
                    ["角色弧推进"] = () => FormCharacterArcProgress,
                    ["主线推进"] = () => FormMainPlotProgress,
                    ["伏笔"] = () => FormForeshadowing,
                    ["出场角色"] = () => FormReferencedCharacterNames,
                    ["涉及势力"] = () => FormReferencedFactionNames,
                    ["涉及地点"] = () => FormReferencedLocationNames,
                },
                ContextProvider = async () =>
                {
                    var sb = new System.Text.StringBuilder();

                    if (AvailableCharacters.Count == 0 && AvailableFactions.Count == 0 && AvailableLocations.Count == 0
                        && !string.IsNullOrWhiteSpace(FormCategory))
                        RefreshEntityPool(FormCategory);

                    var baseContext = !string.IsNullOrWhiteSpace(FormCategory)
                        ? await _contextService.GetChapterContextWithVolumeLocatorAsync(FormCategory)
                        : await _contextService.GetChapterContextStringAsync();

                    if (!string.IsNullOrWhiteSpace(baseContext))
                    {
                        sb.AppendLine(baseContext);
                        sb.AppendLine();
                    }

                    if (AvailableCharacters.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选角色", AvailableCharacters, "「出场角色」必须从以下列表中选择，不得编造"));
                    if (AvailableFactions.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选势力", AvailableFactions, "「涉及势力」必须从以下列表中选择，不得编造"));
                    if (AvailableLocations.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选地点", AvailableLocations, "「涉及地点」必须从以下列表中选择，不得编造"));

                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「章节编号」由系统按批次顺序分配（见 chapter_assignments），AI不应生成此字段。");
                    sb.AppendLine("2. 「章节标题」不要带\"第X章\"前缀（系统会自动规范化标题）。");
                    sb.AppendLine("3. 「关键转折」「伏笔」「世界信息释放」如有多条，请在字符串内用换行分条。");
                    sb.AppendLine("4. 「出场角色」「涉及势力」「涉及地点」必须从上方可选列表中选择，逗号/顿号分隔。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                SequenceFieldName = "ChapterNumber",
                GetCurrentMaxSequence = (scopeId, categoryName) => Service.GetAllChapters()
                    .Where(c => string.IsNullOrEmpty(scopeId) || string.Equals(c.SourceBookId, scopeId, StringComparison.Ordinal))
                    .Where(c => string.IsNullOrEmpty(categoryName) || string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Select(c => c.ChapterNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = new()
                {
                    ["章节目标"] = "MainGoal",
                    ["章节主题"] = "ChapterTheme",
                    ["读者体验目标"] = "ReaderExperienceGoal",
                    ["预估字数"] = "EstimatedWordCount",
                    ["阻力来源"] = "ResistanceSource",
                    ["关键转折"] = "KeyTurn",
                    ["钩子"] = "Hook",
                    ["世界信息释放"] = "WorldInfoDrop",
                    ["角色弧推进"] = "CharacterArcProgress",
                    ["主线推进"] = "MainPlotProgress",
                    ["伏笔"] = "Foreshadowing",
                    ["章节标题"] = "ChapterTitle",
                    ["出场角色"] = "ReferencedCharacterNames",
                    ["涉及势力"] = "ReferencedFactionNames",
                    ["涉及地点"] = "ReferencedLocationNames"
                },
                BatchIndexFields = new() { "ChapterNumber", "ChapterTitle", "KeyTurn", "Hook" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override void OnBatchGenerationFailed(int failedCount)
        {
            if (_currentBatchChapterNumbers?.Count > 0)
            {
                _batchChapterIndex = Math.Max(0, _batchChapterIndex - _currentBatchChapterNumbers.Count);
                TM.App.Log($"[ChapterViewModel] 批次失败，回退章节索引至 {_batchChapterIndex}");
            }
        }

        private List<int>? _batchFullChapterRange;
        private List<int>? _batchPreCalculatedChapterNumbers;
        private int _batchChapterIndex;
        private List<int>? _currentBatchChapterNumbers;

        protected override GenerationRange? GetNextGenerationRange(string? scopeId, string categoryName, int requestedCount)
        {
            if (_batchPreCalculatedChapterNumbers != null && _batchPreCalculatedChapterNumbers.Count > 0)
            {
                var take = Math.Min(requestedCount, _batchPreCalculatedChapterNumbers.Count - _batchChapterIndex);
                if (take > 0)
                {
                    _currentBatchChapterNumbers = _batchPreCalculatedChapterNumbers
                        .Skip(_batchChapterIndex)
                        .Take(take)
                        .ToList();
                    _batchChapterIndex += take;
                }
                else
                {
                    _currentBatchChapterNumbers = null;
                }
                return null;
            }
            _currentBatchChapterNumbers = null;
            return base.GetNextGenerationRange(scopeId, categoryName, requestedCount);
        }

        protected override async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prompt) && _currentBatchChapterNumbers?.Count > 0)
            {
                var sb = new System.Text.StringBuilder(prompt);
                sb.AppendLine();
                sb.AppendLine("<chapter_assignments mandatory=\"true\">");
                sb.AppendLine($"本批生成任务（输出数组长度必须 = {_currentBatchChapterNumbers.Count}，第i项对应第 i 个章节号）：");
                sb.AppendLine(string.Join("、", _currentBatchChapterNumbers.Select(n => $"第{n}章")));
                sb.AppendLine("要求：本批每个对象的 Name/ChapterTitle 必须各不相同，且标题需体现本章核心事件，禁止使用\"第X章\"前缀。");
                sb.AppendLine("</chapter_assignments>");
                return sb.ToString();
            }
            return prompt;
        }

        protected override async System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            var currentScopeId = _workScopeService.CurrentSourceBookId;

            try
            {
                await _volumeDesignService.InitializeAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 初始化分卷服务失败: {ex.Message}");
            }

            var volume = _volumeDesignService.GetAllVolumeDesigns()
                .FirstOrDefault(v => v.IsEnabled
                    && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal))
                    && (string.Equals((v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name), categoryName, StringComparison.Ordinal)
                        || string.Equals(v.Name, categoryName, StringComparison.Ordinal)));

            if (volume == null || volume.StartChapter <= 0 || volume.EndChapter <= 0 || volume.TargetChapterCount <= 0)
            {
                GlobalToast.Warning("前置条件不满足", "请先在分卷设计中执行批量生成，确保本卷有章节范围（StartChapter/EndChapter）");
                return null;
            }

            _batchFullChapterRange = Enumerable.Range(volume.StartChapter, volume.TargetChapterCount).ToList();

            var existingWithContent = Service.GetAllChapters()
                .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(c.ChapterTheme)
                    && !string.IsNullOrWhiteSpace(NormalizeChapterTitle(c.ChapterTitle)))
                .Select(c => c.ChapterNumber)
                .ToHashSet();

            var alreadyCompleted = _batchFullChapterRange.Count(n => existingWithContent.Contains(n));
            _batchPreCalculatedChapterNumbers = _batchFullChapterRange
                .Where(n => !existingWithContent.Contains(n))
                .ToList();
            _batchChapterIndex = 0;

            if (_batchPreCalculatedChapterNumbers.Count == 0)
            {
                GlobalToast.Info("已全部完成", $"本卷 {volume.TargetChapterCount} 章均已有AI内容，无需重新生成");
                _batchFullChapterRange = null;
                _batchPreCalculatedChapterNumbers = null;
                return null;
            }

            string msg;
            if (alreadyCompleted > 0)
            {
                msg = $"即将对「{categoryName}」继续执行 AI 批量重建章节设计：\n\n"
                    + $"• 章节范围：第 {volume.StartChapter} 章 ~ 第 {volume.EndChapter} 章\n"
                    + $"• 已完成：{alreadyCompleted} 章（跳过）\n"
                    + $"• 待生成：{_batchPreCalculatedChapterNumbers.Count} 章\n"
                    + "• 仅展示本卷起止章节，不逐章展开\n\n"
                    + "确认继续生成？";
            }
            else
            {
                msg = $"即将对「{categoryName}」执行 AI 批量重建章节设计：\n\n"
                    + $"• 章节数量：共 {volume.TargetChapterCount} 章\n"
                    + $"• 章节范围：第 {volume.StartChapter} 章 ~ 第 {volume.EndChapter} 章\n"
                    + "• 仅展示本卷起止章节，不逐章展开\n"
                    + $"• 超出范围的旧章节数据将被自动清理\n\n"
                    + "确认开始生成？";
            }

            var confirmed = StandardDialog.ShowConfirm(msg, "批量重建章节设计");
            if (!confirmed)
            {
                _batchFullChapterRange = null;
                _batchPreCalculatedChapterNumbers = null;
                return null;
            }

            return new BatchGenerationConfig
            {
                CategoryName = categoryName,
                TotalCount = _batchPreCalculatedChapterNumbers.Count,
                BatchSize = 15
            };
        }

        protected override async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            var fullRange = _batchFullChapterRange;
            if (fullRange == null || fullRange.Count == 0) return;

            var currentScopeId = _workScopeService.CurrentSourceBookId;
            var validSet = new HashSet<int>(fullRange);

            var tail = Service.GetAllChapters()
                .Where(c => string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal)
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal)))
                .Where(c => !validSet.Contains(c.ChapterNumber))
                .ToList();

            foreach (var c in tail)
            {
                Service.DeleteChapter(c.Id);
                TM.App.Log($"[ChapterViewModel] 清尾: 删除第{c.ChapterNumber}章（不在有效范围内）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[ChapterViewModel] 清尾完成: 删除 {tail.Count} 个旧章节");

            if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllChapters()
                    .Where(c => string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal)
                        && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal))
                        && string.IsNullOrWhiteSpace(NormalizeChapterTitle(c.ChapterTitle))
                        && string.IsNullOrWhiteSpace(c.ChapterTheme)
                        && string.IsNullOrWhiteSpace(c.ReaderExperienceGoal)
                        && string.IsNullOrWhiteSpace(c.MainGoal)
                        && string.IsNullOrWhiteSpace(c.ResistanceSource)
                        && string.IsNullOrWhiteSpace(c.KeyTurn)
                        && string.IsNullOrWhiteSpace(c.Hook)
                        && string.IsNullOrWhiteSpace(c.WorldInfoDrop)
                        && string.IsNullOrWhiteSpace(c.CharacterArcProgress)
                        && string.IsNullOrWhiteSpace(c.MainPlotProgress)
                        && string.IsNullOrWhiteSpace(c.Foreshadowing))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteChapter(shell.Id);
                    TM.App.Log($"[ChapterViewModel] 取消清理: 删除空壳第{shell.ChapterNumber}章");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳章节，下次批量生成会按需续接");
            }
            else
            {
                foreach (var chNum in fullRange)
                {
                    var existing = Service.GetAllChapters()
                        .FirstOrDefault(c => c.ChapterNumber == chNum
                            && string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal)
                            && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                    if (existing == null)
                    {
                        var data = new ChapterData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = $"第{chNum}章",
                            Category = config.CategoryName,
                            Volume = config.CategoryName,
                            IsEnabled = true,
                            SourceBookId = currentScopeId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterNumber = chNum,
                            ChapterTitle = $"第{chNum}章",
                        };
                        await Service.AddChapterAsync(data);
                        TM.App.Log($"[ChapterViewModel] 补缺: 第{chNum}章（AI失败，创建占位）");
                    }
                }
            }

            _batchFullChapterRange = null;
            _batchPreCalculatedChapterNumbers = null;
            _currentBatchChapterNumbers = null;

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
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);

                    int chapterNumber;
                    var entityIndex = entities.IndexOf(entity);
                    if (_currentBatchChapterNumbers != null && entityIndex >= 0 && entityIndex < _currentBatchChapterNumbers.Count)
                    {
                        chapterNumber = _currentBatchChapterNumbers[entityIndex];
                    }
                    else
                    {
                        chapterNumber = reader.GetInt("ChapterNumber");
                    }

                    var existing = Service.GetAllChapters()
                        .FirstOrDefault(c => c.ChapterNumber == chapterNumber
                            && string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                            && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                    if (existing != null)
                    {
                        var aiName = reader.GetString("Name");
                        var aiTitle = reader.GetString("ChapterTitle");
                        var normalizedTitle = NormalizeChapterTitle(aiTitle);
                        var normalizedName = NormalizeChapterTitle(aiName);
                        if (!string.IsNullOrWhiteSpace(normalizedTitle))
                            existing.ChapterTitle = normalizedTitle;
                        else if (!string.IsNullOrWhiteSpace(normalizedName))
                            existing.ChapterTitle = normalizedName;
                        if (!string.IsNullOrWhiteSpace(normalizedName))
                            existing.Name = normalizedName;
                        else if (!string.IsNullOrWhiteSpace(normalizedTitle))
                            existing.Name = normalizedTitle;
                        var aiEwc = reader.GetString("EstimatedWordCount");
                        if (!string.IsNullOrWhiteSpace(aiEwc)) existing.EstimatedWordCount = aiEwc;
                        var aiTheme = reader.GetString("ChapterTheme");
                        if (!string.IsNullOrWhiteSpace(aiTheme)) existing.ChapterTheme = aiTheme;
                        var aiReg = reader.GetString("ReaderExperienceGoal");
                        if (!string.IsNullOrWhiteSpace(aiReg)) existing.ReaderExperienceGoal = aiReg;
                        var aiGoal = reader.GetString("MainGoal");
                        if (!string.IsNullOrWhiteSpace(aiGoal)) existing.MainGoal = aiGoal;
                        var aiRes = reader.GetString("ResistanceSource");
                        if (!string.IsNullOrWhiteSpace(aiRes)) existing.ResistanceSource = aiRes;
                        var aiKey = reader.GetString("KeyTurn");
                        if (!string.IsNullOrWhiteSpace(aiKey)) existing.KeyTurn = aiKey;
                        var aiHook = reader.GetString("Hook");
                        if (!string.IsNullOrWhiteSpace(aiHook)) existing.Hook = aiHook;
                        var aiWid = reader.GetString("WorldInfoDrop");
                        if (!string.IsNullOrWhiteSpace(aiWid)) existing.WorldInfoDrop = aiWid;
                        var aiCap = reader.GetString("CharacterArcProgress");
                        if (!string.IsNullOrWhiteSpace(aiCap)) existing.CharacterArcProgress = aiCap;
                        var aiMpp = reader.GetString("MainPlotProgress");
                        if (!string.IsNullOrWhiteSpace(aiMpp)) existing.MainPlotProgress = aiMpp;
                        var aiFsh = reader.GetString("Foreshadowing");
                        if (!string.IsNullOrWhiteSpace(aiFsh)) existing.Foreshadowing = aiFsh;

                        var aiChars = reader.GetString("ReferencedCharacterNames");
                        if (!string.IsNullOrWhiteSpace(aiChars) && existing.ReferencedCharacterNames.Count == 0)
                            existing.ReferencedCharacterNames = FromCommaSeparated(aiChars);
                        var aiLocs = reader.GetString("ReferencedLocationNames");
                        if (!string.IsNullOrWhiteSpace(aiLocs) && existing.ReferencedLocationNames.Count == 0)
                            existing.ReferencedLocationNames = FromCommaSeparated(aiLocs);
                        var aiFacs = reader.GetString("ReferencedFactionNames");
                        if (!string.IsNullOrWhiteSpace(aiFacs) && existing.ReferencedFactionNames.Count == 0)
                            existing.ReferencedFactionNames = FromCommaSeparated(aiFacs);

                        existing.ChapterNumber = chapterNumber;
                        existing.Volume = categoryName;
                        existing.DependencyModuleVersions = versionSnapshot ?? new();
                        existing.UpdatedAt = DateTime.Now;
                        await Service.UpdateChapterAsync(existing);
                        entity["ChapterNumber"] = chapterNumber;
                        TM.App.Log($"[ChapterViewModel] Upsert更新: 第{chapterNumber}章");
                    }
                    else
                    {
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name)) name = $"第{chapterNumber}章";
                        var title = reader.GetString("ChapterTitle");
                        if (string.IsNullOrWhiteSpace(title)) title = name;
                        var normalizedTitle = NormalizeChapterTitle(title);
                        var normalizedName = NormalizeChapterTitle(name);
                        var finalTitle = !string.IsNullOrWhiteSpace(normalizedTitle) ? normalizedTitle : normalizedName;
                        var finalName = !string.IsNullOrWhiteSpace(normalizedName) ? normalizedName : normalizedTitle;

                        var data = new ChapterData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = finalName,
                            Category = categoryName,
                            IsEnabled = true,
                            SourceBookId = currentScopeId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterNumber = chapterNumber,
                            Volume = categoryName,
                            ChapterTitle = finalTitle,
                            EstimatedWordCount = reader.GetString("EstimatedWordCount"),
                            ChapterTheme = reader.GetString("ChapterTheme"),
                            ReaderExperienceGoal = reader.GetString("ReaderExperienceGoal"),
                            MainGoal = reader.GetString("MainGoal"),
                            ResistanceSource = reader.GetString("ResistanceSource"),
                            KeyTurn = reader.GetString("KeyTurn"),
                            Hook = reader.GetString("Hook"),
                            WorldInfoDrop = reader.GetString("WorldInfoDrop"),
                            CharacterArcProgress = reader.GetString("CharacterArcProgress"),
                            MainPlotProgress = reader.GetString("MainPlotProgress"),
                            Foreshadowing = reader.GetString("Foreshadowing"),
                            ReferencedCharacterNames = FromCommaSeparated(reader.GetString("ReferencedCharacterNames")),
                            ReferencedFactionNames   = FromCommaSeparated(reader.GetString("ReferencedFactionNames")),
                            ReferencedLocationNames  = FromCommaSeparated(reader.GetString("ReferencedLocationNames")),
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };
                        entity["ChapterNumber"] = chapterNumber;
                        entity["ChapterTitle"] = finalTitle;
                        await Service.AddChapterAsync(data);
                        TM.App.Log($"[ChapterViewModel] Upsert新建: 第{chapterNumber}章");
                    }

                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ChapterViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[ChapterViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        private static string NormalizeChapterTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = Regex.Replace(t, "^.{0,30}?[-_—–\\s]+(?=第\\s*[\\d一二三四五六七八九十百千零]+\\s*章)", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*\\d+\\s*章\\s*[：:、\\-—–_]*\\s*", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*[一二三四五六七八九十百千零]+\\s*章\\s*[：:、\\-—–_]*\\s*", string.Empty);
            return t.Trim();
        }

        protected override void OnTreeAfterAction(string? action)
        {
            if (action == "Reorder")
            {
                return;
            }

            base.OnTreeAfterAction(action);
        }

        private void OnVolumeDataChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RefreshTreeAndCategorySelection();
                    UpdateBulkToggleState();

                    if (!string.IsNullOrWhiteSpace(FormCategory))
                    {
                        var categories = GetAllCategoriesFromService() ?? new List<ChapterCategory>();
                        if (!categories.Any(c => string.Equals(c.Name, FormCategory, StringComparison.Ordinal)))
                        {
                            FormCategory = string.Empty;
                            FormVolume = string.Empty;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 同步分卷数据变更失败: {ex.Message}");
            }
        }

        protected override string GetModuleNameForVersionTracking() => "Chapter";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateChapter(_currentEditingData);
        }
    }
}
