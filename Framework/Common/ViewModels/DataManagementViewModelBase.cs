using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Models;
using TM.Framework.Common.Search;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Modules.VersionTracking;

namespace TM.Framework.Common.ViewModels
{
    internal static class AINavigationSessionSaveConfirmState
    {
        internal static bool SuppressThisRun;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService> : TreeDataViewModelBase<TData, TCategory>, IBulkToggleSelectionHost, IAIGeneratingState, IDataTreeHost, IPipelineBatchTarget, IDisposable
        where TData : class, IDataItem
        where TCategory : class, ICategory
        where TService : class
    {
        private bool _baseDisposed;

        public virtual void Dispose()
        {
            if (_baseDisposed) return;
            _baseDisposed = true;

            try
            {
                if (_panelCommunicationService != null)
                {
                    _panelCommunicationService.BusinessDataCleared -= OnBusinessDataCleared;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 基类 Dispose 解除 BusinessDataCleared 订阅失败: {ex.Message}");
            }

            GC.SuppressFinalize(this);
        }

        protected TData? _currentEditingData;
        protected TCategory? _currentEditingCategory;
        private AIService? _cachedAIService;

        private const string SuppressSaveEndAISessionConfirmSettingKey = "ui.ai.save_end_session_confirm.suppress";

        private const double MissingFieldRetryThreshold = 0.4;

        private static readonly Regex YesPatternRegex = new("[:：]\\s*是(\\s|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex WhitespacePunctuationRegex = new(@"[\s\p{P}]", RegexOptions.Compiled);
        protected readonly struct GenerationRange
        {
            public int Start { get; }
            public int End { get; }

            public GenerationRange(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        protected bool _isPipelineExecution;

        protected bool _isPipelineResume;

        private bool IsPipelinePreCalculatedModule()
        {
            var ns = GetType().Namespace ?? string.Empty;
            if (!ns.StartsWith("TM.Modules.Generate.", StringComparison.Ordinal)) return false;
            var name = GetType().Name;
            return string.Equals(name, "VolumeDesignViewModel", StringComparison.Ordinal)
                || string.Equals(name, "ChapterViewModel", StringComparison.Ordinal)
                || string.Equals(name, "BlueprintViewModel", StringComparison.Ordinal);
        }

        private void OnBusinessDataCleared(object? sender, EventArgs e)
        {
            void Refresh()
            {
                _currentEditingData = null;
                _currentEditingCategory = null;
                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke((Action)Refresh, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                Refresh();
            }
        }

        private bool IsSingleMissingFieldsTooHigh(Dictionary<string, object> entity, AIGenerationConfig config)
        {
            if (entity == null || entity.Count == 0)
            {
                return true;
            }

            if (config.OutputFields.Count == 0)
            {
                return false;
            }

            var required = config.OutputFields.Keys
                .Concat(new[] { "Name" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ratio = GetMissingRatio(entity, required);
            return ratio >= MissingFieldRetryThreshold;
        }

        private GenerationRange? _currentBatchRange;
        private bool _batchSessionHasHistory;
        private string _cachedBatchContextText = string.Empty;

        protected GenerationRange? GetCurrentBatchRange()
        {
            return _currentBatchRange;
        }

        private readonly AsyncRelayCommand _deleteAllCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _cancelBatchGenerationCommand;
        private RelayCommand? _toggleSelectedEnabledCommand;
        private RelayCommand? _bulkToggleCommand;
        private readonly Dictionary<string, TreeNodeItem> _categoryNodeIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<TreeNodeItem, TreeNodeItem?> _categorySelectionParentMap = new();
        private readonly Dictionary<TreeNodeItem, string> _categorySelectionPathCache = new();
        private readonly List<TreeNodeItem> _lastCategorySelectionPath = new();
        private Dictionary<string, TCategory> _categoryLookup = new(StringComparer.Ordinal);
        private bool _isCategoryTreeDropdownOpen;
        private string _selectedCategoryTreePath = string.Empty;
        private System.Windows.Media.ImageSource? _selectedCategoryTreeIcon;
        private readonly ObservableCollection<string> _typeOptions = new() { "分类", "数据" };
        private string _formType = "数据";
        private bool _suppressCategorySelectionSync;
        private string? _pendingCategoryFocus;
        private TCategory? _bulkToggleCurrentCategory;
        private bool _isBusinessLevelDeleteActive;

        public bool IsDeleteAllActive => _isBusinessLevelDeleteActive;

        void IBulkToggleSelectionHost.OnTreeNodeSelected(TreeNodeItem? node)
        {
            if (node?.Tag is TCategory category)
            {
                _bulkToggleCurrentCategory = category;
                _isBusinessLevelDeleteActive = false;
                SetSelectedCategoryNodeForBatch(node);
            }
            else if (node == null)
            {
                _bulkToggleCurrentCategory = null;
                _isBusinessLevelDeleteActive = false;
                SetSelectedCategoryNodeForBatch(null);
            }
            else
            {
                _bulkToggleCurrentCategory = null;
                SetSelectedCategoryNodeForBatch(null);
            }

            UpdateBulkToggleState();
            OnPropertyChanged(nameof(IsDeleteAllActive));
        }

        void IBulkToggleSelectionHost.OnBusinessActivated()
        {
            _isBusinessLevelDeleteActive = true;
            _bulkToggleCurrentCategory = null;
            SetSelectedCategoryNodeForBatch(null);
            UpdateBulkToggleState();
            OnPropertyChanged(nameof(IsDeleteAllActive));
            TM.App.Log($"[{GetType().Name}] 业务导航双击激活：IsDeleteAllActive=true");
        }

        private volatile bool _serviceInitialized;

        private readonly IAITextGenerationService _aiTextGenerationService;
        private readonly AIService _aiService;
        private readonly VersionTrackingService _versionTrackingService;
        private readonly TM.Services.Framework.AI.SemanticKernel.SKChatService _skChatService;
        private readonly PanelCommunicationService _panelCommunicationService;

        protected TService Service { get; }

        public bool IsCreateMode { get; protected set; } = true;

        protected DataManagementViewModelBase()
        {
            Service = ServiceLocator.Get<TService>();
            _aiService = ServiceLocator.Get<AIService>();
            _aiTextGenerationService = _aiService;
            _versionTrackingService = ServiceLocator.Get<VersionTrackingService>();
            _skChatService = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
            _panelCommunicationService = ServiceLocator.Get<PanelCommunicationService>();
            _panelCommunicationService.BusinessDataCleared += OnBusinessDataCleared;

            CategorySelectionTree = new RangeObservableCollection<TreeNodeItem>();
            CategoryTreeNodeSelectCommand = new RelayCommand(param => HandleCategoryTreeNodeSelected(param as TreeNodeItem));

            ShowAIGenerateButton = true;
            IsAIGenerateEnabled = false;

            _deleteAllCommand = new AsyncRelayCommand(ExecuteDeleteAllInternalAsync, CanExecuteDeleteAll);
            _refreshCommand = new RelayCommand(ExecuteRefreshInternal);
            _cancelBatchGenerationCommand = new RelayCommand(CancelBatchGeneration, () => IsBatchGenerating && !IsBatchCancelRequested);

            InitializeAndRefreshAsync()
                .SafeFireAndForget(ex => TM.App.Log($"[{GetType().Name}] 初始化失败: {ex.Message}"));
        }

        protected override void RefreshTreeData()
        {
            if (!_serviceInitialized) return;
            base.RefreshTreeData();
        }

        private async System.Threading.Tasks.Task InitializeAndRefreshAsync()
        {
            try
            {
                if (Service is TM.Framework.Common.Services.IAsyncInitializable initializable)
                {
                    await initializable.InitializeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] Service.InitializeAsync失败: {ex.Message}");
            }

            _serviceInitialized = true;

            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ResetRefreshDebounce();
                    RefreshTreeData();
                    UpdateBulkToggleState();
                    OnAfterInitializeRefresh();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 初始化刷新失败: {ex.Message}");
            }
        }

        protected virtual void OnAfterInitializeRefresh() { }

        public ObservableCollection<string> TypeOptions => _typeOptions;

        public string FormType
        {
            get => _formType;
            set
            {
                if (_formType != value)
                {
                    _formType = value;
                    OnPropertyChanged();
                }
            }
        }

        protected List<string> CollectCategoryAndChildrenNames(string categoryName)
        {
            var result = new List<string>();
            var allCategories = GetAllCategoriesFromService() ?? new List<TCategory>();

            void Collect(string name)
            {
                result.Add(name);

                var children = allCategories
                    .Where(c => string.Equals(c.ParentCategory, name, StringComparison.Ordinal))
                    .ToList();

                foreach (var child in children)
                {
                    Collect(child.Name);
                }
            }

            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                Collect(categoryName);
            }

            return result;
        }

        public ICommand DeleteAllCommand => _deleteAllCommand;

        public ICommand RefreshCommand => _refreshCommand;

        public ICommand ToggleSelectedEnabledCommand => _toggleSelectedEnabledCommand ??= new RelayCommand(param => ExecuteToggleEnabled(param as TreeNodeItem));

        public virtual string? AIGenerateDisabledReason => null;

        public ICommand CancelBatchGenerationCommand => _cancelBatchGenerationCommand;

        private bool _isBatchCancelRequested;
        public bool IsBatchCancelRequested
        {
            get => _isBatchCancelRequested;
            protected set
            {
                if (_isBatchCancelRequested != value)
                {
                    _isBatchCancelRequested = value;
                    OnPropertyChanged();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        public ICommand BulkToggleCommand => _bulkToggleCommand ??= new RelayCommand(_ => ExecuteBulkToggle());

        private string _bulkToggleButtonText = "一键启用";
        public string BulkToggleButtonText
        {
            get => _bulkToggleButtonText;
            set { if (_bulkToggleButtonText != value) { _bulkToggleButtonText = value; OnPropertyChanged(); } }
        }

        private bool _isBulkToggleEnabled = true;
        public bool IsBulkToggleEnabled
        {
            get => _isBulkToggleEnabled;
            set { if (_isBulkToggleEnabled != value) { _isBulkToggleEnabled = value; OnPropertyChanged(); } }
        }

        private string _bulkToggleToolTip = string.Empty;
        public string BulkToggleToolTip
        {
            get => _bulkToggleToolTip;
            set { if (_bulkToggleToolTip != value) { _bulkToggleToolTip = value; OnPropertyChanged(); } }
        }

        private bool _isDependencyOutdated;
        public bool IsDependencyOutdated
        {
            get => _isDependencyOutdated;
            set { if (_isDependencyOutdated != value) { _isDependencyOutdated = value; OnPropertyChanged(); } }
        }

        private string _outdatedDependencyNames = string.Empty;
        public string OutdatedDependencyNames
        {
            get => _outdatedDependencyNames;
            set { if (_outdatedDependencyNames != value) { _outdatedDependencyNames = value; OnPropertyChanged(); } }
        }

        private ICommand? _regenerateCommand;
        public ICommand RegenerateCommand => _regenerateCommand ??= new RelayCommand(ExecuteRegenerate, CanExecuteRegenerate);

        protected virtual bool CanExecuteRegenerate() => IsDependencyOutdated && CanExecuteAIGenerate();

        protected virtual void ExecuteRegenerate()
        {
            if (!IsDependencyOutdated) return;

            var result = StandardDialog.ShowConfirm(
                "确认重新生成",
                $"上游数据({OutdatedDependencyNames})已更新，是否立即重新生成当前数据？\n\n注意：重新生成将覆盖当前内容。");

            if (result)
            {
                TM.App.Log($"[{GetType().Name}] 用户确认重新生成，触发 AI 生成");
                if (AIGenerateCommand?.CanExecute(null) == true)
                {
                    AIGenerateCommand.Execute(null);
                }
            }
        }

        protected TCategory? GetBulkToggleCurrentCategory() => _bulkToggleCurrentCategory;

        protected virtual void UpdateBulkToggleState()
        {
            IsBulkToggleEnabled = true;
            BulkToggleToolTip = string.Empty;

            if (_bulkToggleCurrentCategory != null && _bulkToggleCurrentCategory.Level == 1)
            {
                var allEnabled = IsAllEnabledUnderCategory(_bulkToggleCurrentCategory.Name);
                BulkToggleButtonText = allEnabled ? "一键禁用" : "一键启用";
            }
            else
            {
                var allEnabled = IsAllEnabled();
                BulkToggleButtonText = allEnabled ? "一键禁用" : "一键启用";
            }

            _deleteAllCommand.RaiseCanExecuteChanged();
        }

        private bool IsAllEnabled()
        {
            var categories = GetAllCategoriesFromService();
            var data = GetAllDataItems();
            if (!categories.Any() && !data.Any()) return false;
            return categories.All(c => c.IsEnabled) && data.All(d => GetDataIsEnabled(d));
        }

        private bool IsAllEnabledUnderCategory(string rootCategoryName)
        {
            var names = CollectCategoryAndChildrenNames(rootCategoryName);
            if (names.Count == 0) return false;
            var set = new HashSet<string>(names, StringComparer.Ordinal);
            var categories = GetAllCategoriesFromService();
            var allCategoriesEnabled = categories.Where(c => set.Contains(c.Name)).All(c => c.IsEnabled);
            var allDataEnabled = GetAllDataItems().Where(d => set.Contains(GetDataCategory(d))).All(d => GetDataIsEnabled(d));
            return allCategoriesEnabled && allDataEnabled;
        }

        private void ExecuteToggleEnabled(TreeNodeItem? node)
        {
            try
            {
                if (node == null)
                {
                    return;
                }

                var serviceBase = Service as ModuleServiceBase<TCategory, TData>;
                if (serviceBase == null)
                {
                    GlobalToast.Warning("操作失败", "服务未就绪");
                    return;
                }

                if (node.Tag is TCategory category)
                {
                    var names = CollectCategoryAndChildrenNames(category.Name);
                    if (names.Count == 0)
                    {
                        GlobalToast.Warning("提示", "未找到可操作的分类");
                        return;
                    }

                    var newEnabled = !category.IsEnabled;

                    if (newEnabled && !CheckBulkEnableWarning(names))
                    {
                        return;
                    }

                    var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
                    var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);
                    RefreshTreeAndCategorySelection();
                    UpdateBulkToggleState();
                    OnBulkToggleCompleted(newEnabled);
                    GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
                    return;
                }

                if (node.Tag is TData data)
                {
                    var currentEnabled = data.IsEnabled;
                    var newEnabled = !currentEnabled;

                    if (newEnabled && RequiresAsyncEnableVerification)
                    {
                        OnDataEnableRequested(data);
                        return;
                    }

                    data.IsEnabled = newEnabled;
                    serviceBase.UpdateData(data);

                    OnDataEnabledChanged(data, newEnabled);

                    UpdateBulkToggleState();
                    GlobalToast.Success(newEnabled ? "已启用" : "已禁用", "已更新选中条目");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 启用/禁用失败: {ex.Message}");
                GlobalToast.Error("操作失败", $"操作失败：{TrimForToast(ex.Message)}");
            }
        }

        protected virtual bool GetDataIsEnabled(TData data)
        {
            return data?.IsEnabled ?? false;
        }

        protected virtual void OnDataEnabledChanged(TData data, bool isEnabled)
        {
        }

        protected virtual bool RequiresAsyncEnableVerification => false;

        protected virtual void OnDataEnableRequested(TData data)
        {
        }

        protected virtual void OnBulkToggleCompleted(bool newEnabled)
        {
        }

        protected virtual void ExecuteBulkToggle()
        {
            try
            {
                var serviceBase = Service as ModuleServiceBase<TCategory, TData>;
                if (serviceBase == null) return;

                List<string> names;
                bool allEnabled;

                if (_bulkToggleCurrentCategory != null && _bulkToggleCurrentCategory.Level == 1)
                {
                    names = CollectCategoryAndChildrenNames(_bulkToggleCurrentCategory.Name);
                    if (names.Count == 0) { GlobalToast.Warning("提示", "未找到可操作的分类"); return; }
                    allEnabled = IsAllEnabledUnderCategory(_bulkToggleCurrentCategory.Name);
                }
                else
                {
                    var categories = GetAllCategoriesFromService();
                    names = categories.Select(c => c.Name).ToList();
                    if (names.Count == 0) { GlobalToast.Warning("提示", "暂无分类数据"); return; }
                    allEnabled = IsAllEnabled();
                }

                var newEnabled = !allEnabled;

                if (newEnabled && !CheckBulkEnableWarning(names))
                {
                    return;
                }

                var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
                var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);

                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();
                OnBulkToggleCompleted(newEnabled);

                GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 一键启用/禁用失败: {ex.Message}");
                GlobalToast.Error("操作失败", $"操作失败：{TrimForToast(ex.Message)}");
            }
        }

        protected virtual void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            IsAIGenerateEnabled = hasSelection;
        }

        public RangeObservableCollection<TreeNodeItem> CategorySelectionTree { get; }

        public bool IsCategoryTreeDropdownOpen
        {
            get => _isCategoryTreeDropdownOpen;
            set
            {
                if (_isCategoryTreeDropdownOpen != value)
                {
                    _isCategoryTreeDropdownOpen = value;
                    OnPropertyChanged();
                }
            }
        }

        public void ForceRebuildCategorySelectionTree()
        {
            RebuildCategorySelectionTree();
        }

        public string SelectedCategoryTreePath
        {
            get => _selectedCategoryTreePath;
            set
            {
                if (_selectedCategoryTreePath != value)
                {
                    _selectedCategoryTreePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public System.Windows.Media.ImageSource? SelectedCategoryTreeIcon
        {
            get => _selectedCategoryTreeIcon;
            set
            {
                if (!ReferenceEquals(_selectedCategoryTreeIcon, value))
                {
                    _selectedCategoryTreeIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand CategoryTreeNodeSelectCommand { get; }

        private List<TData>? _dataSnapshot;
        private Dictionary<string, string?>? _categoryNameToIdIndex;

        protected override List<TCategory> GetAllCategories()
        {
            var categories = GetAllCategoriesFromService();
            _dataSnapshot = GetAllDataItems();
            _categoryNameToIdIndex = new Dictionary<string, string?>(categories.Count, StringComparer.Ordinal);
            foreach (var c in categories)
                _categoryNameToIdIndex.TryAdd(c.Name, c.Id);
            return categories;
        }

        protected override List<TData> GetChildrenDataForCategory(string categoryName)
        {
            var allData = _dataSnapshot ?? GetAllDataItems();

            string? categoryId = null;
            if (_categoryNameToIdIndex != null)
                _categoryNameToIdIndex.TryGetValue(categoryName, out categoryId);
            else
                categoryId = GetAllCategoriesFromService()
                    .FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal))?.Id;

            IEnumerable<TData> filteredData;
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                filteredData = allData.Where(d =>
                    (!string.IsNullOrWhiteSpace(d.CategoryId) && d.CategoryId == categoryId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && GetDataCategory(d) == categoryName));
            }
            else
            {
                filteredData = allData.Where(d => GetDataCategory(d) == categoryName);
            }

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                return SearchHelper.FilterAndSort(
                    filteredData,
                    SearchKeyword,
                    d => d.Name,
                    d => GetSearchAdditionalFields(d));
            }

            return filteredData.ToList();
        }

        protected override void OnTreeAfterAction(string? action)
        {
            if (action == "Reorder")
            {
                UpdateCategoryOrderAndSave();
                return;
            }

            if (action == "Save" || action == "Edit")
            {
                _deleteAllCommand.RaiseCanExecuteChanged();
                return;
            }

            base.OnTreeAfterAction(action);
            _deleteAllCommand.RaiseCanExecuteChanged();

            if (action == "Delete" || action == "DeleteAll")
            {
                _currentEditingData = null;
                _currentEditingCategory = null;
                UpdateAIGenerateButtonState(hasSelection: false);
            }
        }

        private void UpdateCategoryOrderAndSave()
        {
            try
            {
                int order = 0;
                UpdateOrderRecursive(TreeData, ref order);

                if (Service is TM.Framework.Common.Services.ICategorySaver saver)
                {
                    saver.SaveAllCategories();
                    TM.App.Log($"[{GetType().Name}] 分类排序已保存");
                }
                else
                {
                    TM.App.Log($"[{GetType().Name}] Service未实现ICategorySaver接口");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 保存分类排序失败: {ex.Message}");
            }
        }

        private void UpdateOrderRecursive(ObservableCollection<TreeNodeItem> nodes, ref int order)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is TCategory category)
                {
                    category.Order = order++;
                }

                if (node.Children.Count > 0)
                {
                    UpdateOrderRecursive(node.Children, ref order);
                }
            }
        }

        protected void EnterCreateMode()
        {
            IsCreateMode = true;
            ResetCoherenceState();
            UpdateAIGenerateButtonState(hasSelection: false);
            OnPropertyChanged(nameof(IsCreateMode));
        }

        protected void EnterEditMode()
        {
            IsCreateMode = false;
            UpdateAIGenerateButtonState(hasSelection: true);
            OnPropertyChanged(nameof(IsCreateMode));
        }

        protected void OnDataItemLoaded()
        {
            EnterEditMode();
            ResetCoherenceState();
            CheckDependencyOutdated();
        }

        private string _lastShownOutdatedNames = string.Empty;

        private void CheckDependencyOutdated()
        {
            IsDependencyOutdated = false;
            OutdatedDependencyNames = string.Empty;

            var editingData = GetCurrentEditingDataObject();
            if (editingData is IDependencyTracked tracked &&
                tracked.DependencyModuleVersions?.Count > 0)
            {
                var outdated = _versionTrackingService
                    .CheckOutdatedDependencies(tracked.DependencyModuleVersions);

                if (outdated.Count > 0)
                {
                    var names = DependencyConfig.GetDisplayNames(outdated);
                    IsDependencyOutdated = true;
                    OutdatedDependencyNames = names;
                    if (!string.Equals(_lastShownOutdatedNames, names, StringComparison.Ordinal))
                    {
                        _lastShownOutdatedNames = names;
                        GlobalToast.Warning("数据可能过期",
                            $"上游数据({names})已更新，可点击“重新生成”按钮刷新");
                        TM.App.Log($"[{GetType().Name}] 检测到过期依赖: {names}");
                    }
                }
                else
                {
                    _lastShownOutdatedNames = string.Empty;
                }
            }
        }

        protected virtual bool CheckConsistencyBeforeGenerate()
        {
            var vmName = GetType().Name;
            var moduleName = GetModuleNameForVersionTracking();

            var missingUpstream = CheckUpstreamReady();
            if (missingUpstream.Count > 0)
            {
                var names = DependencyConfig.GetDisplayNames(missingUpstream);
                var result = StandardDialog.ShowConfirm(
                    "上游数据缺失",
                    $"以下模块尚未创建内容：{names}\n\n建议先完成上游模块，是否仍要继续？");

                if (!result)
                {
                    TM.App.Log($"[{vmName}] 用户取消生成，缺失上游: {names}");
                    return false;
                }
                TM.App.Log($"[{vmName}] 用户选择继续生成，忽略缺失上游: {names}");
            }

            if (IsDependencyOutdated && !string.IsNullOrEmpty(OutdatedDependencyNames))
            {
                var result = StandardDialog.ShowConfirm(
                    "依赖已过期",
                    $"上游数据({OutdatedDependencyNames})已更新，继续生成可能导致内容不一致。\n\n是否继续？");

                if (!result)
                {
                    TM.App.Log($"[{vmName}] 用户取消生成，依赖过期: {OutdatedDependencyNames}");
                    return false;
                }
                TM.App.Log($"[{vmName}] 用户选择继续生成，忽略过期依赖: {OutdatedDependencyNames}");
            }

            return true;
        }

        protected virtual List<string> CheckUpstreamReady()
        {
            var moduleName = GetModuleNameForVersionTracking();
            if (string.IsNullOrEmpty(moduleName))
                return new List<string>();

            var missing = new List<string>();
            var dependencies = DependencyConfig.GetDependencies(moduleName);

            foreach (var dep in dependencies)
            {
                var version = _versionTrackingService.GetModuleVersion(dep);
                if (version == 0)
                {
                    missing.Add(dep);
                }
            }

            return missing;
        }

        protected bool CheckBulkEnableWarning(List<string> categoryNames)
        {
            return true;
        }

        protected virtual int GetMaxCategoryCount() => int.MaxValue;
        protected virtual int GetMaxDataCountPerCategory() => int.MaxValue;
        protected virtual string GetCategoryLimitMessage() => "当前模块只允许一个分类，请先删除现有分类。";
        protected virtual string GetDataLimitMessage() => "当前分类只允许一条数据，请先删除现有数据。";

        protected bool CheckBeforeEnable(string? dataBookAnalysisId, string dataName)
        {
            return true;
        }

        protected override void OnTreeDataRefreshed()
        {
            base.OnTreeDataRefreshed();
            RebuildCategorySelectionTree();

            if (!string.IsNullOrWhiteSpace(_pendingCategoryFocus))
            {
                var category = _pendingCategoryFocus;
                _pendingCategoryFocus = null;
                FocusCategoryNode(category, updatePending: false);
            }
        }

        private bool CanExecuteDeleteAll()
        {
            return !_isDeletingAll;
        }

        private bool _isDeletingAll;

        private async System.Threading.Tasks.Task ExecuteDeleteAllInternalAsync()
        {
            try
            {
                if (_isDeletingAll)
                    return;

                if (_bulkToggleCurrentCategory != null)
                {
                    var categoryName = _bulkToggleCurrentCategory.Name;
                    var names = CollectCategoryAndChildrenNames(categoryName);

                    if (Service is not TM.Framework.Common.Services.ICascadeDeleteCategoryService cascadeSvc)
                    {
                        GlobalToast.Warning("不支持", "当前模块未实现按分类删除能力");
                        return;
                    }

                    var isRootBuiltIn = _bulkToggleCurrentCategory.IsBuiltIn;
                    var (catDataCount, deletableCatCount) = await System.Threading.Tasks.Task.Run(() =>
                    {
                        var allCats = GetAllCategoriesFromService() ?? new List<TCategory>();
                        if (isRootBuiltIn)
                        {
                            var dataCount = GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), categoryName, StringComparison.Ordinal));
                            return (dataCount, 0);
                        }
                        else
                        {
                            var set = new HashSet<string>(names, StringComparer.Ordinal);
                            var dataCount = GetAllDataItems().Count(d => set.Contains(GetDataCategory(d)));
                            var deletable = names.Count(n => !allCats.Any(c => c.Name == n && c.IsBuiltIn));
                            return (dataCount, deletable);
                        }
                    });

                    if (deletableCatCount == 0 && catDataCount == 0)
                    {
                        GlobalToast.Warning("无法删除", $"分类『{categoryName}』下没有可删除的分类或数据");
                        return;
                    }

                    var confirmMsg = isRootBuiltIn
                        ? $"确定要清空内置分类『{categoryName}』下的所有数据吗？\n\n将删除：\n• {catDataCount} 条数据\n\n分类本身及子分类将保留，此操作不可撤销！"
                        : $"确定要删除分类『{categoryName}』及其所有子分类和数据吗？\n\n将删除：\n• {deletableCatCount} 个分类\n• {catDataCount} 条数据\n\n系统内置分类将保留，此操作不可撤销！";
                    var confirmed = StandardDialog.ShowConfirm(confirmMsg, "确认删除分类");
                    if (!confirmed) return;

                    _isDeletingAll = true;
                    _deleteAllCommand.RaiseCanExecuteChanged();

                    (int catDeleted, int dataDeleted) deleteResult;
                    try
                    {
                        deleteResult = await System.Threading.Tasks.Task.Run(() =>
                            cascadeSvc.CascadeDeleteCategory(categoryName));
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{GetType().Name}] CascadeDeleteCategory 执行失败: {ex.Message}");
                        GlobalToast.Error("删除失败", "删除分类及其子内容时出现错误，请稍后重试");
                        return;
                    }

                    var (catDeleted, dataDeleted) = deleteResult;

                    if (catDeleted <= 0 && dataDeleted <= 0)
                    {
                        GlobalToast.Warning("删除失败", "未删除任何内容（可能为内置分类保护或数据为空）");
                        return;
                    }

                    _bulkToggleCurrentCategory = null;
                    SetSelectedCategoryNodeForBatch(null);
                    UpdateBulkToggleState();

                    RefreshTreeAndCategorySelection();
                    OnAfterDeleteAll(dataDeleted);
                    var toastMsg = catDeleted > 0
                        ? $"已删除 {catDeleted} 个分类及其 {dataDeleted} 条数据"
                        : $"已清空 {dataDeleted} 条数据（内置分类已保留）";
                    GlobalToast.Success("删除成功", toastMsg);
                    return;
                }

                var (dataCount, userCatCount) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var dc = GetAllDataItems().Count;
                    var cats = GetAllCategoriesFromService();
                    var ucc = cats?.Count(c => !c.IsBuiltIn) ?? 0;
                    return (dc, ucc);
                });

                if (userCatCount == 0 && dataCount == 0)
                {
                    GlobalToast.Warning("无法删除", "当前没有用户自建的分类或数据，系统内置分类不可删除");
                    return;
                }

                var confirmed2 = StandardDialog.ShowConfirm(
                    $"确定要执行「全部删除」吗？\n\n将删除：\n• {userCatCount} 个用户自建分类\n• {dataCount} 条数据\n\n系统内置分类将保留，此操作不可撤销！",
                    "确认全部删除");
                if (!confirmed2) return;

                _isDeletingAll = true;
                _deleteAllCommand.RaiseCanExecuteChanged();

                var deletedCount = await System.Threading.Tasks.Task.Run(() =>
                {
                    var count = ClearAllDataItems();
                    if (Service is TM.Framework.Common.Services.IClearAllService clearService)
                        clearService.ClearAllData();
                    return count;
                });
                TM.App.Log($"[{GetType().Name}] 已删除全部内容，数量={deletedCount}");

                _isBusinessLevelDeleteActive = false;
                OnPropertyChanged(nameof(IsDeleteAllActive));

                RefreshTreeData();
                OnAfterDeleteAll(deletedCount);

                GlobalToast.Success("删除成功", $"已删除 {userCatCount} 个用户自建分类及其 {deletedCount} 条数据");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 删除全部内容失败: {ex.Message}");
                GlobalToast.Error("清空失败", "删除全部内容时出现错误，请稍后重试");
            }
            finally
            {
                _isDeletingAll = false;
                _deleteAllCommand.RaiseCanExecuteChanged();
            }
        }

        protected void NotifyDataCollectionChanged()
        {
            _deleteAllCommand.RaiseCanExecuteChanged();
        }

        private void ExecuteRefreshInternal()
        {
            try
            {
                TM.App.Log($"[{GetType().Name}] 用户触发数据刷新");
                OnRefreshRequested();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 刷新数据失败: {ex.Message}");
                GlobalToast.Error("刷新失败", "刷新数据时出现错误，请稍后重试");
            }
        }

        protected virtual void OnRefreshRequested()
        {
            RefreshTreeData();
        }

        protected void RefreshTreeAndCategorySelection()
        {
            RefreshTreeData();
            ForceRebuildCategorySelectionTree();
        }

        protected void FocusOnDataItem(TData? data)
        {
            if (data == null)
            {
                return;
            }

            _pendingCategoryFocus = null;
            FocusTreeNode(node => ReferenceEquals(node.Tag, data));
        }

        protected void FocusCategoryNode(string? categoryName, bool updatePending = true)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return;
            }

