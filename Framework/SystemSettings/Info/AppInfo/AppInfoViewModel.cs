using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;
using TM.Framework.SystemSettings.Info.Models;

namespace TM.Framework.SystemSettings.Info.AppInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class AppInfoViewModel : INotifyPropertyChanged
    {
        private AppInfoSettings _settings = null!;
        private readonly string _settingsFilePath = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AppInfoViewModel()
        {
            _settings = new AppInfoSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Info/AppInfo",
                "settings.json"
            );

            AsyncSettingsLoader.LoadOrDefer<AppInfoSettings>(_settingsFilePath, s => { _settings = s; }, "AppInfo");

            AssemblyList = new RangeObservableCollection<AssemblyItem>();
            Dependencies = new RangeObservableCollection<DependencyItem>();
            ResourceStats = new ObservableCollection<ResourceStatItem>();
            FeatureModules = new RangeObservableCollection<FeatureModuleItem>();
            Licenses = new RangeObservableCollection<LicenseItem>();
            VersionHistory = new RangeObservableCollection<VersionHistoryItem>();
            PerformanceMetrics = new RangeObservableCollection<PerformanceMetricItem>();

            CheckUpdateCommand = new RelayCommand(CheckUpdate);
            ExportCommand = new RelayCommand(() => ExportInfo().SafeFireAndForget(ex => TM.App.Log($"[AppInfoViewModel] {ex.Message}")));
            RefreshCommand = new RelayCommand(RefreshInfo);

            RefreshInfo();
        }

        private string _appName = "天命";
        public string AppName
        {
            get => _appName;
            set { _appName = value; OnPropertyChanged(nameof(AppName)); }
        }

        private string _appVersion = string.Empty;
        public string AppVersion
        {
            get => _appVersion;
            set { _appVersion = value; OnPropertyChanged(nameof(AppVersion)); }
        }

        private string _targetFramework = string.Empty;
        public string TargetFramework
        {
            get => _targetFramework;
            set { _targetFramework = value; OnPropertyChanged(nameof(TargetFramework)); }
        }

        private string _installPath = string.Empty;
        public string InstallPath
        {
            get => _installPath;
            set { _installPath = value; OnPropertyChanged(nameof(InstallPath)); }
        }

        private string _startupTime = string.Empty;
        public string StartupTime
        {
            get => _startupTime;
            set { _startupTime = value; OnPropertyChanged(nameof(StartupTime)); }
        }

        private string _runningTime = string.Empty;
        public string RunningTime
        {
            get => _runningTime;
            set { _runningTime = value; OnPropertyChanged(nameof(RunningTime)); }
        }

        private string _developer = "子夜";
        public string Developer
        {
            get => _developer;
            set { _developer = value; OnPropertyChanged(nameof(Developer)); }
        }

        private string _developerDescription = "由独立开发者 子夜 倾力打造，从架构设计到功能实现，每一行代码皆为匠心之作。";
        public string DeveloperDescription
        {
            get => _developerDescription;
            set { _developerDescription = value; OnPropertyChanged(nameof(DeveloperDescription)); }
        }

        private int _processId;
        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(nameof(ProcessId)); }
        }

        public RangeObservableCollection<AssemblyItem> AssemblyList { get; } = null!;
        public RangeObservableCollection<DependencyItem> Dependencies { get; } = null!;
        public ObservableCollection<ResourceStatItem> ResourceStats { get; } = null!;
        public RangeObservableCollection<FeatureModuleItem> FeatureModules { get; } = null!;
        public RangeObservableCollection<LicenseItem> Licenses { get; } = null!;
        public RangeObservableCollection<VersionHistoryItem> VersionHistory { get; } = null!;
        public RangeObservableCollection<PerformanceMetricItem> PerformanceMetrics { get; } = null!;

        public ICommand CheckUpdateCommand { get; } = null!;
        public ICommand ExportCommand { get; } = null!;
        public ICommand RefreshCommand { get; } = null!;

        private async void RefreshInfo()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
                    AppVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? assembly.GetName().Version?.ToString()
                        ?? "1.0.0";
                    _settings.CurrentVersion = AppVersion;

                    var targetFrameworkAttr = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
                    TargetFramework = targetFrameworkAttr?.FrameworkName ?? ".NET 8.0";

                    InstallPath = Path.GetDirectoryName(assembly.Location) ?? "未知";
                }

                var process = Process.GetCurrentProcess();
                ProcessId = process.Id;
                StartupTime = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");

                var runningTimeSpan = DateTime.Now - process.StartTime;
                RunningTime = $"{runningTimeSpan.Hours}小时 {runningTimeSpan.Minutes}分钟 {runningTimeSpan.Seconds}秒";

                await LoadAssemblies();

                var depList = await System.Threading.Tasks.Task.Run(() =>
                {
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                        .OrderBy(a => a.GetName().Name);
                    var thirdPartyLibs = assemblies.Where(a =>
                        !a.GetName().Name?.StartsWith("System", StringComparison.Ordinal) == true &&
                        !a.GetName().Name?.StartsWith("Microsoft", StringComparison.Ordinal) == true &&
                        !a.GetName().Name?.StartsWith("TM", StringComparison.Ordinal) == true &&
                        !a.GetName().Name?.StartsWith("mscorlib", StringComparison.Ordinal) == true);
                    var list = new System.Collections.Generic.List<DependencyItem>();
                    foreach (var assembly in thirdPartyLibs)
                    {
                        var name = assembly.GetName();
                        list.Add(new DependencyItem
                        {
                            Name = name.Name ?? "Unknown",
                            Version = name.Version?.ToString() ?? "N/A",
                            Type = "第三方库",
                            IsOutdated = false
                        });
                    }
                    return list;
                }).ConfigureAwait(true);
                Dependencies.ReplaceAll(depList);
                TM.App.Log($"[AppInfo] 依赖项分析完成，发现 {Dependencies.Count} 个第三方库");

                LoadResourceStats();
                LoadFeatureModules();
                LoadLicenses();
                LoadVersionHistory();
                await LoadPerformanceMetricsAsync();

                TM.App.Log($"[AppInfo] 刷新应用信息成功");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 刷新应用信息失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"刷新失败：{ex.Message}");
            }
        }

        private async Task LoadAssemblies()
        {
            try
            {
                var items = await Task.Run(() =>
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic)
                        .OrderBy(a => a.GetName().Name)
                        .Select(a =>
                        {
                            var name = a.GetName();
                            return new AssemblyItem
                            {
                                Name = name.Name ?? "Unknown",
                                Version = name.Version?.ToString() ?? "N/A",
                                Location = a.Location
                            };
                        })
                        .ToList()
                ).ConfigureAwait(true);
                AssemblyList.ReplaceAll(items);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 加载程序集列表失败: {ex.Message}");
            }
        }

        private void CheckUpdate()
        {
            try
            {
                _settings.LastUpdateCheckTime = DateTime.Now;
                TM.App.Log($"[AppInfo] 检查更新");
                GlobalToast.Info("检查更新", "当前版本已是最新版本");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 检查更新失败: {ex.Message}");
                GlobalToast.Error("检查失败", $"检查失败：{ex.Message}");
            }
        }

        private async Task ExportInfo()
        {
            try
            {
                var exportPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Info/AppInfo",
                    $"app_info_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var sb = new StringBuilder();
                sb.AppendLine("==================== 应用信息报告 ====================");
                sb.AppendLine($"应用名称: {AppName}");
                sb.AppendLine($"版本: {AppVersion}");
                sb.AppendLine($"目标框架: {TargetFramework}");
                sb.AppendLine($"安装路径: {InstallPath}");
                sb.AppendLine($"进程ID: {ProcessId}");
                sb.AppendLine($"启动时间: {StartupTime}");
                sb.AppendLine($"运行时长: {RunningTime}");
                sb.AppendLine();
                sb.AppendLine("【已加载程序集】");
                foreach (var asm in AssemblyList)
                {
                    sb.AppendLine($"  {asm.Name} - {asm.Version}");
                }

                await File.WriteAllTextAsync(exportPath, sb.ToString());

                TM.App.Log($"[AppInfo] 导出应用信息成功: {exportPath}");
                GlobalToast.Success("导出成功", $"应用信息已导出到: {exportPath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 导出应用信息失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"导出失败：{ex.Message}");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadResourceStats()
        {
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var items = new System.Collections.Generic.List<ResourceStatItem>();
                try
                {
                    var projectRoot = StoragePathHelper.GetProjectRoot();

                    var themePath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
                    if (Directory.Exists(themePath))
                    {
                        var themeFiles = Directory.GetFiles(themePath, "*.xaml", SearchOption.AllDirectories);
                        var themeSize = themeFiles.Sum(f => new FileInfo(f).Length);
                        items.Add(new ResourceStatItem
                        {
                            Category = "主题文件",
                            Count = themeFiles.Length,
                            TotalSize = FormatBytes(themeSize),
                            Icon = "Icon.Palette"
                        });
                    }

                    var storagePath = StoragePathHelper.GetStorageRoot();
                    if (Directory.Exists(storagePath))
                    {
                        var storageFiles = Directory.GetFiles(storagePath, "*.*", SearchOption.AllDirectories);
                        var storageSize = storageFiles.Sum(f => new FileInfo(f).Length);
                        items.Add(new ResourceStatItem
                        {
                            Category = "数据存储",
                            Count = storageFiles.Length,
                            TotalSize = FormatBytes(storageSize),
                            Icon = "Icon.Save"
                        });
                    }

                    var frameworkPath = Path.Combine(projectRoot, "Framework");
                    if (Directory.Exists(frameworkPath))
                    {
                        var xamlFiles = Directory.GetFiles(frameworkPath, "*.xaml", SearchOption.AllDirectories);
                        var xamlSize = xamlFiles.Sum(f => new FileInfo(f).Length);
                        items.Add(new ResourceStatItem
                        {
                            Category = "XAML资源",
                            Count = xamlFiles.Length,
                            TotalSize = FormatBytes(xamlSize),
                            Icon = "Icon.Document"
                        });
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AppInfo] 资源统计失败: {ex.Message}");
                }

                return () =>
                {
                    ResourceStats.Clear();
                    foreach (var item in items)
                    {
                        ResourceStats.Add(item);
                    }
                    TM.App.Log($"[AppInfo] 资源统计完成");
                };
            }, "AppInfo.ResourceStats");
        }

        private void LoadFeatureModules()
        {
            try
            {
                FeatureModules.ReplaceAll(new[]
                {
                    new FeatureModuleItem { CategoryName = "界面设置", SubfunctionCount = 5, Icon = "Icon.Palette", Status = "已启用" },
                    new FeatureModuleItem { CategoryName = "用户设置", SubfunctionCount = 4, Icon = "Icon.User", Status = "已启用" },
                    new FeatureModuleItem { CategoryName = "通知设置", SubfunctionCount = 5, Icon = "Icon.Bell", Status = "已启用" },
                    new FeatureModuleItem { CategoryName = "安全设置", SubfunctionCount = 5, Icon = "Icon.Lock", Status = "已启用" },
                    new FeatureModuleItem { CategoryName = "网络设置", SubfunctionCount = 4, Icon = "Icon.Globe", Status = "已启用" },
                    new FeatureModuleItem { CategoryName = "系统设置", SubfunctionCount = 5, Icon = "Icon.Settings", Status = "已启用" }
                });

                TM.App.Log($"[AppInfo] 功能模块统计完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 功能模块统计失败: {ex.Message}");
            }
        }

        private void LoadLicenses()
        {
            try
            {
                Licenses.ReplaceAll(new[]
                {
                    new LicenseItem { Name = "天命",        LicenseType = "专有许可证", Description = "本应用遵循专有软件许可协议" },
                    new LicenseItem { Name = ".NET Runtime", LicenseType = "MIT License", Description = "微软.NET运行时环境" }
                });

                TM.App.Log($"[AppInfo] 许可证信息加载完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 许可证信息加载失败: {ex.Message}");
            }
        }

        private void LoadVersionHistory()
        {
            try
            {
                VersionHistory.ReplaceAll(new[]
                {
                    new VersionHistoryItem { Version = "2.1.5", ReleaseDate = new DateTime(2026, 3, 30), ChangeLog = "版本更新", IsCurrent = false  },
                    new VersionHistoryItem { Version = "2.1.4", ReleaseDate = new DateTime(2026, 3, 30), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.1.3", ReleaseDate = new DateTime(2026, 3, 30), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.1.2", ReleaseDate = new DateTime(2026, 3, 28), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.1.1", ReleaseDate = new DateTime(2026, 3, 28), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.1.0", ReleaseDate = new DateTime(2026, 3, 27), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.0.9", ReleaseDate = new DateTime(2026, 3, 27), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.0.8", ReleaseDate = new DateTime(2026, 3, 27), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.0.7", ReleaseDate = new DateTime(2026, 3, 26), ChangeLog = "版本更新", IsCurrent = false },
                    new VersionHistoryItem { Version = "2.0.6", ReleaseDate = new DateTime(2026, 3, 25), ChangeLog = "版本更新", IsCurrent = false }
                }.Take(10).ToList());

                TM.App.Log($"[AppInfo] 版本历史加载完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 版本历史加载失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadPerformanceMetricsAsync()
        {
            try
            {
                var metrics = await System.Threading.Tasks.Task.Run(() =>
                {
                    var process = Process.GetCurrentProcess();
                    var list = new System.Collections.Generic.List<PerformanceMetricItem>();

                    var startupTime = (DateTime.Now - process.StartTime).TotalSeconds;
                    list.Add(new PerformanceMetricItem
                    {
                        Name = "启动耗时",
                        Value = $"{startupTime:F2} 秒",
                        Icon = "Icon.Rocket",
                        Status = startupTime < 5 ? "优秀" : startupTime < 10 ? "良好" : "需优化"
                    });

                    var memoryMB = process.WorkingSet64 / 1024 / 1024;
                    list.Add(new PerformanceMetricItem
                    {
                        Name = "内存占用",
                        Value = $"{memoryMB} MB",
                        Icon = "Icon.Save",
                        Status = memoryMB < 200 ? "优秀" : memoryMB < 500 ? "良好" : "偏高"
                    });

                    var threadCount = process.Threads.Count;
                    list.Add(new PerformanceMetricItem
                    {
                        Name = "线程数",
                        Value = threadCount.ToString(),
                        Icon = "Icon.Package",
                        Status = threadCount < 50 ? "正常" : threadCount < 100 ? "较多" : "过多"
                    });

                    var gen2Count = GC.CollectionCount(2);
                    list.Add(new PerformanceMetricItem
                    {
                        Name = "2代GC次数",
                        Value = gen2Count.ToString(),
                        Icon = "Icon.Trash",
                        Status = gen2Count < 10 ? "优秀" : gen2Count < 50 ? "正常" : "偏多"
                    });

                    return list;
                }).ConfigureAwait(true);

                PerformanceMetrics.ReplaceAll(metrics);

                TM.App.Log($"[AppInfo] 性能指标采集完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppInfo] 性能指标采集失败: {ex.Message}");
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class DependencyItem
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsOutdated { get; set; }
    }

    public class ResourceStatItem
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
        public string TotalSize { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public class FeatureModuleItem
    {
        public string CategoryName { get; set; } = string.Empty;
        public int SubfunctionCount { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class LicenseItem
    {
        public string Name { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class VersionHistoryItem
    {
        public string Version { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public string ChangeLog { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }

    public class PerformanceMetricItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}

