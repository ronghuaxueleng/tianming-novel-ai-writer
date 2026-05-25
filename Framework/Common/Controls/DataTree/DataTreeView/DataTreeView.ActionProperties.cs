using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Controls.DataManagement;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.Controls
{
    public partial class DataTreeView
    {
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

            _selectedNode = item;

            _isActivated = true;
            _activatedNode = item;
            if (SelectOnDoubleClickOnly)
            {
                if (_isDoubleClickInProgress)
                {
                    SelectNodeWithPath(item);
                    UpdateButtonStates();
                    NodeDoubleClickCommand?.Execute(item);
                }
                else
                {
                    if (IsRootNode(item))
                    {
                        bool switched = CollapseExpandedRootsIncremental(item);
                        if (switched) TryScrollRootIntoView(item);
                    }
                    if (item.Children?.Count > 0)
                    {
                        ToggleNodeInFlatList(item);
                    }

                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        SelectNodeWithPath(item);
                        UpdateButtonStates();
                    });
                }
                return;
            }

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
                    NodeDoubleClickCommand?.Execute(item);
                }
                else
                {
                    if (IsRootNode(item))
                    {
                        bool switched = CollapseExpandedRootsIncremental(item);
                        if (switched) TryScrollRootIntoView(item);
                    }
                    ToggleNodeInFlatList(item);

                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        SelectNodeWithPath(item);
                        UpdateButtonStates();
                        NodeDoubleClickCommand?.Execute(item);
                    });
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

        private bool CollapseExpandedRootsIncremental(TreeNodeItem? except)
        {
            if (ItemsSource == null) return false;
            bool anyCollapsed = false;
            var snapshot = ItemsSource.ToList();
            foreach (var root in snapshot)
            {
                if (ReferenceEquals(root, except)) continue;
                if (root.IsExpanded)
                {
                    ToggleNodeInFlatList(root);
                    anyCollapsed = true;
                }
            }
            return anyCollapsed;
        }

        private void CollapseOtherRootNodes(TreeNodeItem selectedRoot)
        {
            if (CollapseExpandedRootsIncremental(selectedRoot))
            {
                TryScrollRootIntoView(selectedRoot);
            }
        }

        private void TryScrollRootIntoView(TreeNodeItem item)
        {
            if (RootItemsControl == null) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                try { RootItemsControl.ScrollIntoView(item); }
                catch { }
            }));
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

            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    MarkSiblingBranchRecursive(child);
                }
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
                ShowBasicFields = true,
                CategorySelectOnDoubleClickOnly = true
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
                icon: "Icon.Plus",
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
                TM.App.Log("[DataTreeView] 未激活节点，忽略全部删除操作");
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
                icon: "Icon.Edit",
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
                AIGenerateButton.ToolTip = "请选择节点后再使用AI生成";
                if (aiMenuItem != null)
                {
                    aiMenuItem.IsEnabled = false;
                    aiMenuItem.ToolTip = "请选择节点后再使用AI生成";
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
                        && ReferenceEquals(_selectedNode, _activatedNode)
                        && _selectedNode?.Tag is ICategory);
                DeleteAllButton.IsEnabled = canDeleteAll;
                DeleteAllButton.ToolTip = canDeleteAll ? null : "请选择分类后再全部删除";
            }

            if (_selectedNode == null)
            {
                AddButtonText.Text = "新建";
                AddButtonIcon.Source = TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Plus");
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
                AddButtonIcon.Source = TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Forbidden");
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
                    DeleteButton.ToolTip = canDelete ? null : "请选择要删除的条目";
                }
            }
            else
            {
                AddButtonText.Text = "新建";
                AddButtonIcon.Source = TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Plus");
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
                    DeleteButton.ToolTip = canDelete ? null : "请选择要删除的条目";
                }
            }

            UpdateAIGenerateButtonState();
            RefreshInternalCommandStates();
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
            _activeExpandTimer?.Stop();
            _activeExpandTimer = null;

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
                    _selectedNode = node;
                    SelectNodeWithPath(node);

                    _isActivated = true;
                    _activatedNode = node;

                    UpdateButtonStates();
                    if (InfoLogDedup.ShouldLog($"DataTreeView:RightButton:{node.Name}"))
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

        private void RefreshInternalCommandStates()
        {
            if (_commandRefreshPending)
                return;

            _commandRefreshPending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _commandRefreshPending = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }));
        }

    }
}