            FocusTreeNode(node =>
            {
                if (node.Tag is ICategory category)
                {
                    return string.Equals(category.Name, categoryName, StringComparison.Ordinal);
                }

                return false;
            });

            if (updatePending)
            {
                _pendingCategoryFocus = categoryName;
            }
        }

        protected static string AlignSelection(string? currentValue, ObservableCollection<string> options)
        {
            if (options == null || options.Count == 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return string.Empty;
            }

            var match = options.FirstOrDefault(option => string.Equals(option, currentValue, StringComparison.Ordinal));
            return match ?? options[0];
        }

        protected void OnCategoryValueChanged(string? categoryName)
        {
            if (_suppressCategorySelectionSync)
            {
                return;
            }

            UpdateCategorySelectionDisplayCore(categoryName);
            _pendingCategoryFocus = categoryName;
        }

        protected abstract string? GetCurrentCategoryValue();

        protected abstract void ApplyCategorySelection(string categoryName);

        protected abstract int ClearAllDataItems();

        protected virtual void OnAfterDeleteAll(int deletedCount)
        {
        }

        protected override TreeNodeItem ConvertDataToTreeNode(TData data)
        {
            return ConvertToTreeNode(data);
        }

        protected abstract List<TCategory> GetAllCategoriesFromService();

