using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Logging.LogOutput
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LogOutputViewModel : INotifyPropertyChanged
    {
        private LogOutputSettings _settings;
        private readonly string _settingsFilePath;
        private readonly string _statisticsFilePath;
        private readonly string _failuresFilePath;
        private readonly DispatcherTimer _statsTimer;
        private readonly ProxyService _proxyService;

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

            Debug.WriteLine($"[LogOutput] {key}: {ex.Message}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public LogOutputViewModel(ProxyService proxyService)
        {
            _proxyService = proxyService;
            _settings = new LogOutputSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogOutput",
                "settings.json"
            );
            _statisticsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogOutput",
                "statistics.json"
            );
            _failuresFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogOutput",
                "failures.json"
            );

            OutputTargets = new ObservableCollection<OutputTarget>();
            TestResults = new ObservableCollection<TestResult>();
            Statistics = new ObservableCollection<OutputStatistics>();
            FailureRecords = new ObservableCollection<FailureRecord>();

            AsyncSettingsLoader.LoadOrDefer<LogOutputSettings>(_settingsFilePath, s =>
            {
                _settings = s;
                OnAllPropertiesChanged();
                LoadOutputTargets();
            }, "LogOutput");

            AsyncSettingsLoader.LoadOrDefer<List<OutputStatistics>>(_statisticsFilePath, stats =>
            {
                Statistics.Clear();
                foreach (var stat in stats)
                {
                    Statistics.Add(stat);
                }
            }, "LogOutput.Stats");

            AsyncSettingsLoader.LoadOrDefer<List<FailureRecord>>(_failuresFilePath, failures =>
            {
                FailureRecords.Clear();
                foreach (var failure in failures)
                {
                    FailureRecords.Add(failure);
                }
            }, "LogOutput.Failures");

            OutputTargetTypes = new List<OutputTargetType>
            {
                OutputTargetType.File,
                OutputTargetType.Console,
                OutputTargetType.EventLog,
                OutputTargetType.RemoteHttp,
                OutputTargetType.RemoteTcp
            };

            FileEncodings = new List<FileEncodingType>
            {
                FileEncodingType.UTF8,
                FileEncodingType.UTF8BOM,
                FileEncodingType.ASCII,
                FileEncodingType.Unicode
            };

            RemoteProtocols = new List<string> { "HTTP", "HTTPS", "TCP", "UDP" };

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };

            SaveCommand = new RelayCommand(SaveSettings);
            TestOutputCommand = new RelayCommand(TestAllTargets);
            TestFileOutputCommand = new RelayCommand(TestFileOutput);
            TestRemoteOutputCommand = new RelayCommand(TestRemoteOutput);
            AddTargetCommand = new RelayCommand(AddTarget);
            RemoveTargetCommand = new RelayCommand(RemoveTarget);
            RefreshStatsCommand = new RelayCommand(RefreshStatistics);
            ResetStatsCommand = new RelayCommand(ResetStatistics);
            ClearFailuresCommand = new RelayCommand(ClearFailures);
            RetryFailedCommand = new RelayCommand(RetryFailed);
        }

        public bool EnableFileOutput
        {
            get => _settings.EnableFileOutput;
            set { _settings.EnableFileOutput = value; OnPropertyChanged(nameof(EnableFileOutput)); }
        }

        public string FileOutputPath
        {
            get => _settings.FileOutputPath;
            set { _settings.FileOutputPath = value; OnPropertyChanged(nameof(FileOutputPath)); }
        }

        public string FileNamingPattern
        {
            get => _settings.FileNamingPattern;
            set { _settings.FileNamingPattern = value; OnPropertyChanged(nameof(FileNamingPattern)); }
        }

        public FileEncodingType FileEncoding
        {
            get => _settings.FileEncoding;
            set { _settings.FileEncoding = value; OnPropertyChanged(nameof(FileEncoding)); }
        }

        public bool EnableConsoleOutput
        {
            get => _settings.EnableConsoleOutput;
            set { _settings.EnableConsoleOutput = value; OnPropertyChanged(nameof(EnableConsoleOutput)); }
        }

        public bool ConsoleColorCoding
        {
            get => _settings.ConsoleColorCoding;
            set { _settings.ConsoleColorCoding = value; OnPropertyChanged(nameof(ConsoleColorCoding)); }
        }

        public bool EnableEventLog
        {
            get => _settings.EnableEventLog;
            set { _settings.EnableEventLog = value; OnPropertyChanged(nameof(EnableEventLog)); }
        }

        public string EventLogSource
        {
            get => _settings.EventLogSource;
            set { _settings.EventLogSource = value; OnPropertyChanged(nameof(EventLogSource)); }
        }

        public bool EnableRemoteOutput
        {
            get => _settings.EnableRemoteOutput;
            set { _settings.EnableRemoteOutput = value; OnPropertyChanged(nameof(EnableRemoteOutput)); }
        }

        public string RemoteProtocol
        {
            get => _settings.RemoteProtocol;
            set { _settings.RemoteProtocol = value; OnPropertyChanged(nameof(RemoteProtocol)); }
        }

        public string RemoteAddress
        {
            get => _settings.RemoteAddress;
            set { _settings.RemoteAddress = value; OnPropertyChanged(nameof(RemoteAddress)); }
        }

        public int BufferSize
        {
            get => _settings.BufferSize;
            set { _settings.BufferSize = value; OnPropertyChanged(nameof(BufferSize)); }
        }

        public bool EnableAsyncOutput
        {
            get => _settings.EnableAsyncOutput;
            set { _settings.EnableAsyncOutput = value; OnPropertyChanged(nameof(EnableAsyncOutput)); }
        }

        public bool EnableRetry
        {
            get => _settings.RetryConfiguration.EnableRetry;
            set { _settings.RetryConfiguration.EnableRetry = value; OnPropertyChanged(nameof(EnableRetry)); }
        }

        public int MaxRetryAttempts
        {
            get => _settings.RetryConfiguration.MaxRetryAttempts;
            set { _settings.RetryConfiguration.MaxRetryAttempts = value; OnPropertyChanged(nameof(MaxRetryAttempts)); }
        }

        public int RetryIntervalMs
        {
            get => _settings.RetryConfiguration.RetryIntervalMs;
            set { _settings.RetryConfiguration.RetryIntervalMs = value; OnPropertyChanged(nameof(RetryIntervalMs)); }
        }

        public bool EnableExponentialBackoff
        {
            get => _settings.RetryConfiguration.EnableExponentialBackoff;
            set { _settings.RetryConfiguration.EnableExponentialBackoff = value; OnPropertyChanged(nameof(EnableExponentialBackoff)); }
        }

        public List<OutputTargetType> OutputTargetTypes { get; }
        public List<FileEncodingType> FileEncodings { get; }
        public List<string> RemoteProtocols { get; }
        public ObservableCollection<OutputTarget> OutputTargets { get; }
        public ObservableCollection<TestResult> TestResults { get; }
        public ObservableCollection<OutputStatistics> Statistics { get; }
        public ObservableCollection<FailureRecord> FailureRecords { get; }

        public ICommand SaveCommand { get; }
        public ICommand TestOutputCommand { get; }
        public ICommand TestFileOutputCommand { get; }
        public ICommand TestRemoteOutputCommand { get; }
        public ICommand AddTargetCommand { get; }
        public ICommand RemoveTargetCommand { get; }
        public ICommand RefreshStatsCommand { get; }
        public ICommand ResetStatsCommand { get; }
        public ICommand ClearFailuresCommand { get; }
        public ICommand RetryFailedCommand { get; }

        private void LoadOutputTargets()
        {
            OutputTargets.Clear();
            foreach (var target in _settings.OutputTargets)
            {
                OutputTargets.Add(target);
            }
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.OutputTargets.Clear();
                foreach (var target in OutputTargets)
                {
                    _settings.OutputTargets.Add(target);
                }

                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpLos = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLos, json);
                File.Move(tmpLos, _settingsFilePath, overwrite: true);

                TM.App.Log($"[LogOutput] 保存设置成功");
                GlobalToast.Success("保存成功", "日志输出设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogOutput] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"无法保存日志输出设置: {ex.Message}");
            }
        }

        private void TestAllTargets()
        {
            _ = TestAllTargetsAsync();
        }

        private async Task TestAllTargetsAsync()
        {
            TestResults.Clear();
            TM.App.Log($"[LogOutput] 开始测试所有输出目标...");

            if (EnableFileOutput)
            {
                var result = await TestFileOutputInternalAsync();
                TestResults.Add(result);
            }

            if (EnableRemoteOutput)
            {
                var result = await TestRemoteOutputInternalAsync();
                TestResults.Add(result);
            }

            if (EnableConsoleOutput)
            {
                TestResults.Add(new TestResult
                {
                    TargetName = "控制台",
                    TargetType = OutputTargetType.Console,
                    Status = TestStatus.Success,
                    TestTime = DateTime.Now,
                    ResponseTime = 0,
                    Message = "控制台输出正常",
                    Details = "已启用控制台日志输出"
                });
            }

            if (EnableEventLog)
            {
                TestResults.Add(new TestResult
                {
                    TargetName = "系统事件日志",
                    TargetType = OutputTargetType.EventLog,
                    Status = TestStatus.Success,
                    TestTime = DateTime.Now,
                    ResponseTime = 0,
                    Message = "事件日志正常",
                    Details = $"事件源: {EventLogSource}"
                });
            }

            TM.App.Log($"[LogOutput] 测试完成，共{TestResults.Count}个目标");
            GlobalToast.Success("测试完成", $"已测试 {TestResults.Count} 个输出目标");
        }

        private async void TestFileOutput()
        {
            TestResults.Clear();
            var result = await TestFileOutputInternalAsync();
            TestResults.Add(result);

            if (result.Status == TestStatus.Success)
            {
                GlobalToast.Success("测试成功", result.Message);
            }
            else
            {
                GlobalToast.Error("测试失败", result.Message);
            }
        }

        private async System.Threading.Tasks.Task<TestResult> TestFileOutputInternalAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var testFilePath = StoragePathHelper.GetFilePath("Framework", "SystemSettings/Logging/LogOutput", "test_output.log");
                var directory = Path.GetDirectoryName(testFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var testContent = $"[TEST] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - 文件输出测试";
                await File.WriteAllTextAsync(testFilePath, testContent);

                if (File.Exists(testFilePath))
                {
                    var content = await File.ReadAllTextAsync(testFilePath);
                    sw.Stop();

                    UpdateStatistics("文件输出", OutputTargetType.File, true, sw.ElapsedMilliseconds, testContent.Length);

                    return new TestResult
                    {
                        TargetName = "文件输出",
                        TargetType = OutputTargetType.File,
                        Status = TestStatus.Success,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = "文件写入成功",
                        Details = $"路径: {testFilePath}\n大小: {testContent.Length} 字节\n耗时: {sw.ElapsedMilliseconds}ms"
                    };
                }
                else
                {
                    sw.Stop();
                    UpdateStatistics("文件输出", OutputTargetType.File, false, sw.ElapsedMilliseconds, 0);

                    return new TestResult
                    {
                        TargetName = "文件输出",
                        TargetType = OutputTargetType.File,
                        Status = TestStatus.Failed,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = "文件写入失败",
                        Details = "无法验证文件写入"
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                UpdateStatistics("文件输出", OutputTargetType.File, false, sw.ElapsedMilliseconds, 0);
                RecordFailure("文件输出", OutputTargetType.File, ex.Message, "测试日志内容");

                return new TestResult
                {
                    TargetName = "文件输出",
                    TargetType = OutputTargetType.File,
                    Status = TestStatus.Failed,
                    TestTime = DateTime.Now,
                    ResponseTime = sw.ElapsedMilliseconds,
                    Message = $"文件写入异常: {ex.Message}",
                    Details = ex.ToString()
                };
            }
        }

        private void TestRemoteOutput()
        {
            _ = TestRemoteOutputAsync();
        }

        private async Task TestRemoteOutputAsync()
        {
            TestResults.Clear();
            var result = await TestRemoteOutputInternalAsync();
            TestResults.Add(result);

            if (result.Status == TestStatus.Success)
            {
                GlobalToast.Success("测试成功", "远程输出连接正常");
            }
            else
            {
                GlobalToast.Warning("测试失败", "远程输出连接异常");
            }
        }

        private async Task<TestResult> TestRemoteOutputInternalAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (RemoteProtocol == "HTTP" || RemoteProtocol == "HTTPS")
                {
                    return await TestHttpRemoteAsync(sw);
                }
                else if (RemoteProtocol == "TCP")
                {
                    return TestTcpRemote(sw);
                }
                else
                {
                    sw.Stop();
                    return new TestResult
                    {
                        TargetName = "远程输出",
                        TargetType = OutputTargetType.RemoteHttp,
                        Status = TestStatus.Failed,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = $"不支持的协议: {RemoteProtocol}",
                        Details = "目前仅支持 HTTP、HTTPS 和 TCP"
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                UpdateStatistics("远程输出", OutputTargetType.RemoteHttp, false, sw.ElapsedMilliseconds, 0);
                RecordFailure("远程输出", OutputTargetType.RemoteHttp, ex.Message, "测试日志内容");

                return new TestResult
                {
                    TargetName = "远程输出",
                    TargetType = OutputTargetType.RemoteHttp,
                    Status = TestStatus.Failed,
                    TestTime = DateTime.Now,
                    ResponseTime = sw.ElapsedMilliseconds,
                    Message = $"远程连接异常: {ex.Message}",
                    Details = ex.ToString()
                };
            }
        }

        private async Task<TestResult> TestHttpRemoteAsync(Stopwatch sw)
        {
            try
            {
                using var client = _proxyService.CreateHttpClient(TimeSpan.FromSeconds(5));
                var testContent = $"{{\"timestamp\":\"{DateTime.Now:o}\",\"level\":\"INFO\",\"message\":\"测试日志\"}}";
                var content = new StringContent(testContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(RemoteAddress, content);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    UpdateStatistics("远程输出", OutputTargetType.RemoteHttp, true, sw.ElapsedMilliseconds, testContent.Length);

                    return new TestResult
                    {
                        TargetName = "远程输出 (HTTP)",
                        TargetType = OutputTargetType.RemoteHttp,
                        Status = TestStatus.Success,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = $"HTTP连接成功 ({response.StatusCode})",
                        Details = $"地址: {RemoteAddress}\n状态码: {(int)response.StatusCode}\n耗时: {sw.ElapsedMilliseconds}ms"
                    };
                }
                else
                {
                    UpdateStatistics("远程输出", OutputTargetType.RemoteHttp, false, sw.ElapsedMilliseconds, 0);

                    return new TestResult
                    {
                        TargetName = "远程输出 (HTTP)",
                        TargetType = OutputTargetType.RemoteHttp,
                        Status = TestStatus.Failed,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = $"HTTP请求失败 ({response.StatusCode})",
                        Details = $"地址: {RemoteAddress}\n状态码: {(int)response.StatusCode}"
                    };
                }
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                UpdateStatistics("远程输出", OutputTargetType.RemoteHttp, false, sw.ElapsedMilliseconds, 0);

                return new TestResult
                {
                    TargetName = "远程输出 (HTTP)",
                    TargetType = OutputTargetType.RemoteHttp,
                    Status = TestStatus.Timeout,
                    TestTime = DateTime.Now,
                    ResponseTime = sw.ElapsedMilliseconds,
                    Message = "HTTP连接超时",
                    Details = $"地址: {RemoteAddress}\n超时: 5秒"
                };
            }
        }

        private TestResult TestTcpRemote(Stopwatch sw)
        {
            try
            {
                var uri = new Uri(RemoteAddress.StartsWith("tcp://") ? RemoteAddress : $"tcp://{RemoteAddress}");
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 514;

                using var client = new TcpClient();
                client.Connect(host, port);

                if (client.Connected)
                {
                    var testContent = $"<14>1 {DateTime.Now:o} TM - - - 测试日志";
                    var bytes = Encoding.UTF8.GetBytes(testContent);
                    var stream = client.GetStream();
                    stream.Write(bytes, 0, bytes.Length);
                    sw.Stop();

                    UpdateStatistics("远程输出", OutputTargetType.RemoteTcp, true, sw.ElapsedMilliseconds, bytes.Length);

                    return new TestResult
                    {
                        TargetName = "远程输出 (TCP)",
                        TargetType = OutputTargetType.RemoteTcp,
                        Status = TestStatus.Success,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = "TCP连接成功",
                        Details = $"地址: {host}:{port}\n耗时: {sw.ElapsedMilliseconds}ms"
                    };
                }
                else
                {
                    sw.Stop();
                    UpdateStatistics("远程输出", OutputTargetType.RemoteTcp, false, sw.ElapsedMilliseconds, 0);

                    return new TestResult
                    {
                        TargetName = "远程输出 (TCP)",
                        TargetType = OutputTargetType.RemoteTcp,
                        Status = TestStatus.Failed,
                        TestTime = DateTime.Now,
                        ResponseTime = sw.ElapsedMilliseconds,
                        Message = "TCP连接失败",
                        Details = $"地址: {host}:{port}"
                    };
                }
            }
            catch (SocketException ex)
            {
                sw.Stop();
                UpdateStatistics("远程输出", OutputTargetType.RemoteTcp, false, sw.ElapsedMilliseconds, 0);

                return new TestResult
                {
                    TargetName = "远程输出 (TCP)",
                    TargetType = OutputTargetType.RemoteTcp,
                    Status = TestStatus.Failed,
                    TestTime = DateTime.Now,
                    ResponseTime = sw.ElapsedMilliseconds,
                    Message = $"TCP连接错误: {ex.Message}",
                    Details = ex.ToString()
                };
            }
        }

        private void AddTarget()
        {
            var targetName = StandardDialog.ShowInput("请输入输出目标名称", "添加输出目标");
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                OutputTargets.Add(new OutputTarget
                {
                    Name = targetName,
                    Type = OutputTargetType.File,
                    IsEnabled = true,
                    Priority = OutputTargets.Count
                });

                TM.App.Log($"[LogOutput] 添加输出目标: {targetName}");
                GlobalToast.Success("添加成功", $"已添加输出目标 '{targetName}'");
            }
        }

        private void RemoveTarget()
        {
            if (OutputTargets.Count > 0)
            {
                var lastTarget = OutputTargets[OutputTargets.Count - 1];
                var result = StandardDialog.ShowConfirm(
                    $"是否删除输出目标 '{lastTarget.Name}'？",
                    "确认删除"
                );

                if (result)
                {
                    OutputTargets.Remove(lastTarget);
                    TM.App.Log($"[LogOutput] 删除输出目标: {lastTarget.Name}");
                    GlobalToast.Success("删除成功", $"已删除输出目标 '{lastTarget.Name}'");
                }
            }
            else
            {
                GlobalToast.Warning("无可删除项", "没有配置任何输出目标");
            }
        }

        private void UpdateStatistics(string targetName, OutputTargetType targetType, bool success, long responseTime, long bytes)
        {
            var stat = Statistics.FirstOrDefault(s => s.TargetName == targetName);
            if (stat == null)
            {
                stat = new OutputStatistics
                {
                    TargetName = targetName,
                    TargetType = targetType,
                    TotalAttempts = 0,
                    SuccessCount = 0,
                    FailureCount = 0,
                    AverageResponseTime = 0,
                    TotalBytes = 0
                };
                Statistics.Add(stat);
            }

            stat.TotalAttempts++;
            if (success)
            {
                stat.SuccessCount++;
            }
            else
            {
                stat.FailureCount++;
            }

            stat.AverageResponseTime = (stat.AverageResponseTime * (stat.TotalAttempts - 1) + responseTime) / stat.TotalAttempts;
            stat.TotalBytes += bytes;
            stat.LastUpdateTime = DateTime.Now;

            SaveStatistics();
        }

        private void RecordFailure(string targetName, OutputTargetType targetType, string errorMessage, string logContent)
        {
            var failure = new FailureRecord
            {
                FailureTime = DateTime.Now,
                TargetName = targetName,
                TargetType = targetType,
                ErrorMessage = errorMessage,
                RetryAttempts = 0,
                IsResolved = false,
                LogContent = logContent
            };

            FailureRecords.Insert(0, failure);

            while (FailureRecords.Count > 100)
            {
                FailureRecords.RemoveAt(FailureRecords.Count - 1);
            }

            SaveFailures();
        }

        private void LoadStatistics()
        {
            LoadStatistics(showToast: false);
        }

        private void LoadStatistics(bool showToast)
        {
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                List<OutputStatistics> stats = new();
                string? error = null;
                try
                {
                    if (File.Exists(_statisticsFilePath))
                    {
                        var json = File.ReadAllText(_statisticsFilePath);
                        stats = JsonSerializer.Deserialize<List<OutputStatistics>>(json) ?? new List<OutputStatistics>();
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                return () =>
                {
                    Statistics.Clear();
                    foreach (var stat in stats)
                    {
                        Statistics.Add(stat);
                    }

                    if (error == null)
                    {
                        TM.App.Log($"[LogOutput] 加载统计信息成功");
                        if (showToast)
                        {
                            GlobalToast.Success("刷新完成", "已刷新输出统计信息");
                        }
                    }
                    else
                    {
                        TM.App.Log($"[LogOutput] 加载统计信息失败: {error}");
                        if (showToast)
                        {
                            GlobalToast.Error("刷新失败", $"无法刷新统计信息: {error}");
                        }
                    }
                };
            }, "LogOutput.Stats");
        }

        private async void SaveStatistics()
        {
            try
            {
                var directory = Path.GetDirectoryName(_statisticsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(Statistics.ToList(), JsonHelper.Default);
                var tmpStat = _statisticsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpStat, json);
                File.Move(tmpStat, _statisticsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogOutput] 保存统计信息失败: {ex.Message}");
            }
        }

        private async void SaveFailures()
        {
            try
            {
                var directory = Path.GetDirectoryName(_failuresFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(FailureRecords.ToList(), JsonHelper.Default);
                var tmpFail = _failuresFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpFail, json);
                File.Move(tmpFail, _failuresFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogOutput] 保存失败记录失败: {ex.Message}");
            }
        }

        private void RefreshStatistics()
        {
            LoadStatistics(showToast: true);
        }

        private void ResetStatistics()
        {
            var result = StandardDialog.ShowConfirm("确定要重置所有统计信息吗？此操作不可撤销。", "确认重置");
            if (result)
            {
                Statistics.Clear();
                SaveStatistics();
                TM.App.Log($"[LogOutput] 重置统计信息");
                GlobalToast.Success("重置完成", "已清空所有输出统计信息");
            }
        }

        private void ClearFailures()
        {
            var result = StandardDialog.ShowConfirm("确定要清空所有失败记录吗？此操作不可撤销。", "确认清空");
            if (result)
            {
                FailureRecords.Clear();
                SaveFailures();
                TM.App.Log($"[LogOutput] 清空失败记录");
                GlobalToast.Success("清空完成", "已清空所有失败记录");
            }
        }

        private void RetryFailed()
        {
            _ = RetryFailedAsync();
        }

        private async Task RetryFailedAsync()
        {
            var unresolvedFailures = FailureRecords.Where(f => !f.IsResolved).ToList();
            if (unresolvedFailures.Count == 0)
            {
                GlobalToast.Info("重试", "没有需要重试的失败记录");
                return;
            }

            int successCount = 0;
            foreach (var failure in unresolvedFailures)
            {
                bool retrySuccess = false;

                try
                {
                    if (failure.TargetType == OutputTargetType.File && EnableFileOutput)
                    {
                        var result = await TestFileOutputInternalAsync();
                        retrySuccess = result.Status == TestStatus.Success;
                    }
                    else if ((failure.TargetType == OutputTargetType.RemoteHttp || failure.TargetType == OutputTargetType.RemoteTcp) && EnableRemoteOutput)
                    {
                        var result = await TestRemoteOutputInternalAsync();
                        retrySuccess = result.Status == TestStatus.Success;
                    }

                    if (retrySuccess)
                    {
                        failure.IsResolved = true;
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(RetryFailedAsync), ex);
                    continue;
                }
            }

            SaveFailures();
            TM.App.Log($"[LogOutput] 重试完成，成功: {successCount}/{unresolvedFailures.Count}");
            GlobalToast.Success("重试完成", $"成功重试 {successCount}/{unresolvedFailures.Count} 条失败记录");
        }

        private void SimulateOutputActivity()
        {
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(EnableFileOutput));
            OnPropertyChanged(nameof(FileOutputPath));
            OnPropertyChanged(nameof(FileNamingPattern));
            OnPropertyChanged(nameof(FileEncoding));
            OnPropertyChanged(nameof(EnableConsoleOutput));
            OnPropertyChanged(nameof(ConsoleColorCoding));
            OnPropertyChanged(nameof(EnableEventLog));
            OnPropertyChanged(nameof(EventLogSource));
            OnPropertyChanged(nameof(EnableRemoteOutput));
            OnPropertyChanged(nameof(RemoteProtocol));
            OnPropertyChanged(nameof(RemoteAddress));
            OnPropertyChanged(nameof(BufferSize));
            OnPropertyChanged(nameof(EnableAsyncOutput));
            OnPropertyChanged(nameof(EnableRetry));
            OnPropertyChanged(nameof(MaxRetryAttempts));
            OnPropertyChanged(nameof(RetryIntervalMs));
            OnPropertyChanged(nameof(EnableExponentialBackoff));
        }
    }
}

