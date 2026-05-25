using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.UI.Workspace;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Helpers;
using TM.Framework.Appearance.ThemeManagement;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ThreeColumnLayout : UserControl
    {
        private readonly DispatcherTimer _timer;
        private UnifiedWindow? _unifiedWindow;
        private Window? _ownerWindow;
        private readonly PanelCommunicationService _comm = ServiceLocator.Get<PanelCommunicationService>();
        private int _memRefreshCounter;

        private enum WindowMode { Single, Multi }
        private WindowMode _windowMode = WindowMode.Multi;
        private bool _lastWasCreative = true;

        public Button MinimizeBtn => MinimizeButton;
        public Button MaximizeBtn => MaximizeButton;
        public Button CloseBtn => CloseButton;

        public WorkspaceLayout Workspace => WorkspaceContainer;

        public ThreeColumnLayout()
        {
            InitializeComponent();

            TM.App.Log("[组件] 3栏布局初始化...");

            SizeChanged += OnLayoutSizeChanged;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += UpdateBottomBar;
            _timer.Start();

            UpdateBottomBar(null, null);

            _comm.FunctionNavigationRequested += OnFunctionNavigationRequested;
            _comm.ModuleNavigationRequested += OnModuleNavigationRequested;

            Loaded += OnThreeColumnLoaded;
            Loaded += (_, _) => Dispatcher.BeginInvoke(LoadAppIcon, System.Windows.Threading.DispatcherPriority.Background);
            Unloaded += OnThreeColumnUnloaded;

            TM.App.Log("[组件] 3栏布局初始化完成");
        }

        private Window? ResolveUnifiedWindowOwner()
        {
            return _ownerWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
        }

        private void EnsureUnifiedWindowOwner()
        {
            if (_unifiedWindow == null)
            {
                return;
            }

            if (_unifiedWindow.Owner != null
                && _unifiedWindow.Owner.IsVisible
                && _unifiedWindow.Owner.WindowState != WindowState.Minimized)
            {
                return;
            }

            var owner = ResolveUnifiedWindowOwner();
            if (owner != null)
            {
                _unifiedWindow.Owner = owner;
            }
        }

        private static string GetWindowModeSettingsPath()
            => StoragePathHelper.GetFilePath("Framework", "UI/Windows/ThreeColumnLayout", "layout_settings.json");

        private async Task LoadWindowModeSettingsAsync()
        {
            try
            {
                var path = GetWindowModeSettingsPath();
                if (!File.Exists(path)) return;
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(true);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("windowMode", out var prop)
                    && prop.GetString() == "Single")
                {
                    _windowMode = WindowMode.Single;
                    UpdateMultiWindowButtonLabel();
                    TM.App.Log("[ThreeColumnLayout] 已恢复单窗口模式");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] 加载窗口模式失败（忽略）: {ex.Message}");
            }
        }

        private void SaveWindowModeSettings()
        {
            var path = GetWindowModeSettingsPath();
            var json = $"{{\"windowMode\":\"{(_windowMode == WindowMode.Single ? "Single" : "Multi")}\"}}";
            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ThreeColumnLayout] 保存窗口模式失败（忽略）: {ex.Message}");
                }
            });
        }

        private async void OnThreeColumnLoaded(object sender, RoutedEventArgs e)
        {
            _ownerWindow = Window.GetWindow(this);
            if (_ownerWindow != null)
            {
                _ownerWindow.Activated += OnOwnerWindowActivated;
                _ownerWindow.Activated += OnOwnerWindowActivatedMarkCreative;
                CenterWindowOnScreen(_ownerWindow);
            }
            await LoadWindowModeSettingsAsync();
        }

        private void OnThreeColumnUnloaded(object sender, RoutedEventArgs e)
        {
            _comm.FunctionNavigationRequested -= OnFunctionNavigationRequested;
            _comm.ModuleNavigationRequested -= OnModuleNavigationRequested;
            _timer.Stop();

            try
            {
                if (_ownerWindow != null)
                {
                    _ownerWindow.Activated -= OnOwnerWindowActivated;
                    _ownerWindow.Activated -= OnOwnerWindowActivatedMarkCreative;
                }
            }
            catch { }
        }

        private void OnOwnerWindowActivatedMarkCreative(object? sender, EventArgs e)
        {
            _lastWasCreative = true;
        }

        private void OnOwnerWindowActivated(object? sender, EventArgs e)
        {
            if (_windowMode == WindowMode.Single) return;
            if (_unifiedWindow != null && _unifiedWindow.IsVisible)
                _unifiedWindow.Activate();
        }

        private void UpdateMultiWindowButtonLabel()
        {
            MultiWindowButtonLabel.Text = _windowMode == WindowMode.Single ? "多窗口" : "单窗口";
        }

        private void OnMultiWindowToggleClick(object sender, RoutedEventArgs e)
        {
            if (_windowMode == WindowMode.Single)
            {
                if (_unifiedWindow != null)
                    _unifiedWindow.IsStandaloneMode = false;
                _ownerWindow?.Show();
                _unifiedWindow?.Show();
                _windowMode = WindowMode.Multi;
            }
            else
            {
                if (_lastWasCreative)
                {
                    if (_unifiedWindow != null)
                        _unifiedWindow.IsStandaloneMode = false;
                    _unifiedWindow?.Hide();
                    _ownerWindow?.Show();
                    _ownerWindow?.Activate();
                }
                else
                {
                    if (_unifiedWindow != null)
                        _unifiedWindow.IsStandaloneMode = true;
                    _ownerWindow?.Hide();
                    _unifiedWindow?.Show();
                    _unifiedWindow?.Activate();
                }
                _windowMode = WindowMode.Single;
            }
            UpdateMultiWindowButtonLabel();
            SaveWindowModeSettings();
        }

        private void OnMainPinToggleClick(object sender, RoutedEventArgs e)
        {
            var win = ResolveUnifiedWindowOwner();
            if (win == null) return;
            win.Topmost = !win.Topmost;
            MainPinButtonContent.Opacity = win.Topmost ? 1.0 : 0.4;
        }

        private void CreateUnifiedWindow()
        {
            _unifiedWindow = new UnifiedWindow();
            _unifiedWindow.Owner = ResolveUnifiedWindowOwner();
            _unifiedWindow.Closed += (s, args) => _unifiedWindow = null;
            _unifiedWindow.Activated += (_, _) => _lastWasCreative = false;
            _unifiedWindow.CreativeWindowRequested += () =>
            {
                _unifiedWindow!.IsStandaloneMode = false;
                _windowMode = WindowMode.Single;
                UpdateMultiWindowButtonLabel();
                SaveWindowModeSettings();
                CenterWindowOnScreen(_ownerWindow);
                _ownerWindow?.Show();
                _ownerWindow?.Activate();
            };
        }

        private static void CenterWindowOnScreen(Window? window)
        {
            if (window == null || window.WindowState == WindowState.Maximized) return;
            var area = System.Windows.SystemParameters.WorkArea;
            var w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            window.Left = area.Left + (area.Width  - w) / 2;
            window.Top  = area.Top  + (area.Height - h) / 2;
        }

        private void ShowUnifiedWindow()
        {
            if (_windowMode == WindowMode.Single)
            {
                _unifiedWindow!.IsStandaloneMode = true;
                _ownerWindow?.Hide();
                CenterWindowOnScreen(_unifiedWindow);
            }
            else
            {
                _unifiedWindow!.IsStandaloneMode = false;
            }
            if (_unifiedWindow.WindowState == WindowState.Minimized)
                _unifiedWindow.WindowState = WindowState.Normal;
            _unifiedWindow.Show();
            _unifiedWindow.Activate();
        }

        private void OnModuleNavigationRequested(string moduleName)
        {
            try
            {
                var currentKey = string.Empty;
                if (_unifiedWindow?.DataContext is UnifiedWindowViewModel currentVm &&
                    currentVm.SelectedTab != null &&
                    !string.IsNullOrWhiteSpace(currentVm.SelectedTab.ModuleName))
                {
                    currentKey = currentVm.SelectedTab.ModuleName;
                }

                if (!BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSession(currentKey))
                {
                    return;
                }

                if (_unifiedWindow == null)
                    CreateUnifiedWindow();

                EnsureUnifiedWindowOwner();

                if (_unifiedWindow!.DataContext is UnifiedWindowViewModel viewModel)
                {
                    viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Writing;

                    var targetTab = viewModel.Tabs.FirstOrDefault(t =>
                        t.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                    if (targetTab != null)
                        viewModel.SelectedTab = targetTab;
                }

                ShowUnifiedWindow();

                TM.App.Log($"[ThreeColumnLayout] 导航到模块: {moduleName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] 模块导航失败: {ex.Message}");
            }
        }

        private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MainGrid.Clip is RectangleGeometry geometry)
            {
                geometry.Rect = new Rect(0, 0, MainGrid.ActualWidth, MainGrid.ActualHeight);
            }
        }

        private void UpdateBottomBar(object? sender, EventArgs? e)
        {
            TimeDisplay.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (++_memRefreshCounter >= 10)
            {
                _memRefreshCounter = 0;
                var memoryMB = Environment.WorkingSet / 1024 / 1024;
                SystemInfo.Text = $"内存: {memoryMB}MB";
            }
        }

        private void OnOpenWorkbench(object sender, RoutedEventArgs e)
        {
            if (_unifiedWindow == null)
                CreateUnifiedWindow();

            EnsureUnifiedWindowOwner();

            if (_unifiedWindow!.DataContext is UnifiedWindowViewModel viewModel)
            {
                if (viewModel.CurrentMode != UnifiedWindowViewModel.WindowMode.Writing)
                    viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Settings;
            }

            ShowUnifiedWindow();

            TM.App.Log("[ThreeColumnLayout] 打开工具台窗口");
        }

        private void OnFunctionNavigationRequested(string moduleName, string subModuleName, Type viewType)
        {
            try
            {
                var currentKey = string.Empty;
                if (_unifiedWindow?.DataContext is UnifiedWindowViewModel currentVm &&
                    currentVm.SelectedTab != null &&
                    !string.IsNullOrWhiteSpace(currentVm.SelectedTab.ModuleName))
                {
                    currentKey = currentVm.SelectedTab.ModuleName;
                }

                if (!BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSession(currentKey))
                {
                    return;
                }

                if (_unifiedWindow == null)
                    CreateUnifiedWindow();

                EnsureUnifiedWindowOwner();

                if (_unifiedWindow!.DataContext is UnifiedWindowViewModel viewModel)
                {
                    viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Writing;

                    var targetTab = viewModel.Tabs.FirstOrDefault(t =>
                        t.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

                    if (targetTab != null)
                    {
                        viewModel.SelectedTab = targetTab;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            NavigateToFunction(viewModel, viewType);
                        }), DispatcherPriority.Background);
                    }
                }

                ShowUnifiedWindow();

                TM.App.Log($"[ThreeColumnLayout] 导航到功能: {moduleName}/{subModuleName} -> {viewType.FullName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] 功能导航失败: {ex.Message}");
            }
        }

        private void NavigateToFunction(UnifiedWindowViewModel viewModel, Type viewType)
        {
            try
            {
                if (viewModel.TreeNodes == null)
                {
                    TM.App.Log("[ThreeColumnLayout] TreeNodes为空，无法定位功能");
                    return;
                }

                foreach (var parentNode in viewModel.TreeNodes)
                {
                    foreach (var childNode in parentNode.Children)
                    {
                        if (childNode.Tag is Type tagType && tagType == viewType)
                        {
                            viewModel.NodeClickCommand?.Execute(childNode);
                            TM.App.Log($"[ThreeColumnLayout] 成功定位到功能: {viewType.FullName}");
                            return;
                        }
                    }
                }

                TM.App.Log($"[ThreeColumnLayout] 未找到功能节点: {viewType.FullName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] 定位功能失败: {ex.Message}");
            }
        }

        public void ShowProgressBar()
        {
            Dispatcher.BeginInvoke(() =>
            {
                GlobalProgressBar.Visibility = Visibility.Visible;
                GlobalProgressBar.Value = 0;
                App.Log("[ThreeColumnLayout] 显示全局进度条");
            });
        }

        public void HideProgressBar()
        {
            Dispatcher.BeginInvoke(() =>
            {
                GlobalProgressBar.Visibility = Visibility.Collapsed;
                GlobalProgressBar.Value = 0;
                App.Log("[ThreeColumnLayout] 隐藏全局进度条");
            });
        }

        public void UpdateProgress(double percentage)
        {
            Dispatcher.BeginInvoke(() =>
            {
                GlobalProgressBar.Value = Math.Max(0, Math.Min(100, percentage));
                App.Log($"[ThreeColumnLayout] 更新进度: {percentage}%");
            });
        }

        public void SetProgress(double percentage, string? message = null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                GlobalProgressBar.Visibility = Visibility.Visible;
                GlobalProgressBar.Value = Math.Max(0, Math.Min(100, percentage));

                if (!string.IsNullOrWhiteSpace(message))
                {
                    StatusText.Text = message;
                }

                App.Log($"[ThreeColumnLayout] 设置进度: {percentage}% - {message}");
            });
        }

        private void LoadAppIcon()
        {
            try
            {
                var iconPath = StoragePathHelper.GetFrameworkPath("UI/Icons/app.ico");
                if (!File.Exists(iconPath))
                {
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    return;
                }

                var decoder = new IconBitmapDecoder(new Uri(iconPath, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var target = 24;
                var best = decoder.Frames
                    .OrderBy(f => Math.Abs(f.PixelWidth - target))
                    .ThenByDescending(f => f.PixelWidth)
                    .FirstOrDefault();

                var source = best ?? decoder.Frames.FirstOrDefault();
                if (source == null)
                {
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    return;
                }

                var brush = new ImageBrush(source)
                {
                    Stretch = Stretch.Uniform
                };
                if (brush.CanFreeze)
                    brush.Freeze();

                AppIconBorder.Background = brush;
                AppIconBorder.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] 加载应用图标失败: {ex.Message}");
                AppIconBorder.Visibility = Visibility.Collapsed;
            }
        }
    }
}

