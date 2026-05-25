using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TM.Framework.Common.Controls
{
    public partial class DataTreeView : System.Windows.Controls.UserControl
    {
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
                control._selectedPath.Clear();
                control._siblingBranchNodes.Clear();
                control._cachedScrollViewer = null;

                control._originalItemsSource = e.NewValue as ObservableCollection<TreeNodeItem>;
                if (control._originalItemsSource != null)
                {
                    control._originalItemsSource.CollectionChanged -= control.OnOriginalItemsSourceChanged;
                    control._originalItemsSource.CollectionChanged += control.OnOriginalItemsSourceChanged;
                }

                if (control.RootItemsControl != null)
                {
                    control.RootItemsControl.ItemsSource = control._flatList;
                }
                control.RebuildFlatList();
                control.RebuildParentMap();
                control.UpdateButtonStates();
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
                RebuildFlatList();
                RebuildParentMap();
                RestoreSelectionAfterRebuild();
                UpdateButtonStates();
            }));
        }

        private void RestoreSelectionAfterRebuild()
        {
            if (_activatedNode == null) return;

            if (_parentMap.ContainsKey(_activatedNode)) return;

            var tag = _activatedNode.Tag;
            if (tag == null) return;

            var displaySource = ItemsSource;
            if (displaySource == null) return;

            TreeNodeItem? matchNode = null;
            foreach (var root in displaySource)
            {
                matchNode = FindNodeByTag(root, tag);
                if (matchNode != null) break;
            }

            if (matchNode != null)
            {
                _activatedNode = matchNode;
                _selectedNode = matchNode;
                SelectNodeWithPath(matchNode);
            }
        }

        private static TreeNodeItem? FindNodeByTag(TreeNodeItem node, object tag)
        {
            if (ReferenceEquals(node.Tag, tag)) return node;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    var found = FindNodeByTag(child, tag);
                    if (found != null) return found;
                }
            }
            return null;
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
        public static readonly DependencyProperty SelectOnDoubleClickOnlyProperty =
            DependencyProperty.Register(
                nameof(SelectOnDoubleClickOnly),
                typeof(bool),
                typeof(DataTreeView),
                new PropertyMetadata(false));

        public bool SelectOnDoubleClickOnly
        {
            get => (bool)GetValue(SelectOnDoubleClickOnlyProperty);
            set => SetValue(SelectOnDoubleClickOnlyProperty, value);
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

        #endregion
    }
}

