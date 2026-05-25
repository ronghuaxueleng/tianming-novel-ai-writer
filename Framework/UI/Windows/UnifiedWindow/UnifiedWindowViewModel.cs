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
using TM.Framework.Common.Constants;
using TM.Framework.User.Services;
using TM.Framework.UI.Helpers;
using TM.Framework.Common.Services.Factories;
using System.Windows.Media;
using System.Windows.Threading;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class UnifiedWindowViewModel : INotifyPropertyChanged, IDisposable
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
        [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
        public class SettingsTab : INotifyPropertyChanged
        {
            private bool _isSelected;
            private string _title = string.Empty;

            public int Index { get; set; }
            public ImageSource? Icon { get; set; }
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
        private WindowMode _currentMode = WindowMode.Writing;
        private SettingsTab? _selectedTab;
        private ObservableCollection<TreeNodeItem>? _treeNodes;
        private UserControl? _currentView;
        private ICommand _nodeClickCommand;
        private ICommand _nodeDoubleClickCommand;
        private int _viewSwitchRequestId;

        public Action<UserControl>? PreWarmViewCallback { get; set; }

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

                    CancelPreWarm();
                    _preWarmTask = null;
                    _ = PreWarmAllViewsAsync();
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
                        var tab = value;
                        Application.Current?.Dispatcher.InvokeAsync(
                            () => LoadTreeForTab(tab),
                            System.Windows.Threading.DispatcherPriority.Input);
                    }
                }
            }
        }

        public ObservableCollection<TreeNodeItem>? TreeNodes
        {
            get => _treeNodes;
            set
            {
                if (ReferenceEquals(_treeNodes, value)) return;
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
                if (_currentView == value) return;
                _currentView = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 构造函数

        private readonly CurrentUserContext _userContext;
        private bool _disposed;
        private readonly EventHandler _userChangedHandler;
        private readonly Action<string, string> _projectChangedHandler;

        public UnifiedWindowViewModel(CurrentUserContext userContext)
        {
            _userContext = userContext;
            _nodeClickCommand = new AsyncRelayCommand(OnNodeClickedAsync);
            _nodeDoubleClickCommand = new AsyncRelayCommand(OnNodeDoubleClickedAsync);

            _userChangedHandler = (s, e) => UpdateUserTabTitle();
            _userContext.UserChanged += _userChangedHandler;

            LoadTabsForMode(WindowMode.Writing);

            _projectChangedHandler = (_, _) =>
            {
                _viewCache = new Dictionary<Type, UserControl>();
                _preWarmTask = null;
                CancelPreWarm();
                CurrentView = null;
                TM.Framework.Common.Services.UIPreWarmService.ClearPreCreatedViews();
                _ = PreWarmAllViewsAsync();
            };
            StoragePathHelper.CurrentProjectChanged += _projectChangedHandler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _userContext.UserChanged -= _userChangedHandler;
            StoragePathHelper.CurrentProjectChanged -= _projectChangedHandler;
            CancelPreWarm();
            _viewCache.Clear();
            GC.SuppressFinalize(this);
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
                    Icon = IconHelper.TryGet(tabDef.Icon),
                    Title = tabDef.Title,
                    ModuleName = tabDef.ModuleName
                });
            }

            SelectedTab = Tabs.FirstOrDefault();
        }

        private static readonly Dictionary<string, ObservableCollection<TreeNodeItem>> _treeNodesCacheByModule = new();

        private void LoadTreeForTab(SettingsTab tab)
        {
            if (tab == null) return;

            if (!_treeNodesCacheByModule.TryGetValue(tab.ModuleName, out var cached))
            {
                var moduleNav = NavigationDefinitions.GetModuleByName(tab.ModuleName);
                cached = moduleNav != null
                    ? BuildTreeFromNavigation(moduleNav)
                    : new ObservableCollection<TreeNodeItem>();
                _treeNodesCacheByModule[tab.ModuleName] = cached;
            }

            TreeNodes = cached;
        }

        #endregion

        #region 节点创建辅助方法

        private TreeNodeItem CreateParentNode(string iconKey, string name, params TreeNodeItem[] children)
        {
            var node = new TreeNodeItem
            {
                Icon = IconHelper.TryGet(iconKey),
                Name = name,
                Level = 1,
                Children = new TM.Framework.Common.ViewModels.RangeObservableCollection<TreeNodeItem>(children)
            };

            SetChildrenLevel(node, 1);
            return node;
        }

        private TreeNodeItem CreateLeafNode(string iconKey, string name, Type viewType)
        {
            return new TreeNodeItem
            {
                Icon = IconHelper.TryGet(iconKey),
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

        private async System.Threading.Tasks.Task OnNodeClickedAsync(object? parameter)
        {
            if (parameter is not TreeNodeItem node) return;
            if (node.Tag is not Type viewType) return;

            if (!await BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSessionAsync(SelectedTab?.ModuleName))
                return;

            if (CurrentView?.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost oldHost)
                oldHost.OnTreeNodeSelected(null);

            var requestId = ++_viewSwitchRequestId;

            if (_viewCache.TryGetValue(viewType, out var cached))
            {
                CurrentView = cached;
                if (cached.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost host)
                    host.OnTreeNodeSelected(null);
                return;
            }

            CancelPreWarm();
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
                SchedulePreWarmResume();
            }, DispatcherPriority.Normal);
        }

        private async System.Threading.Tasks.Task OnNodeDoubleClickedAsync(object? parameter)
        {
            if (parameter is not TreeNodeItem node) return;
            if (node.Tag is not Type viewType) return;

            if (_viewCache.TryGetValue(viewType, out var fastCached) && ReferenceEquals(_currentView, fastCached))
            {
                if (fastCached.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost fastHost)
                    fastHost.OnBusinessActivated();
                return;
            }

            if (!await BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSessionAsync(SelectedTab?.ModuleName))
                return;

            if (CurrentView?.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost oldHost)
                oldHost.OnTreeNodeSelected(null);

            var requestId = ++_viewSwitchRequestId;

            if (_viewCache.TryGetValue(viewType, out var cached))
            {
                CurrentView = cached;
                if (cached.DataContext is TM.Framework.Common.ViewModels.IBulkToggleSelectionHost host)
                    host.OnBusinessActivated();
                return;
            }

            CancelPreWarm();
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
                SchedulePreWarmResume();
            }, DispatcherPriority.Normal);
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

        public TViewModel? GetOrEnsureViewModel<TViewModel>(Type viewType) where TViewModel : class
        {
            var view = GetOrCreateView(viewType);
            return view?.DataContext as TViewModel;
        }

        private UserControl LoadView(Type viewType)
        {
            try
            {
                var preCreated = TM.Framework.Common.Services.UIPreWarmService.TakePreCreatedView(viewType);
                if (preCreated != null)
                {
                    return preCreated;
                }

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
                await _preWarmTask;
                return;
            }

            _preWarmCts?.Cancel();
            _preWarmCts = new System.Threading.CancellationTokenSource();
            _preWarmTask = PreWarmAllViewsCoreAsync(_preWarmCts.Token);
            await _preWarmTask;
        }

        public void CancelPreWarm()
        {
            _preWarmCts?.Cancel();
        }

        private void SchedulePreWarmResume()
        {
            _preWarmTask = null;
            _ = PreWarmAllViewsAsync();
        }

        private static HashSet<Type> BuildViewTypeSet(params ModuleNavigation[] modules)
        {
            var set = new HashSet<Type>();
            foreach (var module in modules)
                foreach (var sub in module.SubModules)
                    foreach (var func in sub.Functions)
                        set.Add(func.ViewType);
            return set;
        }

        private static readonly HashSet<Type> _writingModeViewTypes = BuildViewTypeSet(
            NavigationDefinitions.Design,
            NavigationDefinitions.Generate,
            NavigationDefinitions.Validate,
            NavigationDefinitions.SmartAssistant);

        private static readonly HashSet<Type> _settingsModeViewTypes = BuildViewTypeSet(
            NavigationDefinitions.User,
            NavigationDefinitions.Appearance,
            NavigationDefinitions.Notifications,
            NavigationDefinitions.SystemSettings);

        private static readonly HashSet<Type> _priorityPreWarmTypes = BuildViewTypeSet(
            NavigationDefinitions.Design,
            NavigationDefinitions.Generate,
            NavigationDefinitions.SmartAssistant);

        private static readonly HashSet<Type> _alwaysSkipPreWarmTypes = new()
        {
            typeof(TM.Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView),
            typeof(TM.Framework.SystemSettings.Logging.LogRotation.LogRotationView),
            typeof(TM.Framework.SystemSettings.Info.AppInfo.AppInfoView),
            typeof(TM.Framework.SystemSettings.Info.SystemInfo.SystemInfoView),
            typeof(TM.Framework.SystemSettings.Info.RuntimeEnv.RuntimeEnvView),
            typeof(TM.Framework.SystemSettings.Info.DiagnosticInfo.DiagnosticInfoView),
        };

        private bool ShouldSkipPreWarm(Type viewType)
        {
            if (_alwaysSkipPreWarmTypes.Contains(viewType))
                return true;

            if (_currentMode == WindowMode.Settings && _writingModeViewTypes.Contains(viewType))
                return true;
            if (_currentMode == WindowMode.Writing && _settingsModeViewTypes.Contains(viewType))
                return true;

            return false;
        }

        private async System.Threading.Tasks.Task PreWarmAllViewsCoreAsync(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
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

                    var priority = _priorityPreWarmTypes.Contains(viewType)
                        ? System.Windows.Threading.DispatcherPriority.Background
                        : System.Windows.Threading.DispatcherPriority.ApplicationIdle;

                    await Application.Current.Dispatcher.InvokeAsync(
                        () =>
                        {
                            try
                            {
                                if (cancellationToken.IsCancellationRequested) return;
                                UserControl preWarmView;
                                if (_viewCache.TryGetValue(viewType, out var existingView))
                                {
                                    preWarmView = existingView;
                                }
                                else
                                {
                                    preWarmView = LoadView(viewType);
                                    _viewCache[viewType] = preWarmView;
                                    count++;
                                }
                                PreWarmViewCallback?.Invoke(preWarmView);
                            }
                            catch (Exception ex)
                            {
                                TM.App.Log($"[UnifiedWindowVM] 预热失败: {viewType.Name} - {ex.Message}");
                            }
                        },
                        priority);
                }

                await Application.Current.Dispatcher.InvokeAsync(
                    () => TM.Framework.Common.Services.UIPreWarmService.PreWarmDialogsAndControls(),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                _ = TM.Framework.Common.Services.UIPreWarmService.PreJitCriticalTypesAsync();
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
