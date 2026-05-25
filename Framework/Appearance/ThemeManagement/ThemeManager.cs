using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private ThemeTransitionSettings? _cachedTransitionSettings;
        private bool _transitionSettingsCacheLoaded;
        private int _transitionSettingsVersion;

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

        public async System.Threading.Tasks.Task ReloadTransitionSettingsAsync()
        {
            var settings = await LoadTransitionSettingsFromDiskAsync().ConfigureAwait(true);
            Interlocked.Increment(ref _transitionSettingsVersion);
            _cachedTransitionSettings = settings;
            _transitionSettingsCacheLoaded = true;
        }

        public async System.Threading.Tasks.Task PreloadTransitionSettingsAsync()
        {
            Interlocked.Increment(ref _transitionSettingsVersion);
            _cachedTransitionSettings = await LoadTransitionSettingsFromDiskAsync().ConfigureAwait(false);
            _transitionSettingsCacheLoaded = true;
        }

        private ThemeTransitionSettings? GetCachedTransitionSettings()
        {
            if (!_transitionSettingsCacheLoaded)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var loadVersion = Volatile.Read(ref _transitionSettingsVersion);
                    try
                    {
                        var loaded = await LoadTransitionSettingsFromDiskAsync().ConfigureAwait(false);
                        if (loadVersion != Volatile.Read(ref _transitionSettingsVersion))
                            return;
                        _cachedTransitionSettings = loaded;
                        _transitionSettingsCacheLoaded = true;
                    }
                    catch (Exception ex) { TM.App.Log($"[ThemeManager] 加载过渡设置失败: {ex.Message}"); }
                });
                return null;
            }
            return _cachedTransitionSettings;
        }

        public async void Initialize()
        {
            try
            {
                GetCachedTransitionSettings();
                if (_currentTheme == ThemeType.Custom && !string.IsNullOrWhiteSpace(_currentThemeFileName))
                {
                    await ApplyThemeFromFileWithoutAnimationAsync(_currentThemeFileName);
                }
                else
                {
                    await ApplyThemeAsync(_currentTheme, false);
                }
                DebugLog($"[ThemeManager] 主题系统初始化成功: {_currentTheme}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeManager] 主题初始化失败，已回退到浅色主题: {ex.Message}");
                await ApplyThemeAsync(ThemeType.Light, false);
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

            async Task SwitchThemeCoreAsync()
            {
                try
                {
                    var transitionSettings = GetCachedTransitionSettings();

                    if (transitionSettings != null && transitionSettings.Effect != TransitionEffect.None)
                    {
                        var transitionService = _transitionService;
                        var windows = Application.Current.Windows.OfType<Window>().ToList();

                        int pending = 0;
                        bool applied = false;

                        async void CompleteOne()
                        {
                            pending--;
                            if (pending <= 0 && !applied)
                            {
                                applied = true;
                                try
                                {
                                    await ApplyThemeAsync(theme, true);
                                    _currentTheme = theme;
                                    _currentThemeFileName = null;
                                    SaveThemePreference(theme);
                                    SaveThemeFilePreference(string.Empty);
                                    ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
                                    DebugLog($"[ThemeManager] 主题已切换（带动画）: {theme}");
                                }
                                catch (Exception ex) { TM.App.Log($"[ThemeManager] 过渡动画后主题切换失败: {ex.Message}"); }
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
                            await ApplyThemeWithoutAnimationAsync(theme);
                        }
                    }
                    else
                    {
                        await ApplyThemeWithoutAnimationAsync(theme);
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
                _ = SwitchThemeCoreAsync();
            else
                dispatcher?.BeginInvoke(() => _ = SwitchThemeCoreAsync());
        }

        private async Task ApplyThemeWithoutAnimationAsync(ThemeType theme)
        {
            await ApplyThemeAsync(theme, true);
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

            async Task ApplyThemeFromFileCoreAsync()
            {
                try
                {
                    var transitionSettings = GetCachedTransitionSettings();

                    if (transitionSettings != null && transitionSettings.Effect != TransitionEffect.None)
                    {
                        var themeUri = GetThemeFileUri(normalizedFileName);
                        var transitionService = _transitionService;
                        var windows = Application.Current.Windows.OfType<Window>().ToList();

                        int pending = 0;
                        bool applied = false;

                        async void CompleteOne()
                        {
                            pending--;
                            if (pending <= 0 && !applied)
                            {
                                applied = true;
                                try
                                {
                                    await ApplyThemeUriAsync(themeUri).ConfigureAwait(true);
                                    _currentTheme = ThemeType.Custom;
                                    _currentThemeFileName = normalizedFileName;
                                    SaveThemePreference(ThemeType.Custom);
                                    SaveThemeFilePreference(normalizedFileName);
                                    ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(ThemeType.Custom));
                                    DebugLog($"[ThemeManager] 主题已切换（带动画）: {normalizedFileName}");
                                }
                                catch (Exception ex) { TM.App.Log($"[ThemeManager] 过渡动画后主题文件切换失败: {ex.Message}"); }
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
                            await ApplyThemeFromFileWithoutAnimationAsync(normalizedFileName);
                        }
                    }
                    else
                    {
                        await ApplyThemeFromFileWithoutAnimationAsync(normalizedFileName);
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
                _ = ApplyThemeFromFileCoreAsync();
            else
                dispatcher?.BeginInvoke(() => _ = ApplyThemeFromFileCoreAsync());
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
                case "Linear": themeType = ThemeType.Linear; return true;
                default:
                    themeType = default;
                    return false;
            }
        }

        private async System.Threading.Tasks.Task<ThemeTransitionSettings?> LoadTransitionSettingsFromDiskAsync()
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
                    var json = await System.IO.File.ReadAllTextAsync(settingsFile).ConfigureAwait(false);
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

        private async Task ApplyThemeAsync(ThemeType theme, bool clearCache)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                await ApplyThemeCoreAsync(theme, clearCache);
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(
                    () => ApplyThemeCoreAsync(theme, clearCache)).Task.Unwrap();
            }
        }

        private async Task ApplyThemeCoreAsync(ThemeType theme, bool clearCache)
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
                    await ApplyThemeUriAsync(themeUri).ConfigureAwait(true);
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

            ThemeGradientGenerator.InjectGradients(newTheme);

            mergedDicts.Insert(0, newTheme);

            if (_currentThemeDict != null)
            {
                mergedDicts.Remove(_currentThemeDict);
            }
            else
            {
                var oldThemes = new List<ResourceDictionary>();
                foreach (var dict in mergedDicts)
                {
                    if (dict != newTheme &&
                        dict.Source != null &&
                        (dict.Source.OriginalString.Contains("/Themes/") ||
                         dict.Source.OriginalString.Contains("\\Themes\\")) &&
                        dict.Source.OriginalString.EndsWith("Theme.xaml", StringComparison.Ordinal))
                    {
                        oldThemes.Add(dict);
                    }
                }
                foreach (var old in oldThemes)
                {
                    mergedDicts.Remove(old);
                }
            }

            _currentThemeDict = newTheme;

            TM.Framework.Common.Controls.TreeNodeItem.InvalidateStaticBrushCache();
            TM.Framework.UI.Workspace.RightPanel.Conversation.SKConversationViewModel.InvalidateThemeBrushCache();
            TM.Framework.UI.Windows.UnifiedWindow.InvalidateOverlayBrushCache();
        }

        private async Task ApplyThemeUriAsync(Uri themeUri)
        {
            var bytes = await File.ReadAllBytesAsync(themeUri.LocalPath).ConfigureAwait(true);
            using var ms = new MemoryStream(bytes);
            var fileDict = (ResourceDictionary)XamlReader.Load(ms);
            ApplyResourceDictionary(fileDict);
        }

        private async Task ApplyThemeFromFileWithoutAnimationAsync(string themeFileName)
        {
            var normalizedFileName = NormalizeThemeFileName(themeFileName);
            var themeUri = GetThemeFileUri(normalizedFileName);
            await ApplyThemeUriAsync(themeUri).ConfigureAwait(true);
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
                ThemeType.Linear => "LinearTheme.xaml",
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
                ThemeType.Linear => "Linear 极简",
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
        Linear,

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

