using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Logging.LogOutput;

namespace TM.Framework.SystemSettings.Logging.LogRotation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LogRotationViewModel : INotifyPropertyChanged
    {
        private LogRotationSettings _settings = null!;
        private readonly string _settingsFilePath = null!;
        private readonly string _historyFilePath = null!;
        private readonly string _logOutputSettingsPath = null!;
        private LogOutputSettings _outputSettings = null!;
        private int _currentLogFilesCount;
        private long _currentLogsTotalSizeMB;
        private StorageSpaceInfo _storageInfo = null!;
        private RotationPrediction _prediction = null!;
        private readonly DispatcherTimer _monitoringTimer = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LogRotationViewModel()
        {
            _settings = new LogRotationSettings();
            _outputSettings = new LogOutputSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogRotation",
                "settings.json"
            );
            _historyFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogRotation",
                "history.json"
            );
            _logOutputSettingsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogOutput",
                "settings.json"
            );

            RotationHistories = new ObservableCollection<RotationHistory>();
            CleanupRecommendations = new ObservableCollection<CleanupRecommendation>();

            AsyncSettingsLoader.LoadOrDefer<LogRotationSettings>(_settingsFilePath, s =>
            {
                _settings = s;
                OnAllPropertiesChanged();
                RefreshStatistics();
                CheckStorageSpace();
                UpdatePrediction();
            }, "LogRotation");

            AsyncSettingsLoader.LoadOrDefer<LogOutputSettings>(_logOutputSettingsPath, s =>
            {
                _outputSettings = s;
                RefreshStatistics();
                CheckStorageSpace();
            }, "LogRotation.LogOutput");

            AsyncSettingsLoader.LoadOrDefer<List<RotationHistory>>(_historyFilePath, histories =>
            {
                RotationHistories.Clear();
                foreach (var history in histories.OrderByDescending(h => h.RotationTime).Take(50))
                {
                    RotationHistories.Add(history);
                }
            }, "LogRotation.History");

            _monitoringTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _monitoringTimer.Tick += (s, e) =>
            {
                CheckStorageSpace();
                UpdatePrediction();
            };
            _monitoringTimer.Start();

            RotationTypes = new List<RotationType>
            {
                RotationType.BySize,
                RotationType.ByTime,
                RotationType.Hybrid
            };

            TimeIntervals = new List<TimeInterval>
            {
                TimeInterval.Hourly,
                TimeInterval.Daily,
                TimeInterval.Weekly,
                TimeInterval.Monthly
            };

            CompressionTypes = new List<CompressionType>
            {
                CompressionType.None,
                CompressionType.ZIP,
                CompressionType.GZIP
            };

            CleanupStrategies = new List<CleanupStrategy>
            {
                CleanupStrategy.ByCount,
                CleanupStrategy.ByTime,
                CleanupStrategy.BySize
            };

            FileNamingPatterns = new List<FileNamingPattern>
            {
                FileNamingPattern.Timestamp,
                FileNamingPattern.Sequential
            };

            SaveCommand = new RelayCommand(SaveSettings);
            ManualRotateCommand = new RelayCommand(ManualRotate);
            CleanupCommand = new RelayCommand(Cleanup);
            RefreshStatsCommand = new RelayCommand(RefreshStatistics);
            ViewHistoryCommand = new RelayCommand(ViewHistory);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            ExportHistoryCommand = new RelayCommand(ExportHistory);
            CheckStorageCommand = new RelayCommand(CheckStorageSpace);
            GenerateCleanupRecommendationsCommand = new RelayCommand(GenerateCleanupRecommendations);
        }

        public RotationType RotationType
        {
            get => _settings.RotationType;
            set { _settings.RotationType = value; OnPropertyChanged(nameof(RotationType)); }
        }

        public bool EnableSizeRotation
        {
            get => _settings.EnableSizeRotation;
            set { _settings.EnableSizeRotation = value; OnPropertyChanged(nameof(EnableSizeRotation)); }
        }

        public int MaxFileSizeMB
        {
            get => _settings.MaxFileSizeMB;
            set { _settings.MaxFileSizeMB = value; OnPropertyChanged(nameof(MaxFileSizeMB)); }
        }

        public bool EnableTimeRotation
        {
            get => _settings.EnableTimeRotation;
            set { _settings.EnableTimeRotation = value; OnPropertyChanged(nameof(EnableTimeRotation)); }
        }

        public TimeInterval TimeInterval
        {
            get => _settings.TimeInterval;
            set { _settings.TimeInterval = value; OnPropertyChanged(nameof(TimeInterval)); }
        }

        public int MaxRetainCount
        {
            get => _settings.MaxRetainCount;
            set { _settings.MaxRetainCount = value; OnPropertyChanged(nameof(MaxRetainCount)); }
        }

        public int MaxRetainDays
        {
            get => _settings.MaxRetainDays;
            set { _settings.MaxRetainDays = value; OnPropertyChanged(nameof(MaxRetainDays)); }
        }

        public int MaxRetainSizeMB
        {
            get => _settings.MaxRetainSizeMB;
            set { _settings.MaxRetainSizeMB = value; OnPropertyChanged(nameof(MaxRetainSizeMB)); }
        }

        public bool EnableCompression
        {
            get => _settings.EnableCompression;
            set { _settings.EnableCompression = value; OnPropertyChanged(nameof(EnableCompression)); }
        }

        public CompressionType CompressionType
        {
            get => _settings.CompressionType;
            set { _settings.CompressionType = value; OnPropertyChanged(nameof(CompressionType)); }
        }

        public int CompressAfterDays
        {
            get => _settings.CompressAfterDays;
            set { _settings.CompressAfterDays = value; OnPropertyChanged(nameof(CompressAfterDays)); }
        }

        public bool EnableAutoCleanup
        {
            get => _settings.EnableAutoCleanup;
            set { _settings.EnableAutoCleanup = value; OnPropertyChanged(nameof(EnableAutoCleanup)); }
        }

        public CleanupStrategy CleanupStrategy
        {
            get => _settings.CleanupStrategy;
            set { _settings.CleanupStrategy = value; OnPropertyChanged(nameof(CleanupStrategy)); }
        }

        public string ArchivePath
        {
            get => _settings.ArchivePath;
            set { _settings.ArchivePath = value; OnPropertyChanged(nameof(ArchivePath)); }
        }

        public FileNamingPattern FileNamingPattern
        {
            get => _settings.FileNamingPattern;
            set { _settings.FileNamingPattern = value; OnPropertyChanged(nameof(FileNamingPattern)); }
        }

        public int CurrentLogFilesCount
        {
            get => _currentLogFilesCount;
            set { _currentLogFilesCount = value; OnPropertyChanged(nameof(CurrentLogFilesCount)); }
        }

        public long CurrentLogsTotalSizeMB
        {
            get => _currentLogsTotalSizeMB;
            set { _currentLogsTotalSizeMB = value; OnPropertyChanged(nameof(CurrentLogsTotalSizeMB)); }
        }

        public StorageSpaceInfo StorageInfo
        {
            get => _storageInfo;
            set { _storageInfo = value; OnPropertyChanged(nameof(StorageInfo)); }
        }

        public RotationPrediction Prediction
        {
            get => _prediction;
            set { _prediction = value; OnPropertyChanged(nameof(Prediction)); }
        }

        public bool EnableStorageMonitoring
        {
            get => _settings.EnableStorageMonitoring;
            set { _settings.EnableStorageMonitoring = value; OnPropertyChanged(nameof(EnableStorageMonitoring)); }
        }

        public int WarningThresholdPercentage
        {
            get => _settings.WarningThresholdPercentage;
            set { _settings.WarningThresholdPercentage = value; OnPropertyChanged(nameof(WarningThresholdPercentage)); }
        }

        public int CriticalThresholdPercentage
        {
            get => _settings.CriticalThresholdPercentage;
            set { _settings.CriticalThresholdPercentage = value; OnPropertyChanged(nameof(CriticalThresholdPercentage)); }
        }

        public List<RotationType> RotationTypes { get; }
        public List<TimeInterval> TimeIntervals { get; }
        public List<CompressionType> CompressionTypes { get; }
        public List<CleanupStrategy> CleanupStrategies { get; }
        public List<FileNamingPattern> FileNamingPatterns { get; }
        public ObservableCollection<RotationHistory> RotationHistories { get; }
        public ObservableCollection<CleanupRecommendation> CleanupRecommendations { get; }

        public ICommand SaveCommand { get; }
        public ICommand ManualRotateCommand { get; }
        public ICommand CleanupCommand { get; }
        public ICommand RefreshStatsCommand { get; }
        public ICommand ViewHistoryCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ExportHistoryCommand { get; }
        public ICommand CheckStorageCommand { get; }
        public ICommand GenerateCleanupRecommendationsCommand { get; }

        private async void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpLrs = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLrs, json);
                File.Move(tmpLrs, _settingsFilePath, overwrite: true);

                TM.App.Log($"[LogRotation] 保存设置成功");
                GlobalToast.Success("保存成功", "日志轮转设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"无法保存日志轮转设置: {ex.Message}");
            }
        }

        private void ManualRotate()
        {
            var result = StandardDialog.ShowConfirm(
                "是否立即执行日志文件轮转？",
                "确认轮转"
            );

            if (result)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    TM.App.Log($"[LogRotation] 手动触发日志轮转");

                    var logsPath = ResolveLogDirectory();
                    if (Directory.Exists(logsPath))
                    {
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var originalFile = "application.log";
                        var rotatedFile = $"application_{timestamp}.log";

                        var testFilePath = Path.Combine(logsPath, originalFile);
                        long fileSize = 0;
                        if (File.Exists(testFilePath))
                        {
                            fileSize = new FileInfo(testFilePath).Length;
                        }

                        sw.Stop();

                        RecordRotationHistory(
                            RotationTrigger.Manual,
                            originalFile,
                            rotatedFile,
                            fileSize,
                            false,
                            sw.ElapsedMilliseconds
                        );

                        TM.App.Log($"[LogRotation] 轮转完成: {rotatedFile}, 耗时{sw.ElapsedMilliseconds}ms");
                    }

                    GlobalToast.Success("轮转成功", "日志文件已成功轮转");
                    RefreshStatistics();
                    UpdatePrediction();
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    TM.App.Log($"[LogRotation] 轮转失败: {ex.Message}");
                    GlobalToast.Error("轮转失败", $"日志文件轮转失败: {ex.Message}");
                }
            }
        }

        private void Cleanup()
        {
            var result = StandardDialog.ShowConfirm(
                "是否立即清理过期日志文件？此操作不可撤销。",
                "确认清理"
            );

            if (result)
            {
                try
                {
                    TM.App.Log($"[LogRotation] 手动清理过期日志");

                    var logsPath = ResolveLogDirectory();
                    if (Directory.Exists(logsPath))
                    {
                        var allFiles = Directory.GetFiles(logsPath, "*.log")
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTime)
                            .ToList();
                        int deletedCount = 0;

                        for (int i = 0; i < allFiles.Count; i++)
                        {
                            bool shouldDelete = CleanupStrategy switch
                            {
                                CleanupStrategy.ByTime => allFiles[i].LastWriteTime < DateTime.Now.AddDays(-MaxRetainDays),
                                CleanupStrategy.ByCount => i >= MaxRetainCount,
                                CleanupStrategy.BySize => i >= MaxRetainCount || allFiles[i].LastWriteTime < DateTime.Now.AddDays(-MaxRetainDays),
                                _ => allFiles[i].LastWriteTime < DateTime.Now.AddDays(-MaxRetainDays)
                            };
                            if (shouldDelete)
                            {
                                try { allFiles[i].Delete(); deletedCount++; } catch { }
                            }
                        }

                        TM.App.Log($"[LogRotation] 清理完成，删除了 {deletedCount} 个文件");
                        GlobalToast.Success("清理成功", $"已清理 {deletedCount} 个过期日志文件");
                    }

                    RefreshStatistics();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LogRotation] 清理失败: {ex.Message}");
                    GlobalToast.Error("清理失败", $"过期日志清理失败: {ex.Message}");
                }
            }
        }

        private void RefreshStatistics()
        {
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                int fileCount = 0;
                long totalSizeMB = 0;
                string? error = null;

                try
                {
                    var logsPath = ResolveLogDirectory();
                    if (Directory.Exists(logsPath))
                    {
                        var files = Directory.GetFiles(logsPath, "*.log", SearchOption.AllDirectories);
                        fileCount = files.Length;
                        long totalSize = files.Sum(f => new FileInfo(f).Length);
                        totalSizeMB = totalSize / (1024 * 1024);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                return () =>
                {
                    CurrentLogFilesCount = fileCount;
                    CurrentLogsTotalSizeMB = totalSizeMB;

                    if (error == null)
                    {
                        TM.App.Log($"[LogRotation] 刷新统计: {CurrentLogFilesCount} 个文件, {CurrentLogsTotalSizeMB} MB");
                    }
                    else
                    {
                        TM.App.Log($"[LogRotation] 刷新统计失败: {error}");
                    }
                };
            }, "LogRotation.Stats");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void SaveRotationHistory()
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(RotationHistories.ToList(), JsonHelper.Default);
                var tmpLrh = _historyFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLrh, json);
                File.Move(tmpLrh, _historyFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 保存历史记录失败: {ex.Message}");
            }
        }

        private void RecordRotationHistory(RotationTrigger trigger, string originalFile, string rotatedFile, long fileSizeBytes, bool wasCompressed, long durationMs)
        {
            var history = new RotationHistory
            {
                RotationTime = DateTime.Now,
                Trigger = trigger,
                OriginalFileName = originalFile,
                RotatedFileName = rotatedFile,
                FileSizeBytes = fileSizeBytes,
                WasCompressed = wasCompressed,
                TotalFilesBeforeRotation = CurrentLogFilesCount,
                TotalFilesAfterRotation = CurrentLogFilesCount + 1,
                DurationMs = durationMs,
                Success = true,
                Notes = $"触发原因: {trigger}"
            };

            RotationHistories.Insert(0, history);

            while (RotationHistories.Count > 100)
            {
                RotationHistories.RemoveAt(RotationHistories.Count - 1);
            }

            SaveRotationHistory();
        }

        private void ViewHistory()
        {
            try
            {
                var logDir = ResolveLogDirectory();
                if (!Directory.Exists(logDir))
                {
                    GlobalToast.Info("日志目录不存在", $"未找到日志目录: {logDir}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    Arguments = $"\"{logDir}\""
                };
                Process.Start(psi);
                TM.App.Log($"[LogRotation] 打开日志目录: {logDir}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 打开日志目录失败: {ex.Message}");
                GlobalToast.Error("打开失败", $"无法打开日志目录: {ex.Message}");
            }
        }

        private void ClearHistory()
        {
            var result = StandardDialog.ShowConfirm(
                $"是否清空所有轮转历史记录（共{RotationHistories.Count}条）？此操作不可撤销。",
                "确认清空"
            );

            if (result)
            {
                RotationHistories.Clear();
                SaveRotationHistory();
                TM.App.Log($"[LogRotation] 清空历史记录");
                GlobalToast.Success("清空完成", "已清空所有轮转历史记录");
            }
        }

        private void ExportHistory()
        {
            try
            {
                var logDir = ResolveLogDirectory();
                if (!Directory.Exists(logDir))
                {
                    GlobalToast.Info("无日志文件", $"未找到日志目录: {logDir}");
                    return;
                }

                var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
                var todayFiles = Directory.GetFiles(logDir, $"{todayStr}_*.log", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();
                if (todayFiles.Count == 0)
                {
                    GlobalToast.Info("无当天日志", "当前日志目录下没有可导出的当天 .log 文件");
                    return;
                }
                var sourceLog = todayFiles[0];

                var saveDialog = new SaveFileDialog
                {
                    Title = "选择导出位置",
                    Filter = "日志文件 (*.log)|*.log",
                    DefaultExt = ".log",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = sourceLog.Name
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                var exportPath = saveDialog.FileName;
                var directory = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    if (string.Equals(Path.GetFullPath(exportPath), Path.GetFullPath(sourceLog.FullName), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("导出位置与源日志文件相同");
                    }

                    File.Copy(sourceLog.FullName, exportPath, overwrite: true);
                    TM.App.Log($"[LogRotation] 导出当天日志: {sourceLog.FullName} -> {exportPath}");
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("导出失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            UseShellExecute = true,
                            Arguments = $"/select,\"{exportPath}\""
                        };
                        Process.Start(psi);
                    }
                    catch { }

                    GlobalToast.Success("导出成功", $"已导出当天日志: {sourceLog.Name}");
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 导出历史失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"无法导出日志文件: {ex.Message}");
            }
        }

        private string ResolveLogDirectory()
        {
            try
            {
                var configuredPath = _outputSettings.FileOutputPath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    configuredPath = "Logs/application.log";
                }

                var dir = Path.GetDirectoryName(configuredPath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = "Logs";
                }

                if (!Path.IsPathRooted(dir))
                {
                    dir = Path.Combine(StoragePathHelper.GetStorageRoot(), dir);
                }

                return dir;
            }
            catch
            {
                return Path.Combine(StoragePathHelper.GetStorageRoot(), "Logs");
            }
        }

        private void CheckStorageSpace()
        {
            var logsPath = ResolveLogDirectory();
            var logsSizeMB = CurrentLogsTotalSizeMB;
            var warnPct = WarningThresholdPercentage;
            var critPct = CriticalThresholdPercentage;

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(logsPath)!);
                    var totalSpaceGB = driveInfo.TotalSize / (1024 * 1024 * 1024);
                    var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024);
                    var usedSpaceGB = totalSpaceGB - freeSpaceGB;
                    var usagePercentage = (double)usedSpaceGB / totalSpaceGB * 100;

                    var status = usagePercentage < warnPct ? StorageStatus.Normal :
                                usagePercentage < critPct ? StorageStatus.Warning :
                                StorageStatus.Critical;

                    var statusMessage = status == StorageStatus.Normal ? "存储空间充足" :
                                       status == StorageStatus.Warning ? $"存储空间不足（已使用{usagePercentage:F1}%）" :
                                       $"存储空间严重不足（已使用{usagePercentage:F1}%）！";

                    return (info: new StorageSpaceInfo
                    {
                        DrivePath = driveInfo.Name,
                        TotalSpaceGB = totalSpaceGB,
                        FreeSpaceGB = freeSpaceGB,
                        UsedSpaceGB = usedSpaceGB,
                        UsagePercentage = usagePercentage,
                        LogsSpaceMB = logsSizeMB,
                        Status = status,
                        StatusMessage = statusMessage,
                        LastChecked = DateTime.Now
                    }, status, statusMessage);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LogRotation] 检查存储空间失败: {ex.Message}");
                    return default;
                }
            }).ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result == default) return;
                StorageInfo = t.Result.info;
                if (t.Result.status == StorageStatus.Critical)
                {
                    TM.App.Log($"[LogRotation] 存储空间警告: {t.Result.statusMessage}");
                    GenerateCleanupRecommendations();
                }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void UpdatePrediction()
        {
            try
            {
                var recentHistories = RotationHistories.Take(10).ToList();
                if (recentHistories.Count >= 2)
                {
                    var intervals = new List<TimeSpan>();
                    for (int i = 0; i < recentHistories.Count - 1; i++)
                    {
                        intervals.Add(recentHistories[i].RotationTime - recentHistories[i + 1].RotationTime);
                    }

                    var averageInterval = TimeSpan.FromTicks((long)intervals.Average(t => t.Ticks));
                    var predictedNextRotation = DateTime.Now + averageInterval;
                    var timeUntilNext = predictedNextRotation - DateTime.Now;

                    var totalDays = (recentHistories.First().RotationTime - recentHistories.Last().RotationTime).TotalDays;
                    var totalSizeMB = recentHistories.Sum(h => h.FileSizeBytes) / (1024.0 * 1024.0);
                    var avgDailyGrowth = totalDays > 0 ? totalSizeMB / totalDays : 0;

                    var predictedStorageUsage = CurrentLogsTotalSizeMB + (long)(avgDailyGrowth * timeUntilNext.TotalDays);

                    var recommendation = timeUntilNext.TotalHours < 24 ? "建议尽快检查日志设置" :
                                        predictedStorageUsage > MaxRetainSizeMB * 0.8 ? "建议调整保留策略" :
                                        "当前设置合理";

                    Prediction = new RotationPrediction
                    {
                        PredictedNextRotation = predictedNextRotation,
                        TimeUntilNextRotation = timeUntilNext,
                        PredictedFileSizeMB = MaxFileSizeMB,
                        PredictedStorageUsageMB = predictedStorageUsage,
                        AverageDailyGrowthMB = avgDailyGrowth,
                        RecommendedAction = recommendation
                    };
                }
                else
                {
                    var estimatedNextRotation = DateTime.Now.AddDays(1);
                    Prediction = new RotationPrediction
                    {
                        PredictedNextRotation = estimatedNextRotation,
                        TimeUntilNextRotation = estimatedNextRotation - DateTime.Now,
                        PredictedFileSizeMB = MaxFileSizeMB,
                        PredictedStorageUsageMB = CurrentLogsTotalSizeMB + MaxFileSizeMB,
                        AverageDailyGrowthMB = MaxFileSizeMB,
                        RecommendedAction = "数据不足，基于配置估算"
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 更新预测失败: {ex.Message}");
            }
        }

        private void GenerateCleanupRecommendations()
        {
            CleanupRecommendations.Clear();

            try
            {
                var logsPath = ResolveLogDirectory();
                if (!Directory.Exists(logsPath))
                {
                    return;
                }

                var files = Directory.GetFiles(logsPath, "*.log", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastWriteTime)
                    .ToList();

                var expiredFiles = files.Where(f => f.LastWriteTime < DateTime.Now.AddDays(-MaxRetainDays)).ToList();
                if (expiredFiles.Any())
                {
                    CleanupRecommendations.Add(new CleanupRecommendation
                    {
                        Action = "删除过期日志",
                        Reason = $"超过{MaxRetainDays}天的日志文件",
                        EstimatedSpaceToFree = expiredFiles.Sum(f => f.Length) / (1024 * 1024),
                        FilesToDelete = expiredFiles.Count,
                        Priority = "高"
                    });
                }

                var uncompressedOldFiles = files.Where(f => 
                    !f.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                    f.LastWriteTime < DateTime.Now.AddDays(-CompressAfterDays)).ToList();
                if (uncompressedOldFiles.Any())
                {
                    CleanupRecommendations.Add(new CleanupRecommendation
                    {
                        Action = "压缩旧日志",
                        Reason = $"超过{CompressAfterDays}天的未压缩日志",
                        EstimatedSpaceToFree = (long)(uncompressedOldFiles.Sum(f => f.Length) * 0.7 / (1024 * 1024)),
                        FilesToDelete = 0,
                        Priority = "中"
                    });
                }

                if (files.Count > MaxRetainCount)
                {
                    var excessFiles = files.Count - MaxRetainCount;
                    CleanupRecommendations.Add(new CleanupRecommendation
                    {
                        Action = "减少日志文件数量",
                        Reason = $"当前{files.Count}个文件，超过限制{MaxRetainCount}个",
                        EstimatedSpaceToFree = files.Take(excessFiles).Sum(f => f.Length) / (1024 * 1024),
                        FilesToDelete = excessFiles,
                        Priority = "中"
                    });
                }

                if (StorageInfo?.Status == StorageStatus.Critical)
                {
                    CleanupRecommendations.Insert(0, new CleanupRecommendation
                    {
                        Action = "紧急清理空间",
                        Reason = $"磁盘空间严重不足（{StorageInfo.UsagePercentage:F1}%）",
                        EstimatedSpaceToFree = files.Take(files.Count / 2).Sum(f => f.Length) / (1024 * 1024),
                        FilesToDelete = files.Count / 2,
                        Priority = "紧急"
                    });
                }

                TM.App.Log($"[LogRotation] 生成了 {CleanupRecommendations.Count} 条清理建议");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogRotation] 生成清理建议失败: {ex.Message}");
            }
        }

        private void OnAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(RotationType));
            OnPropertyChanged(nameof(EnableSizeRotation));
            OnPropertyChanged(nameof(MaxFileSizeMB));
            OnPropertyChanged(nameof(EnableTimeRotation));
            OnPropertyChanged(nameof(TimeInterval));
            OnPropertyChanged(nameof(MaxRetainCount));
            OnPropertyChanged(nameof(MaxRetainDays));
            OnPropertyChanged(nameof(MaxRetainSizeMB));
            OnPropertyChanged(nameof(EnableCompression));
            OnPropertyChanged(nameof(CompressionType));
            OnPropertyChanged(nameof(CompressAfterDays));
            OnPropertyChanged(nameof(EnableAutoCleanup));
            OnPropertyChanged(nameof(CleanupStrategy));
            OnPropertyChanged(nameof(ArchivePath));
            OnPropertyChanged(nameof(FileNamingPattern));
            OnPropertyChanged(nameof(EnableStorageMonitoring));
            OnPropertyChanged(nameof(WarningThresholdPercentage));
            OnPropertyChanged(nameof(CriticalThresholdPercentage));
        }
    }
}

