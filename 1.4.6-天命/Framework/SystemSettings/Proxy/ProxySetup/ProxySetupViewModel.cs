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
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxySetup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ProxySetupViewModel : INotifyPropertyChanged
    {
        private readonly string _settingsFile;
        private ProxySetupSettings _settings = new();
        private readonly ProxyService _proxyService;
        private readonly ProxyTestService _proxyTestService;

        private ProxyType _selectedProxyType;
        private string _server = string.Empty;
        private int _port = 8080;
        private bool _requiresAuth;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private bool _enableSystemProxy;
        private bool _pacEnabled;
        private string _pacScript = string.Empty;
        private string _newBypass = string.Empty;

        public ObservableCollection<ProxyType> ProxyTypes { get; } = new()
        {
            ProxyType.HTTP,
            ProxyType.HTTPS,
            ProxyType.SOCKS5,
            ProxyType.SOCKS4
        };

        public ObservableCollection<string> BypassList { get; } = new();

        public ObservableCollection<ProxyConfigHistory> History { get; } = new();
        public ObservableCollection<ProxyConfigPreset> Presets { get; } = new();
        public ObservableCollection<ProxyRecommendation> Recommendations { get; } = new();
        public ProxyUsageStatistics Statistics { get; private set; } = new();
        public ProxyConfigComparison? CurrentComparison { get; private set; }

        public ProxyType SelectedProxyType
        {
            get => _selectedProxyType;
            set { _selectedProxyType = value; OnPropertyChanged(nameof(SelectedProxyType)); }
        }

        public string Server
        {
            get => _server;
            set { _server = value; OnPropertyChanged(nameof(Server)); }
        }

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(nameof(Port)); }
        }

        public bool RequiresAuth
        {
            get => _requiresAuth;
            set { _requiresAuth = value; OnPropertyChanged(nameof(RequiresAuth)); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(nameof(Username)); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(nameof(Password)); }
        }

        public bool EnableSystemProxy
        {
            get => _enableSystemProxy;
            set { _enableSystemProxy = value; OnPropertyChanged(nameof(EnableSystemProxy)); }
        }

        public bool PACEnabled
        {
            get => _pacEnabled;
            set { _pacEnabled = value; OnPropertyChanged(nameof(PACEnabled)); }
        }

        public string PACScript
        {
            get => _pacScript;
            set { _pacScript = value; OnPropertyChanged(nameof(PACScript)); }
        }

        public string NewBypass
        {
            get => _newBypass;
            set { _newBypass = value; OnPropertyChanged(nameof(NewBypass)); }
        }

        public ICommand SaveCommand { get; }
        public ICommand TestCommand { get; }
        public ICommand AddBypassCommand { get; }
        public ICommand RemoveBypassCommand { get; }
        public ICommand LoadDefaultPACCommand { get; }

        public ICommand ViewHistoryCommand { get; }
        public ICommand RestoreConfigCommand { get; }
        public ICommand ViewStatisticsCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand CompareConfigsCommand { get; }
        public ICommand GenerateRecommendationCommand { get; }
        public ICommand ExportReportCommand { get; }

        public ProxySetupViewModel(ProxyService proxyService, ProxyTestService proxyTestService)
        {
            _proxyService = proxyService;
            _proxyTestService = proxyTestService;
            _settingsFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy/ProxySetup", "setup_settings.json");

            SaveCommand = new RelayCommand(Save);
            TestCommand = new RelayCommand(Test);
            AddBypassCommand = new RelayCommand(AddBypass);
            RemoveBypassCommand = new RelayCommand<string>(RemoveBypass);
            LoadDefaultPACCommand = new RelayCommand(LoadDefaultPAC);

            ViewHistoryCommand = new RelayCommand(ViewHistory);
            RestoreConfigCommand = new RelayCommand<ProxyConfigHistory>(RestoreConfig);
            ViewStatisticsCommand = new RelayCommand(ViewStatistics);
            SavePresetCommand = new RelayCommand(SavePreset);
            ApplyPresetCommand = new RelayCommand<ProxyConfigPreset>(ApplyPreset);
            DeletePresetCommand = new RelayCommand<ProxyConfigPreset>(DeletePreset);
            CompareConfigsCommand = new RelayCommand(CompareConfigs);
            GenerateRecommendationCommand = new RelayCommand(GenerateRecommendation);
            ExportReportCommand = new RelayCommand(ExportReport);

            AsyncSettingsLoader.LoadOrDefer<ProxySetupSettings>(_settingsFile, s =>
            {
                _settings = s;
                History.Clear();
                foreach (var item in _settings.History.OrderByDescending(h => h.Timestamp).Take(50))
                    History.Add(item);
                Presets.Clear();
                foreach (var preset in _settings.Presets.OrderByDescending(p => p.IsFavorite).ThenByDescending(p => p.LastUsedTime))
                    Presets.Add(preset);
                Statistics = _settings.Statistics;
                OnPropertyChanged(nameof(Statistics));
                LoadConfig();
            }, "ProxySetup");
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;

                var json = JsonSerializer.Serialize(_settings, JsonHelper.CnDefault);
                var tmpPsv = _settingsFile + ".tmp";
                await File.WriteAllTextAsync(tmpPsv, json);
                File.Move(tmpPsv, _settingsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxySetup] 保存设置失败: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                var config = _proxyService.GetConfig();

                SelectedProxyType = config.Type;
                Server = config.Server;
                Port = config.Port;
                RequiresAuth = config.RequiresAuth;
                Username = config.Username;
                Password = config.Password;
                EnableSystemProxy = config.EnableSystemProxy;
                PACEnabled = config.PACEnabled;
                PACScript = config.PACScript;

                BypassList.Clear();
                foreach (var item in config.BypassList)
                {
                    BypassList.Add(item);
                }

                if (!BypassList.Any())
                {
                    BypassList.Add("localhost");
                    BypassList.Add("127.0.0.1");
                    BypassList.Add("*.local");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("加载失败", $"加载配置失败: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                var oldConfig = _proxyService.GetConfig();
                var oldConfigJson = JsonSerializer.Serialize(oldConfig);

                var config = new ProxyConfig
                {
                    Type = SelectedProxyType,
                    Server = Server,
                    Port = Port,
                    RequiresAuth = RequiresAuth,
                    Username = Username,
                    Password = Password,
                    EnableSystemProxy = EnableSystemProxy,
                    PACEnabled = PACEnabled,
                    PACScript = PACScript,
                    BypassList = BypassList.ToList()
                };

                var newConfigJson = JsonSerializer.Serialize(config);

                if (oldConfigJson != newConfigJson)
                {
                    var history = new ProxyConfigHistory
                    {
                        Timestamp = DateTime.Now,
                        ConfigBefore = oldConfigJson,
                        ConfigAfter = newConfigJson,
                        ModifyReason = "手动保存",
                        ModifiedFields = GetModifiedFields(oldConfig, config)
                    };

                    _settings.History.Add(history);
                    History.Insert(0, history);

                    if (_settings.History.Count > 100)
                    {
                        _settings.History = _settings.History.OrderByDescending(h => h.Timestamp).Take(100).ToList();
                    }
                }

                _settings.Statistics.TotalEnableCount++;
                if (!_settings.Statistics.TypeUsageCount.ContainsKey(config.Type.ToString()))
                {
                    _settings.Statistics.TypeUsageCount[config.Type.ToString()] = 0;
                }
                _settings.Statistics.TypeUsageCount[config.Type.ToString()]++;
                _settings.Statistics.LastUsedTime = DateTime.Now;

                if (_settings.Statistics.FirstUsedTime == default)
                {
                    _settings.Statistics.FirstUsedTime = DateTime.Now;
                }

                OnPropertyChanged(nameof(Statistics));

                _proxyService.SaveConfig(config);
                SaveSettings();

                if (config.Type == ProxyType.SOCKS4 || config.Type == ProxyType.SOCKS5)
                {
                    GlobalToast.Warning("协议限制", $"当前不支持 {config.Type} 代理协议，应用内代理将回退到直连。建议选择 HTTP 或 HTTPS 类型。");
                    TM.App.Log($"[ProxySetup] 代理配置已保存但 {config.Type} 协议不受支持: {Server}:{Port}");
                }
                else
                {
                    GlobalToast.Success("保存成功", "代理配置已保存");
                    TM.App.Log($"[ProxySetup] 代理配置已保存: {Server}:{Port}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("保存失败", $"保存配置失败: {ex.Message}");
                TM.App.Log($"[ProxySetup] 保存失败: {ex.Message}");
            }
        }

        private string GetModifiedFields(ProxyConfig old, ProxyConfig newConfig)
        {
            var fields = new List<string>();

            if (old.Type != newConfig.Type) fields.Add("Type");
            if (old.Server != newConfig.Server) fields.Add("Server");
            if (old.Port != newConfig.Port) fields.Add("Port");
            if (old.RequiresAuth != newConfig.RequiresAuth) fields.Add("RequiresAuth");
            if (old.Username != newConfig.Username) fields.Add("Username");
            if (old.EnableSystemProxy != newConfig.EnableSystemProxy) fields.Add("EnableSystemProxy");
            if (old.PACEnabled != newConfig.PACEnabled) fields.Add("PACEnabled");

            return string.Join(", ", fields);
        }

        private void Test()
        {
            _ = TestAsync();
        }

        private async Task TestAsync()
        {
            try
            {
                var config = new ProxyConfig
                {
                    Type = SelectedProxyType,
                    Server = Server,
                    Port = Port,
                    RequiresAuth = RequiresAuth,
                    Username = Username,
                    Password = Password
                };

                GlobalToast.Info("测试中", "正在测试代理连接...");

                var result = await _proxyTestService.TestConnectivity(config);

                if (result.IsConnected)
                {
                    GlobalToast.Success("测试成功", $"代理连接成功！延迟: {result.Latency}ms");
                }
                else
                {
                    GlobalToast.Error("测试失败", "代理服务器无法连接");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("测试失败", $"测试过程出错: {ex.Message}");
            }
        }

        private void AddBypass()
        {
            if (!string.IsNullOrWhiteSpace(NewBypass) && !BypassList.Contains(NewBypass))
            {
                BypassList.Add(NewBypass);
                NewBypass = string.Empty;
            }
        }

        private void RemoveBypass(string? item)
        {
            if (item != null)
            {
                BypassList.Remove(item);
            }
        }

        private void LoadDefaultPAC()
        {
            PACScript = @"function FindProxyForURL(url, host) {
    // 本地地址直连
    if (isPlainHostName(host) ||
        shExpMatch(host, ""*.local"") ||
        isInNet(dnsResolve(host), ""10.0.0.0"", ""255.0.0.0"") ||
        isInNet(dnsResolve(host), ""172.16.0.0"",  ""255.240.0.0"") ||
        isInNet(dnsResolve(host), ""192.168.0.0"",  ""255.255.0.0"") ||
        isInNet(dnsResolve(host), ""127.0.0.0"", ""255.255.255.0""))
        return ""DIRECT"";

    // 国内网站直连
    if (shExpMatch(host, ""*.cn"") ||
        shExpMatch(host, ""*.baidu.com"") ||
        shExpMatch(host, ""*.qq.com""))
        return ""DIRECT"";

    // 其他走代理
    return ""PROXY " + Server + ":" + Port + @"; DIRECT"";
}";
            GlobalToast.Info("已加载", "已加载默认PAC脚本模板");
        }

        private void ViewHistory()
        {
            if (!History.Any())
            {
                GlobalToast.Info("无历史记录", "暂无配置变更历史");
                return;
            }

            GlobalToast.Info("历史记录", $"共有 {History.Count} 条配置变更记录");
        }

        private void RestoreConfig(ProxyConfigHistory? history)
        {
            if (history == null) return;

            try
            {
                if (StandardDialog.ShowConfirm($"确定要恢复到 {history.Timestamp:yyyy-MM-dd HH:mm:ss} 的配置吗？", "确认恢复"))
                {
                    var config = JsonSerializer.Deserialize<ProxyConfig>(history.ConfigBefore);
                    if (config != null)
                    {
                        SelectedProxyType = config.Type;
                        Server = config.Server;
                        Port = config.Port;
                        RequiresAuth = config.RequiresAuth;
                        Username = config.Username;
                        Password = config.Password;
                        EnableSystemProxy = config.EnableSystemProxy;
                        PACEnabled = config.PACEnabled;
                        PACScript = config.PACScript;

                        BypassList.Clear();
                        foreach (var item in config.BypassList)
                        {
                            BypassList.Add(item);
                        }

                        GlobalToast.Success("恢复成功", "配置已恢复，请保存生效");
                        TM.App.Log($"[ProxySetup] 配置已恢复到: {history.Timestamp}");
                    }
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("恢复失败", $"恢复配置失败: {ex.Message}");
                TM.App.Log($"[ProxySetup] 恢复配置失败: {ex.Message}");
            }
        }

        private void ViewStatistics()
        {
            var stats = Statistics;
            var message = $"总启用次数: {stats.TotalEnableCount}\n" +
                         $"成功连接: {stats.SuccessfulConnections}\n" +
                         $"失败连接: {stats.FailedConnections}\n" +
                         $"成功率: {stats.SuccessRate:F1}%\n" +
                         $"平均延迟: {stats.AverageLatency:F0}ms\n" +
                         $"总流量: {stats.TotalTrafficFormatted}";

            StandardDialog.ShowInfo("使用统计", message);
        }

        private void SavePreset()
        {
            try
            {
                var name = StandardDialog.ShowInput("保存预设", "请输入预设方案名称：", "我的预设");
                if (string.IsNullOrWhiteSpace(name)) return;

                var preset = new ProxyConfigPreset
                {
                    Name = name,
                    Description = $"保存于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    Config = new ProxyConfig
                    {
                        Type = SelectedProxyType,
                        Server = Server,
                        Port = Port,
                        RequiresAuth = RequiresAuth,
                        Username = Username,
                        Password = Password,
                        EnableSystemProxy = EnableSystemProxy,
                        PACEnabled = PACEnabled,
                        PACScript = PACScript,
                        BypassList = BypassList.ToList()
                    },
                    CreatedTime = DateTime.Now
                };

                _settings.Presets.Add(preset);
                Presets.Insert(0, preset);
                SaveSettings();

                GlobalToast.Success("保存成功", $"预设方案 '{name}' 已保存");
                TM.App.Log($"[ProxySetup] 预设方案已保存: {name}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("保存失败", $"保存预设失败: {ex.Message}");
            }
        }

        private void ApplyPreset(ProxyConfigPreset? preset)
        {
            if (preset == null) return;

            try
            {
                var config = preset.Config;
                SelectedProxyType = config.Type;
                Server = config.Server;
                Port = config.Port;
                RequiresAuth = config.RequiresAuth;
                Username = config.Username;
                Password = config.Password;
                EnableSystemProxy = config.EnableSystemProxy;
                PACEnabled = config.PACEnabled;
                PACScript = config.PACScript;

                BypassList.Clear();
                foreach (var item in config.BypassList)
                {
                    BypassList.Add(item);
                }

                preset.LastUsedTime = DateTime.Now;
                preset.UsageCount++;
                SaveSettings();

                GlobalToast.Success("应用成功", $"已应用预设方案 '{preset.Name}'");
                TM.App.Log($"[ProxySetup] 应用预设: {preset.Name}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("应用失败", $"应用预设失败: {ex.Message}");
            }
        }

        private void DeletePreset(ProxyConfigPreset? preset)
        {
            if (preset == null) return;

            if (StandardDialog.ShowConfirm($"确定要删除预设 '{preset.Name}' 吗？", "确认删除"))
            {
                _settings.Presets.Remove(preset);
                Presets.Remove(preset);
                SaveSettings();

                GlobalToast.Success("删除成功", "预设已删除");
            }
        }

        private void CompareConfigs()
        {
            try
            {
                if (History.Count < 2)
                {
                    GlobalToast.Info("历史不足", "至少需要2条历史记录才能对比");
                    return;
                }

                var latestHistory = History.First();
                var oldConfig = JsonSerializer.Deserialize<ProxyConfig>(latestHistory.ConfigBefore);
                var newConfig = JsonSerializer.Deserialize<ProxyConfig>(latestHistory.ConfigAfter);

                if (oldConfig == null || newConfig == null) return;

                var comparison = new ProxyConfigComparison
                {
                    Config1Name = $"修改前 ({latestHistory.Timestamp:HH:mm:ss})",
                    Config2Name = $"修改后 ({latestHistory.Timestamp:HH:mm:ss})",
                    Differences = new List<ConfigDifference>()
                };

                if (oldConfig.Type != newConfig.Type)
                {
                    comparison.Differences.Add(new ConfigDifference
                    {
                        FieldName = "代理类型",
                        Config1Value = oldConfig.Type.ToString(),
                        Config2Value = newConfig.Type.ToString(),
                        Level = DifferenceLevel.Major
                    });
                }

                if (oldConfig.Server != newConfig.Server)
                {
                    comparison.Differences.Add(new ConfigDifference
                    {
                        FieldName = "服务器地址",
                        Config1Value = oldConfig.Server,
                        Config2Value = newConfig.Server,
                        Level = DifferenceLevel.Major
                    });
                }

                if (oldConfig.Port != newConfig.Port)
                {
                    comparison.Differences.Add(new ConfigDifference
                    {
                        FieldName = "端口",
                        Config1Value = oldConfig.Port.ToString(),
                        Config2Value = newConfig.Port.ToString(),
                        Level = DifferenceLevel.Moderate
                    });
                }

                comparison.Summary = $"共发现 {comparison.Differences.Count} 处差异";
                CurrentComparison = comparison;
                OnPropertyChanged(nameof(CurrentComparison));

                GlobalToast.Info("对比完成", comparison.Summary);
            }
            catch (Exception ex)
            {
                GlobalToast.Error("对比失败", $"配置对比失败: {ex.Message}");
            }
        }

        private void GenerateRecommendation()
        {
            try
            {
                Recommendations.Clear();

                if (Statistics.FailedConnections > Statistics.SuccessfulConnections)
                {
                    Recommendations.Add(new ProxyRecommendation
                    {
                        RecommendationType = "稳定性优化",
                        Title = "提高连接成功率",
                        Description = "检测到较高的失败率，建议检查代理服务器配置",
                        Reason = $"当前失败率: {(Statistics.FailedConnections * 100.0 / Statistics.TotalEnableCount):F1}%",
                        Priority = 1,
                        Benefits = new List<string> { "提高稳定性", "减少连接失败", "优化用户体验" }
                    });
                }

                if (Statistics.AverageLatency > 1000)
                {
                    Recommendations.Add(new ProxyRecommendation
                    {
                        RecommendationType = "性能优化",
                        Title = "优化延迟表现",
                        Description = "当前延迟较高，建议更换更快的代理服务器",
                        Reason = $"当前平均延迟: {Statistics.AverageLatency:F0}ms",
                        Priority = 2,
                        Benefits = new List<string> { "降低延迟", "提升访问速度", "改善体验" }
                    });
                }

                Recommendations.Add(new ProxyRecommendation
                {
                    RecommendationType = "场景推荐",
                    Title = "使用预设方案",
                    Description = "根据不同使用场景快速切换代理配置",
                    Reason = "提高配置管理效率",
                    Priority = 3,
                    Benefits = new List<string> { "快速切换", "场景适配", "便捷管理" }
                });

                GlobalToast.Success("推荐生成", $"已生成 {Recommendations.Count} 条优化建议");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("生成失败", $"生成推荐失败: {ex.Message}");
            }
        }

        private async void ExportReport()
        {
            try
            {
                var report = new ProxyConfigReport
                {
                    GeneratedTime = DateTime.Now,
                    CurrentConfig = _proxyService.GetConfig(),
                    Statistics = Statistics,
                    RecentHistory = History.Take(10).ToList(),
                    Recommendations = Recommendations.ToList(),
                    Summary = $"代理配置报告 - 生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    HealthScore = CalculateHealthScore(),
                    HealthIssues = GetHealthIssues()
                };

                var dialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    FileName = $"proxy_setup_report_{DateTime.Now:yyyyMMdd_HHmmss}.json"
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
                    TM.App.Log($"[ProxySetup] 报告已导出: {filePath}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("导出失败", $"导出报告失败: {ex.Message}");
            }
        }

        private void CreateDefaultPresets()
        {
            _settings.Presets = new List<ProxyConfigPreset>
            {
                new ProxyConfigPreset
                {
                    Name = "家庭网络",
                    Description = "适合家庭网络环境的默认配置",
                    Icon = "🏠",
                    Config = new ProxyConfig { Type = ProxyType.HTTP, Server = "127.0.0.1", Port = 8080 }
                },
                new ProxyConfigPreset
                {
                    Name = "办公室",
                    Description = "适合办公环境的代理配置",
                    Icon = "🏢",
                    Config = new ProxyConfig { Type = ProxyType.HTTP, Server = "proxy.company.com", Port = 8080, RequiresAuth = true }
                },
                new ProxyConfigPreset
                {
                    Name = "公共WiFi",
                    Description = "公共场所WiFi的安全配置",
                    Icon = "📶",
                    Config = new ProxyConfig { Type = ProxyType.SOCKS5, Server = "127.0.0.1", Port = 1080 }
                }
            };

            foreach (var preset in _settings.Presets)
            {
                Presets.Add(preset);
            }

            SaveSettings();
        }

        private int CalculateHealthScore()
        {
            int score = 100;

            if (Statistics.TotalEnableCount > 0)
            {
                var failureRate = Statistics.FailedConnections * 100.0 / Statistics.TotalEnableCount;
                score -= (int)(failureRate / 2);
            }

            if (Statistics.AverageLatency > 1000) score -= 20;
            else if (Statistics.AverageLatency > 500) score -= 10;

            return Math.Max(0, score);
        }

        private List<string> GetHealthIssues()
        {
            var issues = new List<string>();

            if (Statistics.FailedConnections > Statistics.SuccessfulConnections)
            {
                issues.Add("连接失败率过高");
            }

            if (Statistics.AverageLatency > 1000)
            {
                issues.Add("平均延迟过高");
            }

            if (!History.Any())
            {
                issues.Add("暂无配置变更历史");
            }

            return issues;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

