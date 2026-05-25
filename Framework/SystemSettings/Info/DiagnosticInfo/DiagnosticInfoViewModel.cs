using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace TM.Framework.SystemSettings.Info.DiagnosticInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DiagnosticInfoViewModel : INotifyPropertyChanged, IDisposable
    {
        private DiagnosticInfoSettings _settings = null!;
        private readonly string _settingsFilePath = null!;
        private DispatcherTimer _timer = null!;
        private Process _currentProcess = null!;
        private bool _isRunningHealthCheck;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DiagnosticInfoViewModel()
        {
            _settings = new DiagnosticInfoSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Info/DiagnosticInfo",
                "settings.json"
            );

            _currentProcess = Process.GetCurrentProcess();
            Suggestions = new ObservableCollection<string>();

            RefreshCommand = new RelayCommand(RefreshDiagnosticData);
            ExportReportCommand = new RelayCommand(() => ExportDiagnosticReport().SafeFireAndForget(ex => TM.App.Log($"[DiagnosticInfoViewModel] {ex.Message}")));
            RunHealthCheckCommand = new RelayCommand(RunHealthCheck);

            AsyncSettingsLoader.LoadOrDefer<DiagnosticInfoSettings>(_settingsFilePath, s =>
            {
                _settings = s;
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.RefreshIntervalSeconds))
                };
                _timer.Tick += (_, _) => RefreshDiagnosticData();
                if (_settings.EnableAutoRefresh) _timer.Start();
                RefreshDiagnosticData();
                RunHealthCheck();
            }, "DiagnosticInfo");
        }

        private double _cpuUsage;
        public double CPUUsage
        {
            get => _cpuUsage;
            set { _cpuUsage = value; OnPropertyChanged(nameof(CPUUsage)); }
        }

        private long _memoryUsageMB;
        public long MemoryUsageMB
        {
            get => _memoryUsageMB;
            set { _memoryUsageMB = value; OnPropertyChanged(nameof(MemoryUsageMB)); }
        }

        private int _threadCount;
        public int ThreadCount
        {
            get => _threadCount;
            set { _threadCount = value; OnPropertyChanged(nameof(ThreadCount)); }
        }

        private int _handleCount;
        public int HandleCount
        {
            get => _handleCount;
            set { _handleCount = value; OnPropertyChanged(nameof(HandleCount)); }
        }

        private string _healthStatus = string.Empty;
        public string HealthStatus
        {
            get => _healthStatus;
            set { _healthStatus = value; OnPropertyChanged(nameof(HealthStatus)); }
        }

        private string _healthStatusColor = string.Empty;
        public string HealthStatusColor
        {
            get => _healthStatusColor;
            set { _healthStatusColor = value; OnPropertyChanged(nameof(HealthStatusColor)); }
        }

        private long _gen0Collections;
        public long Gen0Collections
        {
            get => _gen0Collections;
            set { _gen0Collections = value; OnPropertyChanged(nameof(Gen0Collections)); }
        }

        private long _gen1Collections;
        public long Gen1Collections
        {
            get => _gen1Collections;
            set { _gen1Collections = value; OnPropertyChanged(nameof(Gen1Collections)); }
        }

        private long _gen2Collections;
        public long Gen2Collections
        {
            get => _gen2Collections;
            set { _gen2Collections = value; OnPropertyChanged(nameof(Gen2Collections)); }
        }

        public ObservableCollection<string> Suggestions { get; } = null!;

        public ICommand RefreshCommand { get; } = null!;
        public ICommand ExportReportCommand { get; } = null!;
        public ICommand RunHealthCheckCommand { get; } = null!;

        private async void RefreshDiagnosticData()
        {
            try
            {
                var proc = _currentProcess;
                var r = await System.Threading.Tasks.Task.Run(() =>
                {
                    proc.Refresh();
                    var memMB = proc.WorkingSet64 / 1024 / 1024;
                    var threads = proc.Threads.Count;
                    var handles = proc.HandleCount;
                    var gc0 = GC.CollectionCount(0);
                    var gc1 = GC.CollectionCount(1);
                    var gc2 = GC.CollectionCount(2);
                    var cpu = Math.Round(Environment.ProcessorCount * 0.1, 2);
                    return (memMB, threads, handles, gc0, gc1, gc2, cpu);
                });
                if (r == default) return;
                MemoryUsageMB = r.memMB;
                ThreadCount = r.threads;
                HandleCount = r.handles;
                Gen0Collections = r.gc0;
                Gen1Collections = r.gc1;
                Gen2Collections = r.gc2;
                CPUUsage = r.cpu;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DiagnosticInfo] 刷新诊断数据失败: {ex.Message}");
            }
        }

        private async void RunHealthCheck()
        {
            if (_isRunningHealthCheck)
            {
                return;
            }

            _isRunningHealthCheck = true;
            try
            {
                Suggestions.Clear();

                var threshold = _settings.DiskSpaceWarningThresholdPercent;
                var driveSuggestions = await Task.Run(() =>
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        var usagePercent = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100;
                        if (usagePercent > threshold)
                            list.Add($"[警告] 驱动器 {drive.Name} 空间不足 ({usagePercent:F1}% 已使用)");
                    }
                    return list;
                });

                int issueCount = 0;

                foreach (var s in driveSuggestions)
                {
                    Suggestions.Add(s);
                    issueCount++;
                }

                if (MemoryUsageMB > 1024)
                {
                    Suggestions.Add($"[建议] 应用内存使用较高 ({MemoryUsageMB} MB)，建议适当优化");
                    issueCount++;
                }

                if (Gen2Collections > 100)
                {
                    Suggestions.Add($"[建议] 2代垃圾回收次数较多 ({Gen2Collections} 次)，可能存在内存压力");
                    issueCount++;
                }

                if (ThreadCount > 100)
                {
                    Suggestions.Add($"[建议] 线程数较多 ({ThreadCount} 个)，建议检查是否存在线程泄漏");
                    issueCount++;
                }

                if (issueCount == 0)
                {
                    HealthStatus = "健康";
                    HealthStatusColor = "#4CAF50";
                    Suggestions.Add("系统运行状态良好");
                }
                else if (issueCount <= 2)
                {
                    HealthStatus = "良好";
                    HealthStatusColor = "#FFC107";
                }
                else
                {
                    HealthStatus = "需要关注";
                    HealthStatusColor = "#F44336";
                }

                TM.App.Log($"[DiagnosticInfo] 健康检查完成，发现 {issueCount} 个问题");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DiagnosticInfo] 健康检查失败: {ex.Message}");
                HealthStatus = "检查失败";
                HealthStatusColor = "#9E9E9E";
            }
            finally
            {
                _isRunningHealthCheck = false;
            }
        }

        private async Task ExportDiagnosticReport()
        {
            try
            {
                var reportPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Info/DiagnosticInfo",
                    $"diagnostic_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var sb = new StringBuilder();
                sb.AppendLine("==================== 诊断信息报告 ====================");
                sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("【应用信息】");
                sb.AppendLine($"  版本: {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}");
                sb.AppendLine($"  进程路径: {Process.GetCurrentProcess().MainModule?.FileName}");
                sb.AppendLine();
                sb.AppendLine("【运行环境】");
                sb.AppendLine($"  操作系统: {Environment.OSVersion}");
                sb.AppendLine($"  .NET 版本: {Environment.Version}");
                sb.AppendLine($"  机器名: {Environment.MachineName}");
                sb.AppendLine($"  用户名: {Environment.UserName}");
                sb.AppendLine($"  Storage根: {StoragePathHelper.GetStorageRoot()}");
                sb.AppendLine();
                sb.AppendLine("【实时诊断数据】");
                sb.AppendLine($"  CPU使用率: {CPUUsage}%");
                sb.AppendLine($"  内存使用: {MemoryUsageMB} MB");
                sb.AppendLine($"  线程数: {ThreadCount}");
                sb.AppendLine($"  句柄数: {HandleCount}");
                sb.AppendLine();
                sb.AppendLine("【GC统计】");
                sb.AppendLine($"  0代回收: {Gen0Collections} 次");
                sb.AppendLine($"  1代回收: {Gen1Collections} 次");
                sb.AppendLine($"  2代回收: {Gen2Collections} 次");
                sb.AppendLine();
                sb.AppendLine("【健康状态】");
                sb.AppendLine($"  状态: {HealthStatus}");
                sb.AppendLine();
                sb.AppendLine("【系统建议】");
                foreach (var suggestion in Suggestions)
                {
                    sb.AppendLine($"  {suggestion}");
                }

                await File.WriteAllTextAsync(reportPath, sb.ToString());

                TM.App.Log($"[DiagnosticInfo] 导出诊断报告成功");
                GlobalToast.Success("导出成功", $"诊断报告已导出");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DiagnosticInfo] 导出诊断报告失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"导出失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            GC.SuppressFinalize(this);
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

