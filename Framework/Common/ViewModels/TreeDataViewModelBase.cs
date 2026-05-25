using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.ViewModels
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public abstract partial class TreeDataViewModelBase<TData, TCategory> : INotifyPropertyChanged, ITreeActionHost
        where TCategory : ICategory
    {
        private string _searchKeyword = string.Empty;
        private bool _isAIGenerating;
        private const int MaxLevel = 5;
        private bool _showAIGenerateButton;
        private bool _isAIGenerateEnabled;
        private readonly AsyncRelayCommand _aiGenerateCommand;
        private readonly RelayCommand _treeAfterActionCommand;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private const int SearchDebounceMs = 300;
        private DispatcherTimer? _searchDebounceTimer;
        private CancellationTokenSource? _searchCts;
        private bool _isSearching;

        private bool _refreshScheduled;
        private bool _immediateRefreshScheduled;
        private bool _bulkUpdating;
        private CancellationTokenSource? _refreshCts;

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[TreeDataViewModelBase] {key}: {ex.Message}");
        }

        protected virtual int MaxDisplayCount => 200;

        public ObservableCollection<TData> DataSource { get; }

        public RangeObservableCollection<TreeNodeItem> TreeData { get; }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                if (_searchKeyword != value)
                {
                    _searchKeyword = value;
                    OnPropertyChanged();
                    ScheduleSearch();
                }
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                if (_isSearching != value)
                {
                    _isSearching = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ScheduleSearch()
        {
            _searchCts?.Cancel();

            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
                };
                _searchDebounceTimer.Tick += async (s, e) =>
                {
                    _searchDebounceTimer.Stop();
                    await ExecuteSearchAsync();
                };
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async Task ExecuteSearchAsync()
        {
            var oldSearchCts = _searchCts;
            oldSearchCts?.Cancel();
            oldSearchCts?.Dispose();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            try
            {
                IsSearching = true;

                var plans = await Task.Run(() => CollectSearchTreeData(cts.Token), cts.Token);

                if (cts.Token.IsCancellationRequested)
                    return;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (cts.Token.IsCancellationRequested)
                            return;

                        var result = BuildTreeNodesFromPlan(plans, cts.Token);
                        TreeData.ReplaceAll(result);
                        OnTreeDataRefreshed();
                    });
                }
            }
            catch (OperationCanceledException ex)
            {
                DebugLogOnce("ExecuteSearchAsync_Canceled", ex);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 搜索失败: {ex.Message}");
            }
            finally
            {
                if (_searchCts == cts)
                {
                    IsSearching = false;
                }
            }
        }

        private sealed class TreeBuildPlan
        {
            public TCategory Category { get; set; } = default!;
            public List<TreeBuildPlan> ChildPlans { get; } = new();
            public List<TData> ChildrenData { get; } = new();
        }

        private List<TreeBuildPlan>? CollectSearchTreeData(CancellationToken ct)
        {
            var allCategories = GetAllCategories();
            if (allCategories == null || allCategories.Count == 0)
                return null;

            ct.ThrowIfCancellationRequested();

            var validCategories = FilterOrphanCategories(allCategories);

            var topLevelCategories = validCategories
                .Where(c => string.IsNullOrEmpty(c.ParentCategory))
                .OrderBy(c => c.Order)
                .ToList();

            int totalCount = 0;
            var result = new List<TreeBuildPlan>();

            foreach (var category in topLevelCategories)
            {
                ct.ThrowIfCancellationRequested();

                var plan = CollectCategoryPlanLimited(
                    category, validCategories, MaxLevel,
                    ref totalCount, MaxDisplayCount, ct);

                if (plan != null)
                    result.Add(plan);

                if (totalCount >= MaxDisplayCount)
                    break;
            }

            return result;
        }

        protected TreeDataViewModelBase()
        {
            DataSource = new ObservableCollection<TData>();
            TreeData = new RangeObservableCollection<TreeNodeItem>();

            _aiGenerateCommand = new AsyncRelayCommand(
                ExecuteAIGenerateAsyncInternal,
                () => IsAIGenerateEnabled && !IsAIGenerating && !_isAIGenerateInProgress && CanExecuteAIGenerate());
            _treeAfterActionCommand = new RelayCommand(OnTreeAfterActionInternal);

            DataSource.CollectionChanged += (s, e) =>
            {
                if (_bulkUpdating) return;
                TM.App.Log($"[{GetType().Name}] 检测到DataSource集合变化，自动刷新TreeData");
                RefreshTreeData();
            };

            var weakSelf = new WeakReference<TreeDataViewModelBase<TData, TCategory>>(this);
            TM.Framework.Common.Helpers.AI.ProviderLogoHelper.LogoCacheUpdated += () =>
            {
                if (!weakSelf.TryGetTarget(out var self)) return;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(self.RefreshTreeData));
            };
        }

        private void OnTreeAfterActionInternal(object? parameter)
        {
            var action = parameter?.ToString();
            TM.App.Log($"[{GetType().Name}] TreeAfterAction触发: {action ?? "(未指定)"}");
            try
            {
                OnTreeAfterAction(action);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] TreeAfterAction处理失败: {ex.Message}");
            }
        }

        protected virtual void OnTreeAfterAction(string? action)
        {
            RefreshTreeData();
        }

        private bool _isAIGenerateInProgress;

        private async Task ExecuteAIGenerateAsyncInternal()
        {
            if (_isAIGenerateInProgress || IsAIGenerating)
            {
                TM.App.Log($"[{GetType().Name}] 忽略重复的AI智能生成请求（上一任务尚未完成）");
                return;
            }

            _isAIGenerateInProgress = true;
            IsAIGenerating = true;
            _aiGenerateCommand.RaiseCanExecuteChanged();
            try
            {
                TM.App.Log($"[{GetType().Name}] 开始执行AI智能生成功能");
                await ExecuteAIGenerateAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] AI智能生成失败: {ex}");
                GlobalToast.Error("生成失败", $"AI生成失败：{ex.Message}");
            }
            finally
            {
                IsAIGenerating = false;
                _isAIGenerateInProgress = false;
                _aiGenerateCommand.RaiseCanExecuteChanged();
            }
        }

        protected virtual Task ExecuteAIGenerateAsync()
        {
            TM.App.Log($"[{GetType().Name}] 未实现AI智能生成逻辑");
            GlobalToast.Info("功能待接入", "当前页面尚未实现AI智能生成功能");
            return Task.CompletedTask;
        }

        protected virtual bool CanExecuteAIGenerate() => true;

        protected virtual string AIFeatureId => "writing.ai";

        public bool ShowAIGenerateButton
        {
            get => _showAIGenerateButton;
            set
            {
                if (_showAIGenerateButton != value)
                {
                    _showAIGenerateButton = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAIGenerateEnabled
        {
            get => _isAIGenerateEnabled;
            set
            {
                if (_isAIGenerateEnabled != value)
                {
                    _isAIGenerateEnabled = value;
                    OnPropertyChanged();
                    _aiGenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAIGenerating
        {
            get => _isAIGenerating;
            protected set
            {
                if (_isAIGenerating == value)
                {
                    return;
                }

                _isAIGenerating = value;
                OnPropertyChanged();
                _aiGenerateCommand.RaiseCanExecuteChanged();
            }
        }

        public ICommand AIGenerateCommand => _aiGenerateCommand;

        public ICommand TreeAfterActionCommand => _treeAfterActionCommand;

        protected void BeginBulkUpdate() => _bulkUpdating = true;

        protected void EndBulkUpdate()
        {
            _bulkUpdating = false;
            RefreshTreeData();
        }

        protected virtual DispatcherPriority RefreshDispatcherPriority => DispatcherPriority.Background;

        public void ForceRefreshTreeData()
        {
            ResetRefreshDebounce();

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(() =>
                {
                    _refreshScheduled = false;
                    ExecuteRefreshTreeDataSync();
                });
                return;
            }

            _refreshScheduled = false;
            ExecuteRefreshTreeDataSync();
        }

        public void ScheduleImmediateRefreshTreeData()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ForceRefreshTreeData();
                return;
            }

            if (_immediateRefreshScheduled) return;
            _immediateRefreshScheduled = true;

            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _immediateRefreshScheduled = false;
                _refreshScheduled = false;
                ExecuteRefreshTreeDataAsync();
            }));
        }

        protected virtual void RefreshTreeData()
        {
            if (_refreshScheduled) return;
            _refreshScheduled = true;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(RefreshDispatcherPriority, new Action(ExecuteRefreshTreeDataAsync));
            }
            else
            {
                ExecuteRefreshTreeDataSync();
            }
        }

        protected void ResetRefreshDebounce()
        {
            _refreshScheduled = false;
        }

        private void ExecuteRefreshTreeDataSync()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;

            _refreshScheduled = false;

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                ScheduleSearch();
                return;
            }

            try
            {
                var expandedState = SaveExpandedState(TreeData);

                var allCategories = GetAllCategories();
                if (allCategories == null || allCategories.Count == 0)
                {
                    TreeData.Clear();
                    return;
                }

                var validCategories = FilterOrphanCategories(allCategories);

                var topLevelCategories = validCategories
                    .Where(c => string.IsNullOrEmpty(c.ParentCategory))
                    .OrderBy(c => c.Order)
                    .ToList();

                bool hasSearchKeyword = !string.IsNullOrWhiteSpace(SearchKeyword);

                var newNodes = new List<TreeNodeItem>();
                foreach (var category in topLevelCategories)
                {
                    var categoryNode = BuildCategoryTreeWithChildren(
                        category,
                        validCategories,
                        hasSearchKeyword,
                        MaxLevel);

                    if (categoryNode != null)
                    {
                        newNodes.Add(categoryNode);
                    }
                }

                TreeData.ReplaceAll(newNodes);

                RestoreExpandedState(TreeData, expandedState);

                OnTreeDataRefreshed();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 刷新树形数据失败: {ex.Message}");
            }
        }

        private async void ExecuteRefreshTreeDataAsync()
        {
            _refreshScheduled = false;

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                ScheduleSearch();
                return;
            }

            var oldCts = _refreshCts;
            oldCts?.Cancel();
            oldCts?.Dispose();
            var cts = new CancellationTokenSource();
            _refreshCts = cts;

            var token = cts.Token;

            try
            {
                var expandedState = SaveExpandedState(TreeData);

                var plans = await Task.Run(() => CollectRefreshTreeData(token), token);

                if (token.IsCancellationRequested)
                    return;

                if (plans == null || plans.Count == 0)
                {
                    TreeData.Clear();
                    return;
                }

                var newNodes = BuildTreeNodesFromPlan(plans, token, autoExpand: false);
                TreeData.ReplaceAll(newNodes);
                RestoreExpandedState(TreeData, expandedState);
                OnTreeDataRefreshed();
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 异步刷新树形数据失败: {ex.Message}");
            }
        }

        private List<TreeBuildPlan>? CollectRefreshTreeData(CancellationToken ct)
        {
            var allCategories = GetAllCategories();
            if (allCategories == null || allCategories.Count == 0)
                return null;

            ct.ThrowIfCancellationRequested();

            var validCategories = FilterOrphanCategories(allCategories);
            var topLevel = validCategories
                .Where(c => string.IsNullOrEmpty(c.ParentCategory))
                .OrderBy(c => c.Order)
                .ToList();

            var result = new List<TreeBuildPlan>();
            foreach (var category in topLevel)
            {
                ct.ThrowIfCancellationRequested();
                var plan = CollectCategoryPlanForRefresh(category, validCategories, MaxLevel, ct);
                if (plan != null)
                    result.Add(plan);
            }
            return result;
        }

        private TreeBuildPlan? CollectCategoryPlanForRefresh(
            TCategory category, List<TCategory> allCategories, int maxLevel,
            CancellationToken ct, int currentLevel = 1)
        {
            ct.ThrowIfCancellationRequested();

            var plan = new TreeBuildPlan { Category = category };

            plan.ChildrenData.AddRange(GetChildrenDataForCategory(category.Name));

            if (currentLevel < maxLevel)
            {
                var children = allCategories
                    .Where(c => c.ParentCategory == category.Name)
                    .OrderBy(c => c.Order);

                foreach (var child in children)
                {
                    ct.ThrowIfCancellationRequested();
                    var childPlan = CollectCategoryPlanForRefresh(child, allCategories, maxLevel, ct, currentLevel + 1);
                    if (childPlan != null)
                        plan.ChildPlans.Add(childPlan);
                }
            }

            return plan;
        }

        private HashSet<string> SaveExpandedState(ObservableCollection<TreeNodeItem> nodes)
        {
            var expandedNodes = new HashSet<string>();
            SaveExpandedStateRecursive(nodes, expandedNodes);
            return expandedNodes;
        }

        private void SaveExpandedStateRecursive(IEnumerable<TreeNodeItem> nodes, HashSet<string> expandedNodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsExpanded)
                {
                    expandedNodes.Add(node.Name);
                }

                if (node.Children.Count > 0)
                {
                    SaveExpandedStateRecursive(node.Children, expandedNodes);
                }
            }
        }

        private void RestoreExpandedState(ObservableCollection<TreeNodeItem> nodes, HashSet<string> expandedNodes)
        {
            RestoreExpandedStateRecursive(nodes, expandedNodes);
        }

        private void RestoreExpandedStateRecursive(IEnumerable<TreeNodeItem> nodes, HashSet<string> expandedNodes)
        {
            foreach (var node in nodes)
            {
                if (expandedNodes.Contains(node.Name))
                {
                    node.IsExpanded = true;
                }

                if (node.Children.Count > 0)
                {
                    RestoreExpandedStateRecursive(node.Children, expandedNodes);
                }
            }
        }

    }

    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public RangeObservableCollection() : base() { }
        public RangeObservableCollection(IEnumerable<T> collection) : base(collection) { }

        public void ReplaceAll(IList<T> newItems)
        {
            Items.Clear();
            foreach (var item in newItems)
                Items.Add(item);
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        public void InsertRange(int index, IList<T> items)
        {
            if (items == null || items.Count == 0) return;
            foreach (var item in items)
                Items.Insert(index++, item);
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        public void RemoveRange(int index, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
                Items.RemoveAt(index);
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        public void BatchInsert(int index, IList<T> items)
        {
            if (items == null || items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                Items.Insert(index + i, items[i]);
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                    System.Collections.Specialized.NotifyCollectionChangedAction.Add, items[i], index + i));
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        public void BatchRemove(int index, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                var item = Items[index];
                Items.RemoveAt(index);
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                    System.Collections.Specialized.NotifyCollectionChangedAction.Remove, item, index));
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
    }
}

