using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading.Tasks;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    public class SystemFollowController
    {
        private readonly SystemThemeMonitor _monitor;
        private readonly ThemeManager _themeManager;
        private readonly SceneDetector _sceneDetector;
        private readonly StatisticsAnalyzer _statsAnalyzer;
        private SystemFollowSettings _settings;
        private DateTime _lastSwitchTime;
        private System.Threading.Timer? _debounceTimer;
        private string? _pendingTheme;

        public SystemFollowController(
            SystemThemeMonitor monitor,
            ThemeManager themeManager,
            SceneDetector sceneDetector)
        {
            _monitor = monitor;
            _themeManager = themeManager;
            _sceneDetector = sceneDetector;
            _statsAnalyzer = new StatisticsAnalyzer();
            _settings = LoadSettings();

            _monitor.ThemeChanged += OnSystemThemeChanged;

            TM.App.Log("[SystemFollowController] 初始化完成");
        }

        public void Initialize()
        {
            if (_settings.Enabled && _settings.AutoStart)
            {
                _monitor.StartMonitoring();
                TM.App.Log("[SystemFollowController] 自动启动监听");
            }
        }

        public void Enable()
        {
            if (!_settings.Enabled)
            {
                _settings.Enabled = true;
                SaveSettings();
                _monitor.StartMonitoring();

                var themeInfo = _monitor.DetectCurrentTheme();
                var targetTheme = DetermineTargetTheme(themeInfo);
                _ = SwitchWithDelay(targetTheme);

                TM.App.Log("[SystemFollowController] 已启用跟随系统");
            }
        }

        public void Disable()
        {
            if (_settings.Enabled)
            {
                _settings.Enabled = false;
                SaveSettings();
                _monitor.StopMonitoring();
                TM.App.Log("[SystemFollowController] 已禁用跟随系统");
            }
        }

        private void OnSystemThemeChanged(object? sender, SystemThemeChangedEventArgs e)
        {
            if (!_settings.Enabled) return;

            if (IsInExclusionPeriod())
            {
                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[SystemFollowController] 当前在排除时间段内，跳过切换");
                }
                return;
            }

            if (_settings.EnableSceneDetection)
            {
                var scene = _sceneDetector.DetectCurrentScene(_settings.SceneRules);
                if (scene.IsActive && scene.DisableSwitching)
                {
                    TM.App.Log($"[SystemFollowController] 当前场景 '{scene.SceneName}' 禁用切换");
                    return;
                }
            }

            if (_settings.EnableSmartDelay && _lastSwitchTime != DateTime.MinValue)
            {
                var timeSinceLastSwitch = (DateTime.Now - _lastSwitchTime).TotalSeconds;
                if (timeSinceLastSwitch < _settings.MinSwitchInterval)
                {
                    TM.App.Log($"[SystemFollowController] 距离上次切换仅 {timeSinceLastSwitch:F0}秒，未达到最小间隔 {_settings.MinSwitchInterval}秒，跳过切换");
                    return;
                }
            }

            ThemeType targetTheme;

            if (e.IsHighContrast)
            {
                targetTheme = _settings.HighContrastMapping switch
                {
                    HighContrastBehavior.Ignore => _themeManager.CurrentTheme,
                    HighContrastBehavior.UseLight => ThemeType.Light,
                    HighContrastBehavior.UseDark => ThemeType.Dark,
                    HighContrastBehavior.Custom => _settings.HighContrastCustomTheme,
                    _ => _themeManager.CurrentTheme
                };
            }
            else
            {
                targetTheme = e.IsLightTheme ? _settings.LightThemeMapping : _settings.DarkThemeMapping;
            }

            if (_settings.EnableSmartDelay && _settings.DebounceDelay > 0)
            {
                _pendingTheme = ((int)targetTheme).ToString();
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(_ =>
                {
                    if (_pendingTheme != null && int.TryParse(_pendingTheme, out var themeInt) && Enum.IsDefined(typeof(ThemeType), themeInt))
                    {
                        _ = SwitchWithDelay((ThemeType)themeInt);
                    }
                }, null, _settings.DebounceDelay * 1000, System.Threading.Timeout.Infinite);

                TM.App.Log($"[SystemFollowController] 防抖动延迟 {_settings.DebounceDelay}秒后切换到 {targetTheme}");
            }
            else
            {
                _ = SwitchWithDelay(targetTheme);
            }
        }

        private async Task SwitchWithDelay(ThemeType targetTheme)
        {
            if (_themeManager.CurrentTheme == targetTheme)
            {
                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log($"[SystemFollowController] 已是目标主题 {targetTheme}，跳过切换");
                }
                return;
            }

            if (_settings.DelaySeconds > 0)
            {
                await Task.Delay(_settings.DelaySeconds * 1000);
            }

            try
            {
                var startTime = DateTime.Now;
                var fromTheme = _themeManager.CurrentTheme;

                _themeManager.SwitchTheme(targetTheme);

                var duration = DateTime.Now - startTime;

                _statsAnalyzer.AddSwitchRecord(fromTheme, targetTheme, duration);

                _settings.LastSwitchTime = DateTime.Now;
                _settings.TotalSwitchCount++;
                _lastSwitchTime = DateTime.Now;
                SaveSettings();

                TM.App.Log($"[SystemFollowController] 切换完成，耗时: {duration.TotalMilliseconds:F0}ms");

                if (_settings.ShowNotification)
                {
                    TM.App.Log($"[SystemFollowController] 已切换到 {targetTheme} 主题");
                }

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log($"[SystemFollowController] 主题切换完成: {targetTheme}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowController] 主题切换失败: {ex.Message}");
            }
        }

        private ThemeType DetermineTargetTheme(string themeInfo)
        {
            if (themeInfo.Contains("高对比度"))
            {
                return _settings.HighContrastMapping switch
                {
                    HighContrastBehavior.Ignore => _themeManager.CurrentTheme,
                    HighContrastBehavior.UseLight => ThemeType.Light,
                    HighContrastBehavior.UseDark => ThemeType.Dark,
                    HighContrastBehavior.Custom => _settings.HighContrastCustomTheme,
                    _ => _themeManager.CurrentTheme
                };
            }

            return themeInfo.Contains("浅色") ? _settings.LightThemeMapping : _settings.DarkThemeMapping;
        }

        private bool IsInExclusionPeriod()
        {
            if (_settings.ExclusionPeriods == null || _settings.ExclusionPeriods.Count == 0)
                return false;

            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;
            var currentDay = now.DayOfWeek;

            foreach (var period in _settings.ExclusionPeriods)
            {
                if ((period.Days & currentDay) != currentDay)
                    continue;

                if (period.StartTime <= period.EndTime)
                {
                    if (currentTime >= period.StartTime && currentTime <= period.EndTime)
                        return true;
                }
                else
                {
                    if (currentTime >= period.StartTime || currentTime <= period.EndTime)
                        return true;
                }
            }

            return false;
        }

        public void TestSwitch()
        {
            var themeInfo = _monitor.DetectCurrentTheme();
            var targetTheme = DetermineTargetTheme(themeInfo);

            TM.App.Log($"[SystemFollowController] 测试切换: 系统={themeInfo}, 目标={targetTheme}");

            try
            {
                _themeManager.SwitchTheme(targetTheme);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowController] 测试切换失败: {ex.Message}");
            }
        }

        public SystemFollowSettings GetSettings()
        {
            return _settings;
        }

        public StatisticsAnalyzer GetStatisticsAnalyzer()
        {
            return _statsAnalyzer;
        }

        public void UpdateSettings(SystemFollowSettings settings)
        {
            _settings = settings;
            SaveSettings();

            if (_settings.Enabled)
            {
                _monitor.StartMonitoring();
            }
            else
            {
                _monitor.StopMonitoring();
            }

            TM.App.Log("[SystemFollowController] 设置已更新");
        }

        private SystemFollowSettings LoadSettings()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/SystemFollow", "settings.json");

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var settings = JsonSerializer.Deserialize<SystemFollowSettings>(json);
                    if (settings != null)
                    {
                        TM.App.Log("[SystemFollowController] 设置加载成功");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowController] 加载设置失败: {ex.Message}");
            }

            return SystemFollowSettings.CreateDefault();
        }

        private void SaveSettings()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/SystemFollow", "settings.json");
                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpSfc = filePath + ".tmp";
                File.WriteAllText(tmpSfc, json);
                File.Move(tmpSfc, filePath, overwrite: true);

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[SystemFollowController] 设置已保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowController] 保存设置失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/SystemFollow", "settings.json");
                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpSfcA = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpSfcA, json);
                File.Move(tmpSfcA, filePath, overwrite: true);

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[SystemFollowController] 设置已异步保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemFollowController] 异步保存设置失败: {ex.Message}");
            }
        }
    }
}

