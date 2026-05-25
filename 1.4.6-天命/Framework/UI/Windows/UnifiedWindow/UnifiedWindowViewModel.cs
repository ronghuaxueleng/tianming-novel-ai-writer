using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reflection;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Models;
using TM.Framework.Common.Services;
using TM.Framework.Common.Constants;
using TM.Framework.User.Services;
using TM.Services.Framework.AI.Core;
using TM.Framework.UI.Helpers;
using TM.Framework.Common.Services.Factories;
using System.Windows.Threading;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class UnifiedWindowViewModel : INotifyPropertyChanged
    {
        #region 枚举定义

        public enum WindowMode
        {
            Writing,
            Settings
        }

        #endregion

        #region 配置模型类

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        public class SettingsTab : INotifyPropertyChanged
        {
            private bool _isSelected;
            private string _title = string.Empty;

            public int Index { get; set; }
            public string Icon { get; set; } = string.Empty;
            public string Title
            {
                get => _title;
                set
                {
                    if (_title != value)
                    {
                        _title = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                    }
                }
            }
            public string ModuleName { get; set; } = string.Empty;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        #endregion

        #region 私有字段

        private Dictionary<Type, UserControl> _viewCache = new();
        private System.Threading.CancellationTokenSource? _preWarmCts;
        private System.Threading.Tasks.Task? _preWarmTask;
        private WindowMode _currentMode = WindowMode.Settings;
        private SettingsTab? _selectedTab;
        private ObservableCollection<TreeNodeItem>? _treeNodes;
        private UserControl? _currentView;
        private ICommand _nodeClickCommand;
        private ICommand _nodeDoubleClickCommand;
        private readonly Lazy<UserControl> _loadingPlaceholder = new(() => CreateLoadingPlaceholder());
        private int _viewSwitchRequestId;

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

            System.Diagnostics.Debug.WriteLine($"[UnifiedWindowViewModel] {key}: {ex.Message}");
        }

        #endregion

        #region 公开属性

        public WindowMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsWritingMode));
                    OnPropertyChanged(nameof(IsSettingsMode));
                    OnPropertyChanged(nameof(WindowTitle));
                    LoadTabsForMode(value);
                }
            }
        }

        public bool IsWritingMode
        {
            get => _currentMode == WindowMode.Writing;
            set { if (value) CurrentMode = WindowMode.Writing; }
        }

        public bool IsSettingsMode
        {
            get => _currentMode == WindowMode.Settings;
            set { if (value) CurrentMode = WindowMode.Settings; }
        }

        public string WindowTitle => _currentMode == WindowMode.Writing ? "写作" : "个人";

        public ObservableCollection<SettingsTab> Tabs { get; private set; } = new();

        public SettingsTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != value)
                {
                    if (_selectedTab != null && value != null &&
                        !string.Equals(_selectedTab.ModuleName, value.ModuleName, StringComparison.OrdinalIgnoreCase))
                    {
                        var currentKey = _selectedTab.ModuleName;
                        if (!BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSession(currentKey))
                        {
                            return;
                        }
                    }

                    if (_selectedTab != null) _selectedTab.IsSelected = false;
                    _selectedTab = value;
                    if (_selectedTab != null) _selectedTab.IsSelected = true;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        LoadTreeForTab(value);
                    }
                }
            }
        }

        public ObservableCollection<TreeNodeItem>? TreeNodes
        {
            get => _treeNodes;
            set
            {
                _treeNodes = value;
                OnPropertyChanged();
            }
        }

        public ICommand NodeClickCommand => _nodeClickCommand;

        public ICommand NodeDoubleClickCommand => _nodeDoubleClickCommand;

        public UserControl? CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 构造函数

        private readonly CurrentUserContext _userContext;

        public UnifiedWindowViewModel(CurrentUserContext userContext)
        {
            _userContext = userContext;
            _nodeClickCommand = new AsyncRelayCommand(OnNodeClickedAsync);
            _nodeDoubleClickCommand = new AsyncRelayCommand(OnNodeDoubleClickedAsync);
            _userContext.UserChanged += (s, e) => UpdateUserTabTitle();
            LoadTabsForMode(WindowMode.Settings);
        }

        private void UpdateUserTabTitle()
        {
        }

        #endregion

        #region 模式和Tab管理

        private void LoadTabsForMode(WindowMode mode)
        {
            Tabs.Clear();

            var tabDefs = mode == WindowMode.Writing 
                ? NavigationDefinitions.WritingTabs 
                : NavigationDefinitions.PersonalTabs;

            foreach (var tabDef in tabDefs)
            {
                Tabs.Add(new SettingsTab 
                { 
                    Index = tabDef.Index, 
                    Icon = tabDef.Icon, 
                    Title = tabDef.Title, 
                    ModuleName = tabDef.ModuleName 
                });
            }

            SelectedTab = Tabs.FirstOrDefault();
        }

        private void LoadTreeForTab(SettingsTab tab)
        {
            if (tab == null) return;

            var moduleNav = NavigationDefinitions.GetModuleByName(tab.ModuleName);
            TreeNodes = moduleNav != null 
                ? BuildTreeFromNavigation(moduleNav) 
                : new ObservableCollection<TreeNodeItem>();
        }

        #endregion

        #region 节点创建辅助方法

        private TreeNodeItem CreateParentNode(string icon, string name, params TreeNodeItem[] children)
        {
            var node = new TreeNodeItem
            {
                Icon = icon,
                Name = name,
                Level = 1,
                Children = new ObservableCollection<TreeNodeItem>(children)
            };

            SetChildrenLevel(node, 1);
            return node;
        }

        private TreeNodeItem CreateLeafNode(string icon, string name, Type viewType)
        {
            return new TreeNodeItem
            {
                Icon = icon,
                Name = name,
                Level = 1,
                Tag = viewType
            };
        }

        private void SetChildrenLevel(TreeNodeItem parent, int parentLevel)
        {
            foreach (var child in parent.Children)
            {
                child.Level = parentLevel + 1;
                if (child.Children.Count > 0)
                {
                    SetChildrenLevel(child, child.Level);
                }
            }
        }

        #endregion

        #region 节点点击和视图加载

        private System.Threading.Tasks.Task OnNodeClickedAsync(object? parameter)
        {
            if (parameter is not TreeNodeItem node) return System.Threading.Tasks.Task.CompletedTask;
            if (node.Tag is not Type viewType) return System.Threading.Tasks.Task.CompletedTask;
            CancelPreWarm();

            if (!BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSession(SelectedTab?.ModuleName))
                return System.Threading.Tasks.Task.CompletedTask;

            if (CurrentView?.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost oldHost)
                oldHost.OnTreeNodeSelected(null);

            var requestId = ++_viewSwitchRequestId;

            if (_viewCache.TryGetValue(viewType, out var cached))
            {
                CurrentView = cached;
                if (cached.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost host)
                    host.OnTreeNodeSelected(null);
                return System.Threading.Tasks.Task.CompletedTask;
            }

            CurrentView = _loadingPlaceholder.Value;
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (requestId != _viewSwitchRequestId)
                {
                    return;
                }

                var view = LoadView(viewType);

                if (requestId != _viewSwitchRequestId)
                {
                    return;
                }

                _viewCache[viewType] = view;
                CurrentView = view;
            }, DispatcherPriority.Background);

            return System.Threading.Tasks.Task.CompletedTask;
        }

        private System.Threading.Tasks.Task OnNodeDoubleClickedAsync(object? parameter)
        {
            if (parameter is not TreeNodeItem node) return System.Threading.Tasks.Task.CompletedTask;
            if (node.Tag is not Type viewType) return System.Threading.Tasks.Task.CompletedTask;
            CancelPreWarm();

            if (!BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSession(SelectedTab?.ModuleName))
                return System.Threading.Tasks.Task.CompletedTask;

            if (CurrentView?.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost oldHost)
                oldHost.OnTreeNodeSelected(null);

            var requestId = ++_viewSwitchRequestId;

            if (_viewCache.TryGetValue(viewType, out var cached))
            {
                CurrentView = cached;
                if (cached.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost host)
                    host.OnBusinessActivated();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            CurrentView = _loadingPlaceholder.Value;
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (requestId != _viewSwitchRequestId)
                {
                    return;
                }

                var view = LoadView(viewType);

                if (requestId != _viewSwitchRequestId)
                {
                    return;
                }

                _viewCache[viewType] = view;
                CurrentView = view;

                if (view.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost host)
                    host.OnBusinessActivated();
            }, DispatcherPriority.Background);

            return System.Threading.Tasks.Task.CompletedTask;
        }

        private static UserControl CreateLoadingPlaceholder()
        {
            var placeholder = new UserControl();
            var grid = new System.Windows.Controls.Grid();
            var indicator = new TM.Framework.Common.Controls.Feedback.LoadingIndicator
            {
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(indicator);
            placeholder.Content = grid;
            return placeholder;
        }

        private UserControl GetOrCreateView(Type viewType)
        {
            if (_viewCache.TryGetValue(viewType, out var cachedView))
            {
                return cachedView;
            }

            var newView = LoadView(viewType);
            _viewCache[viewType] = newView;
            return newView;
        }

        private UserControl LoadView(Type viewType)
        {
            try
            {
                var view = ServiceLocator.GetOrDefault(viewType) as UserControl;
                if (view != null)
                {
                    return view;
                }

                var viewFactory = ServiceLocator.GetOrDefault(typeof(IViewFactory)) as IViewFactory;
                if (viewFactory != null)
                {
                    var factoryView = viewFactory.CreateView(viewType);
                    return factoryView;
                }

                if (Activator.CreateInstance(viewType) is UserControl fallbackView)
                {
                    return fallbackView;
                }

                TM.App.Log($"[UnifiedWindow] 视图创建失败: {viewType.FullName}");

                var placeholder = new UserControl();
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = $"功能开发中\n\n视图类型: {viewType.FullName}",
                    FontSize = 16,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                placeholder.Content = textBlock;
                return placeholder;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载视图异常: {viewType.FullName}, {ex.Message}\n{ex.StackTrace}");

                var errorView = new UserControl();
                var errorText = new System.Windows.Controls.TextBlock
                {
                    Text = $"加载失败\n\n{ex.Message}",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.Red,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                errorView.Content = errorText;
                return errorView;
            }
        }

        #endregion

        #region 从NavigationDefinitions构建导航树

        private ObservableCollection<TreeNodeItem> BuildTreeFromNavigation(ModuleNavigation moduleNav)
        {
            var tree = new ObservableCollection<TreeNodeItem>();

            foreach (var subModule in moduleNav.SubModules)
            {
                var functionNodes = subModule.Functions
                    .Select(f => CreateLeafNode(f.Icon, f.Name, f.ViewType))
                    .ToArray();

                var subModuleNode = CreateParentNode(subModule.Icon, subModule.Name, functionNodes);
                tree.Add(subModuleNode);
            }

            return tree;
        }

        #endregion

        #region 视图预热

        public async System.Threading.Tasks.Task PreWarmAllViewsAsync()
        {
            if (_preWarmTask != null)
            {
                await _preWarmTask.ConfigureAwait(false);
                return;
            }

            _preWarmCts?.Cancel();
            _preWarmCts = new System.Threading.CancellationTokenSource();
            _preWarmTask = PreWarmAllViewsCoreAsync(_preWarmCts.Token);
            await _preWarmTask.ConfigureAwait(false);
        }

        public void CancelPreWarm()
        {
            _preWarmCts?.Cancel();
        }

        private bool ShouldSkipPreWarm(Type viewType)
        {
            var fullName = viewType.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
                return false;

            if (fullName.StartsWith("TM.Framework.SystemSettings.Info.", StringComparison.Ordinal))
                return true;
            if (fullName == "TM.Framework.SystemSettings.Logging.LogRotation.LogRotationView")
                return true;
            if (fullName.StartsWith("TM.Modules.Design.SmartParsing.BookAnalysis.", StringComparison.Ordinal))
                return true;

            if (_currentMode == WindowMode.Settings)
            {
                if (fullName.StartsWith("TM.Modules.Design.", StringComparison.Ordinal)
                    || fullName.StartsWith("TM.Modules.Generate.", StringComparison.Ordinal)
                    || fullName.StartsWith("TM.Modules.Validate.", StringComparison.Ordinal))
                    return true;
            }
            else if (_currentMode == WindowMode.Writing)
            {
                if (fullName.StartsWith("TM.Framework.User.", StringComparison.Ordinal)
                    || fullName.StartsWith("TM.Framework.Appearance.", StringComparison.Ordinal)
                    || fullName.StartsWith("TM.Framework.Notifications.", StringComparison.Ordinal)
                    || fullName.StartsWith("TM.Framework.SystemSettings.", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private async System.Threading.Tasks.Task PreWarmAllViewsCoreAsync(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                TM.App.Log("[UnifiedWindowVM] 开始视图预热...");
                int count = 0;

                foreach (var viewType in TM.Framework.Common.Constants.NavigationDefinitions.GetAllViewTypes())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (ShouldSkipPreWarm(viewType))
                    {
                        continue;
                    }

                    if (_viewCache.ContainsKey(viewType))
                    {
                        continue;
                    }

                    await Application.Current.Dispatcher.InvokeAsync(
                        () =>
                        {
                            try
                            {
                                if (cancellationToken.IsCancellationRequested) return;
                                if (_viewCache.ContainsKey(viewType)) return;
                                var view = LoadView(viewType);
                                _viewCache[viewType] = view;
                                count++;
                            }
                            catch (Exception ex)
                            {
                                TM.App.Log($"[UnifiedWindowVM] 预热失败: {viewType.Name} - {ex.Message}");
                            }
                        },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }

                TM.App.Log($"[UnifiedWindowVM] 视图预热完成，共预热 {count} 个视图");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindowVM] 视图预热异常: {ex.Message}");
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