        protected abstract List<TData> GetAllDataItems();

        protected abstract string GetDataCategory(TData data);

        protected abstract TreeNodeItem ConvertToTreeNode(TData data);

        protected virtual string[] GetSearchAdditionalFields(TData data)
        {
            return Array.Empty<string>();
        }

        protected abstract string DefaultDataIcon { get; }

        protected virtual object? GetCurrentEditingDataObject() => _currentEditingData;

        protected virtual string GetModuleNameForVersionTracking() => string.Empty;

        private Dictionary<string, int>? _pendingDependencyVersions;

        private void RecordDependencyVersions()
        {
            var editingData = GetCurrentEditingDataObject();
            var moduleName = GetModuleNameForVersionTracking();

            if (string.IsNullOrEmpty(moduleName)) return;

            var snapshot = _versionTrackingService.GetDependencySnapshot(moduleName);

            if (editingData is IDependencyTracked tracked)
            {
                tracked.DependencyModuleVersions = snapshot;
                TM.App.Log($"[{GetType().Name}] dep recorded: {moduleName}");
            }
            else
            {
                _pendingDependencyVersions = snapshot;
                TM.App.Log($"[{GetType().Name}] dep pending: {moduleName}");
            }
        }

        private void ApplyPendingDependencyVersions(object? newData)
        {
            if (_pendingDependencyVersions != null && newData is IDependencyTracked tracked)
            {
                tracked.DependencyModuleVersions = _pendingDependencyVersions;
                SaveCurrentEditingData();
                TM.App.Log($"[{GetType().Name}] dep applied");
            }
            _pendingDependencyVersions = null;
        }

