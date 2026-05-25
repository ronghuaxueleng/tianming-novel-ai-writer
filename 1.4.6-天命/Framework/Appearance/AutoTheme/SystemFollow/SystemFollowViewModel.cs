using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemFollowViewModel : INotifyPropertyChanged
    {
        private readonly SystemFollowController _controller;
        private readonly SystemThemeMonitor _monitor;
        private readonly TimeBased.TimeScheduleService _timeScheduleService;
        private readonly ThemeManager _themeManager;
        private readonly ConflictResolver _conflictResolver;
        private SystemFollowSettings _settings = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        private bool _autoStart;
        public bool AutoStart
        {
            get => _autoStart;
            set { if (_autoStart != value) { _autoStart = value; OnPropertyChanged(); } }
        }

        private string _currentSystemTheme = "未检测";
        public string CurrentSystemTheme
        {
            get => _currentSystemTheme;
            set { if (_currentSystemTheme != value) { _currentSystemTheme = value; OnPropertyChanged(); } }
        }

        private bool _isHighContrast;
        public bool IsHighContrast
        {
            get => _isHighContrast;
            set { if (_isHighContrast != value) { _isHighContrast = value; OnPropertyChanged(); } }
        }

        private string _accentColor = "#未知";
        public string AccentColor
        {
            get => _accentColor;
            set { if (_accentColor != value) { _accentColor = value; OnPropertyChanged(); } }
        }

        private double _systemDPI;
        public double SystemDPI
        {
            get => _systemDPI;
            set { if (Math.Abs(_systemDPI - value) > 0.01) { _systemDPI = value; OnPropertyChanged(); } }
        }

        private string _colorMode = "未知";
        public string ColorMode
        {
            get => _colorMode;
            set { if (_colorMode != value) { _colorMode = value; OnPropertyChanged(); } }
        }

        private bool _transparencyEnabled;
        public bool TransparencyEnabled
        {
            get => _transparencyEnabled;
            set { if (_transparencyEnabled != value) { _transparencyEnabled = value; OnPropertyChanged(); } }
        }

        private bool _windowAnimationEnabled;
        public bool WindowAnimationEnabled
        {
            get => _windowAnimationEnabled;
            set { if (_windowAnimationEnabled != value) { _windowAnimationEnabled = value; OnPropertyChanged(); } }
        }

        private bool _isAeroEnabled;
        public bool IsAeroEnabled
        {
            get => _isAeroEnabled;
            set { if (_isAeroEnabled != value) { _isAeroEnabled = value; OnPropertyChanged(); } }
        }

        private string _themeName = "未知";
        public string ThemeName
        {
            get => _themeName;
            set { if (_themeName != value) { _themeName = value; OnPropertyChanged(); } }
        }

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            set { if (_isMonitoring != value) { _isMonitoring = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<ThemeChangeRecord> ThemeChangeHistory { get; set; } = null!;

        public ObservableCollection<MonitorInfo> Monitors { get; set; } = null!;

        private ThemeType _lightThemeMapping;
        public ThemeType LightThemeMapping
        {
            get => _lightThemeMapping;
            set { if (_lightThemeMapping != value) { _lightThemeMapping = value; OnPropertyChanged(); } }
        }

        private ThemeType _darkThemeMapping;
        public ThemeType DarkThemeMapping
        {
            get => _darkThemeMapping;
            set { if (_darkThemeMapping != value) { _darkThemeMapping = value; OnPropertyChanged(); } }
        }

        private HighContrastBehavior _highContrastMapping;
        public HighContrastBehavior HighContrastMapping
        {
            get => _highContrastMapping;
            set { if (_highContrastMapping != value) { _highContrastMapping = value; OnPropertyChanged(); } }
        }

        private ThemeType _highContrastCustomTheme;
        public ThemeType HighContrastCustomTheme
        {
            get => _highContrastCustomTheme;
            set { if (_highContrastCustomTheme != value) { _highContrastCustomTheme = value; OnPropertyChanged(); } }
        }

        private int _delaySeconds;
        public int DelaySeconds
        {
            get => _delaySeconds;
            set { if (_delaySeconds != value) { _delaySeconds = value; OnPropertyChanged(); } }
        }

        private bool _showNotification;
        public bool ShowNotification
        {
            get => _showNotification;
            set { if (_showNotification != value) { _showNotification = value; OnPropertyChanged(); } }
        }

        private bool _enableAccentColor;
        public bool EnableAccentColor
        {
            get => _enableAccentColor;
            set { if (_enableAccentColor != value) { _enableAccentColor = value; OnPropertyChanged(); } }
        }

        private bool _onlyWhenNotManual;
        public bool OnlyWhenNotManual
        {
            get => _onlyWhenNotManual;
            set { if (_onlyWhenNotManual != value) { _onlyWhenNotManual = value; OnPropertyChanged(); } }
        }

        private bool _enableVerboseLog;
        public bool EnableVerboseLog
        {
            get => _enableVerboseLog;
            set { if (_enableVerboseLog != value) { _enableVerboseLog = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<ExclusionPeriodItem> ExclusionPeriods { get; set; } = null!;

        private string _lastSwitchTime = "从未切换";
        public string LastSwitchTime
        {
            get => _lastSwitchTime;
            set { if (_lastSwitchTime != value) { _lastSwitchTime = value; OnPropertyChanged(); } }
        }

        private int _totalSwitchCount;
        public int TotalSwitchCount
        {
            get => _totalSwitchCount;
            set { if (_totalSwitchCount != value) { _totalSwitchCount = value; OnPropertyChanged(); } }
        }

        private int _dailySwitchCount;
        public int DailySwitchCount
        {
            get => _dailySwitchCount;
            set { if (_dailySwitchCount != value) { _dailySwitchCount = value; OnPropertyChanged(); } }
        }

        private int _weeklySwitchCount;
        public int WeeklySwitchCount
        {
            get => _weeklySwitchCount;
            set { if (_weeklySwitchCount != value) { _weeklySwitchCount = value; OnPropertyChanged(); } }
        }

        private int _monthlySwitchCount;
        public int MonthlySwitchCount
        {
            get => _monthlySwitchCount;
            set { if (_monthlySwitchCount != value) { _monthlySwitchCount = value; OnPropertyChanged(); } }
        }

        private string _avgSwitchDuration = "0ms";
        public string AvgSwitchDuration
        {
            get => _avgSwitchDuration;
            set { if (_avgSwitchDuration != value) { _avgSwitchDuration = value; OnPropertyChanged(); } }
        }

        private string _lastSwitchDuration = "0ms";
        public string LastSwitchDuration
        {
            get => _lastSwitchDuration;
            set { if (_lastSwitchDuration != value) { _lastSwitchDuration = value; OnPropertyChanged(); } }
        }

        private string _performanceRating = "暂无数据";
        public string PerformanceRating
        {
            get => _performanceRating;
            set { if (_performanceRating != value) { _performanceRating = value; OnPropertyChanged(); } }
        }

        private string _mostUsedTheme = "暂无数据";
        public string MostUsedTheme
        {
            get => _mostUsedTheme;
            set { if (_mostUsedTheme != value) { _mostUsedTheme = value; OnPropertyChanged(); } }
        }

        private string _peakSwitchPeriod = "暂无数据";
        public string PeakSwitchPeriod
        {
            get => _peakSwitchPeriod;
            set { if (_peakSwitchPeriod != value) { _peakSwitchPeriod = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<ConflictWarning> ConflictWarnings { get; set; } = null!;

        private bool _hasConflicts;
        public bool HasConflicts
        {
            get => _hasConflicts;
            set { if (_hasConflicts != value) { _hasConflicts = value; OnPropertyChanged(); } }
        }

        public ICommand ToggleEnabledCommand { get; set; } = null!;
        public ICommand TestSwitchCommand { get; set; } = null!;
        public ICommand RefreshSystemInfoCommand { get; set; } = null!;
        public ICommand AddExclusionPeriodCommand { get; set; } = null!;
        public ICommand RemoveExclusionPeriodCommand { get; set; } = null!;
        public ICommand ApplySettingsCommand { get; set; } = null!;
        public ICommand RefreshMonitorsCommand { get; set; } = null!;
        public ICommand RefreshHistoryCommand { get; set; } = null!;
        public ICommand RefreshStatisticsCommand { get; set; } = null!;
        public ICommand DetectConflictsCommand { get; set; } = null!;

        public SystemFollowViewModel(
            SystemFollowController controller,
            SystemThemeMonitor monitor,
            TimeBased.TimeScheduleService timeScheduleService,
            ThemeManager themeManager,
            ConflictResolver conflictResolver)
        {
            _controller = controller;
            _monitor = monitor;
            _timeScheduleService = timeScheduleService;
            _themeManager = themeManager;
            _conflictResolver = conflictResolver;

            ExclusionPeriods = new ObservableCollection<ExclusionPeriodItem>();
            ThemeChangeHistory = new ObservableCollection<ThemeChangeRecord>();
            Monitors = new ObservableCollection<MonitorInfo>();
            ConflictWarnings = new ObservableCollection<ConflictWarning>();

            _controller.Initialize();

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var s = _controller.GetSettings();
                return () =>
                {
                    _settings = s;
                    _enabled = s.Enabled; _autoStart = s.AutoStart;
                    _lightThemeMapping = s.LightThemeMapping; _darkThemeMapping = s.DarkThemeMapping;
                    _highContrastMapping = s.HighContrastMapping; _highContrastCustomTheme = s.HighContrastCustomTheme;
                    _delaySeconds = s.DelaySeconds; _showNotification = s.ShowNotification;
                    _enableAccentColor = s.EnableAccentColor; _onlyWhenNotManual = s.OnlyWhenNotManual;
                    _enableVerboseLog = s.EnableVerboseLog; _totalSwitchCount = s.TotalSwitchCount;
                    _lastSwitchTime = s.LastSwitchTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "从未切换";
                    _currentSystemTheme = s.LastDetectedTheme;
                    ExclusionPeriods.Clear();
                    if (s.ExclusionPeriods != null)
                        foreach (var p in s.ExclusionPeriods)
                            ExclusionPeriods.Add(new ExclusionPeriodItem { StartTime = p.StartTime, EndTime = p.EndTime, Days = p.Days, Description = p.Description });
                    OnPropertyChanged(string.Empty);
                };
            }, "SystemFollow");

            ToggleEnabledCommand = new RelayCommand(ToggleEnabled);
            TestSwitchCommand = new RelayCommand(TestSwitch);
            RefreshSystemInfoCommand = new RelayCommand(RefreshSystemInfo);
            AddExclusionPeriodCommand = new RelayCommand(AddExclusionPeriod);
            RemoveExclusionPeriodCommand = new RelayCommand(param =>
            {
                if (param is ExclusionPeriodItem item)
                {
                    RemoveExclusionPeriod(item);
                }
            });
            ApplySettingsCommand = new RelayCommand(ApplySettings);
            RefreshMonitorsCommand = new RelayCommand(RefreshMonitors);
            RefreshHistoryCommand = new RelayCommand(RefreshHistory);
            RefreshStatisticsCommand = new RelayCommand(RefreshStatistics);
            DetectConflictsCommand = new RelayCommand(DetectConflicts);

            RefreshSystemInfo();
            RefreshMonitors();
            RefreshHistory();
            RefreshStatistics();
        }

        private void ToggleEnabled()
        {
            if (Enabled)
            {
                _controller.Disable();
                Enabled = false;
            }
            else
            {
                _controller.Enable();
                Enabled = true;
            }
        }

        private void TestSwitch()
        {
            try
            {
            _controller.TestSwitch();
            ToastNotification.ShowSuccess("测试成功", "测试切换已执行");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 测试切换失败: {ex.Message}");
                StandardDialog.ShowError($"测试切换失败: {ex.Message}", "错误", null);
            }
        }

        private void RefreshSystemInfo()
        {
            try
            {
                CurrentSystemTheme = _monitor.DetectCurrentTheme();
                IsHighContrast = _monitor.IsHighContrastMode();
                AccentColor = _monitor.GetAccentColor();
                IsMonitoring = _monitor.IsMonitoring;

                var status = _monitor.GetSystemStatus();
                SystemDPI = status.DPI;
                ColorMode = status.ColorMode;
                TransparencyEnabled = status.TransparencyEnabled;
                WindowAnimationEnabled = status.WindowAnimationEnabled;
                IsAeroEnabled = status.IsAeroEnabled;
                ThemeName = status.ThemeName;

                if (_settings != null) _settings.LastDetectedTheme = CurrentSystemTheme;

                TM.App.Log($"[SystemFollowViewModel] 系统信息已刷新: DPI={SystemDPI}, 色彩={ColorMode}, Aero={IsAeroEnabled}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 刷新系统信息失败: {ex.Message}");
            }
        }

        private void RefreshMonitors()
        {
            try
            {
                Monitors.Clear();
                var monitors = _monitor.DetectMultipleMonitors();
                foreach (var monitor in monitors)
                {
                    Monitors.Add(monitor);
                }

                TM.App.Log($"[SystemFollowViewModel] 已刷新显示器信息，共 {Monitors.Count} 个");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 刷新显示器信息失败: {ex.Message}");
            }
        }

        private void RefreshHistory()
        {
            try
            {
                ThemeChangeHistory.Clear();
                var history = _monitor.GetChangeHistory();
                foreach (var record in history)
                {
                    ThemeChangeHistory.Add(record);
                }

                TM.App.Log($"[SystemFollowViewModel] 已刷新变化历史，共 {ThemeChangeHistory.Count} 条");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 刷新变化历史失败: {ex.Message}");
            }
        }

        private void RefreshStatistics()
        {
            try
            {
                var analyzer = _controller.GetStatisticsAnalyzer();

                DailySwitchCount = analyzer.GetDailySwitchCount();
                WeeklySwitchCount = analyzer.GetWeeklySwitchCount();
                MonthlySwitchCount = analyzer.GetMonthlySwitchCount();

                var avgDuration = analyzer.GetAverageSwitchDuration();
                AvgSwitchDuration = $"{avgDuration.TotalMilliseconds:F0}ms";

                var lastDuration = analyzer.GetLastSwitchDuration();
                LastSwitchDuration = $"{lastDuration.TotalMilliseconds:F0}ms";

                PerformanceRating = analyzer.GetPerformanceRating();

                var mostUsed = analyzer.GetMostUsedTheme();
                MostUsedTheme = mostUsed.ToString();

                PeakSwitchPeriod = analyzer.GetPeakSwitchPeriod();

                TM.App.Log("[SystemFollowViewModel] 统计数据已刷新");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 刷新统计数据失败: {ex.Message}");
            }
        }

        private void AddExclusionPeriod()
        {
            var newPeriod = new ExclusionPeriodItem
            {
                StartTime = TimeSpan.FromHours(12),
                EndTime = TimeSpan.FromHours(14),
                Days = DayOfWeek.Monday | DayOfWeek.Tuesday | DayOfWeek.Wednesday | DayOfWeek.Thursday | DayOfWeek.Friday,
                Description = "新的排除时间段"
            };

            ExclusionPeriods.Add(newPeriod);
        }

        private void RemoveExclusionPeriod(ExclusionPeriodItem item)
        {
            ExclusionPeriods.Remove(item);
        }

        private void ApplySettings()
        {
            try
            {
                _settings.Enabled = Enabled;
                _settings.AutoStart = AutoStart;
                _settings.LightThemeMapping = LightThemeMapping;
                _settings.DarkThemeMapping = DarkThemeMapping;
                _settings.HighContrastMapping = HighContrastMapping;
                _settings.HighContrastCustomTheme = HighContrastCustomTheme;
                _settings.DelaySeconds = DelaySeconds;
                _settings.ShowNotification = ShowNotification;
                _settings.EnableAccentColor = EnableAccentColor;
                _settings.OnlyWhenNotManual = OnlyWhenNotManual;
                _settings.EnableVerboseLog = EnableVerboseLog;

                _settings.ExclusionPeriods = ExclusionPeriods.Select(p => new ExclusionPeriod
                {
                    StartTime = p.StartTime,
                    EndTime = p.EndTime,
                    Days = p.Days,
                    Description = p.Description
                }).ToList();

            _controller.UpdateSettings(_settings);

            ToastNotification.ShowSuccess("保存成功", "设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 应用设置失败: {ex.Message}");
                StandardDialog.ShowError($"保存设置失败: {ex.Message}", "错误", null);
            }
        }

        private void DetectConflicts()
        {
            try
            {
                ConflictWarnings.Clear();

                var timeBasedController = _timeScheduleService;
                var timeBasedSettings = timeBasedController.GetSettings();

                if (!timeBasedSettings.Enabled)
                {
                    HasConflicts = false;
                    TM.App.Log("[SystemFollowViewModel] 定时切换未启用，无冲突");
                    ToastNotification.ShowInfo("无冲突", "定时切换未启用");
                    return;
                }

                var themeInfo = _monitor.DetectCurrentTheme();
                var isLightTheme = themeInfo == "浅色主题";
                var systemFollowTheme = isLightTheme ? LightThemeMapping : DarkThemeMapping;

                var timeBasedThemeNullable = timeBasedController.CalculateCurrentTheme();
                var timeBasedTheme = timeBasedThemeNullable ?? _themeManager.CurrentTheme;

                var resolver = _conflictResolver;
                var timeBasedPriority = 5;
                var conflictInfo = resolver.DetectConflict(
                    Enabled,
                    _settings.Priority,
                    systemFollowTheme,
                    timeBasedSettings.Enabled,
                    timeBasedPriority,
                    timeBasedTheme
                );

                if (conflictInfo != null && conflictInfo.HasConflict)
                {
                    var warning = new ConflictWarning
                    {
                        Message = conflictInfo.Description,
                        Severity = conflictInfo.Winner == "跟随系统" ? "信息" : "警告",
                        Recommendation = conflictInfo.Winner == "跟随系统" 
                            ? "跟随系统优先级更高，将应用系统主题" 
                            : $"定时切换优先级更高，建议调整优先级或禁用其中一个功能"
                    };

                    ConflictWarnings.Add(warning);
                    HasConflicts = true;

                    TM.App.Log($"[SystemFollowViewModel] 检测到冲突: {conflictInfo.Description}");
                    ToastNotification.ShowWarning("检测到冲突", $"发现 {ConflictWarnings.Count} 个冲突");
                }
                else
                {
                    HasConflicts = false;
                    TM.App.Log("[SystemFollowViewModel] 未检测到冲突");
                    ToastNotification.ShowSuccess("无冲突", "当前配置无冲突");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowViewModel] 冲突检测失败: {ex.Message}");
                StandardDialog.ShowError($"冲突检测失败: {ex.Message}", "错误", null);
            }
        }
    }

    public class ExclusionPeriodItem
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public DayOfWeek Days { get; set; }
        public string Description { get; set; } = string.Empty;

        public string DisplayText => $"{StartTime:hh\\:mm} - {EndTime:hh\\:mm}: {Description}";
    }

    public class ConflictWarning
    {
        public string Message { get; set; } = string.Empty;

        public string Severity { get; set; } = "信息";

        public string Recommendation { get; set; } = string.Empty;

        public string Icon => Severity switch
        {
            "错误" => "❌",
            "警告" => "⚠️",
            "信息" => "ℹ️",
            _ => "ℹ️"
        };
    }
}

