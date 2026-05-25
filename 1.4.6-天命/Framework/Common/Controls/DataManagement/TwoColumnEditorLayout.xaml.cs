using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.Common.Controls.DataManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TwoColumnEditorLayout : UserControl
    {
        private ICommand? _originalSaveCommand;
        private ICommand? _wrappedSaveCommand;
        private ICommand? _internalNodeDoubleClickCommand;
        private bool _isUpdatingHeaderTabs;

        public ICommand InternalNodeDoubleClickCommand => _internalNodeDoubleClickCommand ??=
            new RelayCommand(param =>
            {
                if (DataContext is IBulkToggleSelectionHost host)
                {
                    host.OnTreeNodeSelected(param as TreeNodeItem);
                }

                if (NodeDoubleClickCommand?.CanExecute(param) == true)
                {
                    NodeDoubleClickCommand.Execute(param);
                }
            });

        public TwoColumnEditorLayout()
        {
            InitializeComponent();
            DataContextChanged += OnLayoutDataContextChanged;
            Loaded += OnLayoutLoaded;

            UpdateActionPermissionStates();
        }

        private void OnLayoutLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTabHeaders();
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                if (Scheme2ContentGrid != null)
                {
                    AttachStandardEditMenuToTextInputs(Scheme2ContentGrid);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TwoColumnEditorLayout] AttachStandardEditMenu failed: {ex.Message}");
            }
        }

        private static void AttachStandardEditMenuToTextInputs(DependencyObject root)
        {
            if (root == null)
                return;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

                if (child is System.Windows.Controls.Primitives.TextBoxBase textBox)
                {
                    TM.Framework.Common.Helpers.UI.TextInputContextMenuHelper.SetEnableStandardEditMenu(textBox, true);
                }

                AttachStandardEditMenuToTextInputs(child);
            }
        }

        private void UpdateActionPermissionStates()
        {
            var isEnabled = EnableCategoryActions || EnableContentActions;
            IsAddActionEnabled = isEnabled;
            IsDeleteActionEnabled = isEnabled;
        }

        private void OnLayoutDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is ITreeActionHost host)
            {
                TreeAfterActionCommand = host.TreeAfterActionCommand;
            }
            else
            {
                ClearValue(TreeAfterActionCommandProperty);
            }

            EnsureDefaultAIGenerateButtonTextBinding();
            EnsureDefaultAIGenerateCommandBinding();
            EnsureDefaultIsAIGenerateEnabledBinding();
        }

        private void EnsureDefaultAIGenerateButtonTextBinding()
        {
            if (BindingOperations.GetBindingBase(this, AIGenerateButtonTextProperty) != null)
            {
                return;
            }

            if (ReadLocalValue(AIGenerateButtonTextProperty) != DependencyProperty.UnsetValue)
            {
                return;
            }

            var binding = new Binding("AIGenerateButtonText")
            {
                Mode = BindingMode.OneWay
            };

            BindingOperations.SetBinding(this, AIGenerateButtonTextProperty, binding);
        }

        private void EnsureDefaultAIGenerateCommandBinding()
        {
            if (BindingOperations.GetBindingBase(this, AIGenerateCommandProperty) != null)
            {
                return;
            }

            if (ReadLocalValue(AIGenerateCommandProperty) != DependencyProperty.UnsetValue)
            {
                return;
            }

            var binding = new Binding("AIGenerateCommand")
            {
                Mode = BindingMode.OneWay
            };

            BindingOperations.SetBinding(this, AIGenerateCommandProperty, binding);
        }

        private void EnsureDefaultIsAIGenerateEnabledBinding()
        {
            if (BindingOperations.GetBindingBase(this, IsAIGenerateEnabledProperty) != null)
            {
                return;
            }

            if (ReadLocalValue(IsAIGenerateEnabledProperty) != DependencyProperty.UnsetValue)
            {
                return;
            }

            var binding = new Binding("IsAIGenerateEnabled")
            {
                Mode = BindingMode.OneWay
            };

            BindingOperations.SetBinding(this, IsAIGenerateEnabledProperty, binding);
        }

        private void ExecuteRefreshCallback()
        {
            if (AutoRefreshAfterSave && RefreshCallback != null)
            {
                RefreshCallback.Invoke();
                TM.App.Log("[TwoColumnEditorLayout] 已自动执行刷新回调");
            }
        }

        private ICommand WrapSaveCommand(ICommand originalCommand)
        {
            return new TM.Framework.Common.Helpers.MVVM.AsyncRelayCommand(async () =>
            {
                if (originalCommand?.CanExecute(null) == true)
                {
                    originalCommand.Execute(null);

                    await System.Threading.Tasks.Task.Delay(100);

                    ExecuteRefreshCallback();

                    SaveCompleted?.Invoke(this, EventArgs.Empty);
                }
            }, () => originalCommand?.CanExecute(null) == true);
        }

        #region 左侧列表区配置

        public static readonly DependencyProperty LeftIconProperty =
            DependencyProperty.Register(
                nameof(LeftIcon),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("📁"));

        public string LeftIcon
        {
            get => (string)GetValue(LeftIconProperty);
            set => SetValue(LeftIconProperty, value);
        }

        public static readonly DependencyProperty LeftTitleProperty =
            DependencyProperty.Register(
                nameof(LeftTitle),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("数据列表"));

        public string LeftTitle
        {
            get => (string)GetValue(LeftTitleProperty);
            set => SetValue(LeftTitleProperty, value);
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(ObservableCollection<TreeNodeItem>),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ObservableCollection<TreeNodeItem> ItemsSource
        {
            get => (ObservableCollection<TreeNodeItem>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty ShowSearchProperty =
            DependencyProperty.Register(
                nameof(ShowSearch),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool ShowSearch
        {
            get => (bool)GetValue(ShowSearchProperty);
            set => SetValue(ShowSearchProperty, value);
        }

        public static readonly DependencyProperty SearchKeywordProperty =
            DependencyProperty.Register(
                nameof(SearchKeyword),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string SearchKeyword
        {
            get => (string)GetValue(SearchKeywordProperty);
            set => SetValue(SearchKeywordProperty, value);
        }

        public static readonly DependencyProperty SearchPlaceholderProperty =
            DependencyProperty.Register(
                nameof(SearchPlaceholder),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("搜索..."));

        public string SearchPlaceholder
        {
            get => (string)GetValue(SearchPlaceholderProperty);
            set => SetValue(SearchPlaceholderProperty, value);
        }

        public static readonly DependencyProperty LeftColumnWidthProperty =
            DependencyProperty.Register(
                nameof(LeftColumnWidth),
                typeof(double),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(300.0));

        public double LeftColumnWidth
        {
            get => (double)GetValue(LeftColumnWidthProperty);
            set => SetValue(LeftColumnWidthProperty, value);
        }

        #endregion

        #region 右侧编辑区配置

        public static readonly DependencyProperty RightIconProperty =
            DependencyProperty.Register(
                nameof(RightIcon),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("📝"));

        public string RightIcon
        {
            get => (string)GetValue(RightIconProperty);
            set => SetValue(RightIconProperty, value);
        }

        public static readonly DependencyProperty RightTitleProperty =
            DependencyProperty.Register(
                nameof(RightTitle),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("编辑器"));

        public string RightTitle
        {
            get => (string)GetValue(RightTitleProperty);
            set => SetValue(RightTitleProperty, value);
        }

        public static readonly DependencyProperty UseStandardScheme2LayoutProperty =
            DependencyProperty.Register(
                nameof(UseStandardScheme2Layout),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool UseStandardScheme2Layout
        {
            get => (bool)GetValue(UseStandardScheme2LayoutProperty);
            set => SetValue(UseStandardScheme2LayoutProperty, value);
        }

        public static readonly DependencyProperty HeaderTabsProperty =
            DependencyProperty.Register(
                nameof(HeaderTabs),
                typeof(ObservableCollection<TabItemData>),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnHeaderTabsChanged));

        public ObservableCollection<TabItemData> HeaderTabs
        {
            get
            {
                var value = (ObservableCollection<TabItemData>)GetValue(HeaderTabsProperty);
                if (value == null)
                {
                    value = new ObservableCollection<TabItemData>();
                    SetValue(HeaderTabsProperty, value);
                    AttachHeaderTabsHandlers(value);
                }
                return value;
            }
            set => SetValue(HeaderTabsProperty, value);
        }

        private static void OnHeaderTabsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                if (e.OldValue is ObservableCollection<TabItemData> oldTabs)
                {
                    oldTabs.CollectionChanged -= layout.OnHeaderTabsCollectionChanged;
                    foreach (var tab in oldTabs)
                    {
                        tab.PropertyChanged -= layout.OnHeaderTabPropertyChanged;
                    }
                }

                if (e.NewValue is ObservableCollection<TabItemData> newTabs)
                {
                    layout.AttachHeaderTabsHandlers(newTabs);
                }
            }
        }

        private void AttachHeaderTabsHandlers(ObservableCollection<TabItemData> tabs)
        {
            tabs.CollectionChanged -= OnHeaderTabsCollectionChanged;
            tabs.CollectionChanged += OnHeaderTabsCollectionChanged;

            foreach (var tab in tabs)
            {
                tab.PropertyChanged -= OnHeaderTabPropertyChanged;
                tab.PropertyChanged += OnHeaderTabPropertyChanged;
            }
        }

        private void OnHeaderTabsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<TabItemData>())
                {
                    item.PropertyChanged -= OnHeaderTabPropertyChanged;
                    item.PropertyChanged += OnHeaderTabPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<TabItemData>())
                {
                    item.PropertyChanged -= OnHeaderTabPropertyChanged;
                }
            }
        }

        private void OnHeaderTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TabItemData.IsSelected) || _isUpdatingHeaderTabs)
                return;

            if (sender is not TabItemData changedTab || HeaderTabs == null)
                return;

            if (!changedTab.IsSelected)
                return;

            _isUpdatingHeaderTabs = true;

            var index = HeaderTabs.IndexOf(changedTab);
            if (index >= 0)
            {
                SelectedHeaderTabIndex = index;

                for (int i = 0; i < HeaderTabs.Count; i++)
                {
                    if (i != index && HeaderTabs[i].IsSelected)
                    {
                        HeaderTabs[i].IsSelected = false;
                    }
                }
            }

            _isUpdatingHeaderTabs = false;
        }

        public static readonly DependencyProperty SelectedHeaderTabIndexProperty =
            DependencyProperty.Register(
                nameof(SelectedHeaderTabIndex),
                typeof(int),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(0, OnSelectedHeaderTabIndexChanged));

        public int SelectedHeaderTabIndex
        {
            get => (int)GetValue(SelectedHeaderTabIndexProperty);
            set => SetValue(SelectedHeaderTabIndexProperty, value);
        }

        private static void OnSelectedHeaderTabIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                if (layout._isUpdatingHeaderTabs)
                    return;

                var index = (int)e.NewValue;
                if (index < 0 || layout.HeaderTabs == null || index >= layout.HeaderTabs.Count)
                    return;

                layout._isUpdatingHeaderTabs = true;

                for (int i = 0; i < layout.HeaderTabs.Count; i++)
                {
                    layout.HeaderTabs[i].IsSelected = (i == index);
                }

                layout._isUpdatingHeaderTabs = false;
            }
        }

        public static readonly DependencyProperty IsEditAreaEnabledProperty =
            DependencyProperty.Register(
                nameof(IsEditAreaEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool IsEditAreaEnabled
        {
            get => (bool)GetValue(IsEditAreaEnabledProperty);
            set => SetValue(IsEditAreaEnabledProperty, value);
        }

        public static readonly DependencyProperty EditAdditionalContentProperty =
            DependencyProperty.Register(
                nameof(EditAdditionalContent),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnEditAdditionalContentChanged));

        public object? EditAdditionalContent
        {
            get => GetValue(EditAdditionalContentProperty);
            set => SetValue(EditAdditionalContentProperty, value);
        }

        private static void OnEditAdditionalContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                if (layout.Tab1Content == null)
                {
                    layout.Tab1Content = e.NewValue;
                }
            }
        }

        public static readonly DependencyProperty DetailsContentProperty =
            DependencyProperty.Register(
                nameof(DetailsContent),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnDetailsContentChanged));

        public object? DetailsContent
        {
            get => GetValue(DetailsContentProperty);
            set => SetValue(DetailsContentProperty, value);
        }

        private static void OnDetailsContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                if (layout.Tab2Content == null)
                {
                    layout.Tab2Content = e.NewValue;
                }
            }
        }

        public static readonly DependencyProperty Tab1ContentProperty =
            DependencyProperty.Register(
                nameof(Tab1Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public object? Tab1Content
        {
            get => GetValue(Tab1ContentProperty);
            set => SetValue(Tab1ContentProperty, value);
        }

        public static readonly DependencyProperty Tab2ContentProperty =
            DependencyProperty.Register(
                nameof(Tab2Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public object? Tab2Content
        {
            get => GetValue(Tab2ContentProperty);
            set => SetValue(Tab2ContentProperty, value);
        }

        public static readonly DependencyProperty Tab3ContentProperty =
            DependencyProperty.Register(
                nameof(Tab3Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab3Content
        {
            get => GetValue(Tab3ContentProperty);
            set => SetValue(Tab3ContentProperty, value);
        }

        public static readonly DependencyProperty Tab4ContentProperty =
            DependencyProperty.Register(
                nameof(Tab4Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab4Content
        {
            get => GetValue(Tab4ContentProperty);
            set => SetValue(Tab4ContentProperty, value);
        }

        public static readonly DependencyProperty Tab5ContentProperty =
            DependencyProperty.Register(
                nameof(Tab5Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab5Content
        {
            get => GetValue(Tab5ContentProperty);
            set => SetValue(Tab5ContentProperty, value);
        }

        public static readonly DependencyProperty Tab6ContentProperty =
            DependencyProperty.Register(
                nameof(Tab6Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab6Content
        {
            get => GetValue(Tab6ContentProperty);
            set => SetValue(Tab6ContentProperty, value);
        }

        public static readonly DependencyProperty Tab7ContentProperty =
            DependencyProperty.Register(
                nameof(Tab7Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab7Content
        {
            get => GetValue(Tab7ContentProperty);
            set => SetValue(Tab7ContentProperty, value);
        }

        public static readonly DependencyProperty Tab8ContentProperty =
            DependencyProperty.Register(
                nameof(Tab8Content),
                typeof(object),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabContentChanged));

        public object? Tab8Content
        {
            get => GetValue(Tab8ContentProperty);
            set => SetValue(Tab8ContentProperty, value);
        }

        private static void OnTabContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout && layout.IsLoaded)
            {
                layout.RebuildTabs();
            }
        }

        public static readonly DependencyProperty Tab1HeaderProperty =
            DependencyProperty.Register(nameof(Tab1Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata("编辑", OnTabHeaderChanged));

        public string Tab1Header
        {
            get => (string)GetValue(Tab1HeaderProperty);
            set => SetValue(Tab1HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab2HeaderProperty =
            DependencyProperty.Register(nameof(Tab2Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata("详情", OnTabHeaderChanged));

        public string Tab2Header
        {
            get => (string)GetValue(Tab2HeaderProperty);
            set => SetValue(Tab2HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab3HeaderProperty =
            DependencyProperty.Register(nameof(Tab3Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab3Header
        {
            get => (string)GetValue(Tab3HeaderProperty);
            set => SetValue(Tab3HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab4HeaderProperty =
            DependencyProperty.Register(nameof(Tab4Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab4Header
        {
            get => (string)GetValue(Tab4HeaderProperty);
            set => SetValue(Tab4HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab5HeaderProperty =
            DependencyProperty.Register(nameof(Tab5Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab5Header
        {
            get => (string)GetValue(Tab5HeaderProperty);
            set => SetValue(Tab5HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab6HeaderProperty =
            DependencyProperty.Register(nameof(Tab6Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab6Header
        {
            get => (string)GetValue(Tab6HeaderProperty);
            set => SetValue(Tab6HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab7HeaderProperty =
            DependencyProperty.Register(nameof(Tab7Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab7Header
        {
            get => (string)GetValue(Tab7HeaderProperty);
            set => SetValue(Tab7HeaderProperty, value);
        }

        public static readonly DependencyProperty Tab8HeaderProperty =
            DependencyProperty.Register(nameof(Tab8Header), typeof(string), typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnTabHeaderChanged));

        public string Tab8Header
        {
            get => (string)GetValue(Tab8HeaderProperty);
            set => SetValue(Tab8HeaderProperty, value);
        }

        private static void OnTabHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                layout.UpdateTabHeaders();
            }
        }

        private void RebuildTabs()
        {
            HeaderTabs.Clear();
            UpdateTabHeaders();
        }

        private void UpdateTabHeaders()
        {
            int requiredCount = 2;
            if (Tab3Content != null || !string.IsNullOrEmpty(Tab3Header)) requiredCount = 3;
            if (Tab4Content != null || !string.IsNullOrEmpty(Tab4Header)) requiredCount = 4;
            if (Tab5Content != null || !string.IsNullOrEmpty(Tab5Header)) requiredCount = 5;
            if (Tab6Content != null || !string.IsNullOrEmpty(Tab6Header)) requiredCount = 6;
            if (Tab7Content != null || !string.IsNullOrEmpty(Tab7Header)) requiredCount = 7;
            if (Tab8Content != null || !string.IsNullOrEmpty(Tab8Header)) requiredCount = 8;

            if (HeaderTabs.Count != requiredCount)
            {
                var preservedIndex = SelectedHeaderTabIndex;
                if (preservedIndex < 0 || preservedIndex >= requiredCount)
                {
                    preservedIndex = 0;
                }

                HeaderTabs.Clear();

                HeaderTabs.Add(new TabItemData { Header = Tab1Header ?? "编辑", Icon = "", IsSelected = preservedIndex == 0 });
                HeaderTabs.Add(new TabItemData { Header = Tab2Header ?? "详情", Icon = "", IsSelected = preservedIndex == 1 });

                if (requiredCount >= 3)
                    HeaderTabs.Add(new TabItemData { Header = Tab3Header ?? "Tab3", Icon = "", IsSelected = preservedIndex == 2 });
                if (requiredCount >= 4)
                    HeaderTabs.Add(new TabItemData { Header = Tab4Header ?? "Tab4", Icon = "", IsSelected = preservedIndex == 3 });
                if (requiredCount >= 5)
                    HeaderTabs.Add(new TabItemData { Header = Tab5Header ?? "Tab5", Icon = "", IsSelected = preservedIndex == 4 });
                if (requiredCount >= 6)
                    HeaderTabs.Add(new TabItemData { Header = Tab6Header ?? "Tab6", Icon = "", IsSelected = preservedIndex == 5 });
                if (requiredCount >= 7)
                    HeaderTabs.Add(new TabItemData { Header = Tab7Header ?? "Tab7", Icon = "", IsSelected = preservedIndex == 6 });
                if (requiredCount >= 8)
                    HeaderTabs.Add(new TabItemData { Header = Tab8Header ?? "Tab8", Icon = "", IsSelected = preservedIndex == 7 });

                SelectedHeaderTabIndex = preservedIndex;
                AttachHeaderTabsHandlers(HeaderTabs);
                return;
            }

            if (HeaderTabs.Count > 0) HeaderTabs[0].Header = Tab1Header ?? "编辑";
            if (HeaderTabs.Count > 1) HeaderTabs[1].Header = Tab2Header ?? "详情";
            if (HeaderTabs.Count > 2) HeaderTabs[2].Header = Tab3Header ?? "Tab3";
            if (HeaderTabs.Count > 3) HeaderTabs[3].Header = Tab4Header ?? "Tab4";
            if (HeaderTabs.Count > 4) HeaderTabs[4].Header = Tab5Header ?? "Tab5";
            if (HeaderTabs.Count > 5) HeaderTabs[5].Header = Tab6Header ?? "Tab6";
            if (HeaderTabs.Count > 6) HeaderTabs[6].Header = Tab7Header ?? "Tab7";
            if (HeaderTabs.Count > 7) HeaderTabs[7].Header = Tab8Header ?? "Tab8";
        }

        #endregion

        #region 标准分类表单自动模式

        public static readonly DependencyProperty UseCategoryManagementFormProperty =
            DependencyProperty.Register(
                nameof(UseCategoryManagementForm),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool UseCategoryManagementForm
        {
            get => (bool)GetValue(UseCategoryManagementFormProperty);
            set => SetValue(UseCategoryManagementFormProperty, value);
        }

        public static readonly DependencyProperty CategoryFormMaxLevelProperty =
            DependencyProperty.Register(
                nameof(CategoryFormMaxLevel),
                typeof(int),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(5));

        public int CategoryFormMaxLevel
        {
            get => (int)GetValue(CategoryFormMaxLevelProperty);
            set => SetValue(CategoryFormMaxLevelProperty, value);
        }

        #endregion

        #region 自动化功能配置

        public static readonly DependencyProperty AutoRefreshAfterSaveProperty =
            DependencyProperty.Register(
                nameof(AutoRefreshAfterSave),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false, OnAutomationPropertyChanged));

        public bool AutoRefreshAfterSave
        {
            get => (bool)GetValue(AutoRefreshAfterSaveProperty);
            set => SetValue(AutoRefreshAfterSaveProperty, value);
        }

        private static void OnAutomationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout && layout._originalSaveCommand != null)
            {
                if (layout.AutoRefreshAfterSave)
                {
                    layout._wrappedSaveCommand = layout.WrapSaveCommand(layout._originalSaveCommand);
                    TM.App.Log("[TwoColumnEditorLayout] 自动化属性已更新，重新包装SaveCommand");
                }
                else
                {
                    layout._wrappedSaveCommand = null;
                }

                layout.UpdateInternalSaveCommand();
            }
        }

        public static readonly DependencyProperty RefreshCallbackProperty =
            DependencyProperty.Register(
                nameof(RefreshCallback),
                typeof(Action),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public Action RefreshCallback
        {
            get => (Action)GetValue(RefreshCallbackProperty);
            set => SetValue(RefreshCallbackProperty, value);
        }

        public event EventHandler? SaveCompleted;

        #endregion

        #region DataTreeView 配置

        public static readonly DependencyProperty ParentClickModeProperty =
            DependencyProperty.Register(
                nameof(ParentClickMode),
                typeof(ParentNodeClickMode),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(ParentNodeClickMode.Toggle));

        public ParentNodeClickMode ParentClickMode
        {
            get => (ParentNodeClickMode)GetValue(ParentClickModeProperty);
            set => SetValue(ParentClickModeProperty, value);
        }

        public static readonly DependencyProperty TreeLevel1HorizontalAlignmentProperty =
            DependencyProperty.Register(
                nameof(TreeLevel1HorizontalAlignment),
                typeof(HorizontalAlignment),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(HorizontalAlignment.Center));

        public HorizontalAlignment TreeLevel1HorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(TreeLevel1HorizontalAlignmentProperty);
            set => SetValue(TreeLevel1HorizontalAlignmentProperty, value);
        }

        public static readonly DependencyProperty ShowActionButtonsProperty =
            DependencyProperty.Register(
                nameof(ShowActionButtons),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool ShowActionButtons
        {
            get => (bool)GetValue(ShowActionButtonsProperty);
            set => SetValue(ShowActionButtonsProperty, value);
        }

        public static readonly DependencyProperty AIGenerateCommandProperty =
            DependencyProperty.Register(
                nameof(AIGenerateCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand? AIGenerateCommand
        {
            get => (ICommand?)GetValue(AIGenerateCommandProperty);
            set => SetValue(AIGenerateCommandProperty, value);
        }

        public static readonly DependencyProperty IsAIGenerateEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAIGenerateEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsAIGenerateEnabled
        {
            get => (bool)GetValue(IsAIGenerateEnabledProperty);
            set => SetValue(IsAIGenerateEnabledProperty, value);
        }

        public static readonly DependencyProperty ShowAIGenerateButtonProperty =
            DependencyProperty.Register(
                nameof(ShowAIGenerateButton),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true));

        public bool ShowAIGenerateButton
        {
            get => (bool)GetValue(ShowAIGenerateButtonProperty);
            set => SetValue(ShowAIGenerateButtonProperty, value);
        }

        public static readonly DependencyProperty AIGenerateButtonTextProperty =
            DependencyProperty.Register(
                nameof(AIGenerateButtonText),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("AI单次"));

        public string AIGenerateButtonText
        {
            get => (string)GetValue(AIGenerateButtonTextProperty);
            set => SetValue(AIGenerateButtonTextProperty, value);
        }

        private static void OnActionPermissionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                layout.UpdateActionPermissionStates();
            }
        }

        public static readonly DependencyProperty EnableCategoryActionsProperty =
            DependencyProperty.Register(
                nameof(EnableCategoryActions),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true, OnActionPermissionChanged));

        public bool EnableCategoryActions
        {
            get => (bool)GetValue(EnableCategoryActionsProperty);
            set => SetValue(EnableCategoryActionsProperty, value);
        }

        public static readonly DependencyProperty EnableContentActionsProperty =
            DependencyProperty.Register(
                nameof(EnableContentActions),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(true, OnActionPermissionChanged));

        public bool EnableContentActions
        {
            get => (bool)GetValue(EnableContentActionsProperty);
            set => SetValue(EnableContentActionsProperty, value);
        }

        public static readonly DependencyProperty IsAddActionEnabledProperty =
            DependencyProperty.Register(
                nameof(IsAddActionEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsAddActionEnabled
        {
            get => (bool)GetValue(IsAddActionEnabledProperty);
            private set => SetValue(IsAddActionEnabledProperty, value);
        }

        public static readonly DependencyProperty IsDeleteActionEnabledProperty =
            DependencyProperty.Register(
                nameof(IsDeleteActionEnabled),
                typeof(bool),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(false));

        public bool IsDeleteActionEnabled
        {
            get => (bool)GetValue(IsDeleteActionEnabledProperty);
            private set => SetValue(IsDeleteActionEnabledProperty, value);
        }

        public static readonly DependencyProperty CategoryActionDisabledMessageProperty =
            DependencyProperty.Register(
                nameof(CategoryActionDisabledMessage),
                typeof(string),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata("分类由分类配置中心维护"));

        public string CategoryActionDisabledMessage
        {
            get => (string)GetValue(CategoryActionDisabledMessageProperty);
            set => SetValue(CategoryActionDisabledMessageProperty, value);
        }

        public static readonly DependencyProperty MaxLevelProperty =
            DependencyProperty.Register(
                nameof(MaxLevel),
                typeof(int),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(5));

        public int MaxLevel
        {
            get => (int)GetValue(MaxLevelProperty);
            set => SetValue(MaxLevelProperty, value);
        }

        #endregion

        #region 命令绑定

        public static readonly DependencyProperty NodeDoubleClickCommandProperty =
            DependencyProperty.Register(
                nameof(NodeDoubleClickCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand NodeDoubleClickCommand
        {
            get => (ICommand)GetValue(NodeDoubleClickCommandProperty);
            set => SetValue(NodeDoubleClickCommandProperty, value);
        }

        public static readonly DependencyProperty TreeAfterActionCommandProperty =
            DependencyProperty.Register(
                nameof(TreeAfterActionCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand TreeAfterActionCommand
        {
            get => (ICommand)GetValue(TreeAfterActionCommandProperty);
            set => SetValue(TreeAfterActionCommandProperty, value);
        }

        public static readonly DependencyProperty AddCommandProperty =
            DependencyProperty.Register(
                nameof(AddCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand AddCommand
        {
            get => (ICommand)GetValue(AddCommandProperty);
            set => SetValue(AddCommandProperty, value);
        }

        public static readonly DependencyProperty SaveCommandProperty =
            DependencyProperty.Register(
                nameof(SaveCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null, OnSaveCommandChanged));

        public ICommand SaveCommand
        {
            get => (ICommand)GetValue(SaveCommandProperty);
            set => SetValue(SaveCommandProperty, value);
        }

        private static void OnSaveCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TwoColumnEditorLayout layout)
            {
                var newCommand = e.NewValue as ICommand;

                if (newCommand != null && layout.AutoRefreshAfterSave)
                {
                    layout._originalSaveCommand = newCommand;
                    layout._wrappedSaveCommand = layout.WrapSaveCommand(newCommand);

                    TM.App.Log("[TwoColumnEditorLayout] SaveCommand已包装，启用自动化功能");
                }
                else
                {
                    layout._originalSaveCommand = newCommand;
                    layout._wrappedSaveCommand = null;
                }

                layout.UpdateInternalSaveCommand();
            }
        }

        public ICommand GetEffectiveSaveCommand()
        {
            return _wrappedSaveCommand ?? SaveCommand;
        }

        public static readonly DependencyProperty InternalSaveCommandProperty =
            DependencyProperty.Register(
                nameof(InternalSaveCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand InternalSaveCommand
        {
            get => (ICommand)GetValue(InternalSaveCommandProperty);
            private set => SetValue(InternalSaveCommandProperty, value);
        }

        private void UpdateInternalSaveCommand()
        {
            InternalSaveCommand = _wrappedSaveCommand ?? SaveCommand;
        }

        public static readonly DependencyProperty DeleteCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand DeleteCommand
        {
            get => (ICommand)GetValue(DeleteCommandProperty);
            set => SetValue(DeleteCommandProperty, value);
        }

        public static readonly DependencyProperty DeleteAllCommandProperty =
            DependencyProperty.Register(
                nameof(DeleteAllCommand),
                typeof(ICommand),
                typeof(TwoColumnEditorLayout),
                new PropertyMetadata(null));

        public ICommand DeleteAllCommand
        {
            get => (ICommand)GetValue(DeleteAllCommandProperty);
            set => SetValue(DeleteAllCommandProperty, value);
        }

        #endregion
    }
}

