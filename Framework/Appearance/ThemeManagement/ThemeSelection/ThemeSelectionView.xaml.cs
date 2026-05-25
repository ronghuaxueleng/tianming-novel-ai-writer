using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.Appearance.ThemeManagement.ThemeSelection
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThemeSelectionView : UserControl
    {
        private RangeObservableCollection<ThemeCardData> _themes = new();
        private RangeObservableCollection<ThemeCardData> _allThemes = new();
        private string? _selectedThemeId;
        private ThemeType _currentTheme;
        private readonly ThemeManager _themeManager;
        private readonly TM.Services.Framework.Settings.SettingsManager _settings;
        private readonly ThemeSelectionSettings _themeSelectionSettings;
        private bool _showOnlyFavorites = false;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
        private HashSet<string> _favoriteIds = new();
        private string _searchText = "";

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static SolidColorBrush FB(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        private static readonly SolidColorBrush _cHighContrastP = FB("#FFFF00"), _cHighContrastS = FB("#00FFFF"), _cHighContrastBg = FB("#000000"), _cHighContrastT = FB("#FFFFFF");
        private static readonly SolidColorBrush _cLinearP = FB("#5E6AD2"), _cLinearS = FB("#8492E6"), _cLinearBg = FB("#F7F8FA"), _cLinearT = FB("#1E2028");
        private static readonly SolidColorBrush _cLightP = FB("#3B82F6"), _cLightS = FB("#64748B"), _cLightBg = FB("#FFFFFF"), _cLightT = FB("#1E293B");
        private static readonly SolidColorBrush _cGreenP = FB("#8B6914"), _cGreenS = FB("#6B5744"), _cGreenBg = FB("#F5EDDC"), _cGreenT = FB("#4A3728");
        private static readonly SolidColorBrush _cDarkP = FB("#60A5FA"), _cDarkS = FB("#94A3B8"), _cDarkBg = FB("#1E293B"), _cDarkT = FB("#F1F5F9");
        private static readonly SolidColorBrush _cArcticP = FB("#0284C7"), _cArcticS = FB("#2D5087"), _cArcticBg = FB("#F0F7FF"), _cArcticT = FB("#1A365D");
        private static readonly SolidColorBrush _cForestP = FB("#2E7D32"), _cForestS = FB("#3E6B42"), _cForestBg = FB("#F1F8F2"), _cForestT = FB("#1B3A1D");
        private static readonly SolidColorBrush _cVioletP = FB("#7C3AED"), _cVioletS = FB("#5B3E8A"), _cVioletBg = FB("#F8F0FF"), _cVioletT = FB("#2D1B4E");
        private static readonly SolidColorBrush _cBizP = FB("#4A6FA5"), _cBizS = FB("#595959"), _cBizBg = FB("#F7F7F7"), _cBizT = FB("#262626");
        private static readonly SolidColorBrush _cBlackP = FB("#6CB6FF"), _cBlackS = FB("#A0A0A0"), _cBlackBg = FB("#1A1A1A"), _cBlackT = FB("#E8E8E8");
        private static readonly SolidColorBrush _cMBlueP = FB("#1890FF"), _cMBlueS = FB("#8892B0"), _cMBlueBg = FB("#112240"), _cMBlueT = FB("#E2E8F0");
        private static readonly SolidColorBrush _cOrangeP = FB("#E8780A"), _cOrangeS = FB("#8C6540"), _cOrangeBg = FB("#FFF7E6"), _cOrangeT = FB("#5C3A18");
        private static readonly SolidColorBrush _cPinkP = FB("#EB2F96"), _cPinkS = FB("#7A3055"), _cPinkBg = FB("#FFF0F6"), _cPinkT = FB("#4A1030");
        private static readonly SolidColorBrush _cCyanP = FB("#13C2C2"), _cCyanS = FB("#88B0B8"), _cCyanBg = FB("#0D2137"), _cCyanT = FB("#E0F0F0");
        private static readonly SolidColorBrush _cSunsetP = FB("#E85D26"), _cSunsetS = FB("#8C5A3C"), _cSunsetBg = FB("#FFF4EC"), _cSunsetT = FB("#5C2E18");
        private static readonly SolidColorBrush _cMorandiP = FB("#7C9299"), _cMorandiS = FB("#6B6865"), _cMorandiBg = FB("#F5F4F2"), _cMorandiT = FB("#4A4845");

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

            System.Diagnostics.Debug.WriteLine($"[ThemeSelectionView] {key}: {ex.Message}");
        }

        public ThemeSelectionView()
        {
            InitializeComponent();

            _themeManager = ServiceLocator.Get<ThemeManager>();
            _settings = ServiceLocator.Get<TM.Services.Framework.Settings.SettingsManager>();
            _themeSelectionSettings = ServiceLocator.Get<ThemeSelectionSettings>();
            _currentTheme = _themeManager.CurrentTheme;

            _themeManager.ThemeChanged += OnThemeManagerChanged;

            Unloaded += OnViewUnloaded;

            LoadFavorites();
            LoadThemes();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            _themeManager.ThemeChanged -= OnThemeManagerChanged;
            Unloaded -= OnViewUnloaded;

            if (_searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer = null;
            }
        }

        private void OnThemeManagerChanged(object? sender, ThemeChangedEventArgs e)
        {
            _currentTheme = e.NewTheme;
            if (_currentTheme == ThemeType.Custom && !string.IsNullOrWhiteSpace(_themeManager.CurrentThemeFileName))
            {
                var customName = Path.GetFileNameWithoutExtension(_themeManager.CurrentThemeFileName)
                    .Replace("Theme", "");
                CurrentThemeLabel.Text = $"自定义主题：{customName}";
            }
            else
            {
                CurrentThemeLabel.Text = ThemeManager.GetThemeDisplayName(_currentTheme);
            }
            foreach (var card in _allThemes)
                card.IsCurrent = IsCurrentTheme(card);
        }

        private bool IsCurrentTheme(ThemeCardData card)
        {
            if (card.ThemeId.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase))
                return _currentTheme == ThemeType.Custom &&
                       string.Equals(_themeManager.CurrentThemeFileName,
                           card.ThemeId.Substring("Custom_".Length),
                           StringComparison.OrdinalIgnoreCase);
            return int.TryParse(card.ThemeId, out var id) && _currentTheme == (ThemeType)id;
        }

        private void LoadThemes()
        {
            try
            {
                var builtInThemes = new List<ThemeCardData>();

                if (_currentTheme == ThemeType.Custom && !string.IsNullOrWhiteSpace(_themeManager.CurrentThemeFileName))
                {
                    var customName = Path.GetFileNameWithoutExtension(_themeManager.CurrentThemeFileName)
                        .Replace("Theme", "");
                    CurrentThemeLabel.Text = $"自定义主题：{customName}";
                }
                else
                {
                    CurrentThemeLabel.Text = ThemeManager.GetThemeDisplayName(_currentTheme);
                }

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Light).ToString(),
                    ThemeName = "浅色主题",
                    PrimaryColor = _cLightP,
                    SecondaryColor = _cLightS,
                    BackgroundColor = _cLightBg,
                    TextColor = _cLightT,
                    IsCurrent = (_currentTheme == ThemeType.Light),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Green).ToString(),
                    ThemeName = "护眼色",
                    PrimaryColor = _cGreenP,
                    SecondaryColor = _cGreenS,
                    BackgroundColor = _cGreenBg,
                    TextColor = _cGreenT,
                    IsCurrent = (_currentTheme == ThemeType.Green),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Dark).ToString(),
                    ThemeName = "深色主题",
                    PrimaryColor = _cDarkP,
                    SecondaryColor = _cDarkS,
                    BackgroundColor = _cDarkBg,
                    TextColor = _cDarkT,
                    IsCurrent = (_currentTheme == ThemeType.Dark),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Arctic).ToString(),
                    ThemeName = "北极蓝",
                    PrimaryColor = _cArcticP,
                    SecondaryColor = _cArcticS,
                    BackgroundColor = _cArcticBg,
                    TextColor = _cArcticT,
                    IsCurrent = (_currentTheme == ThemeType.Arctic),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Forest).ToString(),
                    ThemeName = "森林绿",
                    PrimaryColor = _cForestP,
                    SecondaryColor = _cForestS,
                    BackgroundColor = _cForestBg,
                    TextColor = _cForestT,
                    IsCurrent = (_currentTheme == ThemeType.Forest),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Violet).ToString(),
                    ThemeName = "紫罗兰",
                    PrimaryColor = _cVioletP,
                    SecondaryColor = _cVioletS,
                    BackgroundColor = _cVioletBg,
                    TextColor = _cVioletT,
                    IsCurrent = (_currentTheme == ThemeType.Violet),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Business).ToString(),
                    ThemeName = "商务灰",
                    PrimaryColor = _cBizP,
                    SecondaryColor = _cBizS,
                    BackgroundColor = _cBizBg,
                    TextColor = _cBizT,
                    IsCurrent = (_currentTheme == ThemeType.Business),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.MinimalBlack).ToString(),
                    ThemeName = "极简黑",
                    PrimaryColor = _cBlackP,
                    SecondaryColor = _cBlackS,
                    BackgroundColor = _cBlackBg,
                    TextColor = _cBlackT,
                    IsCurrent = (_currentTheme == ThemeType.MinimalBlack),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.ModernBlue).ToString(),
                    ThemeName = "现代深蓝",
                    PrimaryColor = _cMBlueP,
                    SecondaryColor = _cMBlueS,
                    BackgroundColor = _cMBlueBg,
                    TextColor = _cMBlueT,
                    IsCurrent = (_currentTheme == ThemeType.ModernBlue),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.WarmOrange).ToString(),
                    ThemeName = "暖阳橙",
                    PrimaryColor = _cOrangeP,
                    SecondaryColor = _cOrangeS,
                    BackgroundColor = _cOrangeBg,
                    TextColor = _cOrangeT,
                    IsCurrent = (_currentTheme == ThemeType.WarmOrange),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Pink).ToString(),
                    ThemeName = "樱花粉",
                    PrimaryColor = _cPinkP,
                    SecondaryColor = _cPinkS,
                    BackgroundColor = _cPinkBg,
                    TextColor = _cPinkT,
                    IsCurrent = (_currentTheme == ThemeType.Pink),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.TechCyan).ToString(),
                    ThemeName = "科技青",
                    PrimaryColor = _cCyanP,
                    SecondaryColor = _cCyanS,
                    BackgroundColor = _cCyanBg,
                    TextColor = _cCyanT,
                    IsCurrent = (_currentTheme == ThemeType.TechCyan),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Sunset).ToString(),
                    ThemeName = "日落橙",
                    PrimaryColor = _cSunsetP,
                    SecondaryColor = _cSunsetS,
                    BackgroundColor = _cSunsetBg,
                    TextColor = _cSunsetT,
                    IsCurrent = (_currentTheme == ThemeType.Sunset),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Morandi).ToString(),
                    ThemeName = "莫兰迪",
                    PrimaryColor = _cMorandiP,
                    SecondaryColor = _cMorandiS,
                    BackgroundColor = _cMorandiBg,
                    TextColor = _cMorandiT,
                    IsCurrent = (_currentTheme == ThemeType.Morandi),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.HighContrast).ToString(),
                    ThemeName = "高对比度",
                    PrimaryColor = _cHighContrastP,
                    SecondaryColor = _cHighContrastS,
                    BackgroundColor = _cHighContrastBg,
                    TextColor = _cHighContrastT,
                    IsCurrent = (_currentTheme == ThemeType.HighContrast),
                    IsSelected = false
                });

                builtInThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Linear).ToString(),
                    ThemeName = "Linear极简",
                    PrimaryColor = _cLinearP,
                    SecondaryColor = _cLinearS,
                    BackgroundColor = _cLinearBg,
                    TextColor = _cLinearT,
                    IsCurrent = (_currentTheme == ThemeType.Linear),
                    IsSelected = false
                });

                _allThemes.ReplaceAll(builtInThemes);

                LoadThemeFiles();

                ApplyFavoriteStatus();

                ApplyFilter();

                ThemesItemsControl.ItemsSource = _themes;

                App.Log($"[ThemeSelection] 已加载 {_allThemes.Count} 个主题（含自定义），当前显示 {_themes.Count} 个");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 加载主题失败: {ex.Message}");
            }
        }

        private void OnThemeCardClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string themeId)
            {
                foreach (var theme in _themes)
                {
                    theme.IsSelected = false;
                }

                var selectedTheme = _themes.FirstOrDefault(t => t.ThemeId == themeId);
                if (selectedTheme != null)
                {
                    selectedTheme.IsSelected = true;
                    _selectedThemeId = themeId;
                    App.Log($"[ThemeSelection] 已选中主题: {selectedTheme.ThemeName}");
                }
            }
        }

        private void OnApplyThemeClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedThemeId))
            {
                StandardDialog.ShowWarning("请先点击选择一个主题！", "提示", Window.GetWindow(this));
                return;
            }

            var selectedTheme = _themes.FirstOrDefault(t => t.ThemeId == _selectedThemeId);
            if (selectedTheme == null) return;

            if (selectedTheme.IsCurrent)
            {
                ToastNotification.ShowInfo("已是当前主题", $"当前已经是「{selectedTheme.ThemeName}」");
                return;
            }

            var result = StandardDialog.ShowConfirm(
                $"确定要切换到「{selectedTheme.ThemeName}」吗？",
                "确认应用",
                Window.GetWindow(this)
            );

            if (!result) return;

            try
            {
                App.Log($"[ThemeSelection] 正在切换主题: {selectedTheme.ThemeName}");

                if (_selectedThemeId.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = _selectedThemeId.Substring("Custom_".Length);
                    _themeManager.ApplyThemeFromFile(fileName);

                    _themeSelectionSettings.RecordRecentTheme(_selectedThemeId, selectedTheme.ThemeName);
                    _currentTheme = ThemeType.Custom;
                }
                else
                {
                    ThemeType themeType;
                    if (int.TryParse(_selectedThemeId, out var themeInt) && Enum.IsDefined(typeof(ThemeType), themeInt))
                        themeType = (ThemeType)themeInt;
                    else if (Enum.TryParse<ThemeType>(_selectedThemeId, out themeType)) { }
                    else
                        throw new InvalidOperationException($"无效的主题类型: {_selectedThemeId}");

                    _themeManager.SwitchTheme(themeType);

                    _themeSelectionSettings.RecordRecentTheme(_selectedThemeId, selectedTheme.ThemeName);

                    _currentTheme = themeType;
                }

                foreach (var card in _themes)
                    card.IsSelected = false;
                _selectedThemeId = null;

                App.Log($"[ThemeSelection] 主题切换成功: {selectedTheme.ThemeName}");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 切换主题失败: {ex.Message}");
                StandardDialog.ShowError($"切换主题失败：{ex.Message}", "切换失败", Window.GetWindow(this));
            }
        }

        private void OnDeleteThemeClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedThemeId))
            {
                StandardDialog.ShowWarning("请先点击选择一个主题！", "提示", Window.GetWindow(this));
                return;
            }

            var selectedTheme = _themes.FirstOrDefault(t => t.ThemeId == _selectedThemeId);
            if (selectedTheme == null)
            {
                StandardDialog.ShowWarning("未找到选中的主题！", "提示", Window.GetWindow(this));
                return;
            }

            var isBuiltIn = (int.TryParse(_selectedThemeId, out var delThemeInt) && Enum.IsDefined(typeof(ThemeType), delThemeInt))
                ? BuiltInThemes.IsBuiltIn((ThemeType)delThemeInt)
                : (Enum.TryParse<ThemeType>(_selectedThemeId, out var selectedType) && BuiltInThemes.IsBuiltIn(selectedType));
            if (isBuiltIn)
            {
                StandardDialog.ShowInfo(
                    "系统内置主题不支持删除！",
                    "提示",
                    Window.GetWindow(this)
                );
                App.Log("[ThemeSelection] 尝试删除系统主题被拒绝");
                return;
            }

            if (selectedTheme.IsCurrent)
            {
                StandardDialog.ShowWarning(
                    $"无法删除正在使用的主题「{selectedTheme.ThemeName}」！\n\n请先切换到其他主题后再删除。",
                    "提示",
                    Window.GetWindow(this)
                );
                return;
            }

            var result = StandardDialog.ShowConfirm(
                $"确定要删除主题「{selectedTheme.ThemeName}」吗？\n\n删除后将无法恢复！",
                "确认删除",
                Window.GetWindow(this)
            );

            if (!result) return;

            try
            {
                if (!_selectedThemeId.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase))
                {
                    StandardDialog.ShowInfo(
                        $"主题「{selectedTheme.ThemeName}」是系统预设主题，不支持删除！\n\n仅支持删除用户自定义主题。",
                        "提示",
                        Window.GetWindow(this)
                    );
                    App.Log($"[ThemeSelection] 尝试删除预设主题被拒绝: {selectedTheme.ThemeName}");
                    return;
                }

                var themesPath = StoragePathHelper.GetFrameworkStoragePath(
                    "Appearance/ThemeManagement/Themes"
                );

                var fileName = _selectedThemeId.Substring("Custom_".Length);
                var filePath = Path.Combine(themesPath, fileName);

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"未找到主题文件: {fileName}", filePath);

                File.Delete(filePath);
                App.Log($"[ThemeSelection] 已删除主题文件: {filePath}");

                if (_favoriteIds.Contains(_selectedThemeId))
                {
                    _themeSelectionSettings.RemoveFavorite(_selectedThemeId);
                    _favoriteIds.Remove(_selectedThemeId);
                }

                _selectedThemeId = null;
                LoadThemes();
                ToastNotification.ShowSuccess("删除成功", $"主题「{selectedTheme.ThemeName}」已删除");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 删除主题失败: {ex.Message}");
                StandardDialog.ShowError($"删除主题失败：{ex.Message}", "删除失败", Window.GetWindow(this));
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentTheme = _themeManager.CurrentTheme;
                LoadThemes();

                ToastNotification.ShowSuccess("刷新成功", "主题列表已刷新");
                App.Log($"[ThemeSelection] 主题列表已刷新，当前主题: {_currentTheme}");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 刷新失败: {ex.Message}");
                StandardDialog.ShowError($"刷新失败：{ex.Message}", "刷新失败", Window.GetWindow(this));
            }
        }

        private void LoadThemeFiles()
        {
            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            var currentThemeSnapshot = _currentTheme;
            var currentFileNameSnapshot = _themeManager.CurrentThemeFileName;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var results = new List<(string ThemeId, string ThemeName, string Primary, string Secondary, string Background, string Text, bool IsCurrent)>();
                try
                {
                    if (!Directory.Exists(themesPath))
                    {
                        App.Log("[ThemeSelection] 主题目录不存在，跳过加载");
                        return;
                    }

                    var builtInThemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "LightTheme.xaml", "GreenTheme.xaml", "DarkTheme.xaml",
                        "ArcticTheme.xaml", "ForestTheme.xaml", "VioletTheme.xaml",
                        "BusinessTheme.xaml", "MinimalBlackTheme.xaml",
                        "ModernBlueTheme.xaml", "WarmOrangeTheme.xaml", "PinkTheme.xaml",
                        "TechCyanTheme.xaml", "SunsetTheme.xaml", "MorandiTheme.xaml"
                    };

                    var themeFiles = Directory.GetFiles(themesPath, "*Theme.xaml")
                        .Where(f => !builtInThemes.Contains(Path.GetFileName(f)))
                        .ToList();

                    App.Log($"[ThemeSelection] 发现 {themeFiles.Count} 个自定义主题");

                    foreach (var themeFile in themeFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileName(themeFile);
                            var themeName = Path.GetFileNameWithoutExtension(fileName).Replace("Theme", "");

                            var (p, s, bg, t) = await ExtractThemeColorStringsAsync(themeFile);
                            var isCurrent = currentThemeSnapshot == ThemeType.Custom &&
                                            string.Equals(currentFileNameSnapshot, fileName, StringComparison.OrdinalIgnoreCase);

                            results.Add(($"Custom_{fileName}", themeName, p, s, bg, t, isCurrent));
                            App.Log($"[ThemeSelection] 已解析自定义主题: {themeName}");
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[ThemeSelection] 解析自定义主题失败 {Path.GetFileName(themeFile)}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[ThemeSelection] 扫描自定义主题失败: {ex.Message}");
                }

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var (themeId, themeName, primary, secondary, background, text, isCurrent) in results)
                    {
                        _allThemes.Add(new ThemeCardData
                        {
                            ThemeId = themeId,
                            ThemeName = themeName,
                            PrimaryColor = MakeBrush(primary, "#3B82F6"),
                            SecondaryColor = MakeBrush(secondary, "#64748B"),
                            BackgroundColor = MakeBrush(background, "#FFFFFF"),
                            TextColor = MakeBrush(text, "#1E293B"),
                            IsCurrent = isCurrent,
                            IsSelected = false
                        });
                    }

                    if (results.Count > 0)
                    {
                        ApplyFavoriteStatus();
                        ApplyFilter();
                    }
                });
            });
        }

        private static async System.Threading.Tasks.Task<(string Primary, string Secondary, string Background, string Text)> ExtractThemeColorStringsAsync(string filePath)
        {
            try
            {
                var xml = await System.IO.File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var doc = XDocument.Parse(xml);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                string GetColor(string key, string fallback)
                {
                    var element = doc.Descendants(ns + "SolidColorBrush")
                        .FirstOrDefault(e => e.Attribute(ns + "Key")?.Value == key);
                    return element?.Attribute("Color")?.Value ?? fallback;
                }

                return (
                    GetColor("PrimaryColor", "#3B82F6"),
                    GetColor("TextSecondary", "#64748B"),
                    GetColor("ContentBackground", "#FFFFFF"),
                    GetColor("TextPrimary", "#1E293B")
                );
            }
            catch
            {
                return ("#3B82F6", "#64748B", "#FFFFFF", "#1E293B");
            }
        }

        private static SolidColorBrush MakeBrush(string colorString, string fallback)
        {
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
                b.Freeze();
                return b;
            }
            catch
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
                b.Freeze();
                return b;
            }
        }

    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ThemeCardData : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isCurrent;
        private bool _isFavorite;

        public string ThemeId { get; set; } = string.Empty;
        public string ThemeName { get; set; } = string.Empty;
        public SolidColorBrush PrimaryColor { get; set; } = Brushes.Blue;
        public SolidColorBrush SecondaryColor { get; set; } = Brushes.Gray;
        public SolidColorBrush BackgroundColor { get; set; } = Brushes.White;
        public SolidColorBrush TextColor { get; set; } = Brushes.Black;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged(nameof(IsCurrent));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged(nameof(IsFavorite));
                    OnPropertyChanged(nameof(FavoriteIcon));
                }
            }
        }

        public string FavoriteIcon => IsFavorite ? "Icon.Star" : "Icon.Star";

        public string StatusText => IsCurrent ? "✓ 使用中" : "点击切换";

        public string StatusColor => IsCurrent ? "#28a745" : "#6c757d";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    #region 收藏功能扩展方法

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThemeSelectionView
    {
        private void LoadFavorites()
        {
            try
            {
                _favoriteIds = _themeSelectionSettings.GetFavoriteIds();
                App.Log($"[ThemeSelection] 从Settings加载 {_favoriteIds.Count} 个收藏主题");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 加载收藏失败: {ex.Message}");
                _favoriteIds = new HashSet<string>();
            }
        }

        private void ApplyFavoriteStatus()
        {
            foreach (var theme in _allThemes)
            {
                theme.IsFavorite = _favoriteIds.Contains(theme.ThemeId);
            }
        }

        private void ApplyFilter()
        {
            var filteredThemes = _allThemes.AsEnumerable();

            if (_showOnlyFavorites)
            {
                filteredThemes = filteredThemes.Where(t => t.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filteredThemes = filteredThemes.Where(t =>
                    t.ThemeName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    t.ThemeId.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }

            _themes.ReplaceAll(filteredThemes.ToList());

            if (SearchResultText != null)
            {
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    SearchResultText.Text = $"找到 {_themes.Count} 个匹配的主题";
                    SearchResultText.Visibility = Visibility.Visible;
                }
                else
                {
                    SearchResultText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;

                if (ClearSearchButton != null)
                {
                    ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchText)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                if (_searchDebounceTimer == null)
                {
                    _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(150)
                    };
                    _searchDebounceTimer.Tick += (_, _) =>
                    {
                        _searchDebounceTimer.Stop();
                        ApplyFilter();
                        App.Log($"[ThemeSelection] 搜索: \"{_searchText}\"，找到 {_themes.Count} 个主题");
                    };
                }
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        }

        private void OnClearSearch(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = "";
            }
        }

        private void OnToggleFavorite(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is System.Windows.Controls.Button button && button.DataContext is ThemeCardData theme)
            {
                try
                {
                    bool isFavorite = _themeSelectionSettings.ToggleFavorite(theme.ThemeId);
                    theme.IsFavorite = isFavorite;

                    if (isFavorite)
                    {
                        _favoriteIds.Add(theme.ThemeId);
                    }
                    else
                    {
                        _favoriteIds.Remove(theme.ThemeId);
                    }

                    App.Log($"[ThemeSelection] 主题 {theme.ThemeName} 收藏状态: {isFavorite}");

                    if (_showOnlyFavorites && !theme.IsFavorite)
                    {
                        ApplyFilter();
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[ThemeSelection] 切换收藏失败: {ex.Message}");
                }
            }
        }

        private void OnToggleFavoritesFilter(object sender, RoutedEventArgs e)
        {
            _showOnlyFavorites = !_showOnlyFavorites;

            if (_showOnlyFavorites)
            {
                FavoritesText.Text = "显示全部";
                FavoritesFilterButton.Style = (Style)FindResource("PrimaryButtonStyle");
            }
            else
            {
                FavoritesText.Text = "我的收藏";
                FavoritesFilterButton.Style = (Style)FindResource("SecondaryButtonStyle");
            }

            ApplyFilter();
            App.Log($"[ThemeSelection] 收藏过滤: {_showOnlyFavorites}，显示 {_themes.Count} 个主题");
        }
    }

    #endregion
}

