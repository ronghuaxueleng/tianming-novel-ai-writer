using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.ThemeManagement.ThemeSelection
{
    public partial class ThemeSelectionView : UserControl
    {
        private ObservableCollection<ThemeCardData> _themes = new();
        private ObservableCollection<ThemeCardData> _allThemes = new();
        private string? _selectedThemeId;
        private ThemeType _currentTheme;
        private readonly ThemeManager _themeManager;
        private readonly TM.Services.Framework.Settings.SettingsManager _settings;
        private readonly ThemeSelectionSettings _themeSelectionSettings;
        private bool _showOnlyFavorites = false;
        private HashSet<string> _favoriteIds = new();
        private string _searchText = "";

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

            LoadFavorites();
            LoadThemes();
        }

        private void OnThemeManagerChanged(object? sender, ThemeChangedEventArgs e)
        {
            _currentTheme = e.NewTheme;
            LoadThemes();
        }

        private void LoadThemes()
        {
            try
            {
                _allThemes.Clear();

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

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Light).ToString(),
                    ThemeName = "浅色主题",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    IsCurrent = (_currentTheme == ThemeType.Light),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Green).ToString(),
                    ThemeName = "护眼色",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B6914")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B5744")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5EDDC")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A3728")),
                    IsCurrent = (_currentTheme == ThemeType.Green),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Dark).ToString(),
                    ThemeName = "深色主题",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                    IsCurrent = (_currentTheme == ThemeType.Dark),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Arctic).ToString(),
                    ThemeName = "北极蓝",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D5087")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F7FF")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A365D")),
                    IsCurrent = (_currentTheme == ThemeType.Arctic),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Forest).ToString(),
                    ThemeName = "森林绿",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E6B42")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F8F2")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B3A1D")),
                    IsCurrent = (_currentTheme == ThemeType.Forest),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Violet).ToString(),
                    ThemeName = "紫罗兰",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5B3E8A")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8F0FF")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D1B4E")),
                    IsCurrent = (_currentTheme == ThemeType.Violet),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Business).ToString(),
                    ThemeName = "商务灰",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A6FA5")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#595959")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F7F7F7")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262626")),
                    IsCurrent = (_currentTheme == ThemeType.Business),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.MinimalBlack).ToString(),
                    ThemeName = "极简黑",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6CB6FF")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8")),
                    IsCurrent = (_currentTheme == ThemeType.MinimalBlack),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.ModernBlue).ToString(),
                    ThemeName = "现代深蓝",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1890FF")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8892B0")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#112240")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    IsCurrent = (_currentTheme == ThemeType.ModernBlue),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.WarmOrange).ToString(),
                    ThemeName = "暖阳橙",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8780A")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8C6540")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7E6")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C3A18")),
                    IsCurrent = (_currentTheme == ThemeType.WarmOrange),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Pink).ToString(),
                    ThemeName = "樱花粉",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EB2F96")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A3055")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF0F6")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A1030")),
                    IsCurrent = (_currentTheme == ThemeType.Pink),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.TechCyan).ToString(),
                    ThemeName = "科技青",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#13C2C2")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88B0B8")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D2137")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F0F0")),
                    IsCurrent = (_currentTheme == ThemeType.TechCyan),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Sunset).ToString(),
                    ThemeName = "日落橙",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E85D26")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8C5A3C")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF4EC")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C2E18")),
                    IsCurrent = (_currentTheme == ThemeType.Sunset),
                    IsSelected = false
                });

                _allThemes.Add(new ThemeCardData
                {
                    ThemeId = ((int)ThemeType.Morandi).ToString(),
                    ThemeName = "莫兰迪",
                    PrimaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C9299")),
                    SecondaryColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B6865")),
                    BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F4F2")),
                    TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A4845")),
                    IsCurrent = (_currentTheme == ThemeType.Morandi),
                    IsSelected = false
                });

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

                _selectedThemeId = null;

                LoadThemes();

                App.Log($"[ThemeSelection] 主题切换成功: {selectedTheme.ThemeName}");
            }
            catch (Exception ex)
            {
                App.Log($"[ThemeSelection] 切换主题失败: {ex.Message}");
                StandardDialog.ShowError($"切换主题失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
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
                StandardDialog.ShowError($"删除主题失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
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
                StandardDialog.ShowError($"刷新失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void LoadThemeFiles()
        {
            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            var currentThemeSnapshot = _currentTheme;
            var currentFileNameSnapshot = _themeManager.CurrentThemeFileName;

            _ = System.Threading.Tasks.Task.Run(() =>
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

                            var (p, s, bg, t) = ExtractThemeColorStrings(themeFile);
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
                            PrimaryColor    = MakeBrush(primary,    "#3B82F6"),
                            SecondaryColor  = MakeBrush(secondary,  "#64748B"),
                            BackgroundColor = MakeBrush(background, "#FFFFFF"),
                            TextColor       = MakeBrush(text,       "#1E293B"),
                            IsCurrent  = isCurrent,
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

        private static (string Primary, string Secondary, string Background, string Text) ExtractThemeColorStrings(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                string GetColor(string key, string fallback)
                {
                    var element = doc.Descendants(ns + "SolidColorBrush")
                        .FirstOrDefault(e => e.Attribute(ns + "Key")?.Value == key);
                    return element?.Attribute("Color")?.Value ?? fallback;
                }

                return (
                    GetColor("PrimaryColor",     "#3B82F6"),
                    GetColor("TextSecondary",    "#64748B"),
                    GetColor("ContentBackground","#FFFFFF"),
                    GetColor("TextPrimary",      "#1E293B")
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
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            }
        }

        private (SolidColorBrush Primary, SolidColorBrush Secondary, SolidColorBrush Background, SolidColorBrush Text) ExtractThemeColors(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                string GetColor(string key, string fallback)
                {
                    var element = doc.Descendants(ns + "SolidColorBrush")
                        .FirstOrDefault(e => e.Attribute(ns + "Key")?.Value == key);
                    return element?.Attribute("Color")?.Value ?? fallback;
                }

                return (
                    Primary: new SolidColorBrush((Color)ColorConverter.ConvertFromString(GetColor("PrimaryColor", "#3B82F6"))),
                    Secondary: new SolidColorBrush((Color)ColorConverter.ConvertFromString(GetColor("TextSecondary", "#64748B"))),
                    Background: new SolidColorBrush((Color)ColorConverter.ConvertFromString(GetColor("ContentBackground", "#FFFFFF"))),
                    Text: new SolidColorBrush((Color)ColorConverter.ConvertFromString(GetColor("TextPrimary", "#1E293B")))
                );
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ExtractThemeColors), ex);
                return (
                    Primary: new SolidColorBrush(Colors.Blue),
                    Secondary: new SolidColorBrush(Colors.Gray),
                    Background: new SolidColorBrush(Colors.White),
                    Text: new SolidColorBrush(Colors.Black)
                );
            }
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
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

        public string FavoriteIcon => IsFavorite ? "⭐" : "☆";

        public string StatusText => IsCurrent ? "✓ 使用中" : "点击切换";

        public string StatusColor => IsCurrent ? "#28a745" : "#6c757d";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    #region 收藏功能扩展方法

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
            _themes.Clear();

            var filteredThemes = _allThemes.AsEnumerable();

            if (_showOnlyFavorites)
            {
                filteredThemes = filteredThemes.Where(t => t.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var searchLower = _searchText.ToLower();
                filteredThemes = filteredThemes.Where(t => 
                    t.ThemeName.ToLower().Contains(searchLower) ||
                    t.ThemeId.ToLower().Contains(searchLower));
            }

            foreach (var theme in filteredThemes)
            {
                _themes.Add(theme);
            }

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
                ApplyFilter();

                if (ClearSearchButton != null)
                {
                    ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchText) 
                        ? Visibility.Collapsed 
                        : Visibility.Visible;
                }

                App.Log($"[ThemeSelection] 搜索: \"{_searchText}\"，找到 {_themes.Count} 个主题");
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

