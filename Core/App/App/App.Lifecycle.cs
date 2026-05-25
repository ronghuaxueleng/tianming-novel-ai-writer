using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TM.Framework.Appearance.Font;
using TM.Framework.User.Security.PasswordProtection;
using TM.Framework.User.Account.PasswordSecurity.Services;
using TM.Framework.User.Account.Login.Bootstrap;
using TM.Framework.Notifications.SystemNotifications.SystemIntegration;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Services.Framework.Settings;
using TM.Services.Framework.SystemIntegration;

namespace TM
{
    public partial class App
    {
        private async Task RunExitCleanupAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                async Task SafeExitFlushChapterAsync()
                {
                    try
                    {
                        Log("[CurrentChapterPersistence] 保存当前章节状态...");
                        var chapterFlushTask = ServiceLocator.Get<TM.Framework.UI.Workspace.Services.CurrentChapterPersistenceService>()
                            .FlushPendingAsync();
                        if (await Task.WhenAny(chapterFlushTask, Task.Delay(3000, cancellationToken)) != chapterFlushTask)
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
                }
                async Task SafeExitFlushGuideAsync()
                {
                    try
                    {
                        Log("[GuideManager] 保存未写入的数据...");
                        var flushTask = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>().FlushOnExitAsync();
                        if (await Task.WhenAny(flushTask, Task.Delay(30000, cancellationToken)) != flushTask)
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
                }
                async Task SafeExitFlushValidationSummaryAsync()
                {
                    try
                    {
                        Log("[ValidationSummaryService] 保存未写入的数据...");
                        var flushTask = ServiceLocator.TryGet<TM.Services.Modules.ProjectData.Interfaces.IValidationSummaryService>()?.FlushPendingAsync() ?? Task.CompletedTask;
                        if (await Task.WhenAny(flushTask, Task.Delay(1000, cancellationToken)) != flushTask)
                        {
                            Log("[ValidationSummaryService] 保存超时（1秒），放弃等待");
                        }
                        else
                        {
                            await flushTask;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[ValidationSummaryService] 保存数据失败: {ex.Message}");
                    }
                }
                async Task SafeExitFlushUnifiedWindowAsync()
                {
                    try
                    {
                        Log("[UnifiedWindow] 保存窗口设置...");
                        await Task.WhenAny(TM.Framework.UI.Windows.UnifiedWindowSettings.FlushAsync(), Task.Delay(1000, cancellationToken));
                    }
                    catch (Exception ex) { Log($"[UnifiedWindow] 保存窗口设置失败: {ex.Message}"); }
                }
                await Task.WhenAll(SafeExitFlushChapterAsync(), SafeExitFlushGuideAsync(), SafeExitFlushValidationSummaryAsync(), SafeExitFlushUnifiedWindowAsync()).ConfigureAwait(false);

                try
                {
                    Log("[TrayIcon] 开始清理托盘图标...");
                    ServiceLocator.Get<TM.Services.Framework.SystemIntegration.TrayIconService>().Dispose();

                    await Task.Delay(100).ConfigureAwait(false);

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

                try { var lm = ServiceLocator.TryGet<TM.Services.Framework.Settings.LogManager>(); if (lm != null) lm.Flush(); } catch { }
            }
            catch (Exception ex)
            {
                Log($"[退出] 退出流程异常: {ex.Message}");
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
                                var ext = Path.GetExtension(firstArg);

                                if (ext.Equals(".tm", StringComparison.OrdinalIgnoreCase))
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

            if (TM.Framework.SystemSettings.DataBackup.Services.ProjectBackupService.HasPendingRestore())
            {
                Log("[启动] 检测到待执行的备份恢复任务，已加入启动队列");
                manager.AddTask(new BootstrapTask(
                    "数据恢复",
                    "正在从备份恢复您的数据...",
                    async () =>
                    {
                        bool restored = false;
                        string failMessage = string.Empty;
                        try
                        {
                            var backupSvc = new TM.Framework.SystemSettings.DataBackup.Services.ProjectBackupService();
                            restored = await backupSvc.TryConsumePendingRestoreAsync();
                        }
                        catch (Exception ex)
                        {
                            failMessage = ex.Message;
                            Log($"[启动] 备份恢复抛出异常: {ex.Message}");
                        }

                        if (restored)
                        {
                            Log("[启动] 备份恢复完成，继续启动其他任务");
                            return;
                        }

                        if (TM.Framework.SystemSettings.DataBackup.Services.ProjectBackupService.HasPendingRestore())
                        {
                            Log("[启动] 备份恢复未完成且状态文件仍存在，终止本次启动以保护数据一致性");
                            try
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    StandardDialog.ShowError(
                                        "备份恢复未完成，应用即将退出以保护数据一致性。\n\n" +
                                        (string.IsNullOrEmpty(failMessage) ? string.Empty : $"原因：{failMessage}\n\n") +
                                        "原始数据已自动回滚到恢复前的状态。\n" +
                                        "请检查备份文件是否完好，然后重新打开应用以重试。",
                                        "恢复未完成");
                                });
                            }
                            catch (Exception dlgEx) { Log($"[启动] 显示恢复失败弹窗异常: {dlgEx.Message}"); }

                            Environment.Exit(-1);
                        }
                        else
                        {
                            Log("[启动] 备份恢复任务已自动清理（无需中止启动）");
                            try
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    StandardDialog.ShowWarning(
                                        "您之前安排的备份恢复任务已被取消。\n\n" +
                                        "可能原因：备份文件已被移动/删除、状态文件损坏，或多次尝试失败已自动放弃。\n\n" +
                                        "应用将以恢复前的数据继续启动。",
                                        "恢复已取消");
                                });
                            }
                            catch (Exception dlgEx) { Log($"[启动] 显示恢复取消弹窗异常: {dlgEx.Message}"); }
                        }
                    }
                ));
            }

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
                new BootstrapTask("服务激活", "激活UI缩放、定时主题、系统跟随、文化区域等服务", () => Task.Run(async () =>
                {
                    try
                    {
                        var uiRes = ServiceLocator.Get<Framework.Appearance.Animation.UIResolution.UIResolutionService>();
                        Log("[Bootstrap] UIResolutionService 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] UIResolution 初始化失败: {ex.Message}"); }

                    try
                    {
                        await ServiceLocator.Get<Framework.Appearance.AutoTheme.TimeBased.TimeScheduleService>().InitializeAsync();
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
                        await ServiceLocator.Get<Framework.User.Preferences.Locale.LocaleService>().ApplyAtStartupAsync();
                        Log("[Bootstrap] LocaleService 已激活");
                    }
                    catch (Exception ex) { Log($"[Bootstrap] LocaleService 初始化失败: {ex.Message}"); }
                })),
                new BootstrapTask("数据对账", "检查并修复崩溃遗留的不一致数据", () => Task.Run(async () =>
                {
                    try
                    {
                        var reconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                        var result = await reconciler.ReconcileAsync();
                        if (result.HasRepairs)
                            Log($"[对账] 已自动修复: staging={result.StagingCleaned}, bak={result.BakCleaned}, 摘要={result.SummariesRepaired}");
                        else { }
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
                        try { _ = TM.Services.Framework.AI.SemanticKernel.ChatModeSettings.MaxTokensLadderTop; } catch { }
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
                        var volumes = volumeService.GetAllVolumeDesigns()
                            .ToList();
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
                new BootstrapTask("功能授权预热", "预热AI功能授权缓存（消除首次使用延迟）", () =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await TM.Framework.Common.Services.ProtectionService.CheckFeatureAuthorizationAsync("writing.ai");
                            Log("[预热] AI功能授权缓存已就绪");
                        }
                        catch (Exception ex) { Log($"[预热] 功能授权预热失败: {ex.Message}"); }
                    });
                    return Task.CompletedTask;
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
                new BootstrapTask("对话上下文服务", "预热章节生成桥接和引用解析（消除首次对话输入卡顿）", () => Task.Run(() =>
                {
                    try { ServiceLocator.Get<TM.Framework.UI.Workspace.Services.ChapterGenerationBridge>(); } catch { }
                    try { ServiceLocator.Get<TM.Framework.UI.Workspace.Services.ReferenceParser>(); } catch { }
                    Log("[预热] 对话上下文服务已就绪");
                }))
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

        private static async void ShowUnlockDialog()
        {
            try
            {
                var password = StandardDialog.ShowInput("请输入密码解锁：", "程序已锁定");

                if (string.IsNullOrEmpty(password))
                {
                    Log("[AppLock] 用户取消解锁，退出程序");
                    Current?.Shutdown();
                    return;
                }

                var verified = await ServiceLocator.Get<AccountSecurityService>().VerifyPasswordAsync(password).ConfigureAwait(true);

                if (verified)
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
            catch (Exception ex)
            {
                Log($"[AppLock] 解锁异常: {ex.Message}");
                GlobalToast.Error("解锁失败", "发生错误，请重试");
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

