using System;
using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using TM.Services.Framework.Settings;
using TM.Framework.Appearance.Animation.ThemeTransition;
using System.Text.Json;

namespace TM.Framework.Appearance.ThemeManagement
{
    public class ThemeManager
    {
        private readonly SettingsManager _settings;
        private readonly ThemeTransitionService _transitionService;
        private ThemeType _currentTheme;
        private string? _currentThemeFileName;

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        private static void DebugLog(string message)
        {
            if (TM.App.IsDebugMode)
            {
                Debug.WriteLine(message);
            }
        }

        public ThemeManager(SettingsManager settings, ThemeTransitionService transitionService)
        {
            _settings = settings;
            _transitionService = transitionService;
            _currentTheme = LoadThemePreference();
            _currentThemeFileName = LoadThemeFilePreference();
            DebugLog($"[ThemeManager] 初始化完成，当前主题: {_currentTheme}");
        }

        public ThemeType CurrentTheme => _currentTheme;

        public string? CurrentThemeFileName => _currentThemeFileName;

        public void Initialize()
        {
            try
            {
                if (_currentTheme == ThemeType.Custom && !string.IsNullOrWhiteSpace(_currentThemeFileName))
                {
                    ApplyThemeFromFileWithoutAnimation(_currentThemeFileName);
                }
                else
                {
                    ApplyTheme(_currentTheme, false);
                }
                DebugLog($"[ThemeManager] 主题系统初始化成功: {_currentTheme}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeManager] 主题初始化失败，已回退到浅色主题: {ex.Message}");
                ApplyTheme(ThemeType.Light, false);
                _currentTheme = ThemeType.Light;
                _currentThemeFileName = null;
                SaveThemePreference(ThemeType.Light);
                SaveThemeFilePreference(string.Empty);
            }
        }

