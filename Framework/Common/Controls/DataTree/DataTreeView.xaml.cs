using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.Common.Controls
{
    public enum ParentNodeClickMode
    {
        Select,

        Toggle
    }

    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DataTreeView : UserControl
    {
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[DataTreeView] {key}: {ex.Message}");
        }

        private readonly ICommand _internalParentNodeClickCommand;
        private readonly ICommand _internalDeleteCommand;
        private readonly ICommand _internalAddCommand;
        private readonly ICommand _internalDeleteAllCommand;
        private readonly ICommand _internalSaveCommand;
        private readonly ICommand _internalEnableSelectedCommand;
        private readonly ICommand _internalEditCommand;
        private readonly ICommand _internalBulkToggleCommand;
        private readonly ICommand _internalAddChapterCommand;
        private TreeNodeItem? _selectedNode;
        private ObservableCollection<TreeNodeItem>? _originalItemsSource;
        private ICommand? _currentAIGenerateCommand;

        private List<TreeNodeItem> _selectedPath = new();
        private List<TreeNodeItem> _siblingBranchNodes = new();
        private readonly Dictionary<TreeNodeItem, TreeNodeItem?> _parentMap = new();

        private ScrollViewer? _cachedScrollViewer;

        private bool _isActivated;
        private TreeNodeItem? _activatedNode;
        private bool _isDoubleClickInProgress;
        private bool _commandRefreshPending;

        private readonly RangeObservableCollection<TreeNodeItem> _flatList = new();

        private System.Windows.Threading.DispatcherTimer? _activeExpandTimer;

        public DataTreeView()
        {
            InitializeComponent();
            _internalParentNodeClickCommand = new RelayCommand(HandleParentNodeClick);
            _internalDeleteCommand = new RelayCommand(() => HandleInternalDelete());
            _internalAddCommand = new RelayCommand(() => HandleInternalAdd());
            _internalDeleteAllCommand = new RelayCommand(HandleInternalDeleteAll, CanExecuteInternalDeleteAll);
            _internalSaveCommand = new RelayCommand(HandleInternalSave);
            _internalEnableSelectedCommand = new RelayCommand(HandleInternalEnableSelected);
            _internalEditCommand = new RelayCommand(HandleInternalEdit);
            _internalBulkToggleCommand = new RelayCommand(HandleInternalBulkToggle);
            _internalAddChapterCommand = new RelayCommand(ExecuteAddChapterFromMenu);

            Loaded += DataTreeView_Loaded;
            Unloaded += DataTreeView_Unloaded;
            IsVisibleChanged += DataTreeView_IsVisibleChanged;

            this.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void RebuildParentMap()
        {
            _parentMap.Clear();
            var roots = ItemsSource;
            if (roots == null)
            {
                return;
            }

            foreach (var root in roots)
            {
                BuildParentMapRecursive(root, null);
            }
        }

        private void BuildParentMapRecursive(TreeNodeItem node, TreeNodeItem? parent)
        {
            _parentMap[node] = parent;
            foreach (var child in node.Children)
            {
                BuildParentMapRecursive(child, node);
            }
        }

        private bool TryBuildPathFromParentMap(TreeNodeItem targetNode, List<TreeNodeItem> path)
        {
            path.Clear();
            if (_parentMap.Count == 0)
            {
                return false;
            }

            if (!_parentMap.ContainsKey(targetNode))
            {
                return false;
            }

            var current = targetNode;
            while (true)
            {
                path.Add(current);
                if (!_parentMap.TryGetValue(current, out var parent) || parent == null)
                {
                    break;
                }

                current = parent;
            }

            path.Reverse();
            return path.Count > 0;
        }

        private void OnNodeContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
                return;

            menu.DataContext = _selectedNode;

            menu.Tag = new NodeContextMenuHost(this);
            RefreshInternalCommandStates();
        }

        private void ExecuteSaveSelectedNode()
        {
            var cmd = SaveCategoryCommand;
            if (cmd == null)
                return;

            try
            {
                if (cmd.CanExecute(_selectedNode))
                {
                    cmd.Execute(_selectedNode);
                    ExecuteAfterAction("Save");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataTreeView] InternalSaveCommand 执行失败: {ex.Message}");
            }
        }

        private void ExecuteAddChapterFromMenu()
        {
            if (AddChapterCommand?.CanExecute(null) == true)
            {
                AddChapterCommand.Execute(null);
            }
        }

        private sealed class NodeContextMenuHost
        {
            private readonly DataTreeView _owner;

            public NodeContextMenuHost(DataTreeView owner)
            {
                _owner = owner;
            }

            public ICommand InternalAddCommand => _owner.InternalAddCommand;
            public ICommand InternalSaveCommand => _owner.InternalSaveCommand;
            public ICommand InternalDeleteCommand => _owner.InternalDeleteCommand;
            public ICommand InternalDeleteAllCommand => _owner.InternalDeleteAllCommand;
            public ICommand InternalEnableSelectedCommand => _owner.InternalEnableSelectedCommand;
            public ICommand InternalEditCommand => _owner.InternalEditCommand;
            public ICommand? RefreshCommand => _owner.RefreshCommand;
            public ICommand InternalAddChapterCommand => _owner.InternalAddChapterCommand;
            public string EnableSelectedHeader
            {
                get
                {
                    var node = _owner._selectedNode;
                    if (node?.Tag is TM.Framework.Common.Models.ICategory category)
                    {
                        return category.IsEnabled ? "禁用" : "启用";
                    }

                    if (node?.Tag is TM.Framework.Common.Models.IEnableable enableable)
                    {
                        return enableable.IsEnabled ? "禁用" : "启用";
                    }

                    return "启用";
                }
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cachedScrollViewer ??= FindChildScrollViewer(RootItemsControl);
            if (_cachedScrollViewer != null)
            {
                _cachedScrollViewer.ScrollToVerticalOffset(_cachedScrollViewer.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            }
        }

        private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;
                var result = FindChildScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        #region 扁平树管理

        internal void RebuildFlatList()
        {
            _activeExpandTimer?.Stop();
            _activeExpandTimer = null;

            var items = new List<TreeNodeItem>();
            if (_originalItemsSource != null)
            {
                foreach (var root in _originalItemsSource)
                    CollectFlatNodes(root, items);
            }
            _flatList.ReplaceAll(items);
        }

        private static void CollectFlatNodes(TreeNodeItem node, List<TreeNodeItem> output)
        {
            output.Add(node);
            if (node.IsExpanded && node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectFlatNodes(child, output);
            }
        }

        private const int FirstBatchSize = 20;
        private const int AsyncBatchSize = 15;

        internal void ToggleNodeInFlatList(TreeNodeItem node)
        {
            int parentIndex = _flatList.IndexOf(node);
            if (parentIndex < 0)
            {
                node.IsExpanded = !node.IsExpanded;
                RebuildFlatList();
                return;
            }

            try
            {
                if (!node.IsExpanded)
                {
                    node.IsExpanded = true;
                    var children = new List<TreeNodeItem>();
                    CollectFlatChildNodes(node, children);

                    if (children.Count <= FirstBatchSize)
                    {
                        if (children.Count > 0)
                            _flatList.BatchInsert(parentIndex + 1, children);
                    }
                    else
                    {
                        var firstBatch = children.GetRange(0, FirstBatchSize);
                        _flatList.BatchInsert(parentIndex + 1, firstBatch);

                        int inserted = FirstBatchSize;
                        var remaining = children;
                        int baseIndex = parentIndex + 1;

                        _activeExpandTimer?.Stop();
                        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ContextIdle);
                        _activeExpandTimer = timer;
                        timer.Interval = TimeSpan.FromMilliseconds(50);
                        timer.Tick += (s, e) =>
                        {
                            if (!ReferenceEquals(_activeExpandTimer, timer) || inserted >= remaining.Count || !node.IsExpanded)
                            {
                                timer.Stop();
                                return;
                            }
                            int batchCount = Math.Min(AsyncBatchSize, remaining.Count - inserted);
                            var batch = remaining.GetRange(inserted, batchCount);
                            int insertAt = baseIndex + inserted;
                            try { _flatList.BatchInsert(insertAt, batch); }
                            catch { timer.Stop(); return; }
                            inserted += batchCount;
                            if (inserted >= remaining.Count) timer.Stop();
                        };
                        timer.Start();
                    }
                }
                else
                {
                    int removeCount = 0;
                    for (int i = parentIndex + 1; i < _flatList.Count; i++)
                    {
                        if (_flatList[i].Level <= node.Level) break;
                        removeCount++;
                    }
                    node.IsExpanded = false;
                    if (removeCount > 0)
                        _flatList.BatchRemove(parentIndex + 1, removeCount);
                }
            }
            catch
            {
                node.IsExpanded = !node.IsExpanded;
                RebuildFlatList();
            }
        }

        private static void CollectFlatChildNodes(TreeNodeItem node, List<TreeNodeItem> output)
        {
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                output.Add(child);
                if (child.IsExpanded && child.Children != null)
                    CollectFlatChildNodes(child, output);
            }
        }

        #endregion

        #region 拖拽排序

        private Point _dragStartPoint;
        private TreeNodeItem? _draggedItem;
        private bool _isDragging;
        private DateTime _dragMouseDownTime;
        private bool _dragLongPressReady;
        private const double DragThreshold = 8.0;
        private const int DragLongPressMs = 3000;

        internal void OnNodePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SelectOnDoubleClickOnly)
                _isDoubleClickInProgress = e.ClickCount >= 2;

            if (sender is Button btn && btn.DataContext is TreeNodeItem item)
            {
                _dragStartPoint = e.GetPosition(this);
                _draggedItem = item;
                _dragMouseDownTime = DateTime.Now;
                _dragLongPressReady = false;
            }
        }

        internal void OnNodePreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                _draggedItem = null;
                _dragLongPressReady = false;
            }
        }

        internal void OnNodePreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
                return;

            if (!_dragLongPressReady)
            {
                var elapsed = (DateTime.Now - _dragMouseDownTime).TotalMilliseconds;
                if (elapsed < DragLongPressMs)
                    return;
                _dragLongPressReady = true;
            }

            var currentPos = e.GetPosition(this);
            var diff = currentPos - _dragStartPoint;

            if (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _draggedItem.IsDragging = true;

                    var data = new DataObject("TreeNodeItem", _draggedItem);
                    DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);

                    _isDragging = false;
                    _draggedItem.IsDragging = false;
                    ClearAllDragOverStates();
                    _draggedItem = null;
                }
            }
        }

        internal void OnNodeDragEnter(object sender, DragEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TreeNodeItem targetItem)
            {
                if (e.Data.GetData("TreeNodeItem") is TreeNodeItem draggedItem && draggedItem != targetItem)
                {
                    if (AreSiblings(draggedItem, targetItem))
                    {
                        targetItem.IsDragOver = true;
                        e.Effects = DragDropEffects.Move;
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }
            }
            e.Handled = true;
        }

        internal void OnNodeDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TreeNodeItem targetItem)
            {
                targetItem.IsDragOver = false;
            }
            e.Handled = true;
        }

        internal void OnNodeDrop(object sender, DragEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TreeNodeItem targetItem)
            {
                targetItem.IsDragOver = false;

                if (e.Data.GetData("TreeNodeItem") is TreeNodeItem draggedItem && draggedItem != targetItem)
                {
                    if (AreSiblings(draggedItem, targetItem))
                    {
                        var parent = FindParentCollection(draggedItem);
                        if (parent != null)
                        {
                            var oldIndex = parent.IndexOf(draggedItem);
                            var newIndex = parent.IndexOf(targetItem);

                            if (oldIndex != newIndex && oldIndex >= 0 && newIndex >= 0)
                            {
                                parent.Move(oldIndex, newIndex);
                                RebuildFlatList();
                                ExecuteAfterAction("Reorder");
                            }
                        }
                    }
                }
            }
            e.Handled = true;
        }

        private bool AreSiblings(TreeNodeItem item1, TreeNodeItem item2)
        {
            if (item1.Level != item2.Level)
                return false;

            var parent1 = FindParentCollection(item1);
            var parent2 = FindParentCollection(item2);

            return parent1 != null && parent1 == parent2;
        }

        private ObservableCollection<TreeNodeItem>? FindParentCollection(TreeNodeItem item)
        {
            if (_parentMap.Count > 0 && _parentMap.TryGetValue(item, out var parent))
                return parent != null ? parent.Children : ItemsSource;

            if (ItemsSource != null && ItemsSource.Contains(item))
                return ItemsSource;

            if (ItemsSource != null)
            {
                foreach (var root in ItemsSource)
                {
                    var result = FindParentCollectionRecursive(root, item);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }

        private ObservableCollection<TreeNodeItem>? FindParentCollectionRecursive(TreeNodeItem parent, TreeNodeItem target)
        {
            if (parent.Children.Contains(target))
                return parent.Children;

            foreach (var child in parent.Children)
            {
                var result = FindParentCollectionRecursive(child, target);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void ClearAllDragOverStates()
        {
            if (ItemsSource == null) return;

            foreach (var item in ItemsSource)
            {
                ClearDragOverRecursive(item);
            }
        }

        private void ClearDragOverRecursive(TreeNodeItem item)
        {
            item.IsDragOver = false;
            item.IsDragging = false;
            foreach (var child in item.Children)
            {
                ClearDragOverRecursive(child);
            }
        }

        #endregion
    }
}
