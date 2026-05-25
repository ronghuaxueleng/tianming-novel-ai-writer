using System;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Appearance.Font;
using TM.Framework.User.Security.PasswordProtection;
using TM.Framework.User.Account.PasswordSecurity.Services;
using TM.Framework.User.Account.Login;
using TM.Framework.User.Account.Login.Bootstrap;
using TM.Framework.User.Profile.BasicInfo;
using TM.Framework.User.Services;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Services;
using TM.Framework.Common.Services.Factories;
using TM.Framework.Notifications.SystemNotifications.SystemIntegration;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Services.Framework.Settings;
using TM.Services.Framework.SystemIntegration;

namespace TM
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess, int ProcessInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const int StdErrorHandle = -12;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;

        public static bool IsDebugMode { get; private set; }

        private IWindowFactory? _windowFactory;
        private ThemeManager? _themeManager;
        private ServerAuthService? _serverAuthService;
        private AuthTokenManager? _authTokenManager;
        private BasicInfoSettings? _basicInfoSettings;
        private CurrentUserContext? _currentUserContext;
        private AppLockSettings? _appLockSettings;
        private AccountSecurityService? _accountSecurityService;
        private SystemIntegrationSettings? _systemIntegrationSettings;
        private UIStateCache? _uiStateCache;
        private string? _lastAnnouncementShown;

        private DispatcherTimer? _autoLockTimer;
        private bool _isReturningToLogin;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {

            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                proc.PriorityBoostEnabled = true;
                GCSettings.LatencyMode = GCLatencyMode.Interactive;
            }
            catch { }

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = 0x4,
                    StateMask = 0
                };
                SetProcessInformation(Process.GetCurrentProcess().Handle, 4,
                    ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
            catch { }

            IsDebugMode = e.Args.Length > 0 && e.Args[0] == "--debug";

            try
            {
                if (!IsDebugMode)
                {
                    var nul = CreateFileW("NUL", GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (nul != IntPtr.Zero && nul.ToInt64() != -1)
                    {
                        SetStdHandle(StdErrorHandle, nul);
                    }
                }
            }
            catch
            {
            }

            try
            {
                var serviceProvider = DependencyInjection.ConfigureServices();
                _windowFactory = serviceProvider.GetRequiredService<IWindowFactory>();

                _themeManager = ServiceLocator.Get<ThemeManager>();
                _serverAuthService = ServiceLocator.Get<ServerAuthService>();
                _authTokenManager = ServiceLocator.Get<AuthTokenManager>();
                _basicInfoSettings = ServiceLocator.Get<BasicInfoSettings>();
                _currentUserContext = ServiceLocator.Get<CurrentUserContext>();
                _appLockSettings = ServiceLocator.Get<AppLockSettings>();
                _accountSecurityService = ServiceLocator.Get<AccountSecurityService>();
                _systemIntegrationSettings = ServiceLocator.Get<SystemIntegrationSettings>();
                _uiStateCache = ServiceLocator.Get<UIStateCache>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DI] 初始化失败: {ex.Message}");
                StandardDialog.ShowError($"DI初始化失败: {ex.Message}", "错误", null);
                Shutdown();
                return;
            }

            _serverAuthService!.OnForceLogout += (msg) => Dispatcher.BeginInvoke(() => ReturnToLogin(msg));
            ServerAuthInitializer.OnReturnToLoginRequired += (msg) => Dispatcher.BeginInvoke(() => ReturnToLogin(msg));
            _serverAuthService!.OnAnnouncementReceived += (msg) => Dispatcher.BeginInvoke(() =>
            {
                if (msg != _lastAnnouncementShown)
                {
                    _lastAnnouncementShown = msg;
                    GlobalToast.Info("📢 系统公告", msg, 5000);
                }
            });

            var scTask = Task.Run(() =>
            {
                try { return ProtectionService.SC(); }
                catch (Exception ex) { Log($"[App] SC err: {ex.Message}"); return false; }
            });

            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

            EventManager.RegisterClassHandler(typeof(Window), Window.PreviewKeyDownEvent, 
                new System.Windows.Input.KeyEventHandler((sender, args) =>
                {
                    if (args.SystemKey == System.Windows.Input.Key.LeftAlt || 
                        args.SystemKey == System.Windows.Input.Key.RightAlt ||
                        args.Key == System.Windows.Input.Key.LeftAlt || 
                        args.Key == System.Windows.Input.Key.RightAlt)
                    {
                        args.Handled = true;
                    }
                }));

            IsDebugMode = e.Args.Length > 0 && e.Args[0] == "--debug";

            string? _lastExMsg = null;
            DateTime _lastExTime = DateTime.MinValue;
            int _repeatCount = 0;

            this.DispatcherUnhandledException += (sender, args) =>
            {
                args.Handled = true;

                if (args.Exception is OperationCanceledException or TaskCanceledException)
                {
                    Log($"[App] 操作已取消（静默处理）: {args.Exception.Message}");
                    return;
                }

                var now = DateTime.Now;
                var msg = args.Exception.Message;
                if (msg == _lastExMsg && (now - _lastExTime).TotalSeconds < 2)
                {
                    _repeatCount++;
                    if (_repeatCount > 3)
                    {
                        return;
                    }
                }
                else
                {
                    _repeatCount = 1;
                }
                _lastExMsg = msg;
                _lastExTime = now;

                string errorMsg = $"[App] 未处理异常: {args.Exception}";

                Log("[App] !!! UI线程未处理异常 !!!");
                Log(errorMsg);

                try
                {
                    StandardDialog.ShowError(args.Exception.Message, "错误", null);
                }
                catch (Exception dialogEx)
                {
                    Log($"[App] 错误弹窗本身失败: {dialogEx.Message}");
                }
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Log($"[Task] 后台任务未捕获异常: {args.Exception}");
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Log($"[Fatal] AppDomain致命异常(IsTerminating={args.IsTerminating}): {ex}");
            };

            if (IsDebugMode)
            {
                if (!AttachConsole(-1))
                {
                    AllocConsole();
                }
            }

            try
            {
                Log("[启动] 初始化主题系统...");
                _themeManager!.Initialize();
                Log($"[主题] 当前主题: {ThemeManager.GetThemeDisplayName(_themeManager!.CurrentTheme)}");
            }
            catch (Exception ex)
            {
                Log($"[主题] 初始化失败: {ex.Message}");
            }

            Log("[启动] 显示登录窗口...");
            var loginWindow = _windowFactory!.CreateWindow<LoginWindow>();
            var loginResult = loginWindow.ShowDialog();

            if (loginResult != true)
            {
                Log("[启动] 用户取消登录，程序退出");
                Shutdown();
                return;
            }

            var scPassed = await scTask;
            if (!scPassed)
            {
                try
                {
                    var reason = ProtectionService.StartupBlockReason;
                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        reason = ProtectionService.NativeProtectIssue;
                    }

                    var message = string.IsNullOrWhiteSpace(reason)
                        ? "启动安全校验失败：关键文件可能缺失、被杀软隔离或被篡改。\n建议：将安装目录加入杀软白名单后重新安装覆盖。"
                        : reason;

                    try
                    {
                        GlobalToast.Error("启动校验失败", "检测到关键文件异常，程序将退出。请查看弹窗详情并按提示处理。");
                    }
                    catch { }

                    try
                    {
                        StandardDialog.ShowError(message, "启动校验失败", null);
                    }
                    catch { }

                    Log($"[启动] SC 校验失败: {message}");
                }
                catch (Exception ex)
                {
                    Log($"[启动] SC 失败提示异常: {ex.Message}");
                }

                await Task.Delay(new Random().Next(500, 2000));
                Environment.Exit(-1);
                return;
            }

            try
            {
                Log("[启动] 初始化服务器授权...");

                ServerAuthInitializer.Initialize();
                Log("[启动] 服务器授权已启动");

                ProtectionService.Initialize();
                Log("[启动] 后台服务已启动");
            }
            catch (Exception ex)
            {
                Log($"[启动] 服务器授权初始化失败: {ex.Message}");
                StandardDialog.ShowError("无法连接到授权服务器，请检查网络后重试。", "连接失败");
                Shutdown();
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(loginWindow.LoggedInUsername))
                {
                    _basicInfoSettings!.SwitchUser(loginWindow.LoggedInUsername);
                    _currentUserContext!.Refresh();
                }
            }
            catch (Exception ex)
            {
                Log($"[启动] 切换用户资料失败: {ex.Message}");
            }

            var bootstrapManager = CreateBootstrapTasks(e.Args);

            Log("[启动] 显示启动进度窗口...");
            var splashWindow = _windowFactory!.CreateWindow<SplashWindow>(bootstrapManager);
            var splashResult = splashWindow.ShowDialog();

            if (splashResult != true)
            {
                Log("[启动] 启动失败，程序退出");
                Shutdown();
                return;
            }

            Log("[启动] 所有任务完成，显示主窗口...");

            var mainWindow = _windowFactory!.CreateWindow<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            if (e.Args.Length > 0)
            {
                await ProcessCommandLineArgsAsync(e.Args);
            }

            Log("[启动] 程序启动完成");
            }
            catch (Exception ex)
            {
                Log($"[启动] 启动流程异常: {ex.Message}");
                Shutdown();
            }
        }

        private async void ReturnToLogin(string message)
        {
            if (_isReturningToLogin) return;
            _isReturningToLogin = true;

            try
            {
                Log($"[登录] 返回登录界面: {message}");

                ProtectionService.Stop();
                ServerAuthInitializer.Stop();

                if (_autoLockTimer != null)
                {
                    _autoLockTimer.Stop();
                    _autoLockTimer = null;
                }

                _authTokenManager?.ClearTokens();
                _serverAuthService?.ClearToken();

                try
                {
                    var chapterFlush = ServiceLocator.Get<TM.Framework.UI.Workspace.Services.CurrentChapterPersistenceService>().FlushPendingAsync();
                    await Task.WhenAny(chapterFlush, Task.Delay(1000));
                }
                catch { }
                try
                {
                    var guideFlush = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>().FlushAllAsync();
                    await Task.WhenAny(guideFlush, Task.Delay(5000));
                }
                catch { }

                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                MainWindow?.Close();
                MainWindow = null;
                try { ServiceLocator.Get<Framework.UI.Workspace.Services.PanelCommunicationService>().ClearAllSubscriptions(); } catch { }

                StandardDialog.ShowError(message, "需要重新登录");

                var loginWindow = _windowFactory!.CreateWindow<LoginWindow>();
                var loginResult = loginWindow.ShowDialog();

                if (loginResult != true)
                {
                    Log("[重新登录] 用户取消登录，退出程序");
                    Shutdown();
                    return;
                }

                Log($"[重新登录] 登录成功: {loginWindow.LoggedInUsername}");

                ServerAuthInitializer.Initialize();
                ProtectionService.Initialize();

                try
                {
                    if (!string.IsNullOrWhiteSpace(loginWindow.LoggedInUsername))
                    {
                        _basicInfoSettings?.SwitchUser(loginWindow.LoggedInUsername);
                        _currentUserContext?.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    Log($"[重新登录] 切换用户资料失败: {ex.Message}");
                }

                var reBootstrap = CreateBootstrapTasks(Array.Empty<string>());
                var reSplash = _windowFactory!.CreateWindow<SplashWindow>(reBootstrap);
                var reSplashResult = reSplash.ShowDialog();
                if (reSplashResult != true)
                {
                    Log("[重新登录] bootstrap 失败，退出程序");
                    Shutdown();
                    return;
                }

                var mainWindow = _windowFactory!.CreateWindow<MainWindow>();
                MainWindow = mainWindow;
                ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                mainWindow.Show();

                try { ServiceLocator.Get<TM.Services.Framework.SystemIntegration.TrayIconService>().UpdateMainWindow(mainWindow); } catch { }

                Log("[重新登录] 主窗口已重新显示");
            }
            catch (Exception ex)
            {
                Log($"[重新登录] 返回登录失败: {ex.Message}");
                StandardDialog.ShowError("返回登录失败，程序将退出。", "错误");
                Shutdown();
            }
            finally
            {
                _isReturningToLogin = false;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                try
                {
                    ServerAuthInitializer.Stop();
                }
                catch (Exception ex)
                {
                    Log($"[退出] 停止服务器授权失败: {ex.Message}");
                }

                try
                {
                    ProtectionService.Stop();
                }
                catch (Exception ex)
                {
                    Log($"[退出] 停止后台服务失败: {ex.Message}");
                }

                try
                {
                    if (_autoLockTimer != null)
                    {
                        _autoLockTimer.Stop();
                        _autoLockTimer = null;
                        Log("[AppLock] 自动锁定定时器已停止");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[AppLock] 停止定时器失败: {ex.Message}");
                }

                try
                {
                    try
                    {
                        Log("[CurrentChapterPersistence] 保存当前章节状态...");
                        var chapterFlushTask = ServiceLocator.Get<TM.Framework.UI.Workspace.Services.CurrentChapterPersistenceService>()
                            .FlushPendingAsync();
                        if (await Task.WhenAny(chapterFlushTask, Task.Delay(3000)) != chapterFlushTask)
                        {
                            Log("[CurrentChapterPersistence] 保存超时（3秒），放弃等待");
                        }
                        else
                        {
                            await chapterFlushTask;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[CurrentChapterPersistence] 保存失败: {ex.Message}");
                    }

                    Log("[GuideManager] 保存未写入的数据...");
                    var flushTask = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>().FlushOnExitAsync();
                    if (await Task.WhenAny(flushTask, Task.Delay(30000)) != flushTask)
                    {
                        Log("[GuideManager] 保存超时（30秒），放弃等待");
                    }
                    else
                    {
                        await flushTask;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[GuideManager] 保存数据失败: {ex.Message}");
                }

                try
                {
                    Log("[TrayIcon] 开始清理托盘图标...");
                    ServiceLocator.Get<TM.Services.Framework.SystemIntegration.TrayIconService>().Dispose();

                    await Task.Delay(100);

                    Log("[TrayIcon] 托盘图标服务已停止");
                }
                catch (Exception ex)
                {
                    Log($"[TrayIcon] 停止服务失败: {ex.Message}");
                }

                Log("");
                Log("[退出] 程序正常退出");
                Log($"退出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Log("========================================");

                try { ServiceLocator.TryGet<TM.Services.Framework.Settings.LogManager>()?.Flush(); } catch { }
            }
            catch (Exception ex)
            {
                Log($"[退出] 退出流程异常: {ex.Message}");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        private async Task ProcessCommandLineArgsAsync(string[] args)
        {
            try
            {
                if (args.Length == 0)
                    return;

                var firstArg = args[0];

                if (firstArg.StartsWith("TM://", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[命令行] 接收到URL协议调用: {firstArg}");

                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.UrlProtocolService.HandleUrlProtocol(firstArg);
                    }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    return;
                }

                switch (firstArg.ToLower())
                {
                    case "--debug":
                        Log("[命令行] 调试模式已启用");
                        break;

                    case "--minimized":
                        Log("[命令行] 启动模式：最小化到托盘");
                        MainWindow.Loaded += (s, e) =>
                        {
                            MainWindow.WindowState = WindowState.Minimized;
                            MainWindow.Hide();
                        };
                        break;

                    case "--delay":
                        if (args.Length > 1 && int.TryParse(args[1], out int delaySeconds))
                        {
                            Log($"[命令行] 延迟{delaySeconds}秒启动");
                            await Task.Delay(delaySeconds * 1000);
                        }
                        break;

                    case "--edit":
                        if (args.Length > 1)
                        {
                            var filePath = args[1];
                            Log($"[命令行] 编辑模式打开文件: {filePath}");

                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.FileTypeAssociationService.HandleFileOpen(filePath, true);
                            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        break;

                    case "--folder":
                        if (args.Length > 1)
                        {
                            var folderPath = args[1];
                            Log($"[命令行] 打开文件夹: {folderPath}");

                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.ContextMenuService.HandleContextMenuAction(folderPath, true);
                            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        break;

                    default:
                        if (File.Exists(firstArg))
                        {
                            Log($"[命令行] 打开文件: {firstArg}");

                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                var ext = Path.GetExtension(firstArg).ToLower();

                                if (ext == ".tm")
                                {
                                    TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.FileTypeAssociationService.HandleFileOpen(firstArg);
                                }
                                else
                                {
                                    TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.ContextMenuService.HandleContextMenuAction(firstArg, false);
                                }
                            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        else
                        {
                            Log($"[命令行] 未知参数: {firstArg}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"[命令行] 处理参数失败: {ex.Message}");
            }
        }

        private BootstrapManager CreateBootstrapTasks(string[] args)
        {
            var manager = new BootstrapManager();

            manager.AddTask(new BootstrapTask(
                "模块服务",
                "初始化依赖注入容器和模块服务",
                async () =>
                {
                    try
                    {
                        var serviceProvider = DependencyInjection.ConfigureServices();
                        Log("[DI] 依赖注入容器已初始化");

                        await DependencyInjection.InitializeServicesAsync(serviceProvider);
                        Log("[DI] 所有模块服务初始化完成");
                    }
                    catch (Exception ex)
                    {
                        Log($"[DI] 模块服务初始化失败: {ex.Message}");
                    }
                }
            ));

            manager.AddTask(new BootstrapTask("生成参数", "从本地存储加载生成参数（LayeredContextConfig）", () => Task.Run(async () =>
            {
                try
                {
                    await TM.Services.Modules.ProjectData.Implementations.LayeredContextConfig.InitializeFromStorageAsync();
                    Log("[生成参数] 生成参数已从本地存储加载");
                }
                catch (Exception ex) { Log($"[生成参数] 生成参数加载失败，使用默认值: {ex.Message}"); }
            })));

            manager.AddParallelBatch(
                new BootstrapTask("系统集成", "初始化Windows通知等系统集成功能", () => Task.Run(() =>
                {
                    try { InitializeSystemIntegration(); }
                    catch (Exception ex) { Log($"[SystemIntegration] 初始化失败: {ex.Message}"); }
                })),
                new BootstrapTask("代理配置", "加载应用内代理配置", () => Task.Run(() =>
                {
                    try { _ = ServiceLocator.Get<ProxyService>(); Log("[代理] 代理配置已加载"); }
                    catch (Exception ex) { Log($"[代理] 初始化失败: {ex.Message}"); }
                })),
                new BootstrapTask("字体配置", "加载UI和编辑器字体设置", () => Task.Run(() =>
                {
                    try
                    {
                        var fontConfig = FontManager.LoadConfiguration();
                        FontManager.ApplyUIFont(fontConfig.UIFont);
                        FontManager.ApplyEditorFont(fontConfig.EditorFont);
                        Log($"[字体] UI字体: {fontConfig.UIFont.FontFamily} {fontConfig.UIFont.FontSize}px");
                    }
                    catch (Exception ex) { Log($"[字体] 加载配置失败: {ex.Message}"); }
                })),
                new BootstrapTask("服务激活", "激活UI缩放、定时主题、系统跟随、文化区域等服务", () => Task.Run(() =>
                {
                    try
                    {
                        var uiRes = ServiceLocator.Get<Framework.Appearance.Animation.UIResolution.UIResolutionService>();
                        Log("[Bootstrap] UIResolutionService 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] UIResolution 初始化失败: {ex.Message}"); }

                    try
                    {
                        ServiceLocator.Get<Framework.Appearance.AutoTheme.TimeBased.TimeScheduleService>().Initialize();
                        Log("[Bootstrap] TimeScheduleService 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] TimeScheduleService 初始化失败: {ex.Message}"); }

                    try
                    {
                        ServiceLocator.Get<Framework.Appearance.AutoTheme.SystemFollow.SystemFollowController>().Initialize();
                        Log("[Bootstrap] SystemFollowController 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] SystemFollowController 初始化失败: {ex.Message}"); }

                    try
                    {
                        ServiceLocator.Get<Framework.User.Preferences.Locale.LocaleService>().ApplyAtStartup();
                        Log("[Bootstrap] LocaleService 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] LocaleService 初始化失败: {ex.Message}"); }
                }))
            );

            manager.AddParallelBatch(
                new BootstrapTask("数据对账", "检查并修复崩溃遗留的不一致数据", () => Task.Run(async () =>
                {
                    try
                    {
                        var reconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                        var result = await reconciler.ReconcileAsync();
                        if (result.HasRepairs)
                            Log($"[对账] 已自动修复: staging={result.StagingCleaned}, bak={result.BakCleaned}, 摘要={result.SummariesRepaired}");
                        else {}
                    }
                    catch (Exception ex) { Log($"[对账] 一致性检查失败: {ex.Message}"); }
                }))
            );

            manager.AddParallelBatch(
                new BootstrapTask("AI服务", "初始化AI核心服务和SK对话服务", () => Task.Run(() =>
                {
                    try
                    {
                        _ = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
                        Log("[AIService] AI核心服务已初始化");
                        _ = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
                        Log("[SKChatService] SK对话服务已初始化");
                    }
                    catch (Exception ex) { Log($"[AI服务] 初始化失败: {ex.Message}"); }
                })),
                new BootstrapTask("会话索引", "预热对话会话索引", () => Task.Run(() =>
                {
                    try
                    {
                        var sessions = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SessionManager>().GetAllSessions();
                        Log($"[预热] 会话索引已加载: {sessions.Count}个");
                        ServiceLocator.Get<UIStateCache>().SetSessionState(sessions.Count);
                    }
                    catch (Exception ex) { Log($"[预热] 会话索引预热失败: {ex.Message}"); }
                })),
                new BootstrapTask("章节索引", "预热章节列表与分类索引", () => Task.Run(async () =>
                {
                    try
                    {
                        var svc = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
                        var chapters = await svc.GetGeneratedChaptersAsync();
                        var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                        await volumeService.InitializeAsync();
                        var volumes = volumeService.GetAllVolumeDesigns();
                        Log($"[预热] 章节索引已加载: 分类{volumes.Count}个, 章节{chapters.Count}个");
                        ServiceLocator.Get<UIStateCache>().SetChapterState(volumes.Count, chapters.Count);
                        await ServiceLocator.Get<TM.Framework.UI.Workspace.Services.CurrentChapterPersistenceService>().RestoreAsync();
                        Log("[预热] 当前章节已恢复");
                    }
                    catch (Exception ex) { Log($"[预热] 章节索引预热失败: {ex.Message}"); }
                })),
                new BootstrapTask("登录历史", "记录本次登录信息", () => Task.Run(() =>
                {
                    try { ServiceLocator.Get<TM.Framework.User.Account.LoginHistory.LoginHistoryService>().RecordLogin(); }
                    catch (Exception ex) { Log($"[登录历史] 记录失败: {ex.Message}"); }
                })),
                new BootstrapTask("功能授权预热", "预热AI功能授权缓存（消除首次使用延迟）", async () =>
                {
                    try
                    {
                        await TM.Framework.Common.Services.ProtectionService.CheckFeatureAuthorizationAsync("writing.ai");
                        Log("[预热] AI功能授权缓存已就绪");
                    }
                    catch (Exception ex) { Log($"[预热] 功能授权预热失败: {ex.Message}"); }
                })
            );

            manager.AddParallelBatch(
                new BootstrapTask("模型库索引", "预热模型库索引（减少首次打开模型列表卡顿）", () => Task.Run(() =>
                {
                    try
                    {
                        var ai = ServiceLocator.Get<TM.Services.Framework.AI.Core.AIService>();
                        _ = ai.GetAllCategories();
                        _ = ai.GetAllProviders();
                        _ = ai.GetAllModels();
                        Log("[预热] AI模型库索引已预热");
                    }
                    catch (Exception ex) { Log($"[预热] AI模型库索引预热失败: {ex.Message}"); }
                })),
                new BootstrapTask("对话实体索引", "预热QueryRouting实体索引（消除对话首次自然语言解析卡顿）", async () =>
                {
                    try
                    {
                        await ServiceLocator.Get<TM.Services.Framework.AI.QueryRouting.QueryRoutingService>().SmartSearchAsync("__warmup__");
                        Log("[预热] 对话实体索引已就绪");
                    }
                    catch (Exception ex) { Log($"[预热] 对话实体索引预热失败: {ex.Message}"); }
                }),
                new BootstrapTask("向量搜索引擎", "预热本地向量搜索（消除首次@引用/语义搜索卡顿）", async () =>
                {
                    try
                    {
                        await ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.VectorSearchService>().InitializeAsync();
                        Log("[预热] 本地向量搜索引擎已就绪");
                    }
                    catch (Exception ex) { Log($"[预热] 向量搜索引擎预热失败: {ex.Message}"); }
                })
            );

            manager.AddParallelBatch(
                new BootstrapTask("UI状态", "完成UI状态预缓存", () => Task.Run(() =>
                {
                    ServiceLocator.Get<UIStateCache>().MarkWarmedUp();
                })),
                new BootstrapTask("密码保护", "初始化自动锁定功能", () =>
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                            _autoLockTimer.Tick += AutoLockTimer_Tick;
                            _autoLockTimer.Start();
                            Log("[AppLock] 自动锁定定时器已启动");
                        }
                        catch (Exception ex) { Log($"[AppLock] 初始化失败: {ex.Message}"); }
                        finally { tcs.TrySetResult(true); }
                    });
                    return tcs.Task;
                }),
                new BootstrapTask("内存管理", "启动内存优化服务", () => Task.Run(() =>
                {
                    try
                    {
                        var memService = ServiceLocator.Get<MemoryOptimizationService>();
                        memService.Start();

                        memService.RegisterCacheCleanup(() =>
                        {
                            try
                            {
                                ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>().CleanupExpiredCache();
                            }
                            catch { }
                            try
                            {
                                var sessionCache = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.SessionContextCache>();
                                var (cached, invalidated) = sessionCache.GetStats();
                                if (invalidated > 50 || cached > 200)
                                {
                                    sessionCache.Clear();
                                }
                            }
                            catch { }
                        });

                        Log("[内存管理] 内存优化服务已启动，已注册缓存清理回调");
                    }
                    catch (Exception ex) { Log($"[内存管理] 启动失败: {ex.Message}"); }
                }))
            );

            return manager;
        }

        private void AutoLockTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var appLockSettings = _appLockSettings!;
                if (appLockSettings.ShouldAutoLock())
                {
                    Log("[AppLock] 触发自动锁定");
                    appLockSettings.LockApp("自动锁定");
                    LockApplication();
                }
            }
            catch (Exception ex)
            {
                Log($"[AppLock] 自动锁定检查失败: {ex.Message}");
            }
        }

        public static void LockApplication()
        {
            Current?.Dispatcher.BeginInvoke(() =>
            {
                if (Current.MainWindow == null) return;

                Current.MainWindow.Opacity = 0.3;
                Current.MainWindow.IsEnabled = false;

                Log("[AppLock] 程序已锁定，等待解锁...");

                ShowUnlockDialog();
            });
        }

        private static void ShowUnlockDialog()
        {
            var password = StandardDialog.ShowInput("请输入密码解锁：", "程序已锁定");

            if (string.IsNullOrEmpty(password))
            {
                Log("[AppLock] 用户取消解锁，退出程序");
                Current?.Shutdown();
                return;
            }

            if (ServiceLocator.Get<AccountSecurityService>().VerifyPassword(password))
            {
                ServiceLocator.Get<AppLockSettings>().UnlockApp();
                if (Current?.MainWindow != null)
                {
                    Current.MainWindow.Opacity = 1.0;
                    Current.MainWindow.IsEnabled = true;
                }
                Log("[AppLock] 解锁成功");
                GlobalToast.Success("解锁成功", "程序已解锁");
            }
            else
            {
                Log("[AppLock] auth fail");
                GlobalToast.Error("密码错误", "请重新输入");
                Current?.Dispatcher.BeginInvoke(ShowUnlockDialog, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public static void Log(string message)
        {
            if (IsDebugMode)
            {
                Console.WriteLine(message);
            }

            if (LogManager.IsInitializing)
            {
                return;
            }

            var logger = ServiceLocator.TryGet<LogManager>();
            if (logger == null)
            {
                return;
            }

            logger.Log(message);
        }

        private static void InitializeSystemIntegration()
        {
            var settings = ServiceLocator.Get<SystemIntegrationSettings>();

            ApplyWindowsNotification(settings.EnableWindowsNotification);

            settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemIntegrationSettings.EnableWindowsNotification))
                {
                    ApplyWindowsNotification(settings.EnableWindowsNotification);
                }
            };

            Log("[SystemIntegration] 设置已加载并应用");
        }

        private static void ApplyWindowsNotification(bool enabled)
        {
            if (enabled)
            {
                WindowsNotificationService.Enable();
                Log("[SystemIntegration] Windows原生通知已启用");
            }
            else
            {
                WindowsNotificationService.Disable();
            }
        }
    }
}