        public void SwitchTheme(ThemeType theme)
        {
            if (_currentTheme == theme)
            {
                DebugLog($"[ThemeManager] 主题未变更: {theme}");
                return;
            }

            void SwitchThemeCore()
            {
                try
                {
                    var transitionSettings = LoadTransitionSettings();

                    if (transitionSettings != null && transitionSettings.Effect != TransitionEffect.None)
                    {
                        var transitionService = _transitionService;
                        var windows = Application.Current.Windows.OfType<Window>().ToList();

                        int pending = 0;
                        bool applied = false;

                        void CompleteOne()
                        {
                            pending--;
                            if (pending <= 0 && !applied)
                            {
                                applied = true;
                                ApplyTheme(theme, true);
                                _currentTheme = theme;
                                _currentThemeFileName = null;
                                SaveThemePreference(theme);
                                SaveThemeFilePreference(string.Empty);
                                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
                                DebugLog($"[ThemeManager] 主题已切换（带动画）: {theme}");
                            }
                        }

                        foreach (var w in windows)
                        {
                            if (w.Content is FrameworkElement content)
                            {
                                pending++;
                                transitionService.PrepareElement(content);
                                transitionService.PlayTransition(content, transitionSettings, CompleteOne);
                            }
                        }

                        if (pending == 0)
                        {
                            ApplyThemeWithoutAnimation(theme);
                        }
                    }
                    else
                    {
                        ApplyThemeWithoutAnimation(theme);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ThemeManager] 主题切换失败: {ex.Message}");
                    throw;
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
                SwitchThemeCore();
            else
                dispatcher?.BeginInvoke((Action)SwitchThemeCore);
        }

        private void ApplyThemeWithoutAnimation(ThemeType theme)
        {
            ApplyTheme(theme, true);
            _currentTheme = theme;
            _currentThemeFileName = null;
            SaveThemePreference(theme);
            SaveThemeFilePreference(string.Empty);
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
            DebugLog($"[ThemeManager] 主题已切换（无动画）: {theme}");
        }

        public void ApplyThemeFromFile(string themeFileName)
        {
            if (string.IsNullOrWhiteSpace(themeFileName))
                throw new ArgumentNullException(nameof(themeFileName));

            var normalizedFileName = NormalizeThemeFileName(themeFileName);
            if (_currentTheme == ThemeType.Custom &&
                string.Equals(_currentThemeFileName, normalizedFileName, StringComparison.OrdinalIgnoreCase))
            {
                DebugLog($"[ThemeManager] 自定义主题未变更: {normalizedFileName}");
                return;
            }

            void ApplyThemeFromFileCore()
            {
                try
                {
                    var transitionSettings = LoadTransitionSettings();

                    if (transitionSettings != null && transitionSettings.Effect != TransitionEffect.None)
                    {
                        var themeUri = GetThemeFileUri(normalizedFileName);
                        var transitionService = _transitionService;
                        var windows = Application.Current.Windows.OfType<Window>().ToList();

                        int pending = 0;
                        bool applied = false;

                        void CompleteOne()
                        {
                            pending--;
                            if (pending <= 0 && !applied)
                            {
                                applied = true;
                                ApplyThemeUri(themeUri);
                                _currentTheme = ThemeType.Custom;
                                _currentThemeFileName = normalizedFileName;
                                SaveThemePreference(ThemeType.Custom);
                                SaveThemeFilePreference(normalizedFileName);
                                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(ThemeType.Custom));
                                DebugLog($"[ThemeManager] 主题已切换（带动画）: {normalizedFileName}");
                            }
                        }

                        foreach (var w in windows)
                        {
                            if (w.Content is FrameworkElement content)
                            {
                                pending++;
                                transitionService.PrepareElement(content);
                                transitionService.PlayTransition(content, transitionSettings, CompleteOne);
                            }
                        }

                        if (pending == 0)
                        {
                            ApplyThemeFromFileWithoutAnimation(normalizedFileName);
                        }
                    }
                    else
                    {
                        ApplyThemeFromFileWithoutAnimation(normalizedFileName);
                    }
                }
                catch (Exception ex)
                {
                    DebugLog($"[ThemeManager] 应用主题文件失败: {ex.Message}");
                    throw;
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
                ApplyThemeFromFileCore();
            else
                dispatcher?.BeginInvoke((Action)ApplyThemeFromFileCore);
        }

        public System.Collections.Generic.List<string> GetAvailableThemes()
        {
            try
            {
                var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
                if (Directory.Exists(themesPath))
                {
                    var themeFiles = Directory.GetFiles(themesPath, "*.xaml");
                    var themes = themeFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
                    TM.App.Log($"[ThemeManager] 找到 {themes.Count} 个可用主题");
                    return themes;
                }
                else
                {
                    TM.App.Log($"[ThemeManager] 主题目录不存在: {themesPath}");
                    return new System.Collections.Generic.List<string>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeManager] 获取主题列表失败: {ex.Message}");
                return new System.Collections.Generic.List<string>();
            }
        }

        public void ApplyTheme(string themeName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(themeName))
                    throw new ArgumentNullException(nameof(themeName));

                if (TryConvertThemeNameToType(themeName, out var themeType))
                {
                    SwitchTheme(themeType);
                }
                else
                {
                    ApplyThemeFromFile(themeName);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeManager] 应用主题失败: {ex.Message}");
                throw;
            }
        }

        private bool TryConvertThemeNameToType(string themeName, out ThemeType themeType)
        {
            themeName = themeName.Replace(".xaml", "").Replace("Theme", "");

            switch (themeName)
            {
                case "Light": themeType = ThemeType.Light; return true;
                case "Dark": themeType = ThemeType.Dark; return true;
                case "Auto": themeType = ThemeType.Auto; return true;
                case "Green": themeType = ThemeType.Green; return true;
                case "Business": themeType = ThemeType.Business; return true;
                case "ModernBlue": themeType = ThemeType.ModernBlue; return true;
                case "Violet": themeType = ThemeType.Violet; return true;
                case "WarmOrange": themeType = ThemeType.WarmOrange; return true;
                case "Pink": themeType = ThemeType.Pink; return true;
                case "TechCyan": themeType = ThemeType.TechCyan; return true;
                case "MinimalBlack": themeType = ThemeType.MinimalBlack; return true;
                case "Arctic": themeType = ThemeType.Arctic; return true;
                case "Forest": themeType = ThemeType.Forest; return true;
                case "Sunset": themeType = ThemeType.Sunset; return true;
                case "Morandi": themeType = ThemeType.Morandi; return true;
                case "HighContrast": themeType = ThemeType.HighContrast; return true;
                default:
                    themeType = default;
                    return false;
            }
        }

        private ThemeTransitionSettings? LoadTransitionSettings()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/ThemeTransition",
                    "settings.json"
                );

