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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyChain
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ProxyChainViewModel : INotifyPropertyChanged
    {
        private readonly string _settingsFile;
        private ProxyChainSettings _settings = new();
        private readonly ProxyService _proxyService;
        private readonly ProxyTestService _proxyTestService;

        private ProxyChainConfig? _selectedChainConfig;
        private CancellationTokenSource? _healthCheckCts;

        public ObservableCollection<ProxyChainConfig> ChainConfigs { get; } = new();
        public ObservableCollection<ProxyChainHistory> History { get; } = new();
        public ObservableCollection<ProxyChainPerformance> PerformanceData { get; } = new();
        public ObservableCollection<ProxyChainOptimization> Optimizations { get; } = new();
        public ProxyChainComparison? CurrentComparison { get; private set; }

        public ObservableCollection<ProxyChainConfig> Chains => ChainConfigs;

        public ProxyChainConfig? SelectedChainConfig
        {
            get => _selectedChainConfig;
            set
            {
                _selectedChainConfig = value;
                OnPropertyChanged(nameof(SelectedChainConfig));
                OnPropertyChanged(nameof(SelectedChain));
                OnPropertyChanged(nameof(HasSelectedChain));
                OnPropertyChanged(nameof(IsSelectedChainActive));
            }
        }

        public ProxyChainConfig? SelectedChain
        {
            get => SelectedChainConfig;
            set => SelectedChainConfig = value;
        }

        public bool HasSelectedChain => SelectedChainConfig != null;

        public bool IsSelectedChainActive => SelectedChainConfig != null && _settings.ActiveChainId == SelectedChainConfig.Id;

        public ICommand RefreshCommand { get; }
        public ICommand TestChainCommand { get; }
        public ICommand CreateChainCommand { get; }
        public ICommand EditChainCommand { get; }
        public ICommand DeleteChainCommand { get; }
        public ICommand ViewChainHistoryCommand { get; }
        public ICommand AnalyzePerformanceCommand { get; }
        public ICommand CompareChainsCommand { get; }
        public ICommand OptimizeChainCommand { get; }
        public ICommand ExportChainReportCommand { get; }
        public ICommand SetActiveChainCommand { get; }

        public ProxyChainViewModel(ProxyService proxyService, ProxyTestService proxyTestService)
        {
            _proxyService = proxyService;
            _proxyTestService = proxyTestService;
            _settingsFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy/ProxyChain", "chain_settings.json");

            RefreshCommand = new RelayCommand(LoadChains);
            TestChainCommand = new AsyncRelayCommand(TestChainAsync);
            CreateChainCommand = new RelayCommand(CreateChain);
            EditChainCommand = new RelayCommand(EditChain);
            DeleteChainCommand = new RelayCommand<ProxyChainConfig>(DeleteChain);
            ViewChainHistoryCommand = new RelayCommand(ViewChainHistory);
            AnalyzePerformanceCommand = new RelayCommand(AnalyzePerformance);
            CompareChainsCommand = new RelayCommand(CompareChains);
            OptimizeChainCommand = new RelayCommand<ProxyChainConfig>(OptimizeChain);
            ExportChainReportCommand = new RelayCommand(ExportChainReport);
            SetActiveChainCommand = new RelayCommand(SetActiveChain);

            AsyncSettingsLoader.LoadOrDefer<ProxyChainSettings>(_settingsFile, s =>
            {
                _settings = s;
                History.Clear();
                foreach (var item in _settings.History.OrderByDescending(h => h.StartTime).Take(50))
                    History.Add(item);
                PerformanceData.Clear();
                foreach (var perf in _settings.Performance)
                    PerformanceData.Add(perf);
                LoadChains();
                _ = StartAutoHealthCheckAsync();
            }, "ProxyChain");
            TM.App.Log("[ProxyChainViewModel] 初始化完成");
        }

        ~ProxyChainViewModel()
        {
            StopHealthCheck();
        }

        private async Task StartAutoHealthCheckAsync()
        {
            if (_settings.Chains.Count == 0) return;

            _healthCheckCts = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                while (!_healthCheckCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(60000, _healthCheckCts.Token);

                        if (!string.IsNullOrEmpty(_settings.ActiveChainId))
                        {
                            var activeChain = _settings.Chains.FirstOrDefault(c => c.Id == _settings.ActiveChainId);
                            if (activeChain != null && activeChain.AutoFailover)
                            {
                                await CheckChainHealth(activeChain);
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ProxyChain] 自动健康检查失败: {ex.Message}");
                    }
                }
            }, _healthCheckCts.Token);
        }

        private void StopHealthCheck()
        {
            _healthCheckCts?.Cancel();
            _healthCheckCts?.Dispose();
            _healthCheckCts = null;
        }

        private async Task CheckChainHealth(ProxyChainConfig chain)
        {
            try
            {
                foreach (var node in chain.Nodes)
                {
                    var testResult = await _proxyTestService.TestConnectivity(node.Config);

                    var wasAvailable = node.IsAvailable;
                    node.IsAvailable = testResult.IsConnected;
                    node.Latency = testResult.Latency;

                    if (wasAvailable != node.IsAvailable)
                    {
                        TM.App.Log($"[ProxyChain] 节点 {node.Config.Server}:{node.Config.Port} 状态变更: {(node.IsAvailable ? "可用" : "不可用")}");
                    }
                }

                SaveSettings();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyChain] 健康检查失败: {ex.Message}");
            }
        }

        private void SetActiveChain()
        {
            if (SelectedChainConfig == null)
            {
                GlobalToast.Warning("未选择", "请先选择要设为生效的代理链");
                return;
            }

            try
            {
                _settings.ActiveChainId = SelectedChainConfig.Id;
                SaveSettings();

                _proxyService.RefreshProxy();
                OnPropertyChanged(nameof(IsSelectedChainActive));

                GlobalToast.Success("已生效", $"已设为生效代理链: {SelectedChainConfig.Name}");
                TM.App.Log($"[ProxyChain] 设为生效代理链: {SelectedChainConfig.Name} ({SelectedChainConfig.Id})");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("操作失败", $"设为生效代理链失败: {ex.Message}");
            }
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;

                var json = JsonSerializer.Serialize(_settings, JsonHelper.CnDefault);
                var tmpPcv = _settingsFile + ".tmp";
                await File.WriteAllTextAsync(tmpPcv, json);
                File.Move(tmpPcv, _settingsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyChain] 保存设置失败: {ex.Message}");
            }
        }

        private void LoadChains()
        {
            ChainConfigs.Clear();
            foreach (var chain in _settings.Chains)
            {
                ChainConfigs.Add(chain);
            }
        }

        private async Task TestChainAsync(object? param)
        {
            if (param is not ProxyChainConfig chain) return;

            try
            {
                GlobalToast.Info("测试中", "正在测试代理链...");

                var history = new ProxyChainHistory
                {
                    ChainId = chain.Id,
                    ChainName = chain.Name,
                    StartTime = DateTime.Now,
                    TotalNodes = chain.Nodes.Count
                };

                int successfulNodes = 0;
                double totalLatency = 0;

                foreach (var node in chain.Nodes)
                {
                    var testResult = await _proxyTestService.TestConnectivity(node.Config);
                    node.IsAvailable = testResult.IsConnected;
                    node.Latency = testResult.Latency;

                    if (testResult.IsConnected)
                    {
                        successfulNodes++;
                        totalLatency += testResult.Latency;
                    }
                }

                history.EndTime = DateTime.Now;
                history.Success = successfulNodes > 0;
                history.SuccessfulNodes = successfulNodes;
                history.AverageLatency = successfulNodes > 0 ? totalLatency / successfulNodes : 0;

                _settings.History.Add(history);
                History.Insert(0, history);

                UpdatePerformanceData(chain.Id, history.Success);
                SaveSettings();

                if (history.Success)
                {
                    GlobalToast.Success("测试成功", $"代理链可用 ({successfulNodes}/{chain.Nodes.Count} 节点)");
                }
                else
                {
                    GlobalToast.Error("测试失败", "所有节点均不可用");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("测试失败", $"测试失败: {ex.Message}");
            }
        }

        private void CreateChain()
        {
            var name = StandardDialog.ShowInput("创建代理链", "请输入代理链名称：", "新代理链");
            if (string.IsNullOrWhiteSpace(name)) return;

            var chain = new ProxyChainConfig
            {
                Name = name,
                Description = "新创建的代理链",
                CreatedTime = DateTime.Now
            };

            _settings.Chains.Add(chain);
            ChainConfigs.Add(chain);
            SaveSettings();

            GlobalToast.Success("创建成功", $"代理链 '{name}' 已创建");
        }

        private void EditChain()
        {
            if (SelectedChainConfig == null)
            {
                GlobalToast.Warning("未选择", "请先选择要编辑的代理链");
                return;
            }

            GlobalToast.Info("编辑功能", "编辑代理链配置（功能占位）");
        }

        private void DeleteChain(ProxyChainConfig? chain)
        {
            if (chain == null) return;

            if (StandardDialog.ShowConfirm($"确定要删除代理链 '{chain.Name}' 吗？", "确认删除"))
            {
                _settings.Chains.Remove(chain);
                ChainConfigs.Remove(chain);
                SaveSettings();

                GlobalToast.Success("删除成功", "代理链已删除");
            }
        }

        private void ViewChainHistory()
        {
            if (!History.Any())
            {
                GlobalToast.Info("无历史记录", "暂无代理链使用历史");
                return;
            }

            GlobalToast.Info("历史记录", $"共有 {History.Count} 条使用记录");
        }

        private void AnalyzePerformance()
        {
            try
            {
                PerformanceData.Clear();

                foreach (var chain in _settings.Chains)
                {
                    var chainHistory = _settings.History.Where(h => h.ChainId == chain.Id).ToList();
                    if (!chainHistory.Any()) continue;

                    var perf = new ProxyChainPerformance
                    {
                        ChainId = chain.Id,
                        ChainName = chain.Name,
                        TotalUses = chainHistory.Count,
                        SuccessfulUses = chainHistory.Count(h => h.Success),
                        FailedUses = chainHistory.Count(h => !h.Success),
                        AverageTotalLatency = chainHistory.Average(h => h.AverageLatency),
                        FirstUsed = chainHistory.Min(h => h.StartTime),
                        LastUsed = chainHistory.Max(h => h.StartTime)
                    };

                    PerformanceData.Add(perf);

                    var existing = _settings.Performance.FirstOrDefault(p => p.ChainId == chain.Id);
                    if (existing != null)
                    {
                        _settings.Performance.Remove(existing);
                    }
                    _settings.Performance.Add(perf);
                }

                SaveSettings();
                GlobalToast.Success("分析完成", $"已分析 {PerformanceData.Count} 个代理链");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("分析失败", $"性能分析失败: {ex.Message}");
            }
        }

        private void CompareChains()
        {
            try
            {
                if (PerformanceData.Count < 2)
                {
                    GlobalToast.Info("数据不足", "至少需要2个代理链的性能数据");
                    return;
                }

                var comparison = new ProxyChainComparison
                {
                    ComparisonTime = DateTime.Now,
                    Items = new List<ChainComparisonItem>()
                };

                foreach (var perf in PerformanceData.OrderByDescending(p => p.SuccessRate))
                {
                    var item = new ChainComparisonItem
                    {
                        ChainId = perf.ChainId,
                        ChainName = perf.ChainName,
                        SuccessRate = perf.SuccessRate,
                        AverageLatency = perf.AverageTotalLatency,
                        TotalUses = perf.TotalUses,
                        Rank = comparison.Items.Count + 1
                    };

                    if (item.SuccessRate > 90 && item.AverageLatency < 200)
                        item.PerformanceGrade = "A";
                    else if (item.SuccessRate > 75 && item.AverageLatency < 500)
                        item.PerformanceGrade = "B";
                    else if (item.SuccessRate > 50)
                        item.PerformanceGrade = "C";
                    else
                        item.PerformanceGrade = "D";

                    comparison.Items.Add(item);
                }

                if (comparison.Items.Any())
                {
                    var best = comparison.Items.First();
                    comparison.BestChainId = best.ChainId;
                    comparison.BestChainName = best.ChainName;
                    comparison.Summary = $"最佳代理链: {best.ChainName} (评级: {best.PerformanceGrade})";
                }

                CurrentComparison = comparison;
                OnPropertyChanged(nameof(CurrentComparison));

                GlobalToast.Success("对比完成", comparison.Summary);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("对比失败", $"代理链对比失败: {ex.Message}");
            }
        }

        private void OptimizeChain(ProxyChainConfig? chain)
        {
            if (chain == null) return;

            try
            {
                var perf = PerformanceData.FirstOrDefault(p => p.ChainId == chain.Id);
                if (perf == null)
                {
                    GlobalToast.Warning("无数据", "该代理链暂无性能数据");
                    return;
                }

                var optimization = new ProxyChainOptimization
                {
                    ChainId = chain.Id,
                    ChainName = chain.Name,
                    Suggestions = new List<string>()
                };

                if (perf.SuccessRate < 80)
                {
                    optimization.Suggestions.Add("成功率较低，建议检查节点配置");
                    optimization.Priority = OptimizationPriority.High;
                }

                if (perf.AverageTotalLatency > 500)
                {
                    optimization.Suggestions.Add("延迟较高，建议优化节点顺序或更换节点");
                    optimization.Priority = OptimizationPriority.Medium;
                }

                if (!optimization.Suggestions.Any())
                {
                    optimization.Suggestions.Add("代理链运行良好，无需优化");
                    optimization.Priority = OptimizationPriority.Low;
                }

                optimization.Summary = string.Join("; ", optimization.Suggestions);

                Optimizations.Add(optimization);
                GlobalToast.Success("优化建议", optimization.Summary);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("优化失败", $"生成优化建议失败: {ex.Message}");
            }
        }

        private async void ExportChainReport()
        {
            try
            {
                var report = new ProxyChainReport
                {
                    GeneratedTime = DateTime.Now,
                    TotalChains = _settings.Chains.Count,
                    ActiveChains = _settings.Chains.Count(c => c.Enabled),
                    TopPerformers = PerformanceData.OrderByDescending(p => p.SuccessRate).Take(3).ToList(),
                    PoorPerformers = PerformanceData.OrderBy(p => p.SuccessRate).Take(3).ToList(),
                    Comparison = CurrentComparison ?? new ProxyChainComparison(),
                    Optimizations = Optimizations.ToList(),
                    Summary = $"代理链报告 - 生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    HealthScore = CalculateHealthScore()
                };

                var dialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    FileName = $"proxy_chain_report_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(report, JsonHelper.CnDefault);
                    var filePath = dialog.FileName;
                    await Task.Run(() =>
                    {
                        var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, filePath, overwrite: true);
                    });

                    GlobalToast.Success("导出成功", $"报告已保存到: {filePath}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("导出失败", $"导出报告失败: {ex.Message}");
            }
        }

        private void UpdatePerformanceData(string chainId, bool success)
        {
            var perf = _settings.Performance.FirstOrDefault(p => p.ChainId == chainId);
            if (perf == null)
            {
                perf = new ProxyChainPerformance { ChainId = chainId };
                _settings.Performance.Add(perf);
            }

            perf.TotalUses++;
            if (success) perf.SuccessfulUses++;
            else perf.FailedUses++;
            perf.LastUsed = DateTime.Now;
        }

        private int CalculateHealthScore()
        {
            if (!PerformanceData.Any()) return 50;

            int score = 0;
            foreach (var perf in PerformanceData)
            {
                if (perf.SuccessRate > 90) score += 30;
                else if (perf.SuccessRate > 75) score += 20;
                else if (perf.SuccessRate > 50) score += 10;

                if (perf.AverageTotalLatency < 200) score += 20;
                else if (perf.AverageTotalLatency < 500) score += 10;
            }

            return Math.Min(100, score / Math.Max(1, PerformanceData.Count));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

