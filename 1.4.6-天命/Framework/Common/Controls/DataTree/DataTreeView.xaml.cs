using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.UI;
using TM.Framework.Common.Controls.DataManagement;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.Controls
{
    public enum ParentNodeClickMode
    {
        Select,

        Toggle
    }

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

        private bool _isActivated;
        private bool _isDoubleClickActivated;
        private TreeNodeItem? _activatedNode;

        private DateTime _lastClickTime = DateTime.MinValue;
        private DateTime _lastDoubleClickTime = DateTime.MinValue;
        private TreeNodeItem? _lastClickedNode;
        private const int DoubleClickThresholdMs = 500;
        private const int DoubleClickProtectionMs = 100;

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
            var scrollViewer = FindChildScrollViewer(RootItemsControl);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
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

        #region 拖拽排序

        private Point _dragStartPoint;
        private TreeNodeItem? _draggedItem;
        private bool _isDragging;
        private DateTime _dragMouseDownTime;
        private bool _dragLongPressReady;
        private const double DragThreshold = 5.0;
        private const int DragLongPressMs = 2000;

        internal void OnNodePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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

        #region 依赖属性

        public static readonly DependencyProperty NodeContextMenuProperty =
            DependencyProperty.Register(
                nameof(NodeContextMenu),
                typeof(ContextMenu),
                typeof(DataTreeView),
                new PropertyMetadata(null, OnNodeContextMenuChanged));

        public ContextMenu? NodeContextMenu
        {
            get => (ContextMenu?)GetValue(NodeContextMenuProperty);
            set => SetValue(NodeContextMenuProperty, value);
        }

        private static void OnNodeContextMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView view)
            {
                view.AttachNodeContextMenu(e.OldValue as ContextMenu, e.NewValue as ContextMenu);
            }
        }

        private void AttachNodeContextMenu(ContextMenu? oldMenu, ContextMenu? newMenu)
        {
            if (oldMenu != null)
            {
                oldMenu.Opened -= OnNodeContextMenuOpened;
            }

            if (newMenu != null)
            {
                newMenu.Opened += OnNodeContextMenuOpened;
            }
        }

        public static readonly DependencyProperty RefreshCommandProperty =
            DependencyProperty.Register(
                nameof(RefreshCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand? RefreshCommand
        {
            get => (ICommand?)GetValue(RefreshCommandProperty);
            set => SetValue(RefreshCommandProperty, value);
        }

        public static readonly DependencyProperty Level1HorizontalAlignmentProperty =
            DependencyProperty.Register(
                nameof(Level1HorizontalAlignment),
                typeof(HorizontalAlignment),
                typeof(DataTreeView),
                new PropertyMetadata(HorizontalAlignment.Center));

        public HorizontalAlignment Level1HorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(Level1HorizontalAlignmentProperty);
            set => SetValue(Level1HorizontalAlignmentProperty, value);
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(ObservableCollection<TreeNodeItem>),
                typeof(DataTreeView),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public ObservableCollection<TreeNodeItem> ItemsSource
        {
            get => (ObservableCollection<TreeNodeItem>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                if (e.OldValue is ObservableCollection<TreeNodeItem> oldCollection)
                {
                    oldCollection.CollectionChanged -= control.OnOriginalItemsSourceChanged;
                }

                control._selectedNode = null;
                control._activatedNode = null;
                control._isActivated = false;
                control._isDoubleClickActivated = false;
                control._selectedPath.Clear();
                control._siblingBranchNodes.Clear();
                control._lastClickedNode = null;

                control._originalItemsSource = e.NewValue as ObservableCollection<TreeNodeItem>;
                if (control._originalItemsSource != null)
                {
                    control._originalItemsSource.CollectionChanged -= control.OnOriginalItemsSourceChanged;
                    control._originalItemsSource.CollectionChanged += control.OnOriginalItemsSourceChanged;
                }

                if (control.RootItemsControl != null)
                {
                    control.RootItemsControl.ItemsSource = control._originalItemsSource;
                }
                control.RebuildParentMap();
                control.UpdateButtonStates();
            }
        }

        private void OnContextAddClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: TreeNodeItem node })
            {
                _selectedNode = node;
            }
            HandleInternalAdd();
        }

        private void OnContextDeleteClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: TreeNodeItem node })
            {
                _selectedNode = node;
            }
            HandleInternalDelete();
        }

        private void OnContextDeleteAllClick(object sender, RoutedEventArgs e)
        {
            HandleInternalDeleteAll();
        }

        private void OnContextAddChapterClick(object sender, RoutedEventArgs e)
        {

            if (AddChapterCommand?.CanExecute(null) == true)
            {
                AddChapterCommand.Execute(null);
            }
        }

        private void OnContextSaveClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { CommandParameter: TreeNodeItem node })
            {
                _selectedNode = node;
            }

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
                TM.App.Log($"[DataTreeView] OnContextSaveClick 执行 SaveCategoryCommand 失败: {ex.Message}");
            }
        }

        private bool _collectionChangedPending;

        private void OnOriginalItemsSourceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_collectionChangedPending) return;
            _collectionChangedPending = true;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                _collectionChangedPending = false;
                if (RootItemsControl != null)
                {
                    RootItemsControl.ItemsSource = _originalItemsSource;
                }
                RebuildParentMap();
                UpdateButtonStates();
            }));
        }

        public static readonly DependencyProperty ParentNodeClickCommandProperty =
            DependencyProperty.Register(
                nameof(ParentNodeClickCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand ParentNodeClickCommand
        {
            get => (ICommand)GetValue(ParentNodeClickCommandProperty);
            set => SetValue(ParentNodeClickCommandProperty, value);
        }

        public static readonly DependencyProperty ChildNodeClickCommandProperty =
            DependencyProperty.Register(
                nameof(ChildNodeClickCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand ChildNodeClickCommand
        {
            get => (ICommand)GetValue(ChildNodeClickCommandProperty);
            set => SetValue(ChildNodeClickCommandProperty, value);
        }

        public static readonly DependencyProperty ParentClickModeProperty =
            DependencyProperty.Register(
                nameof(ParentClickMode),
                typeof(ParentNodeClickMode),
                typeof(DataTreeView),
                new PropertyMetadata(ParentNodeClickMode.Select));

        public ParentNodeClickMode ParentClickMode
        {
            get => (ParentNodeClickMode)GetValue(ParentClickModeProperty);
            set => SetValue(ParentClickModeProperty, value);
        }

        public static readonly DependencyProperty MaxLevelProperty =
            DependencyProperty.Register(
                nameof(MaxLevel),
                typeof(int),
                typeof(DataTreeView),
                new PropertyMetadata(5));

        public int MaxLevel
        {
            get => (int)GetValue(MaxLevelProperty);
            set => SetValue(MaxLevelProperty, value);
        }

        public static readonly DependencyProperty NodeDoubleClickCommandProperty =
            DependencyProperty.Register(
                nameof(NodeDoubleClickCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand NodeDoubleClickCommand
        {
            get => (ICommand)GetValue(NodeDoubleClickCommandProperty);
            set => SetValue(NodeDoubleClickCommandProperty, value);
        }

        public static readonly DependencyProperty EnableSingleClickLoadProperty =
            DependencyProperty.Register(
                nameof(EnableSingleClickLoad),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false));

        public bool EnableSingleClickLoad
        {
            get => (bool)GetValue(EnableSingleClickLoadProperty);
            set => SetValue(EnableSingleClickLoadProperty, value);
        }
        public static readonly DependencyProperty IsDeleteAllEnabledOverrideProperty =
            DependencyProperty.Register(
                nameof(IsDeleteAllEnabledOverride),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false, OnIsDeleteAllEnabledOverrideChanged));

        public bool IsDeleteAllEnabledOverride
        {
            get => (bool)GetValue(IsDeleteAllEnabledOverrideProperty);
            set => SetValue(IsDeleteAllEnabledOverrideProperty, value);
        }

        private static void OnIsDeleteAllEnabledOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                control.UpdateButtonStates();
            }
        }

        public static readonly DependencyProperty ShowActionButtonsProperty =
            DependencyProperty.Register(
                nameof(ShowActionButtons),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false, OnShowActionButtonsChanged));

        public bool ShowActionButtons
        {
            get => (bool)GetValue(ShowActionButtonsProperty);
            set => SetValue(ShowActionButtonsProperty, value);
        }

        private static void OnShowActionButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control && control.ActionButtonPanel != null)
            {
                control.ActionButtonPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
                control.UpdateAIGenerateButtonState();
            }
        }

        public static readonly DependencyProperty AddCategoryCommandProperty =
            DependencyProperty.Register(
                nameof(AddCategoryCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand AddCategoryCommand
        {
            get => (ICommand)GetValue(AddCategoryCommandProperty);
            set => SetValue(AddCategoryCommandProperty, value);
        }

        public static readonly DependencyProperty SaveCategoryCommandProperty =
            DependencyProperty.Register(
                nameof(SaveCategoryCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand SaveCategoryCommand
        {
            get => (ICommand)GetValue(SaveCategoryCommandProperty);
            set => SetValue(SaveCategoryCommandProperty, value);
        }

        public static readonly DependencyProperty DeleteCategoryCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteCategoryCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand DeleteCategoryCommand
        {
            get => (ICommand)GetValue(DeleteCategoryCommandProperty);
            set => SetValue(DeleteCategoryCommandProperty, value);
        }

        public static readonly DependencyProperty DeleteAllCategoriesCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteAllCategoriesCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand DeleteAllCategoriesCommand
        {
            get => (ICommand)GetValue(DeleteAllCategoriesCommandProperty);
            set => SetValue(DeleteAllCategoriesCommandProperty, value);
        }

        public static readonly DependencyProperty BulkToggleCommandProperty =
            DependencyProperty.Register(
                nameof(BulkToggleCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand? BulkToggleCommand
        {
            get => (ICommand?)GetValue(BulkToggleCommandProperty);
            set => SetValue(BulkToggleCommandProperty, value);
        }

        public static readonly DependencyProperty BulkToggleButtonTextProperty =
            DependencyProperty.Register(
                nameof(BulkToggleButtonText),
                typeof(string),
                typeof(DataTreeView),
                new PropertyMetadata("一键启用"));

        public string BulkToggleButtonText
        {
            get => (string)GetValue(BulkToggleButtonTextProperty);
            set => SetValue(BulkToggleButtonTextProperty, value);
        }

        public static readonly DependencyProperty IsBulkToggleEnabledProperty =
            DependencyProperty.Register(
                nameof(IsBulkToggleEnabled),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false));

        public bool IsBulkToggleEnabled
        {
            get => (bool)GetValue(IsBulkToggleEnabledProperty);
            set => SetValue(IsBulkToggleEnabledProperty, value);
        }

        public static readonly DependencyProperty BulkToggleToolTipProperty =
            DependencyProperty.Register(
                nameof(BulkToggleToolTip),
                typeof(string),
                typeof(DataTreeView),
                new PropertyMetadata("请选择主分类"));

        public string BulkToggleToolTip
        {
            get => (string)GetValue(BulkToggleToolTipProperty);
            set => SetValue(BulkToggleToolTipProperty, value);
        }

        public static readonly DependencyProperty IsAddEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAddEnabled),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(true, OnActionAvailabilityChanged));

        public bool IsAddEnabled
        {
            get => (bool)GetValue(IsAddEnabledProperty);
            set => SetValue(IsAddEnabledProperty, value);
        }

        public static readonly DependencyProperty IsDeleteEnabledProperty =
            DependencyProperty.Register(
                nameof(IsDeleteEnabled),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(true, OnActionAvailabilityChanged));

        public bool IsDeleteEnabled
        {
            get => (bool)GetValue(IsDeleteEnabledProperty);
            set => SetValue(IsDeleteEnabledProperty, value);
        }

        public static readonly DependencyProperty DisabledActionToolTipProperty =
            DependencyProperty.Register(
                nameof(DisabledActionToolTip),
                typeof(string),
                typeof(DataTreeView),
                new PropertyMetadata(string.Empty, OnDisabledToolTipChanged));

        public string DisabledActionToolTip
        {
            get => (string)GetValue(DisabledActionToolTipProperty);
            set => SetValue(DisabledActionToolTipProperty, value);
        }

        public static readonly DependencyProperty AIGenerateCommandProperty =
            DependencyProperty.Register(
                nameof(AIGenerateCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null, OnAIGenerateCommandChanged));

        public ICommand? AIGenerateCommand
        {
            get => (ICommand?)GetValue(AIGenerateCommandProperty);
            set => SetValue(AIGenerateCommandProperty, value);
        }

        private static void OnAIGenerateCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                if (e.OldValue is ICommand oldCommand)
                {
                    oldCommand.CanExecuteChanged -= control.OnAIGenerateCanExecuteChanged;
                }

                control._currentAIGenerateCommand = e.NewValue as ICommand;

                if (control._currentAIGenerateCommand != null)
                {
                    control._currentAIGenerateCommand.CanExecuteChanged += control.OnAIGenerateCanExecuteChanged;
                }

                control.UpdateAIGenerateButtonState();
            }
        }

        public static readonly DependencyProperty IsAIGenerateEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAIGenerateEnabled),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(true, OnIsAIGenerateEnabledChanged));

        public bool IsAIGenerateEnabled
        {
            get => (bool)GetValue(IsAIGenerateEnabledProperty);
            set => SetValue(IsAIGenerateEnabledProperty, value);
        }

        private static void OnIsAIGenerateEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                control.UpdateAIGenerateButtonState();
            }
        }

        public static readonly DependencyProperty ShowAIGenerateButtonProperty =
            DependencyProperty.Register(
                nameof(ShowAIGenerateButton),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false, OnShowAIGenerateButtonChanged));

        public bool ShowAIGenerateButton
        {
            get => (bool)GetValue(ShowAIGenerateButtonProperty);
            set => SetValue(ShowAIGenerateButtonProperty, value);
        }

        private static void OnShowAIGenerateButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                control.UpdateAIGenerateButtonState();
            }
        }

        public static readonly DependencyProperty AIGenerateButtonTextProperty =
            DependencyProperty.Register(
                nameof(AIGenerateButtonText),
                typeof(string),
                typeof(DataTreeView),
                new PropertyMetadata("AI单次"));

        public string AIGenerateButtonText
        {
            get => (string)GetValue(AIGenerateButtonTextProperty);
            set => SetValue(AIGenerateButtonTextProperty, value);
        }

        public ICommand InternalParentNodeClickCommand => _internalParentNodeClickCommand;

        public ICommand InternalDeleteCommand => _internalDeleteCommand;

        public ICommand InternalAddCommand => _internalAddCommand;

        public ICommand InternalDeleteAllCommand => _internalDeleteAllCommand;

        public ICommand InternalSaveCommand => _internalSaveCommand;

        public ICommand InternalEnableSelectedCommand => _internalEnableSelectedCommand;

        public ICommand InternalEditCommand => _internalEditCommand;

        public ICommand InternalBulkToggleCommand => _internalBulkToggleCommand;

        public ICommand InternalAddChapterCommand => _internalAddChapterCommand;

        public static readonly DependencyProperty AddCategoryMenuHeaderTextProperty =
            DependencyProperty.Register(
                nameof(AddCategoryMenuHeaderText),
                typeof(string),
                typeof(DataTreeView),
                new PropertyMetadata("新建"));

        public string AddCategoryMenuHeaderText
        {
            get => (string)GetValue(AddCategoryMenuHeaderTextProperty);
            set => SetValue(AddCategoryMenuHeaderTextProperty, value);
        }

        public static readonly DependencyProperty AfterActionCommandProperty =
            DependencyProperty.Register(
                nameof(AfterActionCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand AfterActionCommand
        {
            get => (ICommand)GetValue(AfterActionCommandProperty);
            set => SetValue(AfterActionCommandProperty, value);
        }

        public static readonly DependencyProperty AddChapterCommandProperty =
            DependencyProperty.Register(
                nameof(AddChapterCommand),
                typeof(ICommand),
                typeof(DataTreeView),
                new PropertyMetadata(null));

        public ICommand AddChapterCommand
        {
            get => (ICommand)GetValue(AddChapterCommandProperty);
            set => SetValue(AddChapterCommandProperty, value);
        }

        private void HandleParentNodeClick(object? parameter)
        {
            if (parameter is not TreeNodeItem item) return;

            var now = DateTime.Now;
            var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
            var timeSinceLastDoubleClick = (now - _lastDoubleClickTime).TotalMilliseconds;
            var isSameNode = ReferenceEquals(item, _lastClickedNode);

            if (timeSinceLastDoubleClick < DoubleClickProtectionMs)
            {
                return;
            }

            var isPotentialDoubleClick = isSameNode && timeSinceLastClick < DoubleClickThresholdMs;

            _lastClickTime = now;
            _lastClickedNode = item;

            if (_isActivated && !ReferenceEquals(item, _activatedNode) && !isPotentialDoubleClick)
            {
                _isActivated = false;
                _activatedNode = null;
                _isDoubleClickActivated = false;
            }

            _selectedNode = item;

            if (ParentClickMode == ParentNodeClickMode.Toggle)
            {
                bool isLeafNode = item.Children == null || item.Children.Count == 0;

                if (isLeafNode)
                {
                    SelectNodeWithPath(item);
                    UpdateButtonStates();
                    if (IsRootNode(item))
                    {
                        CollapseOtherRootNodes(item);
                    }
                    ChildNodeClickCommand?.Execute(item);
                    if (EnableSingleClickLoad)
                    {
                        NodeDoubleClickCommand?.Execute(item);
                    }
                }
                else
                {
                    SelectNodeWithPath(item);
                    UpdateButtonStates();

                    if (IsRootNode(item))
                    {
                        CollapseOtherRootNodes(item);
                    }

                    int childCount = item.Children?.Count ?? 0;
                    if (childCount > 50)
                    {
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                        {
                            item.IsExpanded = !item.IsExpanded;
                        });
                    }
                    else
                    {
                        item.IsExpanded = !item.IsExpanded;
                    }
                    if (EnableSingleClickLoad)
                    {
                        NodeDoubleClickCommand?.Execute(item);
                    }
                }
            }
            else
            {
                ParentNodeClickCommand?.Execute(item);
                NodeDoubleClickCommand?.Execute(item);
            }
        }

        private bool IsRootNode(TreeNodeItem item)
        {
            return ItemsSource != null && ItemsSource.Any(r => ReferenceEquals(r, item));
        }

        private void CollapseOtherRootNodes(TreeNodeItem selectedRoot)
        {
            if (ItemsSource == null)
            {
                return;
            }

            foreach (var root in ItemsSource)
            {
                if (ReferenceEquals(root, selectedRoot))
                {
                    continue;
                }

                root.IsExpanded = false;
            }
        }

        private void ClearCachedSelection()
        {
            foreach (var node in _selectedPath)
            {
                node.IsSelected = false;
                node.IsSelectionFocus = false;
            }
            _selectedPath.Clear();
        }

        private void ClearCachedSiblingBranches()
        {
            foreach (var node in _siblingBranchNodes)
            {
                node.IsSiblingBranch = false;
            }
            _siblingBranchNodes.Clear();
        }

        private void MarkSiblingBranchRecursive(TreeNodeItem node)
        {
            if (node.IsSiblingBranch)
            {
                return;
            }

            node.IsSiblingBranch = true;
            _siblingBranchNodes.Add(node);

            foreach (var child in node.Children)
            {
                MarkSiblingBranchRecursive(child);
            }
        }

        private void MarkSiblingBranchesForAncestorPath(IReadOnlyList<TreeNodeItem> selectionPath)
        {
            if (selectionPath.Count < 2)
            {
                return;
            }

            for (int i = 0; i < selectionPath.Count - 1; i++)
            {
                var ancestor = selectionPath[i];
                var childInPath = selectionPath[i + 1];

                foreach (var siblingBranchRoot in ancestor.Children)
                {
                    if (ReferenceEquals(siblingBranchRoot, childInPath))
                    {
                        continue;
                    }

                    MarkSiblingBranchRecursive(siblingBranchRoot);
                }
            }
        }

        private void SelectNodeWithPath(TreeNodeItem targetNode)
        {
            var displaySource = ItemsSource;
            if (displaySource == null) return;

            var selectionPath = new List<TreeNodeItem>();
            var found = TryBuildPathFromParentMap(targetNode, selectionPath);
            if (!found)
            {
                foreach (var rootNode in displaySource)
                {
                    if (FindNodePath(rootNode, targetNode, selectionPath))
                    {
                        found = true;
                        break;
                    }
                    selectionPath.Clear();
                }
            }

            if (!found) return;

            ClearCachedSiblingBranches();
            ClearCachedSelection();

            foreach (var node in selectionPath)
            {
                node.IsSelected = true;
                node.IsSelectionFocus = false;
            }

            if (selectionPath.Count > 0)
            {
                selectionPath[^1].IsSelectionFocus = true;
            }

            _selectedPath = selectionPath;

            MarkSiblingBranchesForAncestorPath(selectionPath);
        }

        private bool FindNodePath(TreeNodeItem current, TreeNodeItem target, List<TreeNodeItem> path)
        {
            path.Add(current);

            if (current == target)
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                if (FindNodePath(child, target, path))
                {
                    return true;
                }
            }

            path.Remove(current);
            return false;
        }

        private void HandleInternalDelete()
        {
            if (!IsDeleteEnabled)
            {
                return;
            }

            if (_selectedNode == null)
            {
                GlobalToast.Warning("删除失败", "请先选择要删除的条目");
                return;
            }

            if (DeleteCategoryCommand != null)
            {
                if (DeleteCategoryCommand.CanExecute(_selectedNode))
                {
                    DeleteCategoryCommand.Execute(_selectedNode);
                }
            }
            else
            {
                GlobalToast.Warning("删除失败", "删除命令未配置");
            }

            UpdateButtonStates();
            ExecuteAfterAction("Delete");
        }

        private void HandleInternalAdd()
        {
            if (!IsAddEnabled)
            {
                return;
            }

            if (AddCategoryCommand != null)
            {
                if (AddCategoryCommand.CanExecute(_selectedNode))
                {
                    AddCategoryCommand.Execute(_selectedNode);
                }
            }
            else
            {
                GlobalToast.Warning("无法新建", "新建命令未配置");
                return;
            }

            var form = new FunctionalDetailForm
            {
                ShowBasicFields = true
            };

            var dc = DataContext;
            if (dc != null)
            {
                form.SetBinding(FunctionalDetailForm.NameValueProperty, new Binding("FormName") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.IconValueProperty, new Binding("FormIcon") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.StatusValueProperty, new Binding("FormStatus") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.CategoryItemsSourceProperty, new Binding("CategorySelectionTree") { Source = dc, Mode = BindingMode.OneWay });
                form.SetBinding(FunctionalDetailForm.CategorySelectedPathProperty, new Binding("SelectedCategoryTreePath") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.CategoryDisplayIconProperty, new Binding("SelectedCategoryTreeIcon") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.CategoryIsDropDownOpenProperty, new Binding("IsCategoryTreeDropdownOpen") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.CategoryNodeSelectCommandProperty, new Binding("CategoryTreeNodeSelectCommand") { Source = dc, Mode = BindingMode.OneWay });

                form.SetBinding(FunctionalDetailForm.TypeItemsSourceProperty, new Binding("TypeOptions") { Source = dc, Mode = BindingMode.OneWay });
                form.SetBinding(FunctionalDetailForm.TypeSelectedItemProperty, new Binding("FormType") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            }

            var confirmed = DialogHelper.ShowFormDialog(
                title: "新建",
                icon: "➕",
                form: form,
                onConfirm: _ => true,
                confirmText: "保存",
                cancelText: "取消",
                owner: Window.GetWindow(this));

            if (confirmed)
            {
                ExecuteSaveSelectedNode();
            }

            UpdateButtonStates();
            ExecuteAfterAction("Add");
        }

        private void HandleInternalDeleteAll()
        {
            TM.App.Log("[DataTreeView] HandleInternalDeleteAll 调用");

            if (!CanExecuteInternalDeleteAll())
            {
                TM.App.Log("[DataTreeView] 未进入双击激活态，忽略全部删除操作");
                return;
            }

            if (!IsDeleteEnabled)
            {
                TM.App.Log("[DataTreeView] 删除功能已禁用，忽略全部删除操作");
                return;
            }

            var totalCount = _originalItemsSource?.Count ?? 0;
            if (totalCount <= 0)
            {
                GlobalToast.Info("暂无条目", "当前没有可删除的条目");
                return;
            }

            if (DeleteAllCategoriesCommand == null)
            {
                TM.App.Log("[DataTreeView] DeleteAllCategoriesCommand为null");
                GlobalToast.Warning("删除失败", "全部删除命令未配置");
                return;
            }

            if (!DeleteAllCategoriesCommand.CanExecute(null))
            {
                TM.App.Log("[DataTreeView] DeleteAllCategoriesCommand.CanExecute返回false");
                return;
            }

            DeleteAllCategoriesCommand.Execute(null);
            _selectedNode = null;
            UpdateButtonStates();
            ExecuteAfterAction("DeleteAll");
        }

        private void HandleInternalSave()
        {
            if (!ConfirmAction("确认保存", "确定要保存当前修改？"))
            {
                return;
            }

            ExecuteSaveSelectedNode();
        }

        private void HandleInternalEnableSelected()
        {
            var actionText = "启用";
            if (ContextMenu is ContextMenu cm && cm.Tag is NodeContextMenuHost host)
            {
                actionText = host.EnableSelectedHeader;
            }

            if (!ConfirmAction($"确认{actionText}", $"确定要{actionText}选中条目？"))
            {
                return;
            }

            if (DataContext is TM.Framework.Common.ViewModels.IDataTreeHost treeHost)
            {
                var cmd = treeHost.ToggleSelectedEnabledCommand;
                if (cmd?.CanExecute(_selectedNode) == true)
                {
                    cmd.Execute(_selectedNode);
                }
            }
        }

        private void HandleInternalEdit()
        {
            if (_selectedNode == null)
            {
                return;
            }

            var form = new FunctionalDetailForm
            {
                ShowBasicFields = true,
                ShowCategoryField = false
            };

            var dc = DataContext;
            if (dc != null)
            {
                form.SetBinding(FunctionalDetailForm.NameValueProperty, new Binding("FormName") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.IconValueProperty, new Binding("FormIcon") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.StatusValueProperty, new Binding("FormStatus") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                form.SetBinding(FunctionalDetailForm.TypeItemsSourceProperty, new Binding("TypeOptions") { Source = dc, Mode = BindingMode.OneWay });
                form.SetBinding(FunctionalDetailForm.TypeSelectedItemProperty, new Binding("FormType") { Source = dc, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            }

            var confirmed = DialogHelper.ShowFormDialog(
                title: "编辑",
                icon: "📝",
                form: form,
                onConfirm: _ => true,
                confirmText: "保存",
                cancelText: "取消",
                owner: Window.GetWindow(this));

            if (confirmed)
            {
                ExecuteSaveSelectedNode();
            }

            UpdateButtonStates();
            ExecuteAfterAction("Edit");
        }

        private void HandleInternalBulkToggle()
        {
            if (!ConfirmAction("确认操作", "确定执行“一键启用/禁用”操作？"))
            {
                return;
            }

            if (BulkToggleCommand?.CanExecute(null) == true)
            {
                BulkToggleCommand.Execute(null);
            }
        }

        private bool ConfirmAction(string title, string message)
        {
            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480
            };

            return DialogHelper.ShowCustomDialog(
                title: title,
                icon: "❓",
                content: textBlock,
                confirmText: "确定",
                cancelText: "取消",
                owner: Window.GetWindow(this));
        }

        private void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button button && button.DataContext is TreeNodeItem item)
            {
                _lastDoubleClickTime = DateTime.Now;

                _selectedNode = item;
                _lastClickedNode = item;

                SelectNodeWithPath(item);

                _isActivated = true;
                _isDoubleClickActivated = true;
                _activatedNode = item;

                UpdateButtonStates();

                TM.App.Log($"[DataTreeView] 双击进入激活态: {item.Name}, Level={item.Level}");

                NodeDoubleClickCommand?.Execute(item);

                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (ReferenceEquals(_activatedNode, item) && !item.IsSelected)
                    {
                        SelectNodeWithPath(item);
                    }
                });

                e.Handled = true;
            }
        }

        private void UpdateAIGenerateButtonState()
        {
            if (AIGenerateButton == null)
            {
                return;
            }
            MenuItem? aiMenuItem = null;
            if (ContextMenu is ContextMenu cm)
            {
                foreach (var item in cm.Items)
                {
                    if (item is MenuItem mi && mi.Header is string header && header == "AI智能生成")
                    {
                        aiMenuItem = mi;
                        break;
                    }
                }
            }

            if (!ShowActionButtons || !ShowAIGenerateButton)
            {
                AIGenerateButton.Visibility = Visibility.Collapsed;
                if (aiMenuItem != null)
                {
                    aiMenuItem.Visibility = Visibility.Collapsed;
                }
                return;
            }

            AIGenerateButton.Visibility = Visibility.Visible;
            if (aiMenuItem != null)
            {
                aiMenuItem.Visibility = Visibility.Visible;
            }

            var command = AIGenerateCommand;

            if (command == null)
            {
                AIGenerateButton.IsEnabled = false;
                AIGenerateButton.ToolTip = "AI命令未配置";
                if (aiMenuItem != null)
                {
                    aiMenuItem.IsEnabled = false;
                    aiMenuItem.ToolTip = "AI命令未配置";
                }
                return;
            }

            if (!IsAIGenerateEnabled)
            {
                AIGenerateButton.IsEnabled = false;
                var reason = TryGetAIGenerateDisabledReason();
                AIGenerateButton.ToolTip = string.IsNullOrWhiteSpace(reason) ? "当前页面不支持AI智能生成" : reason;
                if (aiMenuItem != null)
                {
                    aiMenuItem.IsEnabled = false;
                    aiMenuItem.ToolTip = string.IsNullOrWhiteSpace(reason) ? "当前页面不支持AI智能生成" : reason;
                }
                return;
            }

            if (!_isActivated)
            {
                AIGenerateButton.IsEnabled = false;
                AIGenerateButton.ToolTip = "请双击节点进入编辑后再使用AI生成";
                if (aiMenuItem != null)
                {
                    aiMenuItem.IsEnabled = false;
                    aiMenuItem.ToolTip = "请双击节点进入编辑后再使用AI生成";
                }
                return;
            }

            bool canExecute;
            try
            {
                canExecute = command.CanExecute(null);
            }
            catch (Exception ex)
            {
                canExecute = false;
                TM.App.Log($"[DataTreeView] 调用AI命令CanExecute发生异常: {ex.Message}");
            }

            AIGenerateButton.IsEnabled = canExecute;
            AIGenerateButton.ToolTip = canExecute ? null : "AI命令暂不可用";
            if (aiMenuItem != null)
            {
                aiMenuItem.IsEnabled = canExecute;
                aiMenuItem.ToolTip = canExecute ? null : "AI命令暂不可用";
            }
        }

        private string? TryGetAIGenerateDisabledReason()
        {
            try
            {
                if (DataContext is TM.Framework.Common.ViewModels.IDataTreeHost host)
                {
                    return host.AIGenerateDisabledReason;
                }
                return null;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetAIGenerateDisabledReason), ex);
                return null;
            }
        }

        private void OnRefreshMenuClick(object sender, RoutedEventArgs e)
        {
            var cmd = RefreshCommand;
            if (cmd == null)
                return;

            try
            {
                if (cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataTreeView] 刷新命令执行失败: {ex.Message}");
            }
        }

        private void UpdateButtonStates()
        {
            if (AddButton == null || SaveButton == null || DeleteButton == null || DeleteAllButton == null || AddButtonText == null || AddButtonIcon == null)
                return;

            var disabledMessage = string.IsNullOrWhiteSpace(DisabledActionToolTip)
                ? null
                : DisabledActionToolTip;

            SaveButton.IsEnabled = true;

            if (!IsDeleteEnabled)
            {
                DeleteAllButton.IsEnabled = false;
                DeleteAllButton.ToolTip = disabledMessage;
            }
            else
            {
                var canDeleteAll = IsDeleteAllEnabledOverride
                    || (_isActivated
                        && _isDoubleClickActivated
                        && ReferenceEquals(_selectedNode, _activatedNode)
                        && _selectedNode?.Tag is ICategory);
                DeleteAllButton.IsEnabled = canDeleteAll;
                DeleteAllButton.ToolTip = canDeleteAll ? null : "请双击节点进入编辑后再全部删除";
            }

            if (_selectedNode == null)
            {
                AddButtonText.Text = "新建";
                AddButtonIcon.Text = "➕";
                if (!IsAddEnabled)
                {
                    AddButton.IsEnabled = false;
                    AddButton.ToolTip = disabledMessage;
                }
                else
                {
                    AddButton.IsEnabled = true;
                    AddButton.ToolTip = null;
                }

                if (!IsDeleteEnabled)
                {
                    DeleteButton.IsEnabled = false;
                    DeleteButton.ToolTip = disabledMessage;
                }
                else
                {
                    DeleteButton.IsEnabled = false;
                    DeleteButton.ToolTip = "请选择要删除的条目";
                }
            }
            else if (_selectedNode.Level >= MaxLevel)
            {
                AddButtonText.Text = "达到最大层级";
                AddButtonIcon.Text = "❌";
                AddButton.IsEnabled = false;
                AddButton.ToolTip = IsAddEnabled ? "当前已达最大层级，无法继续新增" : disabledMessage;

                if (!IsDeleteEnabled)
                {
                    DeleteButton.IsEnabled = false;
                    DeleteButton.ToolTip = disabledMessage;
                }
                else
                {
                    var canDelete = _isActivated && ReferenceEquals(_selectedNode, _activatedNode);
                    DeleteButton.IsEnabled = canDelete;
                    DeleteButton.ToolTip = canDelete ? null : "请双击节点进入编辑后再删除";
                }
            }
            else
            {
                AddButtonText.Text = "新建";
                AddButtonIcon.Text = "➕";
                if (!IsAddEnabled)
                {
                    AddButton.IsEnabled = false;
                    AddButton.ToolTip = disabledMessage;
                }
                else
                {
                    AddButton.IsEnabled = true;
                    AddButton.ToolTip = null;
                }

                if (!IsDeleteEnabled)
                {
                    DeleteButton.IsEnabled = false;
                    DeleteButton.ToolTip = disabledMessage;
                }
                else
                {
                    var canDelete = _isActivated && ReferenceEquals(_selectedNode, _activatedNode);
                    DeleteButton.IsEnabled = canDelete;
                    DeleteButton.ToolTip = canDelete ? null : "请双击节点进入编辑后再删除";
                }
            }

            UpdateAIGenerateButtonState();
        }

        private static void OnActionAvailabilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                control.UpdateButtonStates();
            }
        }

        private static void OnDisabledToolTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataTreeView control)
            {
                control.UpdateButtonStates();
            }
        }

        private void DataTreeView_Loaded(object sender, RoutedEventArgs e)
        {
            _isActivated = false;
            _isDoubleClickActivated = false;
            _activatedNode = null;
            UpdateButtonStates();
            UpdateAIGenerateButtonState();

            if (DeleteButton != null)
            {
                ButtonHelper.SetConfirmMessage(DeleteButton, null!);
                ButtonHelper.SetConfirmTitle(DeleteButton, string.Empty);
            }

            if (DeleteAllButton != null)
            {
                ButtonHelper.SetConfirmMessage(DeleteAllButton, null!);
                ButtonHelper.SetConfirmTitle(DeleteAllButton, string.Empty);
            }
        }

        private void DataTreeView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_originalItemsSource != null)
            {
                _originalItemsSource.CollectionChanged -= OnOriginalItemsSourceChanged;
            }

            if (_currentAIGenerateCommand != null)
            {
                _currentAIGenerateCommand.CanExecuteChanged -= OnAIGenerateCanExecuteChanged;
                _currentAIGenerateCommand = null;
            }
        }

        private void DataTreeView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool isVisible && isVisible)
            {
                _isActivated = false;
                _isDoubleClickActivated = false;
                _activatedNode = null;

                UpdateButtonStates();
                UpdateAIGenerateButtonState();
            }
        }

        private void OnRootItemsRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var pos = e.GetPosition(RootItemsControl);
                var hit = VisualTreeHelper.HitTest(RootItemsControl, pos);
                if (hit == null)
                    return;

                DependencyObject current = hit.VisualHit;
                while (current != null && current is not ListBoxItem)
                {
                    current = VisualTreeHelper.GetParent(current);
                }

                if (current is ListBoxItem item && item.DataContext is TreeNodeItem node)
                {
                    var keepDoubleClickActivated = _isDoubleClickActivated && ReferenceEquals(_activatedNode, node);
                    _selectedNode = node;
                    SelectNodeWithPath(node);

                    _isActivated = true;
                    _isDoubleClickActivated = keepDoubleClickActivated;
                    _activatedNode = node;

                    UpdateButtonStates();
                    TM.App.Log($"[DataTreeView] OnRootItemsRightButtonDown 命中节点并进入激活态: {node.Name}, Level={node.Level}");

                    NodeDoubleClickCommand?.Execute(node);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataTreeView] OnRootItemsRightButtonDown 处理失败: {ex.Message}");
            }
        }

        private bool CanExecuteInternalDeleteAll()
        {
            if (!IsDeleteEnabled) return false;
            if (IsDeleteAllEnabledOverride) return true;
            return _isActivated
                   && _isDoubleClickActivated
                   && ReferenceEquals(_selectedNode, _activatedNode)
                   && _selectedNode?.Tag is ICategory;
        }

        private void ExecuteAfterAction(string action)
        {
            if (AfterActionCommand == null)
            {
                return;
            }

            try
            {
                if (AfterActionCommand.CanExecute(action))
                {
                    AfterActionCommand.Execute(action);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataTreeView] 执行AfterActionCommand失败({action}): {ex.Message}");
            }
        }

        private void OnSaveButtonClick(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => ExecuteAfterAction("Save")));
        }

        private void OnAIGenerateCanExecuteChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(UpdateAIGenerateButtonState);
            }
            else
            {
                UpdateAIGenerateButtonState();
            }
        }

        #endregion
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class TreeNodeItem : INotifyPropertyChanged
    {
        private static readonly Color LevelNeutralColor = Color.FromRgb(0x6F, 0x74, 0x88);
        private static readonly Color LevelBlueBaseColor = Color.FromRgb(0x3B, 0x7D, 0xE2);
        private static readonly Color LevelBluePulseColor = Color.FromRgb(0x00, 0xB4, 0xD8);
        private static readonly Color LevelGreenBaseColor = Color.FromRgb(0x10, 0xB9, 0x81);
        private static readonly Color LevelGreenPulseColor = Color.FromRgb(0x8C, 0xF8, 0xBC);
        private static readonly TimeSpan BluePulseDuration = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan GreenPulseDuration = TimeSpan.FromSeconds(1.2);

        private static readonly TimeSpan NameBluePulseDuration = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan NameGreenPulseDuration = TimeSpan.FromSeconds(1.9);

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

            System.Diagnostics.Debug.WriteLine($"[TreeNodeItem] {key}: {ex.Message}");
        }
        private static readonly TimeSpan NameGrayPulseDuration = TimeSpan.FromSeconds(1.6);

        private static LinearGradientBrush? _nameBlueFlowBrush;
        private static TranslateTransform? _nameBlueFlowTransform;
        private static LinearGradientBrush? _nameGreenFlowBrush;
        private static TranslateTransform? _nameGreenFlowTransform;
        private static LinearGradientBrush? _nameGrayFlowBrush;
        private static TranslateTransform? _nameGrayFlowTransform;

        private bool _isExpanded;
        private bool _isSelected;
        private bool _isSelectionFocus;
        private bool _isDragging;
        private bool _isDragOver;
        private bool _isSiblingBranch;
        private Brush _nameBrush;
        private string _name = string.Empty;
        private string _icon = string.Empty;
        private ImageSource? _logoImage = null;
        private bool _showChildCount = true;
        private bool _isFileSystemNode = false;
        private int _level = 1;

        public TreeNodeItem()
        {
            LevelBrush = new SolidColorBrush(LevelNeutralColor);
            _nameBrush = new SolidColorBrush(LevelNeutralColor);
            UpdateLevelBrush();
            UpdateNameBrush();
        }

        public SolidColorBrush LevelBrush { get; }

        public Brush NameBrush
        {
            get => _nameBrush;
            private set
            {
                if (!ReferenceEquals(_nameBrush, value))
                {
                    _nameBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        private static Color TryGetResourceColor(string key, Color fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetResourceColor), ex);
                return fallback;
            }

            return fallback;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? LogoImage
        {
            get => _logoImage;
            set
            {
                if (_logoImage != value)
                {
                    _logoImage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLogoImage));
                }
            }
        }

        public bool HasLogoImage => _logoImage != null;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        public bool IsSelectionFocus
        {
            get => _isSelectionFocus;
            set
            {
                if (_isSelectionFocus != value)
                {
                    _isSelectionFocus = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging != value)
                {
                    _isDragging = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDragOver
        {
            get => _isDragOver;
            set
            {
                if (_isDragOver != value)
                {
                    _isDragOver = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSiblingBranch
        {
            get => _isSiblingBranch;
            set
            {
                if (_isSiblingBranch != value)
                {
                    _isSiblingBranch = value;
                    OnPropertyChanged();
                    UpdateNameBrush();
                }
            }
        }

        public bool ShowChildCount
        {
            get => _showChildCount;
            set
            {
                if (_showChildCount != value)
                {
                    _showChildCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsFileSystemNode
        {
            get => _isFileSystemNode;
            set
            {
                if (_isFileSystemNode != value)
                {
                    _isFileSystemNode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        private void UpdateNameBrush()
        {
            if (IsSelectionFocus)
            {
                NameBrush = GetOrCreateNameGreenFlowBrush();
                return;
            }

            if (IsSelected)
            {
                NameBrush = GetOrCreateNameBlueFlowBrush();
                return;
            }

            if (IsSiblingBranch)
            {
                NameBrush = GetOrCreateNameGrayFlowBrush();
                return;
            }

            NameBrush = TryGetResourceBrush("TextPrimary", Brushes.Black);
        }

        private static Brush TryGetResourceBrush(string key, Brush fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is Brush brush)
                {
                    return brush;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetResourceBrush), ex);
                return fallback;
            }

            return fallback;
        }

        private static LinearGradientBrush GetOrCreateNameBlueFlowBrush()
        {
            if (_nameBlueFlowBrush != null)
            {
                return _nameBlueFlowBrush;
            }

            var baseColor = TryGetResourceColor("PrimaryColor", LevelBlueBaseColor);
            var pulseColor = LevelBluePulseColor;
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameBlueFlowTransform = new TranslateTransform();
            _nameBlueFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameBlueFlowTransform);
            StartFlowAnimation(_nameBlueFlowTransform, NameBluePulseDuration);

            return _nameBlueFlowBrush;
        }

        private static LinearGradientBrush GetOrCreateNameGreenFlowBrush()
        {
            if (_nameGreenFlowBrush != null)
            {
                return _nameGreenFlowBrush;
            }

            var baseColor = TryGetResourceColor("SuccessColor", LevelGreenBaseColor);
            var pulseColor = LevelGreenPulseColor;
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameGreenFlowTransform = new TranslateTransform();
            _nameGreenFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameGreenFlowTransform);
            StartFlowAnimation(_nameGreenFlowTransform, NameGreenPulseDuration);

            return _nameGreenFlowBrush;
        }

        private static LinearGradientBrush GetOrCreateNameGrayFlowBrush()
        {
            if (_nameGrayFlowBrush != null)
            {
                return _nameGrayFlowBrush;
            }

            var baseColor = TryGetResourceColor("TextTertiary", LevelNeutralColor);
            var pulseColor = TryGetResourceColor("TextSecondary", LevelNeutralColor);
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameGrayFlowTransform = new TranslateTransform();
            _nameGrayFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameGrayFlowTransform);
            StartFlowAnimation(_nameGrayFlowTransform, NameGrayPulseDuration);

            return _nameGrayFlowBrush;
        }

        private static LinearGradientBrush CreateHorizontalFlowBrush(Color baseColor, Color pulseColor, Color brighterPulseColor, TranslateTransform transform)
        {
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                RelativeTransform = transform
            };

            brush.GradientStops.Add(new GradientStop(baseColor, 0));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.40));
            brush.GradientStops.Add(new GradientStop(pulseColor, 0.47));
            brush.GradientStops.Add(new GradientStop(brighterPulseColor, 0.5));
            brush.GradientStops.Add(new GradientStop(pulseColor, 0.53));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.60));
            brush.GradientStops.Add(new GradientStop(baseColor, 1));

            return brush;
        }

        private static void StartFlowAnimation(TranslateTransform transform, TimeSpan duration)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);

            var realDuration = duration <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(2)
                : duration;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(realDuration),
                RepeatBehavior = RepeatBehavior.Forever
            };

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private static Color GetBrighterPulseColor(Color pulseColor)
        {
            static byte Boost(byte value)
            {
                const int delta = 45;
                return (byte)Math.Min(byte.MaxValue, value + delta);
            }

            return Color.FromRgb(Boost(pulseColor.R), Boost(pulseColor.G), Boost(pulseColor.B));
        }

        private string? _statusBadge;

        public string? StatusBadge
        {
            get => _statusBadge;
            set
            {
                if (_statusBadge != value)
                {
                    _statusBadge = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasStatusBadge));
                }
            }
        }

        public bool HasStatusBadge => !string.IsNullOrEmpty(_statusBadge);

        private bool _showLevelIndicator = true;

        public bool ShowLevelIndicator
        {
            get => _showLevelIndicator;
            set
            {
                if (_showLevelIndicator != value)
                {
                    _showLevelIndicator = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showIcon = true;

        public bool ShowIcon
        {
            get => _showIcon && !HasLogoImage;
            set
            {
                if (_showIcon != value)
                {
                    _showIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<TreeNodeItem> Children { get; set; } = new();

        public object? Tag { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateLevelBrush()
        {
            if (LevelBrush == null)
            {
                return;
            }

            LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

            if (IsSelectionFocus)
            {
                LevelBrush.Color = LevelGreenBaseColor;
                var animation = new ColorAnimation
                {
                    To = LevelGreenPulseColor,
                    Duration = new Duration(GreenPulseDuration),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            }
            else if (IsSelected)
            {
                LevelBrush.Color = LevelBlueBaseColor;
                var animation = new ColorAnimation
                {
                    To = LevelBluePulseColor,
                    Duration = new Duration(BluePulseDuration),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            }
            else
            {
                LevelBrush.Color = LevelNeutralColor;
            }
        }
    }

    public class NodeReorderedEventArgs : EventArgs
    {
        public TreeNodeItem Node { get; }
        public int OldIndex { get; }
        public int NewIndex { get; }

        public NodeReorderedEventArgs(TreeNodeItem node, int oldIndex, int newIndex)
        {
            Node = node;
            OldIndex = oldIndex;
            NewIndex = newIndex;
        }
    }

    public class TreeNodeCountVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 3 ||
                values[0] is not bool showChildCount ||
                values[1] is not bool isFileSystemNode ||
                values[2] is not int childrenCount)
            {
                return Visibility.Collapsed;
            }

            if (!showChildCount)
            {
                return Visibility.Collapsed;
            }

            if (isFileSystemNode)
            {
                return Visibility.Visible;
            }

            return childrenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TagIsDeletableVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                object? tag = value;

                if (value is TreeNodeItem node)
                {
                    tag = node.Tag;
                }

                if (tag == null)
                {
                    return Visibility.Collapsed;
                }

                if (tag is TM.Framework.Common.Models.ICategory cat)
                {
                    return !cat.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
                }

                if (tag is TM.Framework.Common.Models.IDataItem dataItem)
                {
                    if (string.IsNullOrWhiteSpace(dataItem.Id))
                        return Visibility.Collapsed;

                    var isBuiltInProp = tag.GetType().GetProperty("IsBuiltIn");
                    if (isBuiltInProp?.PropertyType == typeof(bool))
                    {
                        var isBuiltIn = (bool)(isBuiltInProp.GetValue(tag) ?? false);
                        return isBuiltIn ? Visibility.Collapsed : Visibility.Visible;
                    }

                    return TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(dataItem.Id)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                return Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

