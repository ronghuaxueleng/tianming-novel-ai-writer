using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyTest
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ProxyTestViewModel : INotifyPropertyChanged
    {
        private readonly string _settingsFile;
        private ProxyTestSettings _settings = new();
        private readonly ProxyService _proxyService;
        private readonly ProxyTestService _proxyTestService;

        private bool _testConnectivity = true;
        private bool _testLatency = true;
        private bool _testSpeed = true;
        private bool _testIP = true;
        private bool _testDNS = true;
        private bool _isTesting;
        private ProxyTestResult? _lastResult;

        public bool TestConnectivity
        {
            get => _testConnectivity;
            set { _testConnectivity = value; OnPropertyChanged(nameof(TestConnectivity)); }
        }

        public bool TestLatency
        {
            get => _testLatency;
            set { _testLatency = value; OnPropertyChanged(nameof(TestLatency)); }
        }

        public bool TestSpeed
        {
            get => _testSpeed;
            set { _testSpeed = value; OnPropertyChanged(nameof(TestSpeed)); }
        }

        public bool TestIP
        {
            get => _testIP;
            set { _testIP = value; OnPropertyChanged(nameof(TestIP)); }
        }

        public bool TestDNS
        {
            get => _testDNS;
            set { _testDNS = value; OnPropertyChanged(nameof(TestDNS)); }
        }

        public bool IsTesting
        {
            get => _isTesting;
            set { _isTesting = value; OnPropertyChanged(nameof(IsTesting)); }
        }

        public ProxyTestResult? LastResult
        {
            get => _lastResult;
            set { _lastResult = value; OnPropertyChanged(nameof(LastResult)); OnPropertyChanged(nameof(HasResult)); }
        }

        public bool HasResult => LastResult != null;

        public ObservableCollection<ProxyTestResult> TestHistory { get; } = new();
        public ObservableCollection<ScheduledTest> ScheduledTests { get; } = new();
        public TestStatistics? Statistics { get; private set; }
        public TestTrendAnalysis? TrendAnalysis { get; private set; }

        public ICommand StartTestCommand { get; }
        public ICommand ViewDetailedHistoryCommand { get; }
        public ICommand AnalyzeTrendCommand { get; }
        public ICommand CompareTestsCommand { get; }
        public ICommand ScheduleTestCommand { get; }
        public ICommand BatchTestCommand { get; }
        public ICommand GenerateTestReportCommand { get; }
        public ICommand ExportTestDataCommand { get; }
        public ICommand OneClickVerifyCommand { get; }

        public ProxyTestViewModel(ProxyService proxyService, ProxyTestService proxyTestService)
        {
            _proxyService = proxyService;
            _proxyTestService = proxyTestService;
            _settingsFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy/ProxyTest", "test_settings.json");

            StartTestCommand = new AsyncRelayCommand(StartTestAsync);
            ViewDetailedHistoryCommand = new RelayCommand(ViewDetailedHistory);
            AnalyzeTrendCommand = new RelayCommand(AnalyzeTrend);
            CompareTestsCommand = new RelayCommand(CompareTests);
            ScheduleTestCommand = new RelayCommand(ScheduleTest);
            BatchTestCommand = new AsyncRelayCommand(BatchTestAsync);
            GenerateTestReportCommand = new RelayCommand(GenerateTestReport);
            ExportTestDataCommand = new RelayCommand(ExportTestData);
            OneClickVerifyCommand = new AsyncRelayCommand(OneClickVerifyAsync);

            AsyncSettingsLoader.LoadOrDefer<ProxyTestSettings>(_settingsFile, s =>
            {
                _settings = s;
                ScheduledTests.Clear();
                foreach (var test in _settings.ScheduledTests)
                    ScheduledTests.Add(test);
                LoadHistory();
                CalculateStatistics();
            }, "ProxyTest");
        }

        private async System.Threading.Tasks.Task OneClickVerifyAsync()
        {
            if (IsTesting)
            {
                return;
            }

            try
            {
                IsTesting = true;
                GlobalToast.Info("验证中", "正在验证应用内代理是否生效...");

                var verify = await _proxyTestService.VerifyApplicationProxyAsync();

                if (verify.DirectSuccess && verify.ProxySuccess)
                {
                    GlobalToast.Success("验证完成", verify.Summary);
                }
                else if (verify.ProxySuccess)
                {
                    GlobalToast.Success("验证完成", verify.Summary);
                }
                else
                {
                    GlobalToast.Warning("验证完成", verify.Summary);
                }

                var detail =
                    $"直连: {(verify.DirectSuccess ? "成功" : "失败")}\n" +
                    $"直连IP: {verify.DirectIP}\n" +
                    (string.IsNullOrWhiteSpace(verify.DirectError) ? "" : $"直连错误: {verify.DirectError}\n") +
                    $"\n应用内代理: {(verify.ProxySuccess ? "成功" : "失败")}\n" +
                    $"代理IP: {verify.ProxyIP}\n" +
                    (string.IsNullOrWhiteSpace(verify.ProxyError) ? "" : $"代理错误: {verify.ProxyError}\n") +
                    $"\n结论: {verify.Summary}";

                StandardDialog.ShowInfo("一键验证（应用内是否走代理）", detail);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("验证失败", $"一键验证失败: {ex.Message}");
            }
            finally
            {
                IsTesting = false;
            }
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;

                var json = JsonSerializer.Serialize(_settings, JsonHelper.CnDefault);
                var tmpPtv = _settingsFile + ".tmp";
                await File.WriteAllTextAsync(tmpPtv, json);
                File.Move(tmpPtv, _settingsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyTest] 保存设置失败: {ex.Message}");
            }
        }

        private void LoadHistory()
        {
            TestHistory.Clear();
            var history = _proxyTestService.GetHistory();
            foreach (var result in history)
            {
                TestHistory.Add(result);
            }
        }

        private async System.Threading.Tasks.Task StartTestAsync()
        {
            try
            {
                IsTesting = true;
                GlobalToast.Info("测试中", "正在测试代理...");

                var config = _proxyService.GetConfig();
                var result = await _proxyTestService.TestAll(config);

                LastResult = result;
                TestHistory.Insert(0, result);

                CalculateStatistics();

                if (result.IsConnected)
                {
                    GlobalToast.Success("测试完成", $"匿名性评分: {result.AnonymityScore}分");
                }
                else
                {
                    GlobalToast.Error("测试失败", "代理无法连接");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("测试失败", $"测试过程出错: {ex.Message}");
            }
            finally
            {
                IsTesting = false;
            }
        }

        private void ViewDetailedHistory()
        {
            if (!TestHistory.Any())
            {
                GlobalToast.Info("无历史", "暂无测试历史");
                return;
            }

            var message = $"共有 {TestHistory.Count} 条测试记录\n" +
                         $"成功: {Statistics?.SuccessfulTests}\n" +
                         $"失败: {Statistics?.FailedTests}";
            StandardDialog.ShowInfo("测试历史", message);
        }

        private void AnalyzeTrend()
        {
            try
            {
                var trend = new TestTrendAnalysis
                {
                    LatencyTrend = new List<TrendPoint>(),
                    SpeedTrend = new List<TrendPoint>()
                };

                foreach (var result in TestHistory.Take(10))
                {
                    trend.LatencyTrend.Add(new TrendPoint
                    {
                        Timestamp = result.TestTime,
                        Value = result.Latency
                    });

                    trend.SpeedTrend.Add(new TrendPoint
                    {
                        Timestamp = result.TestTime,
                        Value = result.DownloadSpeed
                    });
                }

                if (trend.LatencyTrend.Count >= 2)
                {
                    var first = trend.LatencyTrend.First().Value;
                    var last = trend.LatencyTrend.Last().Value;

                    if (last > first * 1.1)
                        trend.TrendDirection = "延迟上升";
                    else if (last < first * 0.9)
                        trend.TrendDirection = "延迟下降";
                    else
                        trend.TrendDirection = "延迟稳定";
                }

                TrendAnalysis = trend;
                OnPropertyChanged(nameof(TrendAnalysis));

                GlobalToast.Success("趋势分析", $"趋势: {trend.TrendDirection}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("分析失败", $"趋势分析失败: {ex.Message}");
            }
        }

        private void CompareTests()
        {
            try
            {
                if (TestHistory.Count < 2)
                {
                    GlobalToast.Info("数据不足", "至少需要2条测试记录");
                    return;
                }

                var test1 = TestHistory[0];
                var test2 = TestHistory[1];

                var comparison = new TestComparison
                {
                    Test1 = test1,
                    Test2 = test2,
                    Differences = new List<string>()
                };

                if (Math.Abs(test1.Latency - test2.Latency) > 50)
                {
                    comparison.Differences.Add($"延迟差异: {Math.Abs(test1.Latency - test2.Latency):F0}ms");
                }

                if (Math.Abs(test1.DownloadSpeed - test2.DownloadSpeed) > 1000)
                {
                    comparison.Differences.Add($"速度差异: {Math.Abs(test1.DownloadSpeed - test2.DownloadSpeed)/1024:F1}KB/s");
                }

                comparison.Summary = comparison.Differences.Any() 
                    ? string.Join(", ", comparison.Differences)
                    : "两次测试结果相近";

                GlobalToast.Info("对比完成", comparison.Summary);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("对比失败", $"测试对比失败: {ex.Message}");
            }
        }

        private void ScheduleTest()
        {
            var name = StandardDialog.ShowInput("定时测试", "请输入定时任务名称：", "定时测试");
            if (string.IsNullOrWhiteSpace(name)) return;

            var scheduled = new ScheduledTest
            {
                Name = name,
                Enabled = true,
                Interval = TimeSpan.FromHours(1),
                NextRunTime = DateTime.Now.AddHours(1)
            };

            _settings.ScheduledTests.Add(scheduled);
            ScheduledTests.Add(scheduled);
            SaveSettings();

            GlobalToast.Success("已创建", $"定时测试任务 '{name}' 已创建");
        }

        private async System.Threading.Tasks.Task BatchTestAsync()
        {
            try
            {
                GlobalToast.Info("批量测试", "开始批量测试（模拟）");

                var batch = new BatchTestResult
                {
                    StartTime = DateTime.Now
                };

                for (int i = 0; i < 3; i++)
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    var result = await _proxyTestService.TestAll(_proxyService.GetConfig());
                    batch.Results.Add(result);
                }

                batch.EndTime = DateTime.Now;
                batch.AverageLatency = batch.Results.Average(r => r.Latency);
                batch.Summary = $"完成3次测试，成功{batch.SuccessCount}次";

                GlobalToast.Success("批量完成", batch.Summary);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("批量失败", $"批量测试失败: {ex.Message}");
            }
        }

        private void GenerateTestReport()
        {
            try
            {
                CalculateStatistics();
                AnalyzeTrend();

                var report = new TestReport
                {
                    GeneratedTime = DateTime.Now,
                    Statistics = Statistics ?? new TestStatistics(),
                    TrendAnalysis = TrendAnalysis ?? new TestTrendAnalysis(),
                    RecentTests = TestHistory.Take(10).ToList(),
                    Summary = $"测试报告 - 生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    HealthScore = CalculateHealthScore()
                };

                var message = $"总测试次数: {report.Statistics.TotalTests}\n" +
                             $"成功率: {report.Statistics.SuccessRate:F1}%\n" +
                             $"平均延迟: {report.Statistics.AverageLatency:F0}ms\n" +
                             $"健康评分: {report.HealthScore}分";

                StandardDialog.ShowInfo("测试报告", message);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("报告失败", $"生成报告失败: {ex.Message}");
            }
        }

        private async void ExportTestData()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    FileName = $"proxy_test_data_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(TestHistory.ToList(), JsonHelper.CnDefault);
                    await File.WriteAllTextAsync(dialog.FileName, json);

                    GlobalToast.Success("导出成功", $"数据已保存到: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("导出失败", $"导出数据失败: {ex.Message}");
            }
        }

        private void CalculateStatistics()
        {
            if (!TestHistory.Any()) return;

            Statistics = new TestStatistics
            {
                TotalTests = TestHistory.Count,
                SuccessfulTests = TestHistory.Count(r => r.IsConnected),
                FailedTests = TestHistory.Count(r => !r.IsConnected),
                AverageLatency = TestHistory.Average(r => r.Latency),
                MinLatency = TestHistory.Min(r => r.Latency),
                MaxLatency = TestHistory.Max(r => r.Latency),
                AverageSpeed = TestHistory.Average(r => r.DownloadSpeed)
            };

            OnPropertyChanged(nameof(Statistics));
        }

        private int CalculateHealthScore()
        {
            if (Statistics == null) return 50;

            int score = 100;

            if (Statistics.SuccessRate < 80) score -= 30;
            else if (Statistics.SuccessRate < 90) score -= 15;

            if (Statistics.AverageLatency > 500) score -= 20;
            else if (Statistics.AverageLatency > 200) score -= 10;

            return Math.Max(0, score);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

