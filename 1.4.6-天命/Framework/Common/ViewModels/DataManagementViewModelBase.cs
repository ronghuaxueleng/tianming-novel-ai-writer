using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Models;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Framework.Settings;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.VersionTracking;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.Common.ViewModels
{
    internal static class AINavigationSessionSaveConfirmState
    {
        internal static bool SuppressThisRun;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public abstract class DataManagementViewModelBase<TData, TCategory, TService> : TreeDataViewModelBase<TData, TCategory>, IBulkToggleSelectionHost, IAIGeneratingState, IDataTreeHost
        where TData : class, IDataItem
        where TCategory : class, ICategory
        where TService : class
    {
        protected TData? _currentEditingData;
        protected TCategory? _currentEditingCategory;

        private const string SuppressSaveEndAISessionConfirmSettingKey = "ui.ai.save_end_session_confirm.suppress";

        private const double MissingFieldRetryThreshold = 0.4;
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
                dispatcher.BeginInvoke((Action)Refresh);
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
        private readonly List<TreeNodeItem> _lastCategorySelectionPath = new();
        private Dictionary<string, TCategory> _categoryLookup = new(StringComparer.Ordinal);
        private bool _isCategoryTreeDropdownOpen;
        private string _selectedCategoryTreePath = string.Empty;
        private string _selectedCategoryTreeIcon = string.Empty;
        private readonly ObservableCollection<string> _typeOptions = new() { "分类", "数据" };
        private string _formType = "分类";
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

        private readonly IAITextGenerationService _aiTextGenerationService;
        private readonly AIService _aiService;
        private readonly VersionTrackingService _versionTrackingService;
        private readonly IWorkScopeService _workScopeService;
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
            _workScopeService = ServiceLocator.Get<IWorkScopeService>();
            _skChatService = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
            _panelCommunicationService = ServiceLocator.Get<PanelCommunicationService>();
            System.Windows.WeakEventManager<PanelCommunicationService, EventArgs>
                .AddHandler(_panelCommunicationService, nameof(PanelCommunicationService.BusinessDataCleared), OnBusinessDataCleared);

            CategorySelectionTree = new ObservableCollection<TreeNodeItem>();
            CategoryTreeNodeSelectCommand = new RelayCommand(param => HandleCategoryTreeNodeSelected(param as TreeNodeItem));

            ShowAIGenerateButton = true;
            IsAIGenerateEnabled = false;

            _deleteAllCommand = new AsyncRelayCommand(ExecuteDeleteAllInternalAsync, CanExecuteDeleteAll);
            _refreshCommand = new RelayCommand(ExecuteRefreshInternal);
            _cancelBatchGenerationCommand = new RelayCommand(CancelBatchGeneration, () => IsBatchGenerating && !IsBatchCancelRequested);

            _ = InitializeAndRefreshAsync();
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

            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RefreshTreeData();
                    ForceRebuildCategorySelectionTree();
                    UpdateBulkToggleState();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 初始化刷新失败: {ex.Message}");
            }
        }

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

            void Collect(string name)
            {
                result.Add(name);

                var allCategories = GetAllCategoriesFromService() ?? new List<TCategory>();
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
            private set
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

        public ICommand RegenerateCommand => new RelayCommand(ExecuteRegenerate, CanExecuteRegenerate);

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

        protected void SetBulkToggleCurrentCategory(TCategory? category)
        {
            _bulkToggleCurrentCategory = category;
            UpdateBulkToggleState();
        }

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

                    if (newEnabled && !CheckBulkEnableScopeWarning(names))
                    {
                        return;
                    }

                    var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
                    var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);
                    RefreshTreeAndCategorySelection();
                    UpdateBulkToggleState();
                    GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
                    return;
                }

                if (node.Tag is TData data)
                {
                    var currentEnabled = data.IsEnabled;
                    var newEnabled = !currentEnabled;

                    if (newEnabled && !CheckScopeBeforeEnable(GetDataSourceBookId(data), GetDataName(data)))
                    {
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
                GlobalToast.Error("操作失败", ex.Message);
            }
        }

        protected virtual bool GetDataIsEnabled(TData data)
        {
            return data?.IsEnabled ?? false;
        }

        protected virtual void OnDataEnabledChanged(TData data, bool isEnabled)
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

                if (newEnabled && !CheckBulkEnableScopeWarning(names))
                {
                    return;
                }

                var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
                var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);

                RefreshTreeAndCategorySelection();
                UpdateBulkToggleState();

                GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 一键启用/禁用失败: {ex.Message}");
                GlobalToast.Error("操作失败", ex.Message);
            }
        }

        protected virtual void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            IsAIGenerateEnabled = hasSelection;
        }

        public ObservableCollection<TreeNodeItem> CategorySelectionTree { get; }

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

        public string SelectedCategoryTreeIcon
        {
            get => _selectedCategoryTreeIcon;
            set
            {
                if (_selectedCategoryTreeIcon != value)
                {
                    _selectedCategoryTreeIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand CategoryTreeNodeSelectCommand { get; }

        private void OnServiceCategoriesChanged(object? sender, EventArgs e)
        {
            void Refresh()
            {
                RefreshTreeData();
                _deleteAllCommand.RaiseCanExecuteChanged();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke((Action)Refresh);
            }
            else
            {
                Refresh();
            }
        }

        protected override List<TCategory> GetAllCategories()
        {
            return GetAllCategoriesFromService();
        }

        protected override List<TData> GetChildrenDataForCategory(string categoryName)
        {
            var allData = GetAllDataItems();

            var filteredData = allData.Where(d => GetDataCategory(d) == categoryName);

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                filteredData = filteredData.Where(d => MatchesSearchKeyword(d, SearchKeyword));
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

        protected virtual string NewItemTypeName => string.Empty;

        protected void ExecuteAddWithCreateMode()
        {
            EnterCreateMode();

            if (!string.IsNullOrEmpty(NewItemTypeName))
            {
                var typeName = NewItemTypeName;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => GlobalToast.Info("新建", $"选择「主页导航」创建分类，选择具体分类创建{typeName}")));
            }
        }

        protected bool ExecuteSaveWithCreateEditMode(
            Func<bool> validateForm,
            Action createCategoryCore,
            Action createDataCore,
            Func<bool> hasEditingCategory,
            Func<bool> hasEditingData,
            Action updateCategoryCore,
            Action updateDataCore)
        {
            if (validateForm == null)
                throw new ArgumentNullException(nameof(validateForm));

            if (createCategoryCore == null)
                throw new ArgumentNullException(nameof(createCategoryCore));

            if (createDataCore == null)
                throw new ArgumentNullException(nameof(createDataCore));

            if (hasEditingCategory == null)
                throw new ArgumentNullException(nameof(hasEditingCategory));

            if (hasEditingData == null)
                throw new ArgumentNullException(nameof(hasEditingData));

            if (updateCategoryCore == null)
                throw new ArgumentNullException(nameof(updateCategoryCore));

            if (updateDataCore == null)
                throw new ArgumentNullException(nameof(updateDataCore));

            bool isDataSaveIntent = hasEditingData()
                                    || (IsCreateMode && string.Equals(FormType, "数据", StringComparison.Ordinal));
            if (isDataSaveIntent
                && HasCoherenceHardConflict
                && string.Equals(_coherenceConflictScopeId, GetCurrentCoherenceScopeId(), StringComparison.Ordinal))
            {
                GlobalToast.Error("保存已阻止", CoherenceConflictMessage);
                return false;
            }

            if (!validateForm())
            {
                return false;
            }

            if (!ConfirmSaveWillEndAISessionIfNeeded())
            {
                return false;
            }

            var forceCreateData = !hasEditingData()
                                  && !hasEditingCategory()
                                  && string.Equals(FormType, "数据", StringComparison.Ordinal);

            if (IsCreateMode || forceCreateData)
            {
                bool isCreatingCategory = !string.Equals(FormType, "数据", StringComparison.Ordinal);

                if (isCreatingCategory)
                {
                    if (GetAllCategoriesFromService().Count >= GetMaxCategoryCount())
                    {
                        GlobalToast.Warning("创建受限", GetCategoryLimitMessage());
                        return false;
                    }
                    createCategoryCore();
                }
                else
                {
                    var currentCategory = GetCurrentCategoryValue() ?? string.Empty;
                    var count = GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), currentCategory, StringComparison.Ordinal));
                    if (count >= GetMaxDataCountPerCategory())
                    {
                        GlobalToast.Warning("创建受限", GetDataLimitMessage());
                        return false;
                    }
                    createDataCore();
                    ApplyPendingDependencyVersions(GetCurrentEditingDataObject());
                }

                IsCreateMode = false;
                OnPropertyChanged(nameof(IsCreateMode));
                RefreshTreeData();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingCategory())
            {
                if (_currentEditingCategory?.IsBuiltIn == true)
                {
                    EndBusinessSessionAndResetBatchNames();
                    GlobalToast.Success("批量保存成功", $"分类『{_currentEditingCategory.Name}』下的批量数据已全部保存");
                    return true;
                }
                updateCategoryCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingData())
            {
                updateDataCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            GlobalToast.Warning("保存失败", "请先点击\"新建\"，或在左侧选择要编辑的分类或数据");
            return false;
        }

        private bool ConfirmSaveWillEndAISessionIfNeeded()
        {
            try
            {
                if (!_aiService.HasDirtyBusinessSessionsByPrefix(GetType().Name))
                {
                    return true;
                }

                if (AINavigationSessionSaveConfirmState.SuppressThisRun)
                {
                    return true;
                }

                var settings = ServiceLocator.Get<SettingsManager>();
                if (settings.Get(SuppressSaveEndAISessionConfirmSettingKey, false))
                {
                    return true;
                }

                var dialog = new StandardDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);
                dialog.SetTitle("保存确认");
                dialog.SetIcon("⚠️");

                var fg = dialog.TryFindResource("TextPrimary") as System.Windows.Media.Brush;
                var panel = new StackPanel
                {
                    Margin = new Thickness(0)
                };

                panel.Children.Add(new TextBlock
                {
                    Text = "检测到当前业务存在未保存的AI对话上下文。\n\n保存将结束本次上下文，后续生成的连贯性可能下降。\n如果你希望继续保持连贯生成，可先继续调用AI生成，而不是保存。\n\n是否仍要继续保存？",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    MaxWidth = 520,
                    Foreground = fg
                });

                var cbThisRun = new CheckBox
                {
                    Content = "本次不再提示（重启后恢复）",
                    Margin = new Thickness(0, 12, 0, 0),
                    Foreground = fg
                };
                panel.Children.Add(cbThisRun);

                var cbRemember = new CheckBox
                {
                    Content = "记住选择（下次启动也不再提示）",
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = fg
                };
                panel.Children.Add(cbRemember);

                dialog.SetContent(new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 360
                });

                var confirmed = false;
                dialog.AddButton("取消", () => { confirmed = false; dialog.Close(); });
                dialog.AddButton("继续保存", () => { confirmed = true; dialog.Close(); }, true);
                dialog.ShowDialog();

                if (!confirmed)
                {
                    return false;
                }

                var remember = cbRemember.IsChecked == true;
                var suppressThisRun = cbThisRun.IsChecked == true;
                if (remember)
                {
                    settings.Set(SuppressSaveEndAISessionConfirmSettingKey, true);
                }

                if (remember || suppressThisRun)
                {
                    AINavigationSessionSaveConfirmState.SuppressThisRun = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 保存确认弹窗异常: {ex.Message}");
                return true;
            }
        }

        protected async System.Threading.Tasks.Task<bool> ExecuteSaveWithCreateEditModeAsync(
            Func<bool> validateForm,
            Func<System.Threading.Tasks.Task> createCategoryCore,
            Func<System.Threading.Tasks.Task> createDataCore,
            Func<bool> hasEditingCategory,
            Func<bool> hasEditingData,
            Func<System.Threading.Tasks.Task> updateCategoryCore,
            Func<System.Threading.Tasks.Task> updateDataCore)
        {
            if (validateForm == null)
                throw new ArgumentNullException(nameof(validateForm));

            if (createCategoryCore == null)
                throw new ArgumentNullException(nameof(createCategoryCore));

            if (createDataCore == null)
                throw new ArgumentNullException(nameof(createDataCore));

            if (hasEditingCategory == null)
                throw new ArgumentNullException(nameof(hasEditingCategory));

            if (hasEditingData == null)
                throw new ArgumentNullException(nameof(hasEditingData));

            if (updateCategoryCore == null)
                throw new ArgumentNullException(nameof(updateCategoryCore));

            if (updateDataCore == null)
                throw new ArgumentNullException(nameof(updateDataCore));

            bool isDataSaveIntent = hasEditingData()
                                    || (IsCreateMode && string.Equals(FormType, "数据", StringComparison.Ordinal));
            if (isDataSaveIntent
                && HasCoherenceHardConflict
                && string.Equals(_coherenceConflictScopeId, GetCurrentCoherenceScopeId(), StringComparison.Ordinal))
            {
                GlobalToast.Error("保存已阻止", CoherenceConflictMessage);
                return false;
            }

            if (!validateForm())
            {
                return false;
            }

            if (!ConfirmSaveWillEndAISessionIfNeeded())
            {
                return false;
            }

            var forceCreateData = !hasEditingData()
                                  && !hasEditingCategory()
                                  && string.Equals(FormType, "数据", StringComparison.Ordinal);

            if (IsCreateMode || forceCreateData)
            {
                bool isCreatingCategory = !string.Equals(FormType, "数据", StringComparison.Ordinal);

                if (isCreatingCategory)
                {
                    if (GetAllCategoriesFromService().Count >= GetMaxCategoryCount())
                    {
                        GlobalToast.Warning("创建受限", GetCategoryLimitMessage());
                        return false;
                    }
                    await createCategoryCore();
                }
                else
                {
                    var currentCategory = GetCurrentCategoryValue() ?? string.Empty;
                    var count = GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), currentCategory, StringComparison.Ordinal));
                    if (count >= GetMaxDataCountPerCategory())
                    {
                        GlobalToast.Warning("创建受限", GetDataLimitMessage());
                        return false;
                    }
                    await ResolveEntityReferencesBeforeSaveAsync();
                    await createDataCore();
                    ApplyPendingDependencyVersions(GetCurrentEditingDataObject());
                }

                IsCreateMode = false;
                OnPropertyChanged(nameof(IsCreateMode));
                RefreshTreeData();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingCategory())
            {
                if (_currentEditingCategory?.IsBuiltIn == true)
                {
                    EndBusinessSessionAndResetBatchNames();
                    GlobalToast.Success("批量保存成功", $"分类『{_currentEditingCategory.Name}』下的批量数据已全部保存");
                    return true;
                }
                await updateCategoryCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            if (hasEditingData())
            {
                await ResolveEntityReferencesBeforeSaveAsync();
                await updateDataCore();
                EndBusinessSessionAndResetBatchNames();
                return true;
            }

            GlobalToast.Warning("保存失败", "请先点击\"新建\"，或在左侧选择要编辑的分类或数据");
            return false;
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
                    GlobalToast.Warning("数据可能过期", 
                        $"上游数据({names})已更新，可点击“重新生成”按钮刷新");
                    TM.App.Log($"[{GetType().Name}] 检测到过期依赖: {names}");
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

        protected string? GetDataSourceBookId(TData data)
        {
            return (data as ISourceBookBound)?.SourceBookId;
        }

        protected string GetDataName(TData data)
        {
            return data?.Name ?? "未知";
        }

        protected bool CheckBulkEnableScopeWarning(List<string> categoryNames)
        {
            var currentScope = _workScopeService.CurrentSourceBookId;
            if (string.IsNullOrEmpty(currentScope))
                return true;

            var allData = GetAllDataItems();
            var crossScopeItems = allData
                .Where(d => categoryNames.Contains(GetDataCategory(d)))
                .Where(d => !GetDataIsEnabled(d))
                .Where(d => 
                {
                    var sourceBookId = GetDataSourceBookId(d);
                    return !string.IsNullOrEmpty(sourceBookId) && sourceBookId != currentScope;
                })
                .ToList();

            if (crossScopeItems.Count == 0)
                return true;

            var names = string.Join("、", crossScopeItems.Take(3).Select(GetDataName));
            if (crossScopeItems.Count > 3)
                names += $"等{crossScopeItems.Count}条";

            var result = StandardDialog.ShowConfirm(
                "跨Scope批量启用警告",
                $"以下数据属于其他项目：\n{names}\n\n" +
                $"当前Scope：{currentScope}\n\n" +
                "混合启用可能导致设定串线，是否继续？");

            if (!result)
            {
                TM.App.Log($"[Scope治理] 用户取消批量启用，跨Scope数据: {crossScopeItems.Count}条");
            }
            else
            {
                TM.App.Log($"[Scope治理] 用户确认批量混合启用，跨Scope数据: {crossScopeItems.Count}条");
            }

            return result;
        }

        protected virtual int GetMaxCategoryCount() => int.MaxValue;
        protected virtual int GetMaxDataCountPerCategory() => int.MaxValue;
        protected virtual string GetCategoryLimitMessage() => "当前模块只允许一个分类，请先删除现有分类。";
        protected virtual string GetDataLimitMessage() => "当前分类只允许一条数据，请先删除现有数据。";

        protected bool CheckScopeBeforeEnable(string? dataSourceBookId, string dataName)
        {
            var currentScope = _workScopeService.CurrentSourceBookId;

            if (string.IsNullOrEmpty(currentScope) || string.IsNullOrEmpty(dataSourceBookId))
                return true;

            if (dataSourceBookId == currentScope)
                return true;

            var result = StandardDialog.ShowConfirm(
                "跨Scope启用警告",
                $"您正在启用的「{dataName}」属于其他项目\n\n" +
                $"当前Scope：{currentScope}\n" +
                $"数据来源：{dataSourceBookId}\n\n" +
                "混合启用可能导致设定串线，是否继续？");

            if (!result)
            {
                TM.App.Log($"[Scope治理] 用户取消启用: {dataName}");
            }
            else
            {
                TM.App.Log($"[Scope治理] 用户确认混合启用: {dataName} (Scope:{currentScope}, Source:{dataSourceBookId})");
            }

            return result;
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
            FocusCategoryNode(categoryName);
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

        protected abstract bool MatchesSearchKeyword(TData data, string keyword);

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
            return string.IsNullOrWhiteSpace(formIcon) ? "📁" : formIcon;
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

        private HashSet<string>? _sessionDbNamesCache;

        private void EndBusinessSessionAndResetBatchNames()
        {
            _aiService.EndBusinessSessionsByPrefix(GetType().Name);
            _batchGeneratedNames.Clear();
            _batchGeneratedIndex.Clear();
            _sessionDbNamesCache = null;
        }

        private void AppendBatchIndexEntries(List<Dictionary<string, object>> entities, AIGenerationConfig? config)
        {
            if (config?.BatchIndexFields == null || config.BatchIndexFields.Count == 0) return;
            foreach (var entity in entities)
            {
                var parts = config.BatchIndexFields
                    .Select(f => entity.TryGetValue(f, out var v) ? v?.ToString()?.Trim() ?? string.Empty : string.Empty)
                    .ToList();
                if (parts.Any(p => !string.IsNullOrWhiteSpace(p)))
                    _batchGeneratedIndex.Add(string.Join("|", parts));
            }
        }

        private System.Threading.CancellationTokenSource? _singleCancellationTokenSource;

        private bool _isBatchGenerating;
        public bool IsBatchGenerating
        {
            get => _isBatchGenerating;
            private set
            {
                if (_isBatchGenerating != value)
                {
                    _isBatchGenerating = value;
                    OnPropertyChanged();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private string _batchProgressText = string.Empty;
        public string BatchProgressText
        {
            get => _batchProgressText;
            private set
            {
                if (_batchProgressText != value)
                {
                    _batchProgressText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _aiGenerateButtonText = "AI生成";
        public string AIGenerateButtonText
        {
            get => _aiGenerateButtonText;
            set
            {
                if (_aiGenerateButtonText != value)
                {
                    _aiGenerateButtonText = value;
                    OnPropertyChanged();
                }
            }
        }

        protected virtual bool SupportsBatch(TreeNodeItem categoryNode)
        {
            return true;
        }

        public void SetSelectedCategoryNodeForBatch(TreeNodeItem? categoryNode)
        {
            _selectedCategoryNodeForBatch = categoryNode;

            if (_selectedCategoryNodeForBatch != null && _selectedCategoryNodeForBatch.Tag is ICategory)
            {
                IsBatchModeActive = true;
                var isSingleMode = !SupportsBatch(_selectedCategoryNodeForBatch) || IsBatchGenerationDisabledForCurrentModule();
                AIGenerateButtonText = isSingleMode ? "AI单次" : "AI批量";
            }
            else
            {
                IsBatchModeActive = false;
                AIGenerateButtonText = "AI生成";
            }

            TM.App.Log($"[{GetType().Name}] SetSelectedCategoryNodeForBatch: node={categoryNode?.Name ?? "null"}, IsBatchModeActive={IsBatchModeActive}, ButtonText={AIGenerateButtonText}");
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        protected virtual bool IsBatchGenerationDisabledForCurrentModule()
        {
            return false;
        }

        public void CancelBatchGeneration()
        {
            _batchCancellationTokenSource?.Cancel();
            _singleCancellationTokenSource?.Cancel();
            IsBatchCancelRequested = true;
            BatchProgressText = "正在取消...";
            try
            {
                _skChatService.CancelCurrentRequest();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 取消当前请求失败: {ex.Message}");
            }
            TM.App.Log($"[{GetType().Name}] 批量生成已请求取消");
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (IsBatchModeActive && _selectedCategoryNodeForBatch != null)
            {
                return base.CanExecuteAIGenerate() && GetAIGenerationConfig() != null;
            }

            return base.CanExecuteAIGenerate() && GetAIGenerationConfig() != null;
        }

        private static readonly HashSet<string> CoherenceEnabledCategories = new(StringComparer.Ordinal)
        {
            "场景规划",
            "细节设计",
            "要素整合"
        };

        private bool _hasCoherenceHardConflict;
        public bool HasCoherenceHardConflict
        {
            get => _hasCoherenceHardConflict;
            private set
            {
                if (_hasCoherenceHardConflict != value)
                {
                    _hasCoherenceHardConflict = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _coherenceConflictMessage = string.Empty;
        public string CoherenceConflictMessage
        {
            get => _coherenceConflictMessage;
            private set
            {
                if (_coherenceConflictMessage != value)
                {
                    _coherenceConflictMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _coherenceConflictScopeId = string.Empty;

        private string GetCurrentCoherenceScopeId()
        {
            if (IsCreateMode)
            {
                return "CreateMode";
            }

            var editingData = GetCurrentEditingDataObject();
            if (editingData is IDataItem dataItem)
            {
                return dataItem.Id ?? string.Empty;
            }

            return string.Empty;
        }

        private void ResetCoherenceState()
        {
            HasCoherenceHardConflict = false;
            CoherenceConflictMessage = string.Empty;
            _coherenceConflictScopeId = string.Empty;
        }

        private static string BuildCoherencePromptAppendix()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("<coherence_check mandatory=\"true\">");
            sb.AppendLine("在完成主要输出内容之后，必须追加以下区块：");
            sb.AppendLine("<new_facts>");
            sb.AppendLine("- 若没有新增事实，写：- 无");
            sb.AppendLine("</new_facts>");
            sb.AppendLine("<consistency_check>");
            sb.AppendLine("- 与既定设定是否冲突: 是/否");
            sb.AppendLine("- 若是，冲突点: ...");
            sb.AppendLine("- 若否，如何确保不冲突: ...");
            sb.AppendLine("</consistency_check>");
            sb.AppendLine("<missing_info>");
            sb.AppendLine("- 若无需补充，写：- 无");
            sb.AppendLine("</missing_info>");
            sb.AppendLine("</coherence_check>");
            sb.AppendLine();
            return sb.ToString();
        }

        private const string BatchCoherenceConflictKey = "CoherenceSelfCheckConflict";
        private const string BatchCoherenceConflictPointKey = "CoherenceConflictPoint";

        private void EvaluateCoherenceC0(string aiText)
        {
            ResetCoherenceState();

            _coherenceConflictScopeId = GetCurrentCoherenceScopeId();

            if (string.IsNullOrWhiteSpace(aiText))
            {
                return;
            }

            var lines = aiText.Split('\n');

            bool conflict = false;
            string conflictPoint = string.Empty;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.Contains("与既定设定是否冲突", StringComparison.Ordinal))
                {
                    if (Regex.IsMatch(line, "[:：]\\s*是(\\s|$)", RegexOptions.CultureInvariant))
                    {
                        conflict = true;
                    }
                }

                if (line.Contains("冲突点", StringComparison.Ordinal))
                {
                    var idx = line.IndexOf(':');
                    if (idx < 0) idx = line.IndexOf('：');
                    if (idx >= 0 && idx + 1 < line.Length)
                    {
                        var v = line[(idx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(v) && !string.Equals(v, "无", StringComparison.Ordinal))
                        {
                            conflictPoint = v;
                        }
                    }
                }
            }

            if (conflict)
            {
                HasCoherenceHardConflict = true;
                CoherenceConflictMessage = string.IsNullOrWhiteSpace(conflictPoint)
                    ? "检测到硬冲突（连贯性自检标记为是）"
                    : $"检测到硬冲突：{conflictPoint}";
            }
        }

        protected virtual AIGenerationConfig? GetAIGenerationConfig() => null;

        protected virtual IPromptRepository? GetPromptRepository() => null;

        protected virtual System.Threading.Tasks.Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        protected static async System.Threading.Tasks.Task EnsureServiceInitializedAsync(object? service)
        {
            if (service is TM.Framework.Common.Services.IAsyncInitializable initializable)
            {
                await initializable.InitializeAsync().ConfigureAwait(false);
            }
        }

        protected static string FilterToCandidateOrRaw(string value, IEnumerable<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0) return value;
            return EntityNameNormalizeHelper.NormalizeSingle(value, list, EntityMatchMode.Lenient);
        }

        protected static string FilterToCandidatesOrRaw(string value, IEnumerable<string> candidates, string separator = "、")
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0) return value;
            return EntityNameNormalizeHelper.NormalizeMultiple(value, list, EntityMatchMode.Lenient, separator);
        }

        protected override async System.Threading.Tasks.Task ExecuteAIGenerateAsync()
        {
            if (_skChatService.IsMainConversationGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;

                _skChatService.CancelCurrentRequest();
                TM.App.Log($"[{GetType().Name}] 用户确认中断主界面对话，工作台 AI 继续执行");
            }

            if (IsBatchModeActive && _selectedCategoryNodeForBatch != null)
            {
                var isSingleMode = !SupportsBatch(_selectedCategoryNodeForBatch) || IsBatchGenerationDisabledForCurrentModule();
                TM.App.Log($"[{GetType().Name}] 进入分类AI生成模式，分类={_selectedCategoryNodeForBatch.Name}, 单类模式={isSingleMode}");
                await ExecuteBatchAIGenerateEntryAsync(isSingleMode);
                return;
            }

            var config = GetAIGenerationConfig();
            if (config != null)
            {
                await ExecuteAIGenerateWithConfigAsync(config);
            }
            else
            {
                GlobalToast.Warning("未接入新业务", "当前页面未提供AIGenerationConfig，已禁用旧业务AI生成链路");
            }
        }

        private async System.Threading.Tasks.Task ExecuteBatchAIGenerateEntryAsync(bool singleMode = false)
        {
            if (_selectedCategoryNodeForBatch == null)
            {
                GlobalToast.Warning("提示", "请先选择一个分类节点");
                return;
            }

            var categoryName = _selectedCategoryNodeForBatch.Name;

            if (!await ConfirmAIGenerateWhenCategoryHasDataAsync(categoryName, isSingleFillForm: false))
            {
                TM.App.Log($"[{GetType().Name}] 用户取消生成（分类已有数据）: {categoryName}");
                return;
            }

            var config = await ShowBatchGenerationDialogAsync(categoryName, singleMode);
            if (config == null)
            {
                TM.App.Log($"[{GetType().Name}] 用户取消生成");
                return;
            }

            await ExecuteBatchAIGenerateAsync(config);
        }

        protected virtual System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(string categoryName, bool singleMode = false)
        {
            var config = BatchGenerationDialog.Show(categoryName, singleMode ? 1 : GetDefaultTotalCount(), singleMode ? 1 : GetDefaultBatchSize(), null, singleMode);
            return System.Threading.Tasks.Task.FromResult(config);
        }

        protected virtual async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            var vmName = GetType().Name;
            var isSingleMode = config.TotalCount == 1 && config.BatchSize == 1;

            TM.App.Log($"[{vmName}] 开始AI生成: 分类={config.CategoryName}, 总数={config.TotalCount}, 单批={config.BatchSize}, 单类模式={isSingleMode}");

            var activeConfig = _aiService.GetActiveConfiguration();
            if (activeConfig == null)
            {
                GlobalToast.Error("未配置AI模型", "当前没有激活的AI模型，请前往\"智能助手 > 模型管理\"完成配置后重试。");
                TM.App.Log($"[{vmName}] AI生成阻断：未激活任何模型配置");
                return;
            }

            _batchCancellationTokenSource?.Dispose();
            _batchCancellationTokenSource = new System.Threading.CancellationTokenSource();
            var cancellationToken = _batchCancellationTokenSource.Token;

            var aiConfig = GetAIGenerationConfig();
            if (aiConfig != null)
            {
                await PrepareReferenceDataForAIGenerationAsync(aiConfig, isBatch: true, categoryName: config.CategoryName, cancellationToken);
            }

            int totalGenerated = 0;
            int totalFailed = 0;
            var estimatedBatches = config.EstimatedBatches;
            bool wasCancelled = false;

            _sessionDbNamesCache = new HashSet<string>(
                GetExistingNamesForDedup()
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => EntityNameNormalizeHelper.NormalizeBatchEntityName(n))
                    .Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);
            if (_sessionDbNamesCache.Count > 0)
                TM.App.Log($"[{vmName}] DB存在名称缓存: {_sessionDbNamesCache.Count} 条（用于跨会话去重兜底）");

            var maxBatches = estimatedBatches + 3;

            IsBatchGenerating = true;
            IsBatchCancelRequested = false;
            BatchProgressText = $"正在生成... (0/{estimatedBatches} 批)";
            _versionTrackingService.SuppressDownstreamToast = true;
            _skChatService.RegisterWorkspaceBatch(() =>
            {
                CancelBatchGeneration();
            });

            try
            {
                var moduleName = GetModuleNameForVersionTracking();
                Dictionary<string, int>? versionSnapshot = null;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    versionSnapshot = _versionTrackingService.GetDependencySnapshot(moduleName);
                    TM.App.Log($"[{vmName}] dep snapshot: {moduleName}");
                }
                string? lastBatchError = null;
                int consecutiveFailures = 0;
                const int maxConsecutiveFailures = 2;
                bool consecutiveFailureStop = false;
                for (int batchIndex = 0; batchIndex < maxBatches; batchIndex++)
                {
                    if (IsBatchCancelRequested || cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        _batchCancellationTokenSource?.Cancel();
                        TM.App.Log($"[{vmName}] 批量生成已取消，已完成 {batchIndex}/{maxBatches} 批");
                        break;
                    }

                    var remaining = config.TotalCount - totalGenerated;
                    if (remaining <= 0) break;
                    var batchCount = Math.Min(config.BatchSize, remaining);

                    var _batchNum = batchIndex + 1;
                    BatchProgressText = _batchNum > estimatedBatches
                        ? $"正在生成... (第{_batchNum}批/预计{estimatedBatches}批 · 去重补偿)"
                        : $"正在生成... ({_batchNum}/{estimatedBatches} 批)";

                    try
                    {
                        var batchResult = await GenerateBatchAsync(config.CategoryName, batchCount, cancellationToken);

                        cancellationToken.ThrowIfCancellationRequested();

                        if (batchResult != null && batchResult.Count > 0)
                        {
                            int skippedByCoherence = 0;
                            batchResult = FilterBatchEntitiesByCoherence(batchResult, out skippedByCoherence);

                            if (skippedByCoherence > 0)
                            {
                                totalFailed += skippedByCoherence;
                                GlobalToast.Warning("检测到硬冲突", $"已跳过 {skippedByCoherence} 个存在硬冲突的实体（未落库）");
                            }

                            var range = GetCurrentBatchRange();
                            if (range.HasValue)
                            {
                                batchResult = ValidateAndNormalizeGeneratedEntities(range.Value, batchResult);
                            }

                            NormalizeBatchEntityNames(batchResult);

                            if (IsNameDedupEnabled() && _batchGeneratedNames.Count > 0)
                            {
                                var beforeDedup = batchResult.Count;
                                var nameSet = new HashSet<string>(_batchGeneratedNames, StringComparer.OrdinalIgnoreCase);
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                    {
                                        if (nameSet.Contains(ns)) return false;
                                        if (TryStripNumericSuffix(ns, out var baseName) && nameSet.Contains(baseName))
                                            return false;
                                        return true;
                                    }
                                    return true;
                                }).ToList();
                                var skippedCross = beforeDedup - batchResult.Count;
                                if (skippedCross > 0)
                                    TM.App.Log($"[{vmName}] 跨批次去重: 跳过 {skippedCross} 个重名实体");
                            }

                            if (IsNameDedupEnabled() && _sessionDbNamesCache?.Count > 0)
                            {
                                var beforeDbDedup = batchResult.Count;
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                    {
                                        if (_sessionDbNamesCache.Contains(ns)) return false;
                                        if (TryStripNumericSuffix(ns, out var baseName) && _sessionDbNamesCache.Contains(baseName))
                                            return false;
                                        return true;
                                    }
                                    return true;
                                }).ToList();
                                var skippedDb = beforeDbDedup - batchResult.Count;
                                if (skippedDb > 0)
                                    TM.App.Log($"[{vmName}] DB存在名称过滤: 跳过 {skippedDb} 个已存在实体");
                            }

                            if (IsNameDedupEnabled())
                            {
                                var beforeIntra = batchResult.Count;
                                var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                        return seenInBatch.Add(ns);
                                    return true;
                                }).ToList();
                                var skippedIntra = beforeIntra - batchResult.Count;
                                if (skippedIntra > 0)
                                    TM.App.Log($"[{vmName}] 批内去重: 跳过 {skippedIntra} 个同批次重名实体");
                            }

                            var savedEntities = await SaveBatchEntitiesAsync(batchResult, config.CategoryName, versionSnapshot);

                            foreach (var entity in savedEntities)
                            {
                                if (entity.TryGetValue("Name", out var nameObj) && nameObj is string eName && !string.IsNullOrWhiteSpace(eName))
                                    _batchGeneratedNames.Add(eName);
                            }
                            AppendBatchIndexEntries(savedEntities, GetAIGenerationConfig());

                            cancellationToken.ThrowIfCancellationRequested();

                            totalGenerated += savedEntities.Count;
                            consecutiveFailures = 0;
                            TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批完成: 生成={batchResult.Count}, 落库={savedEntities.Count}");
                        }
                        else
                        {
                            OnBatchGenerationFailed(batchCount);
                            totalFailed += batchCount;
                            consecutiveFailures++;
                            TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批失败: AI未返回有效数据");

                            if (consecutiveFailures >= maxConsecutiveFailures)
                            {
                                consecutiveFailureStop = true;
                                BatchProgressText = "连续请求失败，已停止";
                                TM.App.Log($"[{vmName}] 连续失败 {consecutiveFailures} 次（空数据），提前终止");
                                var emptyHint = totalGenerated > 0
                                    ? $"已连续失败 {consecutiveFailures} 次，已生成 {totalGenerated} 个，请检查后重试"
                                    : $"已连续失败 {consecutiveFailures} 次，请检查网络或模型配置后重试";
                                GlobalToast.Warning("AI 请求失败", emptyHint);
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        wasCancelled = true;
                        TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批被取消");
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnBatchGenerationFailed(batchCount);
                        totalFailed += batchCount;
                        lastBatchError = ex.Message;
                        consecutiveFailures++;
                        TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批异常: {ex.Message}");

                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            consecutiveFailureStop = true;
                            BatchProgressText = "连续请求失败，已停止";
                            TM.App.Log($"[{vmName}] 连续失败 {consecutiveFailures} 次，提前终止");
                            var hint = totalGenerated > 0
                                ? $"已连续失败 {consecutiveFailures} 次，已生成 {totalGenerated} 个，请检查后重试"
                                : $"已连续失败 {consecutiveFailures} 次，请检查网络或模型配置后重试";
                            GlobalToast.Warning("AI 请求失败", hint);
                            break;
                        }
                    }
                }

                RefreshTreeData();

                if (consecutiveFailureStop) return;

                if (wasCancelled)
                {
                    GlobalToast.Info("已取消", $"已生成 {totalGenerated} / 目标 {config.TotalCount}");
                    TM.App.Log($"[{vmName}] 批量AI生成已取消: 成功={totalGenerated}, 失败={totalFailed}");
                    return;
                }

                if (totalFailed == 0 && totalGenerated > 0)
                {
                    GlobalToast.Success("生成完成", $"成功生成 {totalGenerated} 个实体");
                }
                else if (totalGenerated > 0 && totalFailed > 0)
                {
                    GlobalToast.Info("部分成功", $"成功 {totalGenerated} 个，失败 {totalFailed} 个");
                }
                else if (totalGenerated == 0)
                {
                    var errorHint = !string.IsNullOrWhiteSpace(lastBatchError)
                        ? $"AI调用失败: {lastBatchError}"
                        : "未能生成任何实体，请检查AI模型配置或提示词模板";
                    GlobalToast.Error("生成失败", errorHint);
                }

                TM.App.Log($"[{vmName}] 批量AI生成完成: 成功={totalGenerated}, 失败={totalFailed}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{vmName}] 批量AI生成异常: {ex.Message}");
                GlobalToast.Error("生成失败", ex.Message);
            }
            finally
            {
                IsBatchGenerating = false;
                _lastBatchWasCancelled = wasCancelled;
                IsBatchCancelRequested = false;
                BatchProgressText = string.Empty;
                _skChatService.UnregisterWorkspaceBatch();

                _batchCancellationTokenSource?.Dispose();
                _batchCancellationTokenSource = null;

                _versionTrackingService.SuppressDownstreamToast = false;
                _versionTrackingService.FlushPendingDownstreamNotifications();
            }
        }

        protected virtual async System.Threading.Tasks.Task<List<Dictionary<string, object>>> GenerateBatchAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var vmName = GetType().Name;

            var maxAttempts = 2;
            var attempt = 0;
            var final = new List<Dictionary<string, object>>();

            if (IsBatchCancelRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var config = GetAIGenerationConfig();
            var scopeId = GetCurrentScopeId();
            var range = GetNextGenerationRange(scopeId, categoryName, count);
            _currentBatchRange = range;

            var businessSessionKey = $"{vmName}_{categoryName}";
            _batchSessionHasHistory = _aiService.HasDirtyBusinessSession(businessSessionKey);

            if (!_batchSessionHasHistory)
            {
                _batchGeneratedNames.Clear();
                _batchGeneratedIndex.Clear();
                _sessionDbNamesCache = null;
            }

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                var remaining = Math.Max(0, count - final.Count);
                if (remaining == 0)
                {
                    break;
                }

                GenerationRange? remainingRange = null;
                if (range.HasValue)
                {
                    var start = range.Value.Start + final.Count;
                    var end = Math.Min(range.Value.End, start + remaining - 1);
                    remainingRange = new GenerationRange(start, end);
                    _currentBatchRange = remainingRange;
                }

                var prompt = await BuildBatchGenerationPromptAsync(categoryName, remaining, cancellationToken);

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    TM.App.Log($"[{vmName}] 批量生成提示词为空（已禁用旧业务fallback），请检查 GetAIGenerationConfig/BatchFieldKeyMap/提示词模板配置");
                    return new List<Dictionary<string, object>>();
                }

                if (attempt > 1)
                {
                    var retryHint = new System.Text.StringBuilder();
                    retryHint.AppendLine();
                    retryHint.AppendLine("<retry_warning>");
                    retryHint.AppendLine("你上一次输出不满足JSON协议（可能是数量不足/格式错误/夸杂解释）。本次只输出JSON，不要任何解释文字。");
                    if (remainingRange.HasValue)
                    {
                        retryHint.AppendLine($"本次仅补齐剩余 {remaining} 个对象，序号范围严格为 {remainingRange.Value.Start}-{remainingRange.Value.End}。");
                    }
                    retryHint.AppendLine("</retry_warning>");
                    prompt += retryHint.ToString();
                }

                var ai = _aiService;
                Func<System.Threading.Tasks.Task<string>>? initialContextProvider = null;
                if (!string.IsNullOrEmpty(_cachedBatchContextText))
                {
                    var _ctxSnapshot = _cachedBatchContextText;
                    initialContextProvider = () => System.Threading.Tasks.Task.FromResult(_ctxSnapshot);
                }
                else if (config?.ContextProvider != null)
                {
                    initialContextProvider = config.ContextProvider;
                }
                var batchProgress = new Progress<string>(msg =>
                    BatchProgressText = $"{BatchProgressText?.Split('|')[0]?.Trim()} | {msg}");

                var aiTask = ai.GenerateInBusinessSessionAsync(businessSessionKey, initialContextProvider, prompt, batchProgress, cancellationToken);
                _ = aiTask.ContinueWith(
                    t =>
                    {
                        _ = t.Exception;
                    },
                    System.Threading.CancellationToken.None,
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
                    System.Threading.Tasks.TaskScheduler.Default);
                var cancelTask = System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                var completed = await System.Threading.Tasks.Task.WhenAny(aiTask, cancelTask);
                if (!ReferenceEquals(completed, aiTask))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                var aiResult = await aiTask;

                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    var errMsg = aiResult.ErrorMessage ?? "AI未返回有效内容";
                    TM.App.Log($"[{vmName}] AI调用失败: {errMsg}");
                    throw new InvalidOperationException(errMsg);
                }

                var parsed = ParseBatchJsonResult(aiResult.Content);
                if (parsed.Count == 0)
                {
                    continue;
                }

                if (parsed.Count != remaining)
                {
                    if (attempt < maxAttempts)
                    {
                        continue;
                    }

                    return new List<Dictionary<string, object>>();
                }

                if (IsBatchMissingFieldsTooHigh(parsed))
                {
                    if (attempt < maxAttempts)
                    {
                        continue;
                    }

                    return new List<Dictionary<string, object>>();
                }

                EnsureRequiredFields(parsed);
                final.AddRange(parsed);

                if (final.Count > count)
                {
                    final = final.Take(count).ToList();
                    break;
                }
            }

            _currentBatchRange = range;

            if (final.Count != count)
            {
                TM.App.Log($"[{vmName}] 批量生成失败：最终数量不足，目标={count}，实际={final.Count}");
                return new List<Dictionary<string, object>>();
            }

            return final;
        }

        private bool IsBatchMissingFieldsTooHigh(List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return true;
            }

            var config = GetAIGenerationConfig();
            if (config == null || config.OutputFields.Count == 0)
            {
                return false;
            }

            var required = config.OutputFields.Keys
                .Concat(new[] { "Name" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            double sumRatio = 0;
            int counted = 0;

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                sumRatio += GetMissingRatio(entity, required);
                counted++;
            }

            if (counted == 0)
            {
                return true;
            }

            var avg = sumRatio / counted;
            return avg >= MissingFieldRetryThreshold;
        }

        private static double GetMissingRatio(Dictionary<string, object> entity, IReadOnlyList<string> requiredKeys)
        {
            if (requiredKeys.Count == 0)
            {
                return 0;
            }

            int missing = 0;
            foreach (var key in requiredKeys)
            {
                if (!entity.TryGetValue(key, out var v) || v == null)
                {
                    missing++;
                    continue;
                }

                if (v is string s && string.IsNullOrWhiteSpace(s))
                {
                    missing++;
                }
            }

            return (double)missing / requiredKeys.Count;
        }

        private void EnsureRequiredFields(List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return;
            }

            var config = GetAIGenerationConfig();
            if (config == null || config.OutputFields.Count == 0)
            {
                return;
            }

            var required = config.OutputFields.Keys
                .Concat(new[] { "Name" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                foreach (var key in required)
                {
                    if (!entity.ContainsKey(key))
                    {
                        if (key.EndsWith("Number", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Count", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Index", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = 0;
                        }
                        else if (key.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
                                 key.StartsWith("Has", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = false;
                        }
                        else if (key.EndsWith("List", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Tags", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Items", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = new List<string>();
                        }
                        else
                        {
                            entity[key] = string.Empty;
                        }
                    }
                }
            }
        }

        private static void NormalizeBatchEntityNames(List<Dictionary<string, object>> entities)
        {
            if (entities == null) return;
            foreach (var entity in entities)
            {
                if (entity != null && entity.TryGetValue("Name", out var nameObj) && nameObj is string nameStr)
                {
                    var cleaned = EntityNameNormalizeHelper.NormalizeBatchEntityName(nameStr);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        entity["Name"] = cleaned;
                }
            }
        }

        private static bool TryStripNumericSuffix(string name, out string baseName)
        {
            baseName = name;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var t = name.Trim();
            var idx = t.LastIndexOf('_');
            if (idx <= 0 || idx >= t.Length - 1) return false;
            var suffix = t.Substring(idx + 1);
            if (!suffix.All(char.IsDigit)) return false;
            var stripped = t.Substring(0, idx).TrimEnd();
            if (string.IsNullOrWhiteSpace(stripped)) return false;
            baseName = stripped;
            return true;
        }

        protected virtual async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName,
            int count,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBatchCancelRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var repo = GetPromptRepository();
            var config = GetAIGenerationConfig();

            var previousInfo = GetPreviousBatchInfo(categoryName);
            var previousInfoText = previousInfo?.ToContextString() ?? string.Empty;

            if (repo == null || config == null || string.IsNullOrWhiteSpace(config.Category))
            {
                GlobalToast.Warning("未接入新业务", "当前页面未提供AIGenerationConfig或提示词分类，已禁用旧业务批量生成fallback");
                return string.Empty;
            }

            if (config.BatchFieldKeyMap == null || config.BatchFieldKeyMap.Count == 0)
            {
                GlobalToast.Warning("未接入新业务", "当前页面未配置BatchFieldKeyMap，已禁用旧业务批量字段推导fallback");
                return string.Empty;
            }

            _cachedBatchContextText = string.Empty;
            string contextText = string.Empty;
            if (config.ContextProvider != null)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    contextText = await config.ContextProvider();
                    _cachedBatchContextText = contextText;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetType().Name}] BuildBatchGenerationPromptAsync: 获取上下文失败 - {ex.Message}");
                    contextText = string.Empty;
                }
            }

            var templates = repo.GetTemplatesByCategory(config.Category);
            var enabled = templates.Where(t => t.IsEnabled).ToList();
            var candidates = enabled.Count > 0 ? enabled : templates.ToList();
            var singleTemplate = candidates
                .Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt))
                .OrderByDescending(t => t.IsDefault)
                .ThenByDescending(t => t.IsBuiltIn)
                .ThenByDescending(t => t.IsEnabled)
                .FirstOrDefault();

            if (singleTemplate == null || string.IsNullOrWhiteSpace(singleTemplate.SystemPrompt))
            {
                GlobalToast.Warning("提示词缺失", $"请先在「提示词管理」中配置「{config.Category}」分类的模板");
                return string.Empty;
            }

            var variableNames = SplitTemplateVariables(singleTemplate.Variables);
            var derived = BuildDerivedBatchPrompt(singleTemplate.SystemPrompt, variableNames, categoryName, count, contextText, previousInfoText,
                CoherenceEnabledCategories.Contains(config.Category));
            return derived;
        }

        protected virtual string? GetCurrentScopeId()
        {
            try
            {
                return _workScopeService.CurrentSourceBookId;
            }
            catch
            {
                return null;
            }
        }

        protected virtual GenerationRange? GetNextGenerationRange(string? scopeId, string categoryName, int requestedCount)
        {
            var config = GetAIGenerationConfig();
            if (config == null || string.IsNullOrWhiteSpace(config.SequenceFieldName) || config.GetCurrentMaxSequence == null)
            {
                return null;
            }

            var max = config.GetCurrentMaxSequence(scopeId, categoryName);
            var start = Math.Max(1, max + 1);
            var end = Math.Max(start, start + Math.Max(1, requestedCount) - 1);
            return new GenerationRange(start, end);
        }

        protected virtual string ApplyGenerationRangeToPrompt(string prompt, GenerationRange range)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            var config = GetAIGenerationConfig();
            var seqField = config?.SequenceFieldName;
            var appendix = new StringBuilder();
            appendix.AppendLine();
            appendix.AppendLine("<batch_range_constraint mandatory=\"true\">");
            appendix.AppendLine($"本批次续写范围：{range.Start}-{range.End}");
            if (!string.IsNullOrWhiteSpace(seqField))
            {
                appendix.AppendLine($"要求：{seqField} 必须严格落在 {range.Start}-{range.End} 且不重复");
            }
            appendix.AppendLine("</batch_range_constraint>");

            return prompt + appendix;
        }

        protected virtual List<Dictionary<string, object>> ValidateAndNormalizeGeneratedEntities(
            GenerationRange range,
            List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return entities ?? new List<Dictionary<string, object>>();
            }

            var config = GetAIGenerationConfig();
            var seqField = config?.SequenceFieldName;
            if (string.IsNullOrWhiteSpace(seqField))
            {
                return entities;
            }

            var used = new HashSet<int>();
            int next = range.Start;

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                int candidate = 0;
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    candidate = reader.GetInt(seqField);
                }
                catch
                {
                    candidate = 0;
                }

                bool ok = candidate >= range.Start && candidate <= range.End && !used.Contains(candidate);
                if (!ok)
                {
                    while (next <= range.End && used.Contains(next))
                    {
                        next++;
                    }

                    candidate = next <= range.End ? next : range.End;
                }

                used.Add(candidate);
                entity[seqField] = candidate;
                if (candidate == next)
                {
                    next++;
                }
            }

            return entities;
        }

        private string BuildDerivedBatchPrompt(
            string singlePrompt,
            IReadOnlyList<string> variableNames,
            string categoryName,
            int count,
            string contextText,
            string previousInfoText,
            bool enableCoherence)
        {
            var config = GetAIGenerationConfig();
            List<string> fieldNames;

            if (config?.BatchFieldKeyMap != null && config.OutputFields.Count > 0)
            {
                fieldNames = config.OutputFields.Keys.ToList();
                if (!fieldNames.Contains("Name", StringComparer.OrdinalIgnoreCase))
                {
                    fieldNames.Insert(0, "Name");
                }
            }
            else
            {
                throw new InvalidOperationException("未配置 BatchFieldKeyMap，已禁用旧业务批量字段推导 fallback");
            }
            if (enableCoherence)
            {
                if (!fieldNames.Contains(BatchCoherenceConflictKey, StringComparer.Ordinal))
                    fieldNames = fieldNames.Concat(new[] { BatchCoherenceConflictKey }).ToList();
                if (!fieldNames.Contains(BatchCoherenceConflictPointKey, StringComparer.Ordinal))
                    fieldNames = fieldNames.Concat(new[] { BatchCoherenceConflictPointKey }).ToList();
            }
            var fieldsText = string.Join(", ", fieldNames.Select(f => $"\"{f}\""));

            var normalizedSinglePrompt = singlePrompt;
            var variableValues = new Dictionary<string, string>(StringComparer.Ordinal);
            if (config?.InputVariables != null)
            {
                foreach (var (varName, getValue) in config.InputVariables)
                {
                    if (string.IsNullOrWhiteSpace(varName))
                    {
                        continue;
                    }

                    try
                    {
                        variableValues[varName] = getValue?.Invoke() ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{GetType().Name}] BuildDerivedBatchPrompt: 获取输入变量失败 - {varName}: {ex.Message}");
                        variableValues[varName] = string.Empty;
                    }
                }
            }

            foreach (var varName in variableNames)
            {
                if (string.IsNullOrWhiteSpace(varName))
                {
                    continue;
                }

                if (variableValues.TryGetValue(varName, out var variableValue) && !string.IsNullOrWhiteSpace(variableValue))
                {
                    normalizedSinglePrompt = normalizedSinglePrompt.Replace($"{{{varName}}}", variableValue);
                }
                else
                {
                    normalizedSinglePrompt = normalizedSinglePrompt.Replace($"{{{varName}}}", $"<自动生成:{varName}>");
                }
            }
            if (!string.IsNullOrWhiteSpace(contextText))
            {
                normalizedSinglePrompt = normalizedSinglePrompt.Replace("{上下文数据}", contextText);
            }
            else
            {
                normalizedSinglePrompt = normalizedSinglePrompt.Replace("{上下文数据}", string.Empty);
            }

            var sb = new StringBuilder();
            sb.AppendLine("<batch_task>");
            sb.AppendLine("你将执行批量生成任务。");
            sb.AppendLine($"目标分类：{categoryName}");
            sb.AppendLine($"本批次生成数量：{count}");

            sb.AppendLine();
            sb.AppendLine("<mandate critical=\"true\">");
            sb.AppendLine($"输出协议：仅输出JSON数组，长度严格等于 {count}，每项必须包含字段 {fieldsText}。禁止Markdown/代码块/额外文字。");
            sb.AppendLine("</mandate>");

            if (!string.IsNullOrWhiteSpace(previousInfoText))
            {
                sb.AppendLine();
                sb.AppendLine("<previous_batch_info>");
                sb.AppendLine(previousInfoText);
                sb.AppendLine("</previous_batch_info>");
            }

            if (!_batchSessionHasHistory && !string.IsNullOrWhiteSpace(contextText))
            {
                sb.AppendLine();
                sb.AppendLine("<context_data>");
                sb.AppendLine(contextText);
                sb.AppendLine("</context_data>");
            }
            else if (_batchSessionHasHistory)
            {
                sb.AppendLine();
                sb.AppendLine("<session_continuation>");
                sb.AppendLine("完整背景设定已在会话初始化时提供，此处不重复。");
                if (_batchGeneratedIndex.Count > 0)
                {
                    sb.AppendLine("<generated_index note=\"已生成条目核心属性摘要，用于保持内容分布一致性，勿重复\">");
                    sb.AppendLine(string.Join("\n", _batchGeneratedIndex));
                    sb.AppendLine("</generated_index>");
                }
                else
                {
                    sb.AppendLine("<session_note>本地分布摘要已重置，请直接参考对话历史中已生成的内容保持风格与分布一致性。</session_note>");
                    if (_batchGeneratedNames.Count > 0)
                        sb.AppendLine($"本会话内仍追踪的名称（勿重复）：{string.Join("、", _batchGeneratedNames)}");
                }
                sb.AppendLine("</session_continuation>");
            }

            sb.AppendLine();
            sb.AppendLine("<single_gen_spec note=\"仅用于理解内容要求；输出格式以output_requirements为准\">");
            sb.AppendLine(normalizedSinglePrompt);
            if (variableNames.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("说明：单次规范中的 <自动生成:...> 代表原本的模板变量占位符。批量生成时，这些值不由外部输入提供，请为每个对象自行生成合理内容。");
            }
            sb.AppendLine("</single_gen_spec>");

            sb.AppendLine();
            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine("1. 只输出一个有效的JSON数组（不要Markdown、不要代码块、不要额外解释文本）。");
            sb.AppendLine($"2. 数组长度必须严格等于 {count}。");
            sb.AppendLine("3. 每个对象必须至少包含以下字段：");
            sb.AppendLine(fieldsText);
            if (_batchGeneratedNames.Count > 0)
            {
                sb.AppendLine($"4. Name 字段必须有区分度，且严禁与以下已生成的 Name 重复：{string.Join("、", _batchGeneratedNames)}");
            }
            else
            {
                sb.AppendLine("4. Name 字段必须有区分度，避免重复。");
            }
            sb.AppendLine("5. 所有字段值必须是字符串，不要用数组或嵌套对象。多项内容请在字符串内换行。");
            sb.AppendLine("</output_requirements>");

            if (enableCoherence)
            {
                sb.AppendLine();
                sb.AppendLine("<coherence_requirements mandatory=\"true\">");
                sb.AppendLine($"- {BatchCoherenceConflictKey}: \"是\" 或 \"否\"（仅当你确认存在硬冲突时才填\"是\"）");
                sb.AppendLine($"- {BatchCoherenceConflictPointKey}: 若 {BatchCoherenceConflictKey}=\"是\"，必须填写具体冲突点；否则填\"无\"");
                sb.AppendLine("</coherence_requirements>");
            }

            sb.AppendLine();
            sb.AppendLine("<output_example note=\"仅示意字段结构，内容请按单次规范生成\">");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            for (int i = 0; i < fieldNames.Count; i++)
            {
                var field = fieldNames[i];
                var comma = i == fieldNames.Count - 1 ? string.Empty : ",";
                sb.AppendLine($"    \"{field}\": \"...\"{comma}");
            }
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("</output_example>");
            sb.AppendLine("</batch_task>");

            var prompt = sb.ToString();
            if (_currentBatchRange.HasValue)
            {
                prompt = ApplyGenerationRangeToPrompt(prompt, _currentBatchRange.Value);
            }

            return prompt;
        }

        protected virtual ModuleNormalizationConfig? GetNormalizationConfig() => null;

        protected virtual System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
            => System.Threading.Tasks.Task.CompletedTask;

        protected string NormalizeFieldValue(string fieldName, string rawValue)
        {
            var config = GetNormalizationConfig();
            if (config == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return rawValue;
            }

            var rule = config.Rules.FirstOrDefault(r => string.Equals(r.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                return rawValue;
            }

            var candidates = GetNormalizationCandidates(rule);
            if (candidates.Count == 0)
            {
                if (rule.Type == NormalizationType.DynamicList)
                {
                    return rawValue;
                }

                return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
            }

            var normalized = EntityNameNormalizeHelper.FilterToCandidate(rawValue, candidates);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            if (rule.LogWarning && !string.IsNullOrWhiteSpace(rawValue))
            {
                TM.App.Log($"[{config.ModuleName}] 字段 {fieldName} 归一化失败（未知实体已丢弃）。原始值: {rawValue}");
            }

            return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
        }

        protected string NormalizeMultipleFieldValue(string fieldName, string rawValue)
        {
            var config = GetNormalizationConfig();
            if (config == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return rawValue;
            }

            var rule = config.Rules.FirstOrDefault(r => string.Equals(r.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                return rawValue;
            }

            var candidates = GetNormalizationCandidates(rule);
            if (candidates.Count == 0)
            {
                if (rule.Type == NormalizationType.DynamicList)
                {
                    return rawValue;
                }

                return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
            }

            var normalized = EntityNameNormalizeHelper.FilterToCandidates(rawValue, candidates);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            if (rule.LogWarning && !string.IsNullOrWhiteSpace(rawValue))
            {
                TM.App.Log($"[{config.ModuleName}] 字段 {fieldName} 多值归一化失败（未知实体已丢弃）。原始值: {rawValue}");
            }

            return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
        }

        private static List<string> GetNormalizationCandidates(FieldNormalizationRule rule)
        {
            List<string> candidates = rule.Type switch
            {
                NormalizationType.StaticOptions => rule.StaticOptions?.ToList() ?? new List<string>(),
                NormalizationType.DynamicList => rule.DynamicOptionsProvider?.Invoke() ?? new List<string>(),
                _ => new List<string>()
            };

            return candidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Dictionary<string, object>> FilterBatchEntitiesByCoherence(
            List<Dictionary<string, object>> entities,
            out int skippedByCoherence)
        {
            skippedByCoherence = 0;
            if (entities == null)
                return new List<Dictionary<string, object>>();
            if (entities.Count == 0)
                return entities;

            var filtered = new List<Dictionary<string, object>>(entities.Count);

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                if (!entity.TryGetValue(BatchCoherenceConflictKey, out var conflictObj))
                {
                    filtered.Add(entity);
                    continue;
                }

                bool isConflict = conflictObj is bool b
                    ? b
                    : string.Equals(conflictObj?.ToString()?.Trim(), "是", StringComparison.Ordinal);

                if (!isConflict)
                {
                    filtered.Add(entity);
                    continue;
                }

                if (!entity.TryGetValue(BatchCoherenceConflictPointKey, out var pointObj))
                {
                    filtered.Add(entity);
                    continue;
                }

                var point = pointObj?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(point) || string.Equals(point, "无", StringComparison.Ordinal))
                {
                    filtered.Add(entity);
                    continue;
                }

                if (!IsHighConfidenceConflictPoint(point))
                {
                    filtered.Add(entity);
                    continue;
                }

                skippedByCoherence++;
            }

            return filtered;
        }

        private static bool IsHighConfidenceConflictPoint(string point)
        {
            if (string.IsNullOrWhiteSpace(point))
                return false;

            var domainKeywords = new[]
            {
                "世界", "规则", "设定", "角色", "能力", "关系", "时间", "时间线", "剧情", "冲突", "伏笔"
            };

            var conflictKeywords = new[]
            {
                "冲突", "矛盾", "违背", "不一致"
            };

            return conflictKeywords.Any(k => point.Contains(k, StringComparison.Ordinal))
                   && domainKeywords.Any(k => point.Contains(k, StringComparison.Ordinal));
        }

        private static IReadOnlyList<string> SplitTemplateVariables(string? variables)
        {
            if (string.IsNullOrWhiteSpace(variables))
            {
                return Array.Empty<string>();
            }

            return variables
                .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        protected virtual PreviousBatchInfo? GetPreviousBatchInfo(string categoryName)
        {
            return null;
        }

        protected virtual IEnumerable<string> GetExistingNamesForDedup() => Enumerable.Empty<string>();

        protected virtual bool IsNameDedupEnabled() => true;

        protected virtual int GetDefaultTotalCount() => GetDefaultBatchSize();

        protected virtual int GetDefaultBatchSize() => 10;

        protected virtual void OnBatchGenerationFailed(int failedCount) { }

        protected virtual System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            TM.App.Log($"[{GetType().Name}] SaveBatchEntitiesAsync: 子类未重写，无法保存 {entities.Count} 个实体");
            return System.Threading.Tasks.Task.FromResult(new List<Dictionary<string, object>>());
        }

        private async System.Threading.Tasks.Task ExecuteAIGenerateWithConfigAsync(AIGenerationConfig config)
        {
            var vmName = GetType().Name;
            TM.App.Log($"[{vmName}] 开始AI生成（配置化）");

            var categoryForSingle = GetCurrentCategoryValue();
            if (!string.IsNullOrWhiteSpace(categoryForSingle) && !await ConfirmAIGenerateWhenCategoryHasDataAsync(categoryForSingle, isSingleFillForm: true))
            {
                TM.App.Log($"[{vmName}] 用户取消生成（分类已有数据）: {categoryForSingle}");
                return;
            }

            if (!CheckConsistencyBeforeGenerate())
            {
                TM.App.Log($"[{vmName}] 一致性检查未通过，取消生成");
                return;
            }

            try
            {
                _singleCancellationTokenSource?.Dispose();
                _singleCancellationTokenSource = new System.Threading.CancellationTokenSource();
                var cancellationToken = _singleCancellationTokenSource.Token;

                await PrepareReferenceDataForAIGenerationAsync(config, isBatch: false, categoryName: categoryForSingle, cancellationToken);

                var repo = GetPromptRepository();
                if (repo == null)
                {
                    GlobalToast.Warning("配置错误", "请在子类重写GetPromptRepository()提供提示词仓库");
                    return;
                }
                var templates = repo.GetTemplatesByCategory(config.Category);
                var enabled = templates
                    .Where(t => t.IsEnabled)
                    .Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .ToList();
                var candidates = enabled.Count > 0
                    ? enabled
                    : templates.Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt)).ToList();
                var template = candidates
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .ThenByDescending(t => t.IsEnabled)
                    .FirstOrDefault();
                if (template == null || string.IsNullOrWhiteSpace(template.SystemPrompt))
                {
                    GlobalToast.Warning("提示", $"请先在「提示词管理」中配置「{config.Category}」分类的模板");
                    return;
                }

                var prompt = template.SystemPrompt;
                foreach (var (varName, getValue) in config.InputVariables)
                {
                    prompt = prompt.Replace($"{{{varName}}}", getValue?.Invoke() ?? string.Empty);
                }

                Func<System.Threading.Tasks.Task<string>>? initialContextProvider = null;
                string singleContextText = string.Empty;
                if (config.ContextProvider != null)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        singleContextText = await config.ContextProvider();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{vmName}] 单次生成：获取上下文失败 - {ex.Message}");
                    }
                    var _singleCtxSnapshot = singleContextText;
                    initialContextProvider = () => System.Threading.Tasks.Task.FromResult(_singleCtxSnapshot);
                }
                prompt = prompt.Replace("{上下文数据}", singleContextText);

                if (CoherenceEnabledCategories.Contains(config.Category))
                {
                    prompt += BuildCoherencePromptAppendix();
                }

                var scopeId = GetCurrentScopeId();
                var range = GetNextGenerationRange(scopeId, GetCurrentCategoryValue() ?? string.Empty, 1);
                if (range.HasValue)
                {
                    _currentBatchRange = range;
                    prompt = ApplyGenerationRangeToPrompt(prompt, range.Value);
                }

                var singleJsonContract = BuildSingleJsonContract(config);
                if (!string.IsNullOrWhiteSpace(singleJsonContract))
                {
                    prompt += singleJsonContract;
                }

                TM.App.Log($"[{vmName}] 构建提示词完成，长度: {prompt.Length}");

                GlobalToast.Info("AI生成中", config.ProgressMessage);
                var progress = new Progress<string>(msg =>
                    BatchProgressText = $"正在生成... | {msg}");
                string result;

                var ai = _aiService;
                AIService.GenerationResult aiResult;
                var attempts = 0;
                while (true)
                {
                    attempts++;
                    aiResult = await ai.GenerateInBusinessSessionAsync(vmName, initialContextProvider, prompt, progress, cancellationToken);
                    if (!aiResult.Success)
                    {
                        GlobalToast.Error("生成失败", aiResult.ErrorMessage ?? "AI生成失败");
                        return;
                    }
                    result = aiResult.Content ?? string.Empty;

                    var entity = ParseSingleJsonEntity(result);
                    if (entity.Count > 0)
                    {
                        if (IsSingleMissingFieldsTooHigh(entity, config) && attempts < 2)
                        {
                            prompt += "\n<retry_warning>你上一次输出字段缺失过多。请严格按字段白名单输出完整JSON对象，不要遗漏字段。</retry_warning>";
                            continue;
                        }

                        if (range.HasValue)
                        {
                            var list = new List<Dictionary<string, object>> { entity };
                            list = ValidateAndNormalizeGeneratedEntities(range.Value, list);
                            entity = list[0];
                        }

                        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in entity)
                        {
                            extracted[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                        }

                        FillFieldsFromExtracted(config, extracted);

                        if (extracted.TryGetValue("Name", out var singleName) && !string.IsNullOrWhiteSpace(singleName))
                        {
                            _batchGeneratedNames.Add(singleName);
                        }

                        break;
                    }

                    if (attempts >= 2)
                    {
                        GlobalToast.Warning("生成失败", "未能从AI返回中解析到有效JSON对象，请检查提示词模板与输出协议");
                        return;
                    }

                    prompt += "\n<retry_warning>你上一次输出不满足JSON协议（可能是格式错误/夹杂解释）。本次只输出JSON对象，不要任何解释文字。</retry_warning>";
                }

                if (CoherenceEnabledCategories.Contains(config.Category))
                {
                    EvaluateCoherenceC0(result);

                    if (HasCoherenceHardConflict)
                    {
                        GlobalToast.Warning("检测到硬冲突", CoherenceConflictMessage);
                    }
                }

                if (string.IsNullOrWhiteSpace(result))
                {
                    GlobalToast.Warning("生成失败", "AI未返回任何内容");
                    return;
                }

                TM.App.Log($"[{vmName}] AI生成完成（JSON-only）");

                RecordDependencyVersions();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{vmName}] AI生成失败: {ex.Message}");
                GlobalToast.Error("生成失败", ex.Message);
            }
            finally
            {
                BatchProgressText = string.Empty;
                _singleCancellationTokenSource?.Dispose();
                _singleCancellationTokenSource = null;
            }
        }

        private async System.Threading.Tasks.Task<bool> ConfirmAIGenerateWhenCategoryHasDataAsync(string categoryName, bool isSingleFillForm)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return true;
            }

            try
            {
                var existingCount = await System.Threading.Tasks.Task.Run(() =>
                    GetAllDataItems().Count(d => string.Equals(GetDataCategory(d), categoryName, StringComparison.Ordinal)));

                if (existingCount <= 0)
                {
                    return true;
                }

                if (isSingleFillForm)
                {
                    return StandardDialog.ShowConfirm(
                        "当前『数据』节点已有数据。\n继续生成可能导致重复或与既有内容不一致，是否继续？",
                        "确认生成");
                }

                return StandardDialog.ShowConfirm(
                    "当前『分类』节点已有数据。\n继续生成可能导致重复或与既有内容不一致，是否继续？",
                    "确认生成");
            }
            catch
            {
                return true;
            }
        }

        private void FillFieldsFromExtracted(AIGenerationConfig config, Dictionary<string, string> extracted)
        {
            int updated = 0;
            var missingFields = new List<string>();

            foreach (var (fieldName, setValue) in config.OutputFields)
            {
                string? currentValue = null;
                if (config.OutputFieldGetters?.TryGetValue(fieldName, out var getter) == true)
                {
                    currentValue = getter?.Invoke();
                }

                if (extracted.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    setValue?.Invoke(value);
                    updated++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(currentValue))
                    {
                        setValue?.Invoke("[待补充]");
                        missingFields.Add(fieldName);
                    }
                }
            }

            if (updated == config.OutputFields.Count)
            {
                GlobalToast.Success("生成完成", $"已更新全部 {updated} 个字段");
            }
            else if (updated > 0)
            {
                GlobalToast.Info("部分生成", $"已更新 {updated} 个字段，{missingFields.Count} 个需手动补充");
            }
            else
            {
                GlobalToast.Warning("生成失败", "未能从AI返回中提取任何字段，请检查提示词配置");
            }
        }

        private string BuildSingleJsonContract(AIGenerationConfig config)
        {
            if (config.OutputFields.Count == 0)
            {
                return string.Empty;
            }

            var fields = config.OutputFields.Keys.ToList();
            if (!fields.Contains("Name", StringComparer.OrdinalIgnoreCase))
            {
                fields.Insert(0, "Name");
            }

            var sb = new StringBuilder();
            sb.AppendLine();

            if (_batchGeneratedIndex.Count > 0)
            {
                sb.AppendLine("<generated_index note=\"已生成条目核心属性摘要，保持内容分布一致性，勿重复\">");
                sb.AppendLine(string.Join("\n", _batchGeneratedIndex));
                sb.AppendLine("</generated_index>");
                sb.AppendLine();
            }

            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine("1. 只输出一个有效的JSON对象（不要Markdown、不要代码块、不要额外解释文本）。");
            sb.AppendLine("2. 对象必须至少包含以下字段：");
            sb.AppendLine(string.Join(", ", fields.Select(f => $"\"{f}\"")));
            if (_batchGeneratedNames.Count > 0)
                sb.AppendLine($"3. Name 字段必须有区分度，且严禁与以下已生成的 Name 重复：{string.Join("、", _batchGeneratedNames)}");
            else
                sb.AppendLine("3. Name 字段必须有区分度，避免重复。");
            sb.AppendLine("4. 所有字段值必须是字符串，不要用数组或嵌套对象。多项内容请在字符串内换行。");
            sb.AppendLine("</output_requirements>");
            sb.AppendLine();
            sb.AppendLine("<output_example note=\"仅示意字段结构，内容请按模板规范生成\">");
            sb.AppendLine("{");
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var comma = i == fields.Count - 1 ? string.Empty : ",";
                sb.AppendLine($"  \"{field}\": \"...\"{comma}");
            }
            sb.AppendLine("}");
            sb.AppendLine("</output_example>");
            return sb.ToString();
        }

        private Dictionary<string, object> ParseSingleJsonEntity(string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                }

                var trimmed = response.Trim();
                string jsonPayload;
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    jsonPayload = ExtractJsonArrayFromResponse(response);
                }
                else
                {
                    jsonPayload = ExtractJsonObjectFromResponse(response);
                }

                using var document = JsonDocument.Parse(jsonPayload);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() != 1)
                    {
                        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    }

                    root = root[0];
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                }

                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in root.EnumerateObject())
                {
                    object value = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.TryGetInt32(out var intVal) ? intVal : property.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Array => property.Value.EnumerateArray()
                            .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList(),
                        JsonValueKind.Object => property.Value.ToString(),
                        _ => string.Empty
                    };

                    dict[property.Name] = value;
                }

                return dict;
            }
            catch
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }

        protected static string ExtractJsonObjectFromResponse(string response)
        {
            var trimmed = response.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('{');
                var end = trimmed.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    return trimmed[start..(end + 1)];
                }
            }

            var first = trimmed.IndexOf('{');
            var last = trimmed.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                return trimmed[first..(last + 1)];
            }

            return trimmed;
        }

        private static string BuildOutputContract(IEnumerable<string> fieldNames)
        {
            var fields = fieldNames.ToList();
            if (fields.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("<output_format mandatory=\"true\">");
            sb.AppendLine("请严格按以下 JSON 格式输出，不要输出其他内容：");
            sb.AppendLine("{");
            sb.AppendLine("  \"fields\": {");

            for (int i = 0; i < fields.Count; i++)
            {
                var comma = i < fields.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{fields[i]}\": \"[{fields[i]}的具体内容]\"{comma}");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("</output_format>");
            sb.AppendLine();
            sb.AppendLine("<field_requirements>");
            sb.AppendLine("- 每个字段必须填写，不可为空");
            sb.AppendLine("- 内容要具体、有创意、符合上下文");
            sb.AppendLine("- 保持与已有设定的一致性");
            sb.AppendLine("</field_requirements>");

            return sb.ToString();
        }

        protected virtual List<Dictionary<string, object>> ParseBatchJsonResult(string jsonResponse)
        {
            var result = new List<Dictionary<string, object>>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 输入为空");
                return result;
            }

            try
            {
                var jsonArray = ExtractJsonArrayFromResponse(jsonResponse);

                using var document = JsonDocument.Parse(jsonArray);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                {
                    TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 根元素不是数组，类型={root.ValueKind}");
                    return result;
                }

                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 跳过非对象元素，类型={element.ValueKind}");
                        continue;
                    }

                    var entity = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    foreach (var property in element.EnumerateObject())
                    {
                        object value = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                            JsonValueKind.Number => property.Value.TryGetInt32(out var intVal) ? intVal : property.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Array => property.Value.EnumerateArray()
                                .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList(),
                            JsonValueKind.Object => property.Value.ToString(),
                            _ => string.Empty
                        };

                        entity[property.Name] = value;
                    }

                    result.Add(entity);
                }

                var config = GetAIGenerationConfig();
                if (config?.BatchFieldKeyMap != null && config.BatchFieldKeyMap.Count > 0)
                {
                    foreach (var entity in result)
                    {
                        foreach (var kv in config.BatchFieldKeyMap)
                        {
                            var sourceKey = kv.Key;
                            var targetKey = kv.Value;
                            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey))
                            {
                                continue;
                            }

                            if (entity.ContainsKey(targetKey))
                            {
                                continue;
                            }
                            if (entity.TryGetValue(sourceKey, out var v) && v != null)
                            {
                                entity[targetKey] = v;
                                continue;
                            }
                            var matchedKey = FindBestEntityKey(entity, sourceKey);
                            if (matchedKey != null && entity.TryGetValue(matchedKey, out var mv) && mv != null)
                            {
                                entity[targetKey] = mv;
                            }
                        }
                    }
                }

                TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult: 成功解析 {result.Count} 个实体");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] ParseBatchJsonResult 解析失败: {ex.Message}");
            }

            return result;
        }
        private static string? FindBestEntityKey(Dictionary<string, object> entity, string expectedKey)
        {
            if (entity == null || entity.Count == 0 || string.IsNullOrWhiteSpace(expectedKey))
                return null;

            var normalizedExpected = NormalizeBatchKey(expectedKey);
            if (string.IsNullOrWhiteSpace(normalizedExpected))
                return null;

            string? bestMatch = null;
            int bestScore = -1;

            foreach (var key in entity.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var normalizedKey = NormalizeBatchKey(key);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                    continue;

                int score = -1;

                if (string.Equals(normalizedKey, normalizedExpected, StringComparison.Ordinal))
                {
                    score = 1000 + normalizedExpected.Length;
                }
                else if (normalizedKey.Contains(normalizedExpected, StringComparison.Ordinal))
                {
                    score = 500 + normalizedExpected.Length;
                }
                else if (normalizedExpected.Contains(normalizedKey, StringComparison.Ordinal))
                {
                    score = 100 + normalizedKey.Length;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = key;
                }
            }

            return bestMatch;
        }
        private static string NormalizeBatchKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return Regex.Replace(key, @"[\s\p{P}]", string.Empty).ToLowerInvariant();
        }

        protected static string ExtractJsonArrayFromResponse(string response)
        {
            var trimmed = response.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('[');
                var end = trimmed.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    return trimmed[start..(end + 1)];
                }
            }

            var firstBracket = trimmed.IndexOf('[');
            var lastBracket = trimmed.LastIndexOf(']');
            if (firstBracket >= 0 && lastBracket > firstBracket)
            {
                return trimmed[firstBracket..(lastBracket + 1)];
            }

            throw new InvalidOperationException("AI响应中未找到有效的JSON数组内容");
        }

        private void RebuildCategorySelectionTree()
        {
            void Rebuild()
            {
                _categoryNodeIndex.Clear();
                _categorySelectionParentMap.Clear();
                _categoryLookup = new Dictionary<string, TCategory>(StringComparer.Ordinal);

                List<TCategory> categories;
                try
                {
                    categories = GetAllCategoriesFromService() ?? new List<TCategory>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetType().Name}] 构建分类下拉树失败: {ex.Message}");
                    categories = new List<TCategory>();
                }

                var validCategories = categories.Count > 0 ? FilterCategories(categories) : new List<TCategory>();

                if (validCategories.Count > 0)
                {
                    _categoryLookup = validCategories
                        .GroupBy(c => c.Name, StringComparer.Ordinal)
                        .Select(g => g.First())
                        .ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);
                }

                var topLevel = validCategories
                    .Where(c => string.IsNullOrWhiteSpace(c.ParentCategory))
                    .OrderBy(c => c.Order)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .ToList();

                var newTree = new List<TreeNodeItem>();

                var homepageNode = new TreeNodeItem
                {
                    Name = "主页导航",
                    Icon = "🏠",
                    Level = 0,
                    Tag = null,
                    ShowChildCount = false
                };
                _categorySelectionParentMap[homepageNode] = null;

                foreach (var category in topLevel)
                {
                    var mirrorNode = CreateCategorySelectionNode(category, validCategories, 1, homepageNode);
                    homepageNode.Children.Add(mirrorNode);
                }

                newTree.Add(homepageNode);

                CategorySelectionTree.Clear();
                foreach (var node in newTree)
                {
                    CategorySelectionTree.Add(node);
                }

                if (categories.Count == 0)
                {
                    TM.App.Log($"[{GetType().Name}] 无分类数据，仅显示主页导航");
                }

                UpdateCategorySelectionDisplayCore(GetCurrentCategoryValue());
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(Rebuild);
            }
            else
            {
                Rebuild();
            }
        }

        private static List<TCategory> FilterCategories(List<TCategory> categories)
        {
            var unique = categories
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);

            return unique
                .Values
                .Where(c => string.IsNullOrWhiteSpace(c.ParentCategory) || unique.ContainsKey(c.ParentCategory))
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
        }

        private TreeNodeItem CreateCategorySelectionNode(TCategory category, List<TCategory> allCategories, int level, TreeNodeItem parentNode)
        {
            var node = new TreeNodeItem
            {
                Name = category.Name,
                Icon = string.IsNullOrWhiteSpace(category.Icon) ? "📁" : category.Icon,
                Level = level,
                Tag = category,
                ShowChildCount = false
            };

            _categoryNodeIndex[category.Name] = node;
            _categorySelectionParentMap[node] = parentNode;

            var children = allCategories
                .Where(c => string.Equals(c.ParentCategory, category.Name, StringComparison.Ordinal))
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var child in children)
            {
                node.Children.Add(CreateCategorySelectionNode(child, allCategories, level + 1, node));
            }

            return node;
        }

        private void HandleCategoryTreeNodeSelected(TreeNodeItem? node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Tag == null && node.Name == "主页导航")
            {
                TM.App.Log($"[{GetType().Name}] 选中主页导航，用于创建顶级分类");

                SelectedCategoryTreePath = "主页导航";
                SelectedCategoryTreeIcon = "🏠";
                IsCategoryTreeDropdownOpen = false;
                return;
            }

            _suppressCategorySelectionSync = true;
            try
            {
                ApplyCategorySelection(node.Name);
            }
            finally
            {
                _suppressCategorySelectionSync = false;
            }

            var fullPath = BuildNodePathFromTree(node);
            SelectedCategoryTreePath = fullPath;

            if (node.Tag is ICategory category)
            {
                SelectedCategoryTreeIcon = string.IsNullOrWhiteSpace(category.Icon) ? "📁" : category.Icon;
            }
            else
            {
                SelectedCategoryTreeIcon = "📁";
            }

            IsCategoryTreeDropdownOpen = false;
        }

        private string BuildNodePathFromTree(TreeNodeItem targetNode)
        {
            var path = new List<TreeNodeItem>();

            foreach (var root in CategorySelectionTree)
            {
                if (FindNodePathInTree(root, targetNode, path))
                {
                    return string.Join(" > ", path.Select(n => n.Name));
                }
                path.Clear();
            }

            return targetNode.Name;
        }

        private bool FindNodePathInTree(TreeNodeItem current, TreeNodeItem target, List<TreeNodeItem> path)
        {
            path.Add(current);

            if (current == target)
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                if (FindNodePathInTree(child, target, path))
                {
                    return true;
                }
            }

            path.Remove(current);
            return false;
        }

        private void UpdateCategorySelectionDisplayCore(string? categoryName)
        {
            foreach (var prevNode in _lastCategorySelectionPath)
            {
                prevNode.IsSelected = false;
            }
            _lastCategorySelectionPath.Clear();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                SelectedCategoryTreePath = string.Empty;
                SelectedCategoryTreeIcon = string.Empty;
                return;
            }

            if (_categoryNodeIndex.TryGetValue(categoryName, out var node))
            {
                node.IsSelected = true;
                SelectedCategoryTreePath = BuildNodePath(node);
                if (node.Tag is ICategory category)
                {
                    SelectedCategoryTreeIcon = string.IsNullOrWhiteSpace(category.Icon) ? "📁" : category.Icon;
                }
                else
                {
                    SelectedCategoryTreeIcon = "📁";
                }

                _lastCategorySelectionPath.Add(node);
            }
            else
            {
                SelectedCategoryTreePath = categoryName;
                if (_categoryLookup.TryGetValue(categoryName, out var category))
                {
                    SelectedCategoryTreeIcon = string.IsNullOrWhiteSpace(category.Icon) ? "📁" : category.Icon;
                }
                else
                {
                    SelectedCategoryTreeIcon = "📁";
                }
            }
        }

        private void ClearSelectionState(TreeNodeItem node)
        {
            node.IsSelected = false;
            foreach (var child in node.Children)
            {
                ClearSelectionState(child);
            }
        }

        private string BuildNodePath(TreeNodeItem node)
        {
            if (_categorySelectionParentMap.Count > 0 && _categorySelectionParentMap.ContainsKey(node))
            {
                var parts = new List<string>();
                var current = node;
                while (true)
                {
                    parts.Add(current.Name);
                    if (!_categorySelectionParentMap.TryGetValue(current, out var parent) || parent == null)
                    {
                        break;
                    }

                    current = parent;
                }

                parts.Reverse();
                return string.Join(" > ", parts);
            }

            var stack = new Stack<string>();
            var cursor = node;

            while (cursor != null)
            {
                stack.Push(cursor.Name);

                if (cursor.Tag is not ICategory category || string.IsNullOrWhiteSpace(category.ParentCategory))
                {
                    TreeNodeItem? parentNode = null;
                    bool found = false;

                    foreach (var rootNode in CategorySelectionTree)
                    {
                        if (FindParentNode(rootNode, cursor, out parentNode))
                        {
                            cursor = parentNode;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        break;
                    }
                }
                else
                {
                    if (!_categoryNodeIndex.TryGetValue(category.ParentCategory, out cursor))
                    {
                        stack.Push(category.ParentCategory);
                        break;
                    }
                }
            }

            return string.Join(" > ", stack);
        }

        private bool FindParentNode(TreeNodeItem searchRoot, TreeNodeItem targetNode, out TreeNodeItem? parentNode)
        {
            parentNode = null;
            foreach (var child in searchRoot.Children)
            {
                if (child == targetNode)
                {
                    parentNode = searchRoot;
                    return true;
                }

                if (FindParentNode(child, targetNode, out parentNode))
                {
                    return true;
                }
            }
            return false;
        }

        protected bool IsSelectedFromHomepageMirror()
        {
            if (string.IsNullOrWhiteSpace(SelectedCategoryTreePath))
                return false;

            if (SelectedCategoryTreePath == "主页导航")
                return true;

            var pathParts = SelectedCategoryTreePath.Split(new[] { " > " }, StringSplitOptions.None);
            return pathParts.Length > 1 && pathParts[0] == "主页导航";
        }

        protected bool ShouldCreateCategory(string? formCategory)
        {
            return !string.Equals(FormType, "数据", StringComparison.Ordinal);
        }
    }

    public enum NormalizationType
    {
        StaticOptions,
        DynamicList
    }

    public class FieldNormalizationRule
    {
        public string FieldName { get; set; } = string.Empty;

        public NormalizationType Type { get; set; }

        public List<string>? StaticOptions { get; set; }

        public Func<List<string>>? DynamicOptionsProvider { get; set; }

        public string DefaultValue { get; set; } = string.Empty;

        public bool AllowEmpty { get; set; }

        public bool LogWarning { get; set; } = true;
    }

    public class ModuleNormalizationConfig
    {
        public string ModuleName { get; set; } = string.Empty;

        public List<FieldNormalizationRule> Rules { get; set; } = new();
    }
}