        protected virtual void SaveCurrentEditingData() { }

        protected virtual string GetCategoryIconForSave(string formIcon)
        {
            return string.IsNullOrWhiteSpace(formIcon) ? "Icon.Folder" : formIcon;
        }

        protected virtual string GetDataIconForSave(string formIcon)
        {
            return DefaultDataIcon;
        }

        private TreeNodeItem? _selectedCategoryNodeForBatch;

        protected bool IsBatchModeActive { get; private set; }

        private System.Threading.CancellationTokenSource? _batchCancellationTokenSource;

        private readonly List<string> _batchGeneratedNames = new();

        private readonly List<string> _batchGeneratedIndex = new();

        protected bool _lastBatchWasCancelled;

        protected bool _lastBatchStoppedBySlotExhausted;
        protected bool _lastBatchKeyExhausted;

        private IProgress<string>? _pipelineBatchProgress;
        private int _pipelineLastGeneratedCount;
        private int _pipelineLastTotalCount;
        private int _pipelineLastGeneratedThisRunCount;
        private int _pipelineProgressBaseCount;
        private int _pipelineProgressTargetCount;

        private HashSet<string>? _sessionDbNamesCache;

        private void EndBusinessSessionAndResetBatchNames()
        {
            _aiService.EndBusinessSessionsByPrefix(GetType().Name);
            _batchGeneratedNames.Clear();
            _batchGeneratedIndex.Clear();
            _sessionDbNamesCache = null;
            _cachedBatchContextText = string.Empty;
        }

    }
}

