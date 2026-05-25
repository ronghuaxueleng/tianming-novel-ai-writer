using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Framework.Common.Models;

namespace TM.Modules.Generate.Elements.Blueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class BlueprintViewModel : DataManagementViewModelBase<BlueprintData, BlueprintCategory, BlueprintService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterService;
        private readonly LocationRulesService _locationService;
        private readonly FactionRulesService _factionService;
        private readonly ChapterService _chapterService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly IWorkScopeService _workScopeService;
        private string _formName = string.Empty;
        private string _formIcon = "🎬";
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
                    ReloadAvailableChapterIds();
                }
            }
        }

        private string _formChapterId = string.Empty;
        private string _formOneLineStructure = string.Empty;
        private string _formPacingCurve = string.Empty;

        public string FormChapterId { get => _formChapterId; set { _formChapterId = value; OnPropertyChanged(); } }
        public string FormOneLineStructure { get => _formOneLineStructure; set { _formOneLineStructure = value; OnPropertyChanged(); } }
        public string FormPacingCurve { get => _formPacingCurve; set { _formPacingCurve = value; OnPropertyChanged(); } }

        private int _formSceneNumber = 0;
        private string _formSceneTitle = string.Empty;
        private string _formPovCharacter = string.Empty;
        private string _formEstimatedWordCount = string.Empty;
        private string _formOpening = string.Empty;
        private string _formDevelopment = string.Empty;
        private string _formTurning = string.Empty;
        private string _formEnding = string.Empty;
        private string _formInfoDrop = string.Empty;

        public int FormSceneNumber { get => _formSceneNumber; set { _formSceneNumber = value; OnPropertyChanged(); } }
        public string FormSceneTitle { get => _formSceneTitle; set { _formSceneTitle = value; OnPropertyChanged(); } }
        public string FormPovCharacter { get => _formPovCharacter; set { _formPovCharacter = value; OnPropertyChanged(); } }
        public string FormEstimatedWordCount { get => _formEstimatedWordCount; set { _formEstimatedWordCount = value; OnPropertyChanged(); } }
        public string FormOpening { get => _formOpening; set { _formOpening = value; OnPropertyChanged(); } }
        public string FormDevelopment { get => _formDevelopment; set { _formDevelopment = value; OnPropertyChanged(); } }
        public string FormTurning { get => _formTurning; set { _formTurning = value; OnPropertyChanged(); } }
        public string FormEnding { get => _formEnding; set { _formEnding = value; OnPropertyChanged(); } }
        public string FormInfoDrop { get => _formInfoDrop; set { _formInfoDrop = value; OnPropertyChanged(); } }

        private string _formItemsClues = string.Empty;
        public string FormItemsClues { get => _formItemsClues; set { _formItemsClues = value; OnPropertyChanged(); } }

        private string _formCast = string.Empty;
        private string _formLocations = string.Empty;
        private string _formFactions = string.Empty;

        public string FormCast { get => _formCast; set { _formCast = value; OnPropertyChanged(); } }
        public string FormLocations { get => _formLocations; set { _formLocations = value; OnPropertyChanged(); } }
        public string FormFactions { get => _formFactions; set { _formFactions = value; OnPropertyChanged(); } }

        public ObservableCollection<string> AvailableCharacters { get; } = new();
        public ObservableCollection<string> AvailableLocations { get; } = new();
        public ObservableCollection<string> AvailableFactions { get; } = new();

        public ObservableCollection<string> AvailableChapterIds { get; } = new();

        public List<string> AvailablePovCharacters => AvailableCharacters.Prepend(string.Empty).ToList();

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        public BlueprintViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterService, LocationRulesService locationService, FactionRulesService factionService, ChapterService chapterService, VolumeDesignService volumeDesignService, IWorkScopeService workScopeService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterService = characterService;
            _locationService = locationService;
            _factionService = factionService;
            _chapterService = chapterService;
            _volumeDesignService = volumeDesignService;
            _workScopeService = workScopeService;
            LoadAvailableEntities();
            RefreshTreeAndCategorySelection();

            try
            {
                System.Windows.WeakEventManager<VolumeDesignService, EventArgs>.AddHandler(
                    _volumeDesignService,
                    nameof(VolumeDesignService.DataChanged),
                    OnVolumeDataChanged);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 订阅 VolumeDesignService.DataChanged 失败: {ex.Message}");
            }

            try
            {
                System.Windows.WeakEventManager<ChapterService, EventArgs>.AddHandler(
                    _chapterService,
                    nameof(ChapterService.DataChanged),
                    OnChapterDataChanged);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 订阅 ChapterService.DataChanged 失败: {ex.Message}");
            }
        }

        private void LoadAvailableEntities()
        {
            var currentScope = _workScopeService.CurrentSourceBookId;

            AvailableCharacters.Clear();
            try
            {
                var characters = _characterService.GetAllCharacterRules()
                    .Where(c => c.IsEnabled && (string.IsNullOrEmpty(currentScope) || c.SourceBookId == currentScope));
                foreach (var c in characters)
                    AvailableCharacters.Add(c.Name);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载角色列表失败: {ex.Message}");
            }

            AvailableLocations.Clear();
            try
            {
                var locations = _locationService.GetAllLocationRules()
                    .Where(l => l.IsEnabled && (string.IsNullOrEmpty(currentScope) || l.SourceBookId == currentScope));
                foreach (var l in locations)
                    AvailableLocations.Add(l.Name);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载地点列表失败: {ex.Message}");
            }

            AvailableFactions.Clear();
            try
            {
                var factions = _factionService.GetAllFactionRules()
                    .Where(f => f.IsEnabled && (string.IsNullOrEmpty(currentScope) || f.SourceBookId == currentScope));
                foreach (var f in factions)
                    AvailableFactions.Add(f.Name);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载势力列表失败: {ex.Message}");
            }

            ReloadAvailableChapterIds();

            OnPropertyChanged(nameof(AvailablePovCharacters));
        }

        private static int ExtractVolumeNumber(string? volume)
        {
            if (string.IsNullOrWhiteSpace(volume)) return 1;

            var cnMatch = Regex.Match(volume, @"第\s*(\d+)\s*卷", RegexOptions.IgnoreCase);
            if (cnMatch.Success && int.TryParse(cnMatch.Groups[1].Value, out var cnNum)) return cnNum;

            if (int.TryParse(volume, out var num)) return num;

            var cleaned = volume.Replace("vol", "").Replace("_", "").Trim();
            if (int.TryParse(cleaned, out var parsed)) return parsed;

            return 1;
        }

        protected override string DefaultDataIcon => "🎬";

        protected override BlueprintData? CreateNewData(string? categoryName = null)
        {
            return new BlueprintData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新蓝图",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override async System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var scope = _workScopeService.CurrentSourceBookId;

            var castEmpty = string.IsNullOrWhiteSpace(FormCast) || EntityNameNormalizeHelper.IsIgnoredValue(FormCast.Trim());
            var locationsEmpty = string.IsNullOrWhiteSpace(FormLocations) || EntityNameNormalizeHelper.IsIgnoredValue(FormLocations.Trim());
            var factionsEmpty = string.IsNullOrWhiteSpace(FormFactions) || EntityNameNormalizeHelper.IsIgnoredValue(FormFactions.Trim());

            if (castEmpty || locationsEmpty || factionsEmpty)
            {
                try
                {
                    var volMatch = System.Text.RegularExpressions.Regex.Match(FormChapterId ?? string.Empty, @"vol(\d+)_ch(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (volMatch.Success)
                    {
                        var volNum = int.Parse(volMatch.Groups[1].Value);
                        var chNum  = int.Parse(volMatch.Groups[2].Value);
                        await _chapterService.InitializeAsync();
                        var chapter = _chapterService.GetAllChapters()
                            .FirstOrDefault(c => c.IsEnabled && c.ChapterNumber == chNum
                                && (string.IsNullOrEmpty(scope) || string.Equals(c.SourceBookId, scope, StringComparison.Ordinal)));
                        if (chapter != null)
                        {
                            if (castEmpty && chapter.ReferencedCharacterNames?.Count > 0)
                            { FormCast = string.Join("、", chapter.ReferencedCharacterNames); castEmpty = false; }
                            if (locationsEmpty && chapter.ReferencedLocationNames?.Count > 0)
                            { FormLocations = string.Join("、", chapter.ReferencedLocationNames); locationsEmpty = false; }
                            if (factionsEmpty && chapter.ReferencedFactionNames?.Count > 0)
                            { FormFactions = string.Join("、", chapter.ReferencedFactionNames); factionsEmpty = false; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BlueprintViewModel] 从章节设计继承实体引用失败: {ex.Message}");
                }

                if (castEmpty || locationsEmpty || factionsEmpty)
                {
                    try
                    {
                        await _volumeDesignService.InitializeAsync();
                        var volume = _volumeDesignService.GetAllVolumeDesigns()
                            .FirstOrDefault(v => v.IsEnabled
                                && (string.IsNullOrEmpty(scope) || string.Equals(v.SourceBookId, scope, StringComparison.Ordinal))
                                && (string.Equals(v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name, FormCategory, StringComparison.Ordinal)
                                    || string.Equals(v.Name, FormCategory, StringComparison.Ordinal)));
                        if (volume != null)
                        {
                            if (castEmpty && volume.ReferencedCharacterNames?.Count > 0)
                                FormCast = string.Join("、", volume.ReferencedCharacterNames);
                            if (locationsEmpty && volume.ReferencedLocationNames?.Count > 0)
                                FormLocations = string.Join("、", volume.ReferencedLocationNames);
                            if (factionsEmpty && volume.ReferencedFactionNames?.Count > 0)
                                FormFactions = string.Join("、", volume.ReferencedFactionNames);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BlueprintViewModel] 从分卷设计继承实体引用失败: {ex.Message}");
                    }
                }
            }

            FormPovCharacter = await BlueprintResolveCharacterAsync(FormPovCharacter, scope);
            FormCast         = await BlueprintResolveCharactersAsync(FormCast, scope);
            FormLocations    = await BlueprintResolveLocationsAsync(FormLocations, scope);
            FormFactions     = await BlueprintResolveFactionsAsync(FormFactions, scope);
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveCharacterAsync(string raw, string? scope)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var name = raw.Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(name)) return System.Threading.Tasks.Task.FromResult(string.Empty);
            if (_characterService.GetAllCharacterRules().Any(c => c.IsEnabled &&
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return System.Threading.Tasks.Task.FromResult(name);
            TM.App.Log($"[BlueprintViewModel] 实体引用：角色 '{name}' 在上游不存在，已忽略");
            return System.Threading.Tasks.Task.FromResult(string.Empty);
        }

        private async System.Threading.Tasks.Task<string> BlueprintResolveCharactersAsync(string raw, string? scope)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts) resolved.Add(await BlueprintResolveCharacterAsync(n, scope));
            return string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveLocationsAsync(string raw, string? scope)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_locationService.GetAllLocationRules().Any(l => l.IsEnabled &&
                    string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase))) { resolved.Add(n); continue; }
                TM.App.Log($"[BlueprintViewModel] 实体引用：地点 '{n}' 在上游不存在，已忽略");
            }
            return System.Threading.Tasks.Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveFactionsAsync(string raw, string? scope)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_factionService.GetAllFactionRules().Any(f => f.IsEnabled &&
                    string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))) { resolved.Add(n); continue; }
                TM.App.Log($"[BlueprintViewModel] 实体引用：势力 '{n}' 在上游不存在，已忽略");
            }
            return System.Threading.Tasks.Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
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
            await EnsureServiceInitializedAsync(_factionService);
            await EnsureServiceInitializedAsync(_chapterService);
            await EnsureServiceInitializedAsync(_volumeDesignService);

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        LoadAvailableEntities();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    LoadAvailableEntities();
                }
            }
            catch
            {
                LoadAvailableEntities();
            }
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllBlueprints();

        protected override List<BlueprintCategory> GetAllCategoriesFromService()
        {
            return Service.GetAllCategories();
        }

        protected override List<BlueprintData> GetAllDataItems()
            => Service.GetAllBlueprints().OrderBy(b => b.SceneNumber).ToList();

        protected override string GetDataCategory(BlueprintData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(BlueprintData data)
        {
            var chapterNo = TryParseChapterNumberFromChapterId(data.ChapterId);
            var chapterTitle = TryResolveChapterTitle(chapterNo);
            var fallbackTitle = string.IsNullOrWhiteSpace(chapterTitle)
                ? CleanBlueprintSceneTitle(string.IsNullOrWhiteSpace(data.SceneTitle) ? data.Name : data.SceneTitle)
                : chapterTitle;
            var titlePart = string.IsNullOrWhiteSpace(fallbackTitle) ? string.Empty : $" {fallbackTitle}";
            return new TreeNodeItem
            {
                Name = chapterNo > 0 ? $"第{chapterNo}章蓝图{titlePart}" : $"蓝图{titlePart}".Trim(),
                Icon = "🎬",
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(BlueprintData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SceneTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OneLineStructure.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.PovCharacter.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: BlueprintData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: BlueprintCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(BlueprintData data)
        {
            FormName = data.Name;
            FormIcon = "🎬";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormChapterId = AlignSelection(MatchChapterId(data.ChapterId), AvailableChapterIds);
            FormOneLineStructure = data.OneLineStructure;
            FormPacingCurve = data.PacingCurve;

            FormSceneNumber = data.SceneNumber;
            FormSceneTitle = data.SceneTitle;
            FormPovCharacter = data.PovCharacter;
            FormEstimatedWordCount = data.EstimatedWordCount;
            FormOpening = data.Opening;
            FormDevelopment = data.Development;
            FormTurning = data.Turning;
            FormEnding = data.Ending;
            FormInfoDrop = data.InfoDrop;

            FormCast = data.Cast;
            FormLocations = data.Locations;
            FormFactions = data.Factions;
            FormItemsClues = data.ItemsClues;
        }

        private void LoadCategoryToForm(BlueprintCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.Name;
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
            FormChapterId = GetDefaultChapterId();
            FormOneLineStructure = string.Empty;
            FormPacingCurve = string.Empty;

            FormSceneNumber = 0;
            FormSceneTitle = string.Empty;
            FormPovCharacter = string.Empty;
            FormEstimatedWordCount = string.Empty;
            FormOpening = string.Empty;
            FormDevelopment = string.Empty;
            FormTurning = string.Empty;
            FormEnding = string.Empty;
            FormInfoDrop = string.Empty;

            FormCast = string.Empty;
            FormLocations = string.Empty;
            FormFactions = string.Empty;
            FormItemsClues = string.Empty;
        }

        protected override string NewItemTypeName => "蓝图";
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
                TM.App.Log($"[BlueprintViewModel] 新建失败: {ex.Message}");
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
                TM.App.Log($"[BlueprintViewModel] 保存失败: {ex.Message}");
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

            var unmatchedCharacters = EntityNameNormalizeHelper.GetUnmatchedNames(FormCast, AvailableCharacters);
            var unmatchedLocations = EntityNameNormalizeHelper.GetUnmatchedNames(FormLocations, AvailableLocations);
            var unmatchedFactions = EntityNameNormalizeHelper.GetUnmatchedNames(FormFactions, AvailableFactions);

            if (unmatchedCharacters.Count > 0 || unmatchedLocations.Count > 0 || unmatchedFactions.Count > 0)
            {
                var parts = new List<string>();
                if (unmatchedCharacters.Count > 0)
                    parts.Add($"角色: {string.Join("、", unmatchedCharacters)}");
                if (unmatchedLocations.Count > 0)
                    parts.Add($"地点: {string.Join("、", unmatchedLocations)}");
                if (unmatchedFactions.Count > 0)
                    parts.Add($"势力: {string.Join("、", unmatchedFactions)}");

                GlobalToast.Warning("断链预警", $"以下名称未在当前Scope候选列表中找到，可能导致上下文变弱：{string.Join("；", parts)}");
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或蓝图");
                return false;
            }

            if (!ValidateVolumeConsistency())
            {
                return false;
            }

            return true;
        }

        private bool ValidateVolumeConsistency()
        {
            if (string.IsNullOrWhiteSpace(FormChapterId) || string.IsNullOrWhiteSpace(FormCategory))
                return true;

            var volMatch = Regex.Match(FormChapterId, @"vol(\d+)_ch\d+", RegexOptions.IgnoreCase);
            if (!volMatch.Success) return true;

            var chapterVolumeNumber = int.Parse(volMatch.Groups[1].Value);

            var categoryVolMatch = Regex.Match(FormCategory, @"第(\d+)卷", RegexOptions.IgnoreCase);
            if (!categoryVolMatch.Success) return true;

            var categoryVolumeNumber = int.Parse(categoryVolMatch.Groups[1].Value);

            if (chapterVolumeNumber != categoryVolumeNumber)
            {
                GlobalToast.Error("卷匹配错误",
                    $"关联章节属于【第{chapterVolumeNumber}卷】，但当前分类是【{FormCategory}】，请修正");
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

            if (string.IsNullOrWhiteSpace(FormChapterId))
            {
                FormChapterId = GetDefaultChapterId();
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            await Service.AddBlueprintAsync(newData);
            _currentEditingData = newData;
            GlobalToast.Success("保存成功", $"蓝图『{newData.SceneTitle}』已创建");
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
            await Service.UpdateBlueprintAsync(_currentEditingData);
            GlobalToast.Success("保存成功", $"蓝图『{_currentEditingData.SceneTitle}』已更新");
        }

        private void UpdateDataFromForm(BlueprintData data)
        {
            var cleanedName = CleanBlueprintSceneTitle(FormName);
            data.Name = string.IsNullOrWhiteSpace(cleanedName) ? FormName : cleanedName;
            data.Category = FormCategory;
            data.IsEnabled = (FormStatus == "已启用");
            data.UpdatedAt = DateTime.Now;

            data.ChapterId = MatchChapterId(FormChapterId);
            data.OneLineStructure = FormOneLineStructure;
            data.PacingCurve = FormPacingCurve;

            data.SceneNumber = FormSceneNumber;
            var cleanedSceneTitle = CleanBlueprintSceneTitle(FormSceneTitle);
            data.SceneTitle = string.IsNullOrWhiteSpace(cleanedSceneTitle) ? FormSceneTitle : cleanedSceneTitle;
            data.PovCharacter = FormPovCharacter;
            data.EstimatedWordCount = FormEstimatedWordCount;
            data.Opening = FormOpening;
            data.Development = FormDevelopment;
            data.Turning = FormTurning;
            data.Ending = FormEnding;
            data.InfoDrop = FormInfoDrop;

            data.Cast = FormCast;
            data.Locations = FormLocations;
            data.Factions = FormFactions;
            data.ItemsClues = FormItemsClues;
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
                        $"确定要删除蓝图『{_currentEditingData.SceneTitle}』吗？",
                        "确认删除");
                    if (!result) return;

                    Service.DeleteBlueprint(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"蓝图『{_currentEditingData.SceneTitle}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或蓝图");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 删除失败: {ex.Message}");
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
                MessagePrefix = "蓝图设计",
                ProgressMessage = "正在设计章节蓝图...",
                CompleteMessage = "蓝图设计完成",
                InputVariables = new()
                {
                    ["场景标题"] = () => FormSceneTitle,
                    ["大纲名称"] = () => string.Empty,
                    ["章节标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["场景标题"] = v => FormSceneTitle = v,
                    ["一句话结构"] = v => { if (string.IsNullOrWhiteSpace(FormOneLineStructure)) FormOneLineStructure = v; },
                    ["节奏曲线"]   = v => { if (string.IsNullOrWhiteSpace(FormPacingCurve))     FormPacingCurve      = v; },
                    ["视点角色"] = v => FormPovCharacter    = FilterToCandidateOrRaw(v, AvailableCharacters),
                    ["预估字数"] = v => FormEstimatedWordCount = v,
                    ["开场"]     = v => { if (string.IsNullOrWhiteSpace(FormOpening))     FormOpening     = v; },
                    ["发展"]     = v => { if (string.IsNullOrWhiteSpace(FormDevelopment)) FormDevelopment = v; },
                    ["转折"]     = v => { if (string.IsNullOrWhiteSpace(FormTurning))     FormTurning     = v; },
                    ["结尾"]     = v => { if (string.IsNullOrWhiteSpace(FormEnding))      FormEnding      = v; },
                    ["信息释放"] = v => { if (string.IsNullOrWhiteSpace(FormInfoDrop))    FormInfoDrop    = v; },
                    ["物品线索"] = v => { if (string.IsNullOrWhiteSpace(FormItemsClues)) FormItemsClues = v; },
                    ["出场角色"] = v => FormCast      = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及地点"] = v => FormLocations = FilterToCandidatesOrRaw(v, AvailableLocations),
                    ["涉及势力"] = v => FormFactions  = FilterToCandidatesOrRaw(v, AvailableFactions),
                },
                OutputFieldGetters = new()
                {
                    ["场景标题"] = () => FormSceneTitle,
                    ["一句话结构"] = () => FormOneLineStructure,
                    ["节奏曲线"] = () => FormPacingCurve,
                    ["视点角色"] = () => FormPovCharacter,
                    ["预估字数"] = () => FormEstimatedWordCount,
                    ["开场"] = () => FormOpening,
                    ["发展"] = () => FormDevelopment,
                    ["转折"] = () => FormTurning,
                    ["结尾"] = () => FormEnding,
                    ["信息释放"] = () => FormInfoDrop,
                    ["物品线索"] = () => FormItemsClues,
                    ["出场角色"] = () => FormCast,
                    ["涉及地点"] = () => FormLocations,
                    ["涉及势力"] = () => FormFactions,
                },
                ContextProvider = async () => await GetEnhancedBlueprintContextAsync(),
                SequenceFieldName = "SceneNumber",
                GetCurrentMaxSequence = (scopeId, categoryName) => Service.GetAllBlueprints()
                    .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Where(c => string.IsNullOrEmpty(scopeId) || string.Equals(c.SourceBookId, scopeId, StringComparison.Ordinal))
                    .Select(c => c.SceneNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = new()
                {
                    ["一句话结构"] = "OneLineStructure",
                    ["节奏曲线"] = "PacingCurve",
                    ["视点角色"] = "PovCharacter",
                    ["预估字数"] = "EstimatedWordCount",
                    ["开场"] = "Opening",
                    ["发展"] = "Development",
                    ["转折"] = "Turning",
                    ["结尾"] = "Ending",
                    ["信息释放"] = "InfoDrop",
                    ["物品线索"] = "ItemsClues",
                    ["出场角色"] = "Cast",
                    ["涉及地点"] = "Locations",
                    ["涉及势力"] = "Factions",
                    ["场景编号"] = "SceneNumber",
                    ["场景标题"] = "SceneTitle"
                },
                BatchIndexFields = new() { "SceneNumber", "SceneTitle", "OneLineStructure" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override void OnBatchGenerationFailed(int failedCount)
        {
            if (_currentBatchChapterIds?.Count > 0)
            {
                _batchChapterIdIndex = Math.Max(0, _batchChapterIdIndex - _currentBatchChapterIds.Count);
                TM.App.Log($"[BlueprintViewModel] 批次失败，回退章节ID索引至 {_batchChapterIdIndex}");
            }
        }

        private List<string>? _batchFullChapterIds;
        private List<string>? _batchPreCalculatedChapterIds;
        private int _batchChapterIdIndex;
        private List<string>? _currentBatchChapterIds;
        private int ResolveSceneNumberForChapterId(string chapterId)
        {
            if (!string.IsNullOrWhiteSpace(chapterId) && _batchFullChapterIds != null && _batchFullChapterIds.Count > 0)
            {
                var idx = _batchFullChapterIds.FindIndex(id => string.Equals(id, chapterId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx + 1;
            }
            var parsed = TryParseChapterNumberFromChapterId(chapterId);
            return parsed > 0 ? parsed : 0;
        }

        protected override GenerationRange? GetNextGenerationRange(string? scopeId, string categoryName, int requestedCount)
        {
            if (_batchPreCalculatedChapterIds != null && _batchPreCalculatedChapterIds.Count > 0)
            {
                var take = Math.Min(requestedCount, _batchPreCalculatedChapterIds.Count - _batchChapterIdIndex);
                if (take > 0)
                {
                    _currentBatchChapterIds = _batchPreCalculatedChapterIds
                        .Skip(_batchChapterIdIndex)
                        .Take(take)
                        .ToList();
                    _batchChapterIdIndex += take;
                }
                else
                {
                    _currentBatchChapterIds = null;
                }
                return null;
            }
            _currentBatchChapterIds = null;
            return base.GetNextGenerationRange(scopeId, categoryName, requestedCount);
        }

        protected override async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prompt) && _currentBatchChapterIds?.Count > 0)
            {
                var sb = new System.Text.StringBuilder(prompt);
                sb.AppendLine();
                sb.AppendLine("<blueprint_assignments mandatory=\"true\">");
                sb.AppendLine($"本批生成任务（输出数组长度必须 = {_currentBatchChapterIds.Count}，第i项对应第 i 个 ChapterId）：");
                sb.AppendLine(string.Join("、", _currentBatchChapterIds));
                sb.AppendLine("要求：本批每个对象的 Name/SceneTitle 必须互不重复，且需体现对应章节的场景特征（不要使用 vol/ch 编号前缀）。");
                sb.AppendLine("</blueprint_assignments>");
                return sb.ToString();
            }
            return prompt;
        }

        protected override async System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            var currentScopeId = _workScopeService.CurrentSourceBookId;
            int volNum = 1;
            try
            {
                await _volumeDesignService.InitializeAsync();
                var volume = _volumeDesignService.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.IsEnabled
                        && (string.IsNullOrEmpty(currentScopeId) || string.Equals(v.SourceBookId, currentScopeId, StringComparison.Ordinal))
                        && (string.Equals((v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name), categoryName, StringComparison.Ordinal)
                            || string.Equals(v.Name, categoryName, StringComparison.Ordinal)));
                if (volume != null && volume.VolumeNumber > 0)
                {
                    volNum = volume.VolumeNumber;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 解析卷号失败: {ex.Message}");
            }

            await _chapterService.InitializeAsync();

            var chapters = _chapterService.GetAllChapters()
                .Where(c => c.IsEnabled
                    && string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(currentScopeId) || string.Equals(c.SourceBookId, currentScopeId, StringComparison.Ordinal)))
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            if (chapters.Count == 0)
            {
                GlobalToast.Warning("前置条件不满足", "请先在章节设计中为本卷生成章节，再执行蓝图批量生成");
                return null;
            }

            _batchFullChapterIds = chapters
                .Select(ch => $"vol{volNum}_ch{ch.ChapterNumber}")
                .ToList();

            var existingWithContent = Service.GetAllBlueprints()
                .Where(b => string.Equals(b.Category, categoryName, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(currentScopeId) || string.Equals(b.SourceBookId, currentScopeId, StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(b.OneLineStructure)
                    && !string.IsNullOrWhiteSpace(CleanBlueprintSceneTitle(b.SceneTitle)))
                .Select(b => b.ChapterId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var alreadyCompleted = _batchFullChapterIds.Count(id => existingWithContent.Contains(id));
            _batchPreCalculatedChapterIds = _batchFullChapterIds
                .Where(id => !existingWithContent.Contains(id))
                .ToList();
            _batchChapterIdIndex = 0;

            if (_batchPreCalculatedChapterIds.Count == 0)
            {
                GlobalToast.Info("已全部完成", $"本卷 {chapters.Count} 个蓝图均已有AI内容，无需重新生成");
                _batchFullChapterIds = null;
                _batchPreCalculatedChapterIds = null;
                return null;
            }

            var startChapter = chapters.Min(c => c.ChapterNumber);
            var endChapter = chapters.Max(c => c.ChapterNumber);
            var chapterRangeText = $"vol{volNum}_ch{startChapter} ~ vol{volNum}_ch{endChapter}";

            string msg;
            if (alreadyCompleted > 0)
            {
                msg = $"即将对「{categoryName}」继续执行 AI 批量重建章节蓝图：\n\n"
                    + $"• 章节范围：{chapterRangeText}\n"
                    + $"• 已完成：{alreadyCompleted} 个（跳过）\n"
                    + $"• 待生成：{_batchPreCalculatedChapterIds.Count} 个\n"
                    + "• 仅展示本卷起止章节，不逐章展开\n\n"
                    + "确认继续生成？";
            }
            else
            {
                msg = $"即将对「{categoryName}」执行 AI 批量重建章节蓝图：\n\n"
                    + $"• 蓝图数量：共 {chapters.Count} 个（每章一蓝图）\n"
                    + $"• 章节范围：{chapterRangeText}\n"
                    + $"• 超出范围的旧蓝图数据将被自动清理\n\n"
                    + "确认开始生成？";
            }

            var confirmed = StandardDialog.ShowConfirm(msg, "批量重建章节蓝图");
            if (!confirmed)
            {
                _batchFullChapterIds = null;
                _batchPreCalculatedChapterIds = null;
                return null;
            }

            return new BatchGenerationConfig
            {
                CategoryName = categoryName,
                TotalCount = _batchPreCalculatedChapterIds.Count,
                BatchSize = 8
            };
        }

        protected override async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            var fullIds = _batchFullChapterIds;
            if (fullIds == null || fullIds.Count == 0) return;

            var currentScopeId = _workScopeService.CurrentSourceBookId;
            var validSet = new HashSet<string>(fullIds, StringComparer.OrdinalIgnoreCase);

            var tail = Service.GetAllBlueprints()
                .Where(b => string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal)
                       && (string.IsNullOrEmpty(currentScopeId) || string.Equals(b.SourceBookId, currentScopeId, StringComparison.Ordinal)))
                .Where(b => !validSet.Contains(b.ChapterId))
                .ToList();

            foreach (var b in tail)
            {
                Service.DeleteBlueprint(b.Id);
                TM.App.Log($"[BlueprintViewModel] 清尾: 删除蓝图 {b.ChapterId}（不在有效集合内）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[BlueprintViewModel] 清尾完成: 删除 {tail.Count} 个旧蓝图");

            if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllBlueprints()
                    .Where(b => string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal)
                        && (string.IsNullOrEmpty(currentScopeId) || string.Equals(b.SourceBookId, currentScopeId, StringComparison.Ordinal))
                        && string.IsNullOrWhiteSpace(CleanBlueprintSceneTitle(b.SceneTitle))
                        && string.IsNullOrWhiteSpace(b.OneLineStructure)
                        && string.IsNullOrWhiteSpace(b.PacingCurve)
                        && string.IsNullOrWhiteSpace(b.PovCharacter)
                        && string.IsNullOrWhiteSpace(b.EstimatedWordCount)
                        && string.IsNullOrWhiteSpace(b.Opening)
                        && string.IsNullOrWhiteSpace(b.Development)
                        && string.IsNullOrWhiteSpace(b.Turning)
                        && string.IsNullOrWhiteSpace(b.Ending)
                        && string.IsNullOrWhiteSpace(b.InfoDrop)
                        && string.IsNullOrWhiteSpace(b.ItemsClues)
                        && string.IsNullOrWhiteSpace(b.Cast)
                        && string.IsNullOrWhiteSpace(b.Locations)
                        && string.IsNullOrWhiteSpace(b.Factions))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteBlueprint(shell.Id);
                    TM.App.Log($"[BlueprintViewModel] 取消清理: 删除空壳蓝图 {shell.ChapterId}");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳蓝图，下次批量生成会按需续接");
            }
            else
            {
                foreach (var chId in fullIds)
                {
                    var existing = Service.GetAllBlueprints()
                        .FirstOrDefault(b => string.Equals(b.ChapterId, chId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal)
                            && (string.IsNullOrEmpty(currentScopeId) || string.Equals(b.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                    if (existing == null)
                    {
                        var data = new BlueprintData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = $"蓝图_{chId}",
                            Category = config.CategoryName,
                            IsEnabled = true,
                            SourceBookId = currentScopeId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterId = chId,
                            SceneTitle = $"蓝图_{chId}",
                        };
                        await Service.AddBlueprintAsync(data);
                        TM.App.Log($"[BlueprintViewModel] 补缺: {chId}（AI失败，创建占位）");
                    }
                }
            }

            _batchFullChapterIds = null;
            _batchPreCalculatedChapterIds = null;
            _currentBatchChapterIds = null;

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

                    string chapterId;
                    int sceneNumber;
                    var entityIndex = entities.IndexOf(entity);
                    if (_currentBatchChapterIds != null && entityIndex >= 0 && entityIndex < _currentBatchChapterIds.Count)
                    {
                        chapterId = _currentBatchChapterIds[entityIndex];
                        sceneNumber = ResolveSceneNumberForChapterId(chapterId);
                    }
                    else
                    {
                        chapterId = MatchChapterId(reader.GetString("ChapterId"));
                        sceneNumber = reader.GetInt("SceneNumber");
                    }

                    var existing = Service.GetAllBlueprints()
                        .FirstOrDefault(b => string.Equals(b.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(b.Category, categoryName, StringComparison.Ordinal)
                            && (string.IsNullOrEmpty(currentScopeId) || string.Equals(b.SourceBookId, currentScopeId, StringComparison.Ordinal)));

                    if (existing != null)
                    {
                        var aiName = reader.GetString("Name");
                        var aiSt = reader.GetString("SceneTitle");
                        var cleanedSceneTitle = CleanBlueprintSceneTitle(aiSt);
                        var cleanedName = CleanBlueprintSceneTitle(aiName);
                        if (!string.IsNullOrWhiteSpace(cleanedSceneTitle))
                            existing.SceneTitle = cleanedSceneTitle;
                        else if (!string.IsNullOrWhiteSpace(cleanedName))
                            existing.SceneTitle = cleanedName;
                        if (!string.IsNullOrWhiteSpace(cleanedName))
                            existing.Name = cleanedName;
                        else if (!string.IsNullOrWhiteSpace(cleanedSceneTitle))
                            existing.Name = cleanedSceneTitle;
                        var aiOls = reader.GetString("OneLineStructure");
                        if (!string.IsNullOrWhiteSpace(aiOls)) existing.OneLineStructure = aiOls;
                        var aiPc = reader.GetString("PacingCurve");
                        if (!string.IsNullOrWhiteSpace(aiPc)) existing.PacingCurve = aiPc;
                        var batchScope = _workScopeService.CurrentSourceBookId;
                        var aiPov = reader.GetString("PovCharacter");
                        if (!string.IsNullOrWhiteSpace(aiPov)) existing.PovCharacter = await BlueprintResolveCharacterAsync(aiPov, batchScope);
                        var aiEwc = reader.GetString("EstimatedWordCount");
                        if (!string.IsNullOrWhiteSpace(aiEwc)) existing.EstimatedWordCount = aiEwc;
                        var aiOp = reader.GetString("Opening");
                        if (!string.IsNullOrWhiteSpace(aiOp)) existing.Opening = aiOp;
                        var aiDev = reader.GetString("Development");
                        if (!string.IsNullOrWhiteSpace(aiDev)) existing.Development = aiDev;
                        var aiTrn = reader.GetString("Turning");
                        if (!string.IsNullOrWhiteSpace(aiTrn)) existing.Turning = aiTrn;
                        var aiEnd = reader.GetString("Ending");
                        if (!string.IsNullOrWhiteSpace(aiEnd)) existing.Ending = aiEnd;
                        var aiInf = reader.GetString("InfoDrop");
                        if (!string.IsNullOrWhiteSpace(aiInf)) existing.InfoDrop = aiInf;
                        var aiItm = reader.GetString("ItemsClues");
                        if (!string.IsNullOrWhiteSpace(aiItm)) existing.ItemsClues = aiItm;
                        var aiCst = reader.GetString("Cast");
                        if (!string.IsNullOrWhiteSpace(aiCst)) existing.Cast = await BlueprintResolveCharactersAsync(aiCst, batchScope);
                        var aiLoc = reader.GetString("Locations");
                        if (!string.IsNullOrWhiteSpace(aiLoc)) existing.Locations = await BlueprintResolveLocationsAsync(aiLoc, batchScope);
                        var aiFac = reader.GetString("Factions");
                        if (!string.IsNullOrWhiteSpace(aiFac)) existing.Factions = await BlueprintResolveFactionsAsync(aiFac, batchScope);

                        var chapterForFallback = _chapterService.GetAllChapters()
                            .FirstOrDefault(c => string.Equals(c.Id, chapterId, StringComparison.OrdinalIgnoreCase));
                        if (chapterForFallback != null)
                        {
                            if (string.IsNullOrWhiteSpace(existing.Cast) && chapterForFallback.ReferencedCharacterNames.Count > 0)
                                existing.Cast = string.Join("、", chapterForFallback.ReferencedCharacterNames);
                            if (string.IsNullOrWhiteSpace(existing.Locations) && chapterForFallback.ReferencedLocationNames.Count > 0)
                                existing.Locations = string.Join("、", chapterForFallback.ReferencedLocationNames);
                            if (string.IsNullOrWhiteSpace(existing.Factions) && chapterForFallback.ReferencedFactionNames.Count > 0)
                                existing.Factions = string.Join("、", chapterForFallback.ReferencedFactionNames);
                        }

                        existing.ChapterId = chapterId;
                        existing.SceneNumber = sceneNumber;
                        existing.DependencyModuleVersions = versionSnapshot ?? new();
                        existing.UpdatedAt = DateTime.Now;
                        await Service.UpdateBlueprintAsync(existing);
                        entity["SceneNumber"] = sceneNumber;
                        TM.App.Log($"[BlueprintViewModel] Upsert更新: {chapterId}");
                    }
                    else
                    {
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name)) name = $"蓝图_{chapterId}";
                        var title = reader.GetString("SceneTitle");
                        if (string.IsNullOrWhiteSpace(title)) title = name;

                        var data = new BlueprintData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = CleanBlueprintSceneTitle(name),
                            Category = categoryName,
                            IsEnabled = true,
                            SourceBookId = currentScopeId,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterId = chapterId,
                            SceneNumber = sceneNumber,
                            OneLineStructure = reader.GetString("OneLineStructure"),
                            PacingCurve = reader.GetString("PacingCurve"),
                            SceneTitle = CleanBlueprintSceneTitle(title),
                            PovCharacter = await BlueprintResolveCharacterAsync(reader.GetString("PovCharacter"), currentScopeId),
                            EstimatedWordCount = reader.GetString("EstimatedWordCount"),
                            Opening = reader.GetString("Opening"),
                            Development = reader.GetString("Development"),
                            Turning = reader.GetString("Turning"),
                            Ending = reader.GetString("Ending"),
                            InfoDrop = reader.GetString("InfoDrop"),
                            Cast = await BlueprintResolveCharactersAsync(reader.GetString("Cast"), currentScopeId),
                            Locations = await BlueprintResolveLocationsAsync(reader.GetString("Locations"), currentScopeId),
                            Factions = await BlueprintResolveFactionsAsync(reader.GetString("Factions"), currentScopeId),
                            ItemsClues = reader.GetString("ItemsClues"),
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };

                        var chapterFallback = _chapterService.GetAllChapters()
                            .FirstOrDefault(c => string.Equals(c.Id, chapterId, StringComparison.OrdinalIgnoreCase));
                        if (chapterFallback != null)
                        {
                            if (string.IsNullOrWhiteSpace(data.Cast) && chapterFallback.ReferencedCharacterNames.Count > 0)
                                data.Cast = string.Join("、", chapterFallback.ReferencedCharacterNames);
                            if (string.IsNullOrWhiteSpace(data.Locations) && chapterFallback.ReferencedLocationNames.Count > 0)
                                data.Locations = string.Join("、", chapterFallback.ReferencedLocationNames);
                            if (string.IsNullOrWhiteSpace(data.Factions) && chapterFallback.ReferencedFactionNames.Count > 0)
                                data.Factions = string.Join("、", chapterFallback.ReferencedFactionNames);
                        }
                        entity["SceneNumber"] = sceneNumber;
                        entity["SceneTitle"] = CleanBlueprintSceneTitle(title);
                        await Service.AddBlueprintAsync(data);
                        TM.App.Log($"[BlueprintViewModel] Upsert新建: {chapterId}");
                    }

                    result.Add(entity);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BlueprintViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                }
            }

            TM.App.Log($"[BlueprintViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
            return result;
        }

        protected override void OnTreeDataRefreshed()
        {
            if (_chapterService == null)
                return;

            LoadAvailableEntities();

            if (string.IsNullOrWhiteSpace(FormChapterId))
            {
                FormChapterId = GetDefaultChapterId();
            }
            else
            {
                FormChapterId = AlignSelection(MatchChapterId(FormChapterId), AvailableChapterIds);
            }
        }

        private string GetDefaultChapterId()
        {
            var first = AvailableChapterIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            return first ?? string.Empty;
        }

        private static int TryParseChapterNumberFromChapterId(string? chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId)) return 0;
            var m = Regex.Match(chapterId.Trim(), "_ch(?<ch>\\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            return int.TryParse(m.Groups["ch"].Value, out var ch) ? ch : 0;
        }

        private string? TryResolveChapterTitle(int chapterNumber)
        {
            if (chapterNumber <= 0) return null;
            if (_chapterService == null || _workScopeService == null) return null;
            try
            {
                var currentScope = _workScopeService.CurrentSourceBookId;
                var chapter = _chapterService.GetAllChapters()
                    .Where(c => c.IsEnabled && (string.IsNullOrEmpty(currentScope) || c.SourceBookId == currentScope))
                    .FirstOrDefault(c => c.ChapterNumber == chapterNumber);

                return chapter == null ? null : NormalizeChapterTitle(chapter.ChapterTitle);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeChapterTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = Regex.Replace(t, "^\\s*第\\s*\\d+\\s*卷\\s*[：:、\\-—–]*\\s*第\\s*\\d+\\s*章\\s*[：:、\\-—–]*\\s*", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*[一二三四五六七八九十百千零]+\\s*卷\\s*[：:、\\-—–]*\\s*第\\s*[一二三四五六七八九十百千零]+\\s*章\\s*[：:、\\-—–]*\\s*", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*\\d+\\s*章\\s*[：:、\\-—–]*\\s*", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*[一二三四五六七八九十百千零]+\\s*章\\s*[：:、\\-—–]*\\s*", string.Empty);
            return t.Trim();
        }

        private static string CleanBlueprintSceneTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = Regex.Replace(t, "^\\s*\\d+\\s*[-_－—–]\\s*\\d+\\s*", string.Empty);
            t = Regex.Replace(t, "^\\s*vol\\s*\\d+\\s*[_-]?ch\\s*\\d+\\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "^\\s*ch\\s*\\d+\\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "^\\s*场景蓝图[-_]*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "^\\s*场景\\s*[-_]?\\d+(?:-\\d+)?\\s*", string.Empty, RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "^\\s*第\\s*\\d+\\s*卷\\s*[-_\\s]*第\\s*\\d+\\s*章\\s*[-_\\s]*", string.Empty);
            t = Regex.Replace(t, "^\\s*第\\s*[一二三四五六七八九十百千零]+\\s*卷\\s*[-_\\s]*第\\s*[一二三四五六七八九十百千零]+\\s*章\\s*[-_\\s]*", string.Empty);
            t = Regex.Replace(t, "(^|[-_\\s])scene\\s*[-_]?\\d+(?:-\\d+)?", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "(^|[-_\\s])vol\\d+(_?ch\\d+)?(-\\d+)?", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "(^|[-_\\s])卷\\d+[-_\\s]*", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "(^|[-_\\s])章\\d+[-_\\s]*", " ", RegexOptions.IgnoreCase);
            t = t.Replace("__", " ").Replace("--", " ");
            t = t.Trim(' ', '-', '_');
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
                        var categories = GetAllCategoriesFromService() ?? new List<BlueprintCategory>();
                        if (!categories.Any(c => string.Equals(c.Name, FormCategory, StringComparison.Ordinal)))
                        {
                            FormCategory = string.Empty;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 同步分卷数据变更失败: {ex.Message}");
            }
        }

        private void OnChapterDataChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ReloadAvailableChapterIds();
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 同步章节数据变更失败: {ex.Message}");
            }
        }

        private void ReloadAvailableChapterIds()
        {
            var currentScope = _workScopeService.CurrentSourceBookId;

            AvailableChapterIds.Clear();
            AvailableChapterIds.Add(string.Empty);
            try
            {
                if (!_chapterService.IsInitialized)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try { await _chapterService.InitializeAsync().ConfigureAwait(false); } catch { }
                        if (_chapterService.IsInitialized)
                        {
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                ReloadAvailableChapterIds,
                                System.Windows.Threading.DispatcherPriority.Background);
                        }
                    });
                    return;
                }

                var chaptersQuery = _chapterService.GetAllChapters()
                    .Where(c => c.IsEnabled && (string.IsNullOrEmpty(currentScope) || c.SourceBookId == currentScope));

                if (!string.IsNullOrWhiteSpace(FormCategory))
                {
                    chaptersQuery = chaptersQuery.Where(c => string.Equals(c.Category, FormCategory, StringComparison.Ordinal));
                }

                var chapters = chaptersQuery.OrderBy(c => c.ChapterNumber);
                foreach (var ch in chapters)
                {
                    var volNum = ExtractVolumeNumber(ch.Volume);
                    var chapterId = $"vol{volNum}_ch{ch.ChapterNumber}";
                    if (!AvailableChapterIds.Contains(chapterId))
                        AvailableChapterIds.Add(chapterId);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 刷新章节ID列表失败: {ex.Message}");
            }
        }

        protected override string GetModuleNameForVersionTracking() => "Blueprint";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateBlueprint(_currentEditingData);
        }

        private async System.Threading.Tasks.Task<string> GetEnhancedBlueprintContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            if (IsBatchModeActive && !string.IsNullOrWhiteSpace(FormCategory))
            {
                var volumeContext = await _contextService.GetChapterContextWithVolumeLocatorAsync(FormCategory);
                if (!string.IsNullOrWhiteSpace(volumeContext))
                {
                    sb.AppendLine(volumeContext);
                    sb.AppendLine();
                }

                var chapterSummary = BuildVolumeChapterSummary(FormCategory);
                if (!string.IsNullOrWhiteSpace(chapterSummary))
                {
                    sb.AppendLine("<section name=\"volume_chapter_plan\">");
                    sb.AppendLine(chapterSummary);
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }

                var allChapterIds = AvailableChapterIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
                var usedChapterIds = GetUsedChapterIdsInCategory(FormCategory);
                var unusedChapterIds = allChapterIds.Where(id => !usedChapterIds.Contains(id)).ToList();

                if (allChapterIds.Count > 0)
                {
                    sb.AppendLine("<section name=\"blueprint_allocation_state\">");
                    sb.AppendLine($"- 该卷可用章节ID：{string.Join("、", allChapterIds)}");
                    if (usedChapterIds.Count > 0)
                        sb.AppendLine($"- 已有蓝图的章节：{string.Join("、", usedChapterIds)}");
                    if (unusedChapterIds.Count > 0)
                        sb.AppendLine($"- 待生成蓝图的章节：{string.Join("、", unusedChapterIds)}");
                    sb.AppendLine("- 说明：批量重建时「关联章节ID」由系统按章节列表依次分配，AI不应生成此字段");
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }
            else
            {
                var baseContext = await _contextService.GetBlueprintContextWithChapterLocatorAsync(FormChapterId);
                if (!string.IsNullOrWhiteSpace(baseContext))
                {
                    sb.AppendLine(baseContext);
                    sb.AppendLine();
                }

                var chapterIds = AvailableChapterIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (chapterIds.Any())
                {
                    sb.AppendLine("<section name=\"available_chapter_ids\">");
                    sb.AppendLine("关联章节ID必须从以下列表中选择，格式：vol{卷号}_ch{章号}");
                    sb.AppendLine(string.Join("、", chapterIds));
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }

            if (AvailableCharacters.Any())
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选角色",
                    candidates: AvailableCharacters,
                    fieldHint: "出场角色必须从以下列表中选择"));
            }

            if (AvailableLocations.Any())
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选地点",
                    candidates: AvailableLocations,
                    fieldHint: "涉及地点必须从以下列表中选择"));
            }

            if (AvailableFactions.Any())
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选势力",
                    candidates: AvailableFactions,
                    fieldHint: "涉及势力必须从以下列表中选择"));
            }

            var povCharacters = AvailableCharacters.ToList();
            if (povCharacters.Any())
            {
                sb.AppendLine("<section name=\"available_pov_characters\">");
                sb.AppendLine("视点角色必须从以下列表中选择");
                sb.AppendLine(string.Join("、", povCharacters));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「关联章节ID」由系统自动分配，AI不应生成此字段。");
            sb.AppendLine("2. 「视点角色」只能填写角色名称；有则填写，无则填写「暂无」。");
            sb.AppendLine("3. 「出场角色」「涉及地点」「涉及势力」为多选字段，只填写名称；如有多项，请在字符串内用换行分条；无则填写「暂无」。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            return sb.ToString();
        }

        private string BuildVolumeChapterSummary(string categoryName)
        {
            var currentScope = _workScopeService.CurrentSourceBookId;

            _chapterService.EnsureInitialized();

            var chapters = _chapterService.GetAllChapters()
                .Where(c => c.IsEnabled && string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                .Where(c => string.IsNullOrEmpty(currentScope) || c.SourceBookId == currentScope)
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            if (chapters.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var ch in chapters)
            {
                var volNum = ExtractVolumeNumber(ch.Volume);
                var chapterId = $"vol{volNum}_ch{ch.ChapterNumber}";
                sb.AppendLine($"<item name=\"{chapterId} - 第{ch.ChapterNumber}章：{ch.ChapterTitle}\">");
                if (!string.IsNullOrWhiteSpace(ch.EstimatedWordCount))
                    sb.AppendLine($"  预计字数：{ch.EstimatedWordCount}");
                if (!string.IsNullOrWhiteSpace(ch.ChapterTheme))
                    sb.AppendLine($"  章节主题：{ch.ChapterTheme}");
                if (!string.IsNullOrWhiteSpace(ch.ReaderExperienceGoal))
                    sb.AppendLine($"  读者体验目标：{ch.ReaderExperienceGoal}");
                if (!string.IsNullOrWhiteSpace(ch.MainGoal))
                    sb.AppendLine($"  章节主目标：{ch.MainGoal}");
                if (!string.IsNullOrWhiteSpace(ch.ResistanceSource))
                    sb.AppendLine($"  阻力来源：{ch.ResistanceSource}");
                if (!string.IsNullOrWhiteSpace(ch.KeyTurn))
                    sb.AppendLine($"  关键转折：{ch.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(ch.Hook))
                    sb.AppendLine($"  结尾钉子：{ch.Hook}");
                if (!string.IsNullOrWhiteSpace(ch.WorldInfoDrop))
                    sb.AppendLine($"  世界观投放：{ch.WorldInfoDrop}");
                if (!string.IsNullOrWhiteSpace(ch.CharacterArcProgress))
                    sb.AppendLine($"  角色弧光推进：{ch.CharacterArcProgress}");
                if (!string.IsNullOrWhiteSpace(ch.MainPlotProgress))
                    sb.AppendLine($"  主线推进点：{ch.MainPlotProgress}");
                if (!string.IsNullOrWhiteSpace(ch.Foreshadowing))
                    sb.AppendLine($"  伏笔埋设/回收：{ch.Foreshadowing}");
                sb.AppendLine("</item>");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private HashSet<string> GetUsedChapterIdsInCategory(string categoryName)
        {
            return Service.GetAllBlueprints()
                .Where(b => string.Equals(b.Category, categoryName, StringComparison.Ordinal))
                .Where(b => !string.IsNullOrWhiteSpace(b.ChapterId))
                .Select(b => b.ChapterId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private string MatchChapterId(string aiValue)
        {
            if (string.IsNullOrWhiteSpace(aiValue)) return string.Empty;

            var trimmed = aiValue.Trim();

            var exactMatch = AvailableChapterIds.FirstOrDefault(id => 
                string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactMatch)) return exactMatch;

            var volMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"vol\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var chMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"ch\s*(\d+)|第\s*(\d+)\s*章|章\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            int? chapterNum = null;
            if (chMatch.Success)
            {
                var chNumText = chMatch.Groups[1].Success ? chMatch.Groups[1].Value :
                               (chMatch.Groups[2].Success ? chMatch.Groups[2].Value : chMatch.Groups[3].Value);
                if (int.TryParse(chNumText, out var parsed))
                    chapterNum = parsed;
            }

            if (volMatch.Success && chapterNum.HasValue)
            {
                var volNum = volMatch.Groups[1].Value;
                var constructedId = $"vol{volNum}_ch{chapterNum.Value}";
                if (AvailableChapterIds.Contains(constructedId)) return constructedId;
            }

            if (chapterNum.HasValue)
            {
                var categoryVolume = ExtractVolumeNumber(FormCategory);
                if (categoryVolume > 0)
                {
                    var categoryMatch = $"vol{categoryVolume}_ch{chapterNum.Value}";
                    if (AvailableChapterIds.Contains(categoryMatch)) return categoryMatch;
                }

                var suffixMatch = AvailableChapterIds.FirstOrDefault(id =>
                    id.EndsWith($"_ch{chapterNum.Value}", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(suffixMatch)) return suffixMatch;

                var fallbackId = $"vol1_ch{chapterNum.Value}";
                if (AvailableChapterIds.Contains(fallbackId)) return fallbackId;
            }

            if (int.TryParse(trimmed, out var numericChapter))
            {
                var fallbackId = $"vol1_ch{numericChapter}";
                if (AvailableChapterIds.Contains(fallbackId)) return fallbackId;
            }

            TM.App.Log($"[BlueprintViewModel] 章节ID匹配失败: {aiValue}");
            return trimmed;
        }

    }
}
