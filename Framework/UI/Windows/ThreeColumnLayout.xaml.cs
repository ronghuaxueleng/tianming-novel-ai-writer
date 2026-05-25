using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TM.Framework.UI.Workspace;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Helpers;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThreeColumnLayout : UserControl
    {
        private UnifiedWindow? _unifiedWindow;
        private Window? _ownerWindow;
        private readonly PanelCommunicationService _comm = ServiceLocator.Get<PanelCommunicationService>();

        private static readonly CubicEase _easeIn = FreezeCubicEase(EasingMode.EaseIn);
        private static readonly CubicEase _easeOut = FreezeCubicEase(EasingMode.EaseOut);
        private static CubicEase FreezeCubicEase(EasingMode mode) { var e = new CubicEase { EasingMode = mode }; e.Freeze(); return e; }
        private static readonly DoubleAnimation _fadeOutTemplate = CreateFadeOutTemplate();
        private static readonly DoubleAnimation _fadeInTemplate = CreateFadeInTemplate();
        private static DoubleAnimation CreateFadeOutTemplate() { var a = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(500)), EasingFunction = _easeIn }; a.Freeze(); return a; }
        private static DoubleAnimation CreateFadeInTemplate() { var a = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500))) { EasingFunction = _easeOut }; a.Freeze(); return a; }

        public Button MinimizeBtn => MinimizeButton;
        public Button MaximizeBtn => MaximizeButton;
        public Button CloseBtn => CloseButton;

        public WorkspaceLayout Workspace => WorkspaceContainer;

        public ThreeColumnLayout()
        {
            InitializeComponent();

            TM.App.Log("[组件] 3栏布局初始化...");

            SizeChanged += OnLayoutSizeChanged;

            _comm.FunctionNavigationRequested += OnFunctionNavigationRequested;
            _comm.ModuleNavigationRequested += OnModuleNavigationRequested;

            Loaded += OnThreeColumnLoaded;
            Loaded += (_, _) => TM.Framework.Common.Helpers.UI.AppIconLoader.Load(AppIconBorder, 24, logTag: "ThreeColumnLayout");
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

        private void OnThreeColumnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _ownerWindow = Window.GetWindow(this);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThreeColumnLayout] OnThreeColumnLoaded 失败: {ex.Message}");
            }
        }

        private void OnThreeColumnUnloaded(object sender, RoutedEventArgs e)
        {
            _comm.FunctionNavigationRequested -= OnFunctionNavigationRequested;
            _comm.ModuleNavigationRequested -= OnModuleNavigationRequested;
        }

        private void OnMainPinToggleClick(object sender, RoutedEventArgs e)
        {
            var win = ResolveUnifiedWindowOwner();
            if (win == null) return;
            win.Topmost = !win.Topmost;
            MainPinButtonContent.Opacity = win.Topmost ? 1.0 : 0.4;
        }

        private void SyncSizeFromUnifiedToOwner()
        {
            if (_ownerWindow == null || _unifiedWindow == null) return;

            var bounds = _unifiedWindow.GetSharedWindowBounds();

            if (_unifiedWindow.IsCustomMaximized)
            {
                _ownerWindow.Left = bounds.Left;
                _ownerWindow.Top = bounds.Top;
                _ownerWindow.Width = bounds.Width;
                _ownerWindow.Height = bounds.Height;
                _ownerWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                if (_ownerWindow.WindowState == WindowState.Maximized)
                {
                    _ownerWindow.WindowState = WindowState.Normal;
                }

                _ownerWindow.Left = bounds.Left;
                _ownerWindow.Top = bounds.Top;
                _ownerWindow.Width = bounds.Width;
                _ownerWindow.Height = bounds.Height;
            }
        }

        private void SyncSizeFromOwnerToUnified()
        {
            if (_ownerWindow == null || _unifiedWindow == null) return;

            var bounds = _ownerWindow.WindowState == WindowState.Maximized
                ? _ownerWindow.RestoreBounds
                : new Rect(
                    _ownerWindow.Left,
                    _ownerWindow.Top,
                    _ownerWindow.ActualWidth > 0 ? _ownerWindow.ActualWidth : _ownerWindow.Width,
                    _ownerWindow.ActualHeight > 0 ? _ownerWindow.ActualHeight : _ownerWindow.Height);

            _unifiedWindow.ApplySharedWindowBounds(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                _ownerWindow.WindowState == WindowState.Maximized);
        }

        private void CreateUnifiedWindow()
        {
            _unifiedWindow = new UnifiedWindow();
            _unifiedWindow.Owner = ResolveUnifiedWindowOwner();
            _unifiedWindow.Closed += (s, args) =>
            {
                _unifiedWindow = null;
            };
            _unifiedWindow.CreativeWindowRequested += () =>
            {
                SyncSizeFromUnifiedToOwner();

                if (_ownerWindow != null)
                {
                    if (_unifiedWindow!.Owner == null)
                        _unifiedWindow.Owner = _ownerWindow;

                    _ownerWindow.BeginAnimation(Window.OpacityProperty, null);
                    _ownerWindow.Opacity = 1;
                    _ownerWindow.Show();

                    var unifiedRef = _unifiedWindow;
                    var ownerRef = _ownerWindow;
                    var fadeOut = (DoubleAnimation)_fadeOutTemplate.Clone();
                    fadeOut.Completed += (_, _) =>
                    {
                        unifiedRef.BeginAnimation(Window.OpacityProperty, null);
                        unifiedRef.Opacity = 1;
                        unifiedRef.Hide();
                        unifiedRef.IsStandaloneMode = false;
                        ownerRef.Activate();
                    };
                    _unifiedWindow.BeginAnimation(Window.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
                    return;
                }

                _unifiedWindow!.IsStandaloneMode = false;
            };
        }

        private void ShowUnifiedWindow()
        {
            SyncSizeFromOwnerToUnified();
            _unifiedWindow!.IsStandaloneMode = true;

            if (_unifiedWindow!.WindowState == WindowState.Minimized)
                _unifiedWindow.WindowState = WindowState.Normal;

            var wasHidden = !_unifiedWindow.IsVisible;
            if (!wasHidden)
            {
                _unifiedWindow.BeginAnimation(Window.OpacityProperty, null);
                _unifiedWindow.Opacity = 1;
                _unifiedWindow.Activate();

                if (_ownerWindow != null && _ownerWindow.IsVisible)
                    _ownerWindow.Hide();

                return;
            }

            _unifiedWindow.BeginAnimation(Window.OpacityProperty, null);
            _unifiedWindow.Opacity = 0;
            _unifiedWindow.Show();
            _unifiedWindow.Activate();

            var ownerToHide = _ownerWindow;
            var fadeIn = (DoubleAnimation)_fadeInTemplate.Clone();
            fadeIn.Completed += (_, _) =>
            {
                _unifiedWindow.BeginAnimation(Window.OpacityProperty, null);
                _unifiedWindow.Opacity = 1;
                ownerToHide?.Hide();
            };
            var unifiedForFade = _unifiedWindow;
            Dispatcher.InvokeAsync(() =>
            {
                unifiedForFade.BeginAnimation(Window.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private string GetCurrentBusinessKey()
        {
            if (_unifiedWindow?.DataContext is UnifiedWindowViewModel currentVm &&
                currentVm.SelectedTab != null &&
                !string.IsNullOrWhiteSpace(currentVm.SelectedTab.ModuleName))
            {
                return currentVm.SelectedTab.ModuleName;
            }
            return string.Empty;
        }

        private async void OnModuleNavigationRequested(string moduleName)
        {
            try
            {
                if (!await BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSessionAsync(GetCurrentBusinessKey()))
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

        private void OnOpenWorkbench(object sender, RoutedEventArgs e)
        {
            if (_unifiedWindow == null)
                CreateUnifiedWindow();

            EnsureUnifiedWindowOwner();

            if (_unifiedWindow!.DataContext is UnifiedWindowViewModel viewModel)
            {
                if (viewModel.CurrentMode != UnifiedWindowViewModel.WindowMode.Writing)
                    viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Writing;
            }

            ShowUnifiedWindow();

            TM.App.Log("[ThreeColumnLayout] 打开工作台窗口");
        }

        private async void OnFunctionNavigationRequested(string moduleName, string subModuleName, Type viewType)
        {
            try
            {
                if (!await BusinessSessionNavigationGuard.TryConfirmAndEndDirtyBusinessSessionAsync(GetCurrentBusinessKey()))
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
                        NavigateToFunction(viewModel, viewType);
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

    }
}

