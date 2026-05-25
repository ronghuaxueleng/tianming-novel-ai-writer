using System;
using System.Reflection;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using TM.Framework.User.Account.Login;
using TM.Framework.User.Security.PasswordProtection;
using TM.Framework.Common.Services;

namespace TM
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class MainWindow : Window
    {
        private bool _wasDeactivated = false;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!App.IsDebugMode)
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

            System.Diagnostics.Debug.WriteLine($"[MainWindow] {key}: {ex.Message}");
        }

        private CancellationTokenSource? _networkDebounceCts;
        private bool _networkMonitoringStarted;
        private bool _reloginInProgress;

        public static readonly DependencyProperty IsNetworkBlockedProperty =
            DependencyProperty.Register(
                nameof(IsNetworkBlocked),
                typeof(bool),
                typeof(MainWindow),
                new PropertyMetadata(false));

        public bool IsNetworkBlocked
        {
            get => (bool)GetValue(IsNetworkBlockedProperty);
            set => SetValue(IsNetworkBlockedProperty, value);
        }

        public static readonly DependencyProperty NetworkOverlayMessageProperty =
            DependencyProperty.Register(
                nameof(NetworkOverlayMessage),
                typeof(string),
                typeof(MainWindow),
                new PropertyMetadata(""));

        public string NetworkOverlayMessage
        {
            get => (string)GetValue(NetworkOverlayMessageProperty);
            set => SetValue(NetworkOverlayMessageProperty, value);
        }

        public MainWindow()
        {
            InitializeComponent();

            if (App.IsDebugMode)
            {
                Console.WriteLine("[主窗口] 初始化...");
            }

            LayoutComponent.MinimizeBtn.Click += (s, e) => this.WindowState = WindowState.Minimized;
            LayoutComponent.MaximizeBtn.Click += (s, e) =>
            {
                this.WindowState = this.WindowState == WindowState.Maximized 
                    ? WindowState.Normal 
                    : WindowState.Maximized;
            };
            LayoutComponent.CloseBtn.Click += (s, e) => this.Close();

            this.MouseLeftButtonDown += (sender, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try
                    {
                        this.DragMove();
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[MainWindow] DragMove失败: {ex.Message}");
                    }
                }
            };

            InitializeTrayIconService();

            this.Loaded += (s, e) =>
            {
                StartNetworkMonitor();

                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(800);
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ToastNotification.ShowSuccess("欢迎使用天命", "程序已成功启动！", 4000);
                    });
                });
            };

            InitializePasswordProtection();

            this.PreviewMouseDown += OnGlobalMouseDown;

            this.Closed += (s, e) => StopNetworkMonitor();

            if (App.IsDebugMode)
            {
                Console.WriteLine("[主窗口] 初始化完成");
            }
        }

        private void StartNetworkMonitor()
        {
            if (_networkMonitoringStarted)
                return;

            _networkMonitoringStarted = true;

            try
            {
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

                App.Log("[NetworkMonitor] 已启动系统网络监测");
                EvaluateNetworkState();
            }
            catch (Exception ex)
            {
                App.Log($"[NetworkMonitor] 启动失败: {ex.Message}");
            }
        }

        private void StopNetworkMonitor()
        {
            if (!_networkMonitoringStarted)
                return;

            _networkMonitoringStarted = false;

            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            if (_networkDebounceCts != null)
            {
                _networkDebounceCts.Cancel();
                _networkDebounceCts.Dispose();
                _networkDebounceCts = null;
            }

            App.Log("[NetworkMonitor] 已停止系统网络监测");
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            Dispatcher.BeginInvoke(new Action(EvaluateNetworkState));
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            Dispatcher.BeginInvoke(new Action(EvaluateNetworkState));
        }

        private void EvaluateNetworkState()
        {
            bool isAvailable;
            try
            {
                isAvailable = NetworkInterface.GetIsNetworkAvailable();
            }
            catch (Exception ex)
            {
                DebugLogOnce("EvaluateNetworkState_GetIsNetworkAvailable", ex);
                isAvailable = false;
            }

            if (!isAvailable)
            {
                StartNetworkDebounceLock();
                return;
            }

            CancelNetworkDebounceLock();

            if (IsNetworkBlocked)
            {
                BeginReloginAfterNetworkRestored();
            }
        }

        private void StartNetworkDebounceLock()
        {
            if (_networkDebounceCts != null)
            {
                _networkDebounceCts.Cancel();
                _networkDebounceCts.Dispose();
                _networkDebounceCts = null;
            }

            _networkDebounceCts = new CancellationTokenSource();
            var token = _networkDebounceCts.Token;

            App.Log("[NetworkMonitor] 检测到断网，开始10秒防抖计时");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(10), token);
                    if (token.IsCancellationRequested)
                        return;

                    bool stillOffline;
                    try
                    {
                        stillOffline = !NetworkInterface.GetIsNetworkAvailable();
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("NetworkDebounce_GetIsNetworkAvailable", ex);
                        stillOffline = true;
                    }

                    if (!stillOffline)
                        return;

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (IsNetworkBlocked)
                            return;

                        IsNetworkBlocked = true;
                        NetworkOverlayMessage = "等待网络恢复...";
                        App.Log("[NetworkMonitor] 断网持续超过10秒，已触发遮罩锁定");
                    });
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    App.Log($"[NetworkMonitor] 防抖计时异常: {ex.Message}");
                }
            });
        }

        private void CancelNetworkDebounceLock()
        {
            if (_networkDebounceCts != null)
            {
                _networkDebounceCts.Cancel();
                _networkDebounceCts.Dispose();
                _networkDebounceCts = null;
            }
        }

        private void BeginReloginAfterNetworkRestored()
        {
            if (_reloginInProgress)
                return;

            _reloginInProgress = true;
            NetworkOverlayMessage = "网络已恢复，请重新登录";
            App.Log("[NetworkMonitor] 网络已恢复，开始重新登录流程");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var loginWindow = ServiceLocator.Get<TM.Framework.Common.Services.Factories.IWindowFactory>()
                        .CreateWindow<LoginWindow>();
                    loginWindow.Owner = this;

                    var result = loginWindow.ShowDialog();
                    if (result == true)
                    {
                        IsNetworkBlocked = false;
                        NetworkOverlayMessage = string.Empty;
                        App.Log("[NetworkMonitor] 重新登录成功，已解除遮罩");
                    }
                    else
                    {
                        App.Log("[NetworkMonitor] 用户取消重新登录，退出程序");
                        Application.Current.Shutdown();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[NetworkMonitor] 重新登录失败: {ex.Message}");
                    Application.Current.Shutdown();
                    return;
                }
                finally
                {
                    _reloginInProgress = false;
                }
            }));
        }

        private void InitializeTrayIconService()
        {
            try
            {
                var trayService = ServiceLocator.Get<TM.Services.Framework.SystemIntegration.TrayIconService>();
                trayService.Initialize(this);
                App.Log("[主窗口] 托盘图标服务已初始化");
            }
            catch (Exception ex)
            {
                App.Log($"[主窗口] 托盘图标服务初始化失败: {ex.Message}");
            }
        }

        private void InitializePasswordProtection()
        {
            try
            {
                this.Deactivated += MainWindow_Deactivated;
                this.Activated += MainWindow_Activated;

                this.PreviewMouseMove += MainWindow_PreviewMouseMove;
                this.PreviewKeyDown += MainWindow_PreviewKeyDown;
                this.PreviewMouseDown += MainWindow_PreviewMouseDown;

                App.Log("[AppLock] init ok");
            }
            catch (Exception ex)
            {
                App.Log($"[AppLock] init err: {ex.Message}");
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _wasDeactivated = true;
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (_wasDeactivated)
            {
                _wasDeactivated = false;

                try
                {
                    var lockSettings = ServiceLocator.Get<AppLockSettings>();
                    if (lockSettings.ShouldLockOnSwitch())
                    {
                        App.Log("[AppLock] 窗口切换时需要锁定");
                        lockSettings.LockApp("切换锁定");
                        App.LockApplication();
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[AppLock] 窗口切换锁定检查失败: {ex.Message}");
                }
            }
        }

        private void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            UpdateUserActivity();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            UpdateUserActivity();
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            UpdateUserActivity();
        }

        private DateTime _lastActivityUpdate = DateTime.MinValue;

        private void UpdateUserActivity()
        {
            var now = DateTime.Now;
            if ((now - _lastActivityUpdate).TotalSeconds < 60)
                return;

            try
            {
                var lockSettings = ServiceLocator.Get<AppLockSettings>();
                var config = lockSettings.LoadConfig();
                if (!config.EnableAutoLock || !config.EnablePasswordLock)
                    return;

                _lastActivityUpdate = now;
                lockSettings.UpdateLastActivity();
            }
            catch (Exception ex)
            {
                App.Log($"[AppLock] 更新用户活动时间失败: {ex.Message}");
            }
        }

        private void OnGlobalMouseDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            while (hitElement != null)
            {
                if (hitElement is System.Windows.Controls.ListBoxItem)
                {
                    return;
                }

                if (hitElement is System.Windows.Media.Visual || hitElement is System.Windows.Media.Media3D.Visual3D)
                {
                    hitElement = System.Windows.Media.VisualTreeHelper.GetParent(hitElement);
                }
                else
                {
                    hitElement = LogicalTreeHelper.GetParent(hitElement);
                }
            }

            ServiceLocator.Get<TM.Framework.UI.Workspace.Services.PanelCommunicationService>()
                .RequestClearMessageSelection();
        }

    }
}

