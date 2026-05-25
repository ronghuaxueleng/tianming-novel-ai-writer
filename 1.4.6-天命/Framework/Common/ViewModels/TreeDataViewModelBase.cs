using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Models;
using TM.Framework.Common.Services;

namespace TM.Framework.Common.ViewModels
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public abstract class TreeDataViewModelBase<TData, TCategory> : INotifyPropertyChanged, ITreeActionHost
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
        private bool _bulkUpdating;

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
            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            try
            {
                IsSearching = true;
                await Task.Yield();

                var result = BuildTreeDataAsync(cts.Token);

                if (cts.Token.IsCancellationRequested)
                    return;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (cts.Token.IsCancellationRequested)
                            return;

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

        private List<TreeNodeItem> BuildTreeDataAsync(CancellationToken ct)
        {
            var result = new List<TreeNodeItem>();

            var allCategories = GetAllCategories();
            if (allCategories == null || allCategories.Count == 0)
                return result;

            ct.ThrowIfCancellationRequested();

            var validCategories = FilterOrphanCategories(allCategories);

            var topLevelCategories = validCategories
                .Where(c => string.IsNullOrEmpty(c.ParentCategory))
                .OrderBy(c => c.Order)
                .ToList();

            bool hasSearchKeyword = !string.IsNullOrWhiteSpace(SearchKeyword);
            int totalCount = 0;

            foreach (var category in topLevelCategories)
            {
                ct.ThrowIfCancellationRequested();

                var categoryNode = BuildCategoryTreeWithChildrenLimited(
                    category, validCategories, hasSearchKeyword, MaxLevel, 
                    ref totalCount, MaxDisplayCount, ct);

                if (categoryNode != null)
                {
                    result.Add(categoryNode);
                }

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
                () => IsAIGenerateEnabled && !IsAIGenerating && CanExecuteAIGenerate());
            _treeAfterActionCommand = new RelayCommand(OnTreeAfterActionInternal);

            DataSource.CollectionChanged += (s, e) =>
            {
                if (_bulkUpdating) return;
                TM.App.Log($"[{GetType().Name}] 检测到DataSource集合变化，自动刷新TreeData");
                RefreshTreeData();
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

        private async Task ExecuteAIGenerateAsyncInternal()
        {
            if (IsAIGenerating)
            {
                TM.App.Log($"[{GetType().Name}] 忽略重复的AI智能生成请求（上一任务尚未完成）");
                return;
            }

            if (!await ProtectionService.CheckFeatureAuthorizationAsync(AIFeatureId))
            {
                GlobalToast.Warning("功能受限", "您的订阅计划不支持此功能，请升级订阅");
                return;
            }

            try
            {
                IsAIGenerating = true;
                TM.App.Log($"[{GetType().Name}] 开始执行AI智能生成功能");
                await ExecuteAIGenerateAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] AI智能生成失败: {ex}");
                GlobalToast.Error("生成失败", $"AI智能生成失败：{ex.Message}");
            }
            finally
            {
                IsAIGenerating = false;
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
            private set
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

        protected virtual void RefreshTreeData()
        {
            if (_refreshScheduled) return;
            _refreshScheduled = true;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ExecuteRefreshTreeData));
            }
            else
            {
                ExecuteRefreshTreeData();
            }
        }

        private void ExecuteRefreshTreeData()
        {
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

                if (node.Children.Any())
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

                if (node.Children.Any())
                {
                    RestoreExpandedStateRecursive(node.Children, expandedNodes);
                }
            }
        }

        private List<TCategory> FilterOrphanCategories(List<TCategory> allCategories)
        {
            var categoryNames = new HashSet<string>(allCategories.Select(c => c.Name));
            return allCategories
                .Where(c => string.IsNullOrEmpty(c.ParentCategory) || categoryNames.Contains(c.ParentCategory))
                .ToList();
        }

        private TreeNodeItem? BuildCategoryTreeWithChildren(
            TCategory category,
            List<TCategory> allCategories,
            bool hasSearchKeyword,
            int maxLevel)
        {
            var childrenData = GetChildrenDataForCategory(category.Name);

            var childCategories = allCategories
                .Where(c => c.ParentCategory == category.Name)
                .OrderBy(c => c.Order)
                .ToList();

            var childCategoryNodes = new List<TreeNodeItem>();
            if (category.Level < maxLevel)
            {
                foreach (var child in childCategories)
                {
                    var childNode = BuildCategoryTreeWithChildren(child, allCategories, hasSearchKeyword, maxLevel);
                    if (childNode != null)
                    {
                        childCategoryNodes.Add(childNode);
                    }
                }
            }

            if (hasSearchKeyword)
            {
                bool hasMatchedContent = childrenData.Any() || childCategoryNodes.Any();
                if (!hasMatchedContent)
                {
                    return null;
                }
            }

            var categoryNode = new TreeNodeItem
            {
                Name = category.Name,
                Icon = category.Icon,
                LogoImage = GetCategoryLogoImage(category),
                Level = category.Level,
                Tag = category,
                IsExpanded = hasSearchKeyword && (childrenData.Any() || childCategoryNodes.Any()),
                ShowChildCount = true
            };

            foreach (var childNode in childCategoryNodes)
            {
                categoryNode.Children.Add(childNode);
            }

            foreach (var childData in childrenData)
            {
                var childNode = ConvertDataToTreeNode(childData);
                childNode.Level = category.Level + 1;
                categoryNode.Children.Add(childNode);
            }

            return categoryNode;
        }

        private TreeNodeItem? BuildCategoryTreeWithChildrenLimited(
            TCategory category,
            List<TCategory> allCategories,
            bool hasSearchKeyword,
            int maxLevel,
            ref int totalCount,
            int maxCount,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (totalCount >= maxCount)
                return null;

            var childrenData = GetChildrenDataForCategory(category.Name);

            var childCategories = allCategories
                .Where(c => c.ParentCategory == category.Name)
                .OrderBy(c => c.Order)
                .ToList();

            var childCategoryNodes = new List<TreeNodeItem>();
            if (category.Level < maxLevel)
            {
                foreach (var child in childCategories)
                {
                    if (totalCount >= maxCount) break;
                    ct.ThrowIfCancellationRequested();

                    var childNode = BuildCategoryTreeWithChildrenLimited(
                        child, allCategories, hasSearchKeyword, maxLevel, 
                        ref totalCount, maxCount, ct);
                    if (childNode != null)
                    {
                        childCategoryNodes.Add(childNode);
                    }
                }
            }

            if (hasSearchKeyword)
            {
                bool hasMatchedContent = childrenData.Any() || childCategoryNodes.Any();
                if (!hasMatchedContent)
                {
                    return null;
                }
            }

            var categoryNode = new TreeNodeItem
            {
                Name = category.Name,
                Icon = category.Icon,
                LogoImage = GetCategoryLogoImage(category),
                Level = category.Level,
                Tag = category,
                IsExpanded = hasSearchKeyword && (childrenData.Any() || childCategoryNodes.Any()),
                ShowChildCount = true
            };

            foreach (var childNode in childCategoryNodes)
            {
                categoryNode.Children.Add(childNode);
            }

            foreach (var childData in childrenData)
            {
                if (totalCount >= maxCount) break;

                var childNode = ConvertDataToTreeNode(childData);
                childNode.Level = category.Level + 1;
                categoryNode.Children.Add(childNode);
                totalCount++;
            }

            return categoryNode;
        }

        protected abstract List<TCategory> GetAllCategories();

        protected abstract List<TData> GetChildrenDataForCategory(string categoryName);

        protected abstract TreeNodeItem ConvertDataToTreeNode(TData data);

        protected virtual bool IsNodeCategory(TreeNodeItem node)
        {
            return node?.Tag is TCategory;
        }

        protected virtual TCategory? GetCategoryFromNode(TreeNodeItem node)
        {
            return node?.Tag is TCategory category ? category : default;
        }

        protected virtual TData? GetDataFromNode(TreeNodeItem node)
        {
            return node?.Tag is TData data ? data : default;
        }

        protected virtual bool TryAddCategory(string categoryName, string? parentCategoryName = null)
        {
            TM.App.Log($"[TreeDataViewModelBase] TryAddCategory未实现，子类需要重写此方法以支持分类管理");
            return false;
        }

        protected virtual bool TryDeleteCategory(TCategory category)
        {
            TM.App.Log($"[TreeDataViewModelBase] TryDeleteCategory未实现，子类需要重写此方法以支持分类管理");
            return false;
        }

        protected virtual bool TryUpdateCategory(TCategory category)
        {
            TM.App.Log($"[TreeDataViewModelBase] TryUpdateCategory未实现，子类需要重写此方法以支持分类管理");
            return false;
        }

        protected virtual string GetCategoryIcon(string categoryName)
        {
            return "📁";
        }

        protected virtual System.Windows.Media.ImageSource? GetCategoryLogoImage(TCategory category)
        {
            try
            {
                var logoPath = (category as ILogoPathHost)?.LogoPath;
                var icon = category.Icon ?? "";

                if (category is ILogoPathHost)
                {
                    if (string.IsNullOrEmpty(logoPath) && category.Level == 1)
                    {
                        logoPath = "app.png";
                    }
                    else if (string.IsNullOrEmpty(logoPath) && category.Level >= 2)
                    {
                        logoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(category.Name);
                    }

                    return TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(logoPath, icon);
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetCategoryLogoImage), ex);
            }

            return null;
        }

        protected virtual void OnNodeDoubleClick(TreeNodeItem node)
        {
            var data = GetDataFromNode(node);
            if (data != null)
            {
                OnDataSelected(data);
            }
        }

        protected virtual void OnDataSelected(TData data)
        {
        }

        protected virtual bool TrySaveData(TData data)
        {
            TM.App.Log($"[TreeDataViewModelBase] TrySaveData未实现，子类需要重写此方法以支持数据保存");
            return false;
        }

        protected virtual bool TryDeleteData(TData data)
        {
            TM.App.Log($"[TreeDataViewModelBase] TryDeleteData未实现，子类需要重写此方法以支持数据删除");
            return false;
        }

        protected virtual TData? CreateNewData(string? categoryName = null)
        {
            TM.App.Log($"[TreeDataViewModelBase] CreateNewData未实现，子类需要重写此方法以支持新建数据");
            return default;
        }

        protected virtual void OnDataChanged()
        {
            RefreshTreeData();
        }

        protected virtual void OnTreeDataRefreshed()
        {
        }

        protected void FocusTreeNode(Func<TreeNodeItem, bool> predicate)
        {
            if (predicate == null)
            {
                return;
            }

            var path = FindNodePath(TreeData, predicate);
            if (path == null || path.Count == 0)
            {
                return;
            }

            ClearSelection(TreeData);

            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];

                if (i < path.Count - 1 && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                }

                node.IsSelected = true;
                node.IsSelectionFocus = i == path.Count - 1;
            }
        }

        private static List<TreeNodeItem>? FindNodePath(IEnumerable<TreeNodeItem> nodes, Func<TreeNodeItem, bool> predicate)
        {
            foreach (var node in nodes)
            {
                var path = FindNodePathRecursive(node, predicate);
                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        private static List<TreeNodeItem>? FindNodePathRecursive(TreeNodeItem current, Func<TreeNodeItem, bool> predicate)
        {
            if (predicate(current))
            {
                return new List<TreeNodeItem> { current };
            }

            foreach (var child in current.Children)
            {
                var childPath = FindNodePathRecursive(child, predicate);
                if (childPath != null)
                {
                    childPath.Insert(0, current);
                    return childPath;
                }
            }

            return null;
        }

        private static void ClearSelection(IEnumerable<TreeNodeItem> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    node.IsSelected = false;
                }

                if (node.IsSelectionFocus)
                {
                    node.IsSelectionFocus = false;
                }

                if (node.Children.Count > 0)
                {
                    ClearSelection(node.Children);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
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
    }
}