                if (System.IO.File.Exists(settingsFile))
                {
                    var json = System.IO.File.ReadAllText(settingsFile);
                    return JsonSerializer.Deserialize<ThemeTransitionSettings>(json);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 加载过渡设置失败: {ex.Message}");
            }
            return null;
        }

        private ResourceDictionary? _currentThemeDict;

        private void ApplyTheme(ThemeType theme, bool clearCache)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                ApplyThemeCore(theme, clearCache);
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(() => ApplyThemeCore(theme, clearCache));
            }
        }

        private void ApplyThemeCore(ThemeType theme, bool clearCache)
        {
            try
            {
                var builtInDict = BuiltInThemes.CreateResourceDictionary(theme);
                if (builtInDict != null)
                {
                    ApplyResourceDictionary(builtInDict);
                }
                else
                {
                    var themeUri = GetThemeResourceUri(theme);
                    var fileDict = new ResourceDictionary { Source = themeUri };
                    ApplyResourceDictionary(fileDict);
                }

                TM.App.Log($"[ThemeManager] 主题切换成功: {theme}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeManager] 主题切换失败: {ex.Message}");
                throw;
            }
        }

        private void ApplyResourceDictionary(ResourceDictionary newTheme)
        {
            var mergedDicts = Application.Current.Resources.MergedDictionaries;

            if (_currentThemeDict != null)
            {
                mergedDicts.Remove(_currentThemeDict);
            }
            else
            {
                var oldThemes = new List<ResourceDictionary>();
                foreach (var dict in mergedDicts)
                {
                    if (dict.Source != null &&
                        (dict.Source.OriginalString.Contains("/Themes/") ||
                         dict.Source.OriginalString.Contains("\\Themes\\")) &&
                        dict.Source.OriginalString.EndsWith("Theme.xaml"))
                    {
                        oldThemes.Add(dict);
                    }
                }
                foreach (var old in oldThemes)
                {
                    mergedDicts.Remove(old);
                }
            }

            mergedDicts.Insert(0, newTheme);
            _currentThemeDict = newTheme;
        }

        private void ApplyThemeUri(Uri themeUri)
        {
            var fileDict = new ResourceDictionary { Source = themeUri };
            ApplyResourceDictionary(fileDict);
        }

        private void ApplyThemeFromFileWithoutAnimation(string themeFileName)
        {
            var normalizedFileName = NormalizeThemeFileName(themeFileName);
            var themeUri = GetThemeFileUri(normalizedFileName);
            ApplyThemeUri(themeUri);
            _currentTheme = ThemeType.Custom;
            _currentThemeFileName = normalizedFileName;
            SaveThemePreference(ThemeType.Custom);
            SaveThemeFilePreference(normalizedFileName);
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(ThemeType.Custom));
            DebugLog($"[ThemeManager] 主题已切换（无动画）: {normalizedFileName}");
        }

        private Uri GetThemeFileUri(string themeFileName)
        {
            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            var themeFilePath = Path.Combine(themesPath, themeFileName);

            if (!File.Exists(themeFilePath))
                throw new FileNotFoundException($"找不到主题文件: {themeFileName}", themeFilePath);

            return new Uri(themeFilePath, UriKind.Absolute);
        }

        private static string NormalizeThemeFileName(string themeFileName)
        {
            var n = themeFileName.Trim();
            if (n.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                return n;

            if (n.EndsWith("Theme", StringComparison.OrdinalIgnoreCase))
                return n + ".xaml";

            return n + "Theme.xaml";
        }

        private Uri GetThemeResourceUri(ThemeType theme)
        {
            string themeName = theme switch
            {
                ThemeType.Light => "LightTheme.xaml",
                ThemeType.Dark => "DarkTheme.xaml",
                ThemeType.Auto => DetermineAutoTheme(),
                ThemeType.Green => "GreenTheme.xaml",
                ThemeType.Business => "BusinessTheme.xaml",
                ThemeType.ModernBlue => "ModernBlueTheme.xaml",
                ThemeType.Violet => "VioletTheme.xaml",
                ThemeType.WarmOrange => "WarmOrangeTheme.xaml",
                ThemeType.Pink => "PinkTheme.xaml",
                ThemeType.TechCyan => "TechCyanTheme.xaml",
                ThemeType.MinimalBlack => "MinimalBlackTheme.xaml",
                ThemeType.Arctic => "ArcticTheme.xaml",
                ThemeType.Forest => "ForestTheme.xaml",
                ThemeType.Sunset => "SunsetTheme.xaml",
                ThemeType.Morandi => "MorandiTheme.xaml",
                ThemeType.HighContrast => "HighContrastTheme.xaml",
                ThemeType.Custom => NormalizeThemeFileName(_currentThemeFileName ?? "LightTheme.xaml"),
                _ => "LightTheme.xaml"
            };

            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            var themeFilePath = Path.Combine(themesPath, themeName);

            if (!File.Exists(themeFilePath))
            {
                DebugLog($"[ThemeManager] 主题文件不存在: {themeFilePath}");
                if (themeName != "LightTheme.xaml")
                {
                    themeFilePath = Path.Combine(themesPath, "LightTheme.xaml");
                    if (!File.Exists(themeFilePath))
                    {
                        throw new FileNotFoundException($"找不到主题文件: {themeName} 和默认主题文件");
                    }
                }
            }

            DebugLog($"[ThemeManager] 主题文件路径: {themeFilePath}");
            return new Uri(themeFilePath, UriKind.Absolute);
        }

        private string DetermineAutoTheme()
        {
            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 1 ? "LightTheme.xaml" : "DarkTheme.xaml";
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 读取系统主题失败: {ex.Message}");
            }

            return "LightTheme.xaml";
        }

        private ThemeType LoadThemePreference()
        {
            try
            {
                string themeStr = _settings.Get("appearance/theme", "0");
                if (int.TryParse(themeStr, out var themeInt) && Enum.IsDefined(typeof(ThemeType), themeInt))
                {
                    return (ThemeType)themeInt;
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 加载主题偏好失败: {ex.Message}");
            }

            return ThemeType.Light;
        }

        private void SaveThemePreference(ThemeType theme)
        {
            try
            {
                _settings.Set("appearance/theme", ((int)theme).ToString());
                DebugLog($"[ThemeManager] 主题偏好已保存: {theme}");
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 保存主题偏好失败: {ex.Message}");
            }
        }

        private string? LoadThemeFilePreference()
        {
            try
            {
                var themeFile = _settings.Get("appearance/theme_file", string.Empty);
                return string.IsNullOrWhiteSpace(themeFile) ? null : themeFile;
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 加载主题文件偏好失败: {ex.Message}");
            }

            return null;
        }

        private void SaveThemeFilePreference(string themeFileName)
        {
            try
            {
                _settings.Set("appearance/theme_file", themeFileName ?? string.Empty);
                DebugLog($"[ThemeManager] 主题文件偏好已保存: {themeFileName}");
            }
            catch (Exception ex)
            {
                DebugLog($"[ThemeManager] 保存主题文件偏好失败: {ex.Message}");
            }
        }

        public static string GetThemeDisplayName(ThemeType theme)
        {
            return theme switch
            {
                ThemeType.Light => "浅色主题",
                ThemeType.Dark => "深色主题",
                ThemeType.Auto => "跟随系统",
                ThemeType.Green => "护眼色",
                ThemeType.Business => "商务灰",
                ThemeType.ModernBlue => "现代深蓝",
                ThemeType.Violet => "紫罗兰",
                ThemeType.WarmOrange => "暖阳橙",
                ThemeType.Pink => "樱花粉",
                ThemeType.TechCyan => "科技青",
                ThemeType.MinimalBlack => "极简黑",
                ThemeType.Arctic => "北极蓝",
                ThemeType.Forest => "森林绿",
                ThemeType.Sunset => "日落橙",
                ThemeType.Morandi => "莫兰迪",
                ThemeType.HighContrast => "高对比度",
                ThemeType.Custom => "自定义主题",
                _ => "未知主题"
            };
        }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ThemeType
    {
        Light,

        Dark,

        Auto,

        Green,

        Business,

        ModernBlue,

        Violet,

        WarmOrange,

        Pink,

        TechCyan,

        MinimalBlack,

        Arctic,

        Forest,

        Sunset,

        Morandi,

        HighContrast,

        Custom
    }

    public class ThemeChangedEventArgs : EventArgs
    {
        public ThemeType NewTheme { get; }

        public ThemeChangedEventArgs(ThemeType newTheme)
        {
            NewTheme = newTheme;
        }
    }
}

