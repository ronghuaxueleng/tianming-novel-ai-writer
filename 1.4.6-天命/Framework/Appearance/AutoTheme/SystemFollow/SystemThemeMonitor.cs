using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    public class SystemThemeMonitor
    {
        private static readonly object _debugLogLock = new object();
        private static readonly HashSet<string> _debugLoggedKeys = new HashSet<string>();

        private bool _isMonitoring;
        private readonly object _changeHistoryLock = new object();
        private readonly List<ThemeChangeRecord> _changeHistory = new();
        private const int MaxHistoryCount = 20;

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

            System.Diagnostics.Debug.WriteLine($"[SystemThemeMonitor] {key}: {ex.Message}");
        }

        public event EventHandler<SystemThemeChangedEventArgs>? ThemeChanged;

        public SystemThemeMonitor()
        {
            TM.App.Log("[SystemThemeMonitor] 初始化完成");
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _isMonitoring = true;
                TM.App.Log("[SystemThemeMonitor] 开始监听系统主题变化");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 启动监听失败: {ex.Message}");
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            try
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _isMonitoring = false;
                TM.App.Log("[SystemThemeMonitor] 停止监听系统主题变化");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 停止监听失败: {ex.Message}");
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && 
                e.Category != UserPreferenceCategory.VisualStyle &&
                e.Category != UserPreferenceCategory.Color)
            {
                return;
            }

            try
            {
                var startTime = DateTime.Now;
                var previousTheme = _changeHistory.Count > 0 ? _changeHistory[0].ToTheme : "未知";
                var themeInfo = DetectCurrentTheme();
                var duration = DateTime.Now - startTime;

                TM.App.Log($"[SystemThemeMonitor] 检测到系统主题变化: {themeInfo}");

                AddChangeRecord(previousTheme, themeInfo, duration);

                ThemeChanged?.Invoke(this, new SystemThemeChangedEventArgs
                {
                    IsLightTheme = themeInfo.Contains("浅色"),
                    IsHighContrast = themeInfo.Contains("高对比度"),
                    AccentColor = GetAccentColor(),
                    DetectedAt = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 处理主题变化失败: {ex.Message}");
            }
        }

        public string DetectCurrentTheme()
        {
            try
            {
                bool isLightTheme = IsLightTheme();
                bool isHighContrast = SystemParameters.HighContrast;

                if (isHighContrast)
                {
                    return "高对比度模式";
                }

                return isLightTheme ? "浅色主题" : "深色主题";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 检测主题失败: {ex.Message}");
                return "未知";
            }
        }

        private bool IsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 1;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 读取注册表失败: {ex.Message}");
            }

            return true;
        }

        public bool IsHighContrastMode()
        {
            return SystemParameters.HighContrast;
        }

        public string GetAccentColor()
        {
            try
            {
                var brush = SystemParameters.WindowGlassBrush;
                if (brush != null)
                {
                    return brush.ToString();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 获取强调色失败: {ex.Message}");
            }

            return "#未知";
        }

        public SystemStatusDetails GetSystemStatus()
        {
            try
            {
                return new SystemStatusDetails
                {
                    DPI = GetSystemDPI(),
                    ColorMode = GetColorMode(),
                    TransparencyEnabled = SystemParameters.MinimizeAnimation,
                    WindowAnimationEnabled = SystemParameters.MinimizeAnimation,
                    IsAeroEnabled = IsAeroEnabled(),
                    ThemeName = GetCurrentThemeName(),
                    DesktopCompositionEnabled = IsDesktopCompositionEnabled()
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 获取系统状态失败: {ex.Message}");
                return new SystemStatusDetails();
            }
        }

        private double GetSystemDPI()
        {
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetSystemDPI), ex);
                return 96.0;
            }
        }

        private string GetColorMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Colors");

                if (key != null)
                {
                    var windowColor = key.GetValue("Window");
                    if (windowColor != null)
                    {
                        return windowColor.ToString()?.StartsWith("255") == true ? "浅色" : "深色";
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetColorMode), ex);
            }

            return "未知";
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        private bool IsAeroEnabled()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    DwmIsCompositionEnabled(out bool enabled);
                    return enabled;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(IsAeroEnabled), ex);
            }

            return false;
        }

        private bool IsDesktopCompositionEnabled()
        {
            return IsAeroEnabled();
        }

        private string GetCurrentThemeName()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes");

                if (key != null)
                {
                    var currentTheme = key.GetValue("CurrentTheme");
                    if (currentTheme != null)
                    {
                        var path = currentTheme.ToString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            return System.IO.Path.GetFileNameWithoutExtension(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetCurrentThemeName), ex);
            }

            return "未知";
        }

        public List<MonitorInfo> DetectMultipleMonitors()
        {
            var monitors = new List<MonitorInfo>();

            try
            {
                var screens = Screen.AllScreens;
                int index = 1;

                foreach (var screen in screens)
                {
                    monitors.Add(new MonitorInfo
                    {
                        Name = screen.DeviceName,
                        IsPrimary = screen.Primary,
                        Resolution = $"{screen.Bounds.Width}x{screen.Bounds.Height}",
                        WorkingArea = $"{screen.WorkingArea.Width}x{screen.WorkingArea.Height}",
                        BitsPerPixel = screen.BitsPerPixel,
                        Index = index++
                    });
                }

                TM.App.Log($"[SystemThemeMonitor] 检测到 {monitors.Count} 个显示器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemThemeMonitor] 检测显示器失败: {ex.Message}");
            }

            return monitors;
        }

        public List<ThemeChangeRecord> GetChangeHistory()
        {
            lock (_changeHistoryLock)
            {
                return new List<ThemeChangeRecord>(_changeHistory);
            }
        }

        private void AddChangeRecord(string fromTheme, string toTheme, TimeSpan duration)
        {
            lock (_changeHistoryLock)
            {
                var record = new ThemeChangeRecord
                {
                    Timestamp = DateTime.Now,
                    FromTheme = fromTheme,
                    ToTheme = toTheme,
                    Duration = duration,
                    Details = $"{fromTheme} → {toTheme}"
                };

                _changeHistory.Insert(0, record);

                if (_changeHistory.Count > MaxHistoryCount)
                {
                    _changeHistory.RemoveRange(MaxHistoryCount, _changeHistory.Count - MaxHistoryCount);
                }
            }
        }

        public bool IsMonitoring => _isMonitoring;
    }

    public class SystemThemeChangedEventArgs : EventArgs
    {
        public bool IsLightTheme { get; set; }

        public bool IsHighContrast { get; set; }

        public string AccentColor { get; set; } = string.Empty;

        public DateTime DetectedAt { get; set; }
    }

    public class SystemStatusDetails
    {
        public double DPI { get; set; }

        public string ColorMode { get; set; } = "未知";

        public bool TransparencyEnabled { get; set; }

        public bool WindowAnimationEnabled { get; set; }

        public bool IsAeroEnabled { get; set; }

        public string ThemeName { get; set; } = "未知";

        public bool DesktopCompositionEnabled { get; set; }
    }

    public class MonitorInfo
    {
        public int Index { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public string Resolution { get; set; } = string.Empty;

        public string WorkingArea { get; set; } = string.Empty;

        public int BitsPerPixel { get; set; }

        public string DisplayText => $"{(IsPrimary ? "🖥️ 主显示器" : "🖥️")} {Index}: {Resolution} ({BitsPerPixel}位色彩)";
    }

    public class ThemeChangeRecord
    {
        public DateTime Timestamp { get; set; }

        public string FromTheme { get; set; } = string.Empty;

        public string ToTheme { get; set; } = string.Empty;

        public TimeSpan Duration { get; set; }

        public string Details { get; set; } = string.Empty;

        public string DisplayText => $"{Timestamp:HH:mm:ss} - {Details} (耗时: {Duration.TotalMilliseconds:F0}ms)";
    }
}

