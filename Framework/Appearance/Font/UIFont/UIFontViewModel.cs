using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Appearance.Font.Models;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.UIFont
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum SortMode
    {
        Name,
        RecentUsed,
        FavoriteFirst
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class UIFontViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> _availableFonts = new();
        private TM.Framework.Common.ViewModels.RangeObservableCollection<FontItem> _filteredFonts = new();
        private ObservableCollection<FontCategory> _categories = new();
        private FontCategory _selectedCategory = FontCategory.All;
        private SortMode _sortMode = SortMode.Name;

        private FontSettings _currentSettings;
        private string _selectedFontFamily;
        private double _selectedFontSize;
        private string _selectedFontWeight;
        private double _selectedLineHeight;
        private double _selectedLetterSpacing;
        private string _searchText = string.Empty;
        private List<string> _cachedAllFonts = new();
        private System.Windows.Threading.DispatcherTimer? _filterDebounceTimer;

        private readonly FontCategoryService _categoryService;
        private readonly FontFavoriteService _favoriteService;
        private TM.Framework.Common.Controls.TreeNodeItem? _selectedFontNode;

        private bool _isComparisonMode;
        private FontSettings _comparisonSettings;
        private string _comparisonFontFamily;
        private double _comparisonFontSize;
        private string _comparisonFontWeight;
        private double _comparisonLineHeight;
        private double _comparisonLetterSpacing;

        public ObservableCollection<string> AvailableFonts
        {
            get => _availableFonts;
            set
            {
                if (_availableFonts != value)
                {
                    _availableFonts = value;
                    OnPropertyChanged(nameof(AvailableFonts));
                }
            }
        }

        public TM.Framework.Common.ViewModels.RangeObservableCollection<FontItem> FilteredFonts
        {
            get => _filteredFonts;
            set
            {
                if (_filteredFonts != value)
                {
                    _filteredFonts = value;
                    OnPropertyChanged(nameof(FilteredFonts));
                }
            }
        }

        public TM.Framework.Common.ViewModels.RangeObservableCollection<TM.Framework.Common.Controls.TreeNodeItem> FontTree { get; } = new();

        public ObservableCollection<FontCategory> Categories
        {
            get => _categories;
            set
            {
                if (_categories != value)
                {
                    _categories = value;
                    OnPropertyChanged(nameof(Categories));
                }
            }
        }

        public FontCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value;
                    OnPropertyChanged(nameof(SelectedCategory));
                    ApplyFiltersAndSort();
                }
            }
        }

        public SortMode CurrentSortMode
        {
            get => _sortMode;
            set
            {
                if (_sortMode != value)
                {
                    _sortMode = value;
                    OnPropertyChanged(nameof(CurrentSortMode));
                    ApplyFiltersAndSort();
                }
            }
        }

        public ObservableCollection<string> FontWeightOptions { get; } = new()
        {
            "Thin", "ExtraLight", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black"
        };

        public ObservableCollection<string> SortModeOptions { get; } = new()
        {
            "名称", "最近使用", "收藏优先"
        };

        public string SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                if (_selectedFontFamily != value)
                {
                    _selectedFontFamily = value;
                    _currentSettings.FontFamily = value;
                    OnPropertyChanged(nameof(SelectedFontFamily));
                }
            }
        }

        public double SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                if (_selectedFontSize != value)
                {
                    _selectedFontSize = value;
                    _currentSettings.FontSize = value;
                    OnPropertyChanged(nameof(SelectedFontSize));
                }
            }
        }

        public string SelectedFontWeight
        {
            get => _selectedFontWeight;
            set
            {
                if (_selectedFontWeight != value)
                {
                    _selectedFontWeight = value;
                    _currentSettings.FontWeight = value;
                    OnPropertyChanged(nameof(SelectedFontWeight));
                }
            }
        }

        public double SelectedLineHeight
        {
            get => _selectedLineHeight;
            set
            {
                if (_selectedLineHeight != value)
                {
                    _selectedLineHeight = value;
                    _currentSettings.LineHeight = value;
                    OnPropertyChanged(nameof(SelectedLineHeight));
                }
            }
        }

        public double SelectedLetterSpacing
        {
            get => _selectedLetterSpacing;
            set
            {
                if (_selectedLetterSpacing != value)
                {
                    _selectedLetterSpacing = value;
                    _currentSettings.LetterSpacing = value;
                    OnPropertyChanged(nameof(SelectedLetterSpacing));
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    ScheduleFilter();
                }
            }
        }

        public FontSettings CurrentSettings => _currentSettings;

        public string PreviewText => "字体预览 Font Preview 1234567890";

        public ICommand ApplyCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ClearRecentCommand { get; }
        public ICommand ToggleComparisonModeCommand { get; }
        public ICommand ApplyMainFontCommand { get; }
        public ICommand ApplyComparisonFontCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand ShareConfigCommand { get; }
        public ICommand SelectFontCommand { get; }

        public bool IsComparisonMode
        {
            get => _isComparisonMode;
            set
            {
                if (_isComparisonMode != value)
                {
                    _isComparisonMode = value;
                    OnPropertyChanged(nameof(IsComparisonMode));
                }
            }
        }

        public FontSettings ComparisonSettings => _comparisonSettings;

        public string ComparisonFontFamily
        {
            get => _comparisonFontFamily;
            set
            {
                if (_comparisonFontFamily != value)
                {
                    _comparisonFontFamily = value;
                    _comparisonSettings.FontFamily = value;
                    OnPropertyChanged(nameof(ComparisonFontFamily));
                }
            }
        }

        public double ComparisonFontSize
        {
            get => _comparisonFontSize;
            set
            {
                if (_comparisonFontSize != value)
                {
                    _comparisonFontSize = value;
                    _comparisonSettings.FontSize = value;
                    OnPropertyChanged(nameof(ComparisonFontSize));
                }
            }
        }

        public string ComparisonFontWeight
        {
            get => _comparisonFontWeight;
            set
            {
                if (_comparisonFontWeight != value)
                {
                    _comparisonFontWeight = value;
                    _comparisonSettings.FontWeight = value;
                    OnPropertyChanged(nameof(ComparisonFontWeight));
                }
            }
        }

        public double ComparisonLineHeight
        {
            get => _comparisonLineHeight;
            set
            {
                if (_comparisonLineHeight != value)
                {
                    _comparisonLineHeight = value;
                    _comparisonSettings.LineHeight = value;
                    OnPropertyChanged(nameof(ComparisonLineHeight));
                }
            }
        }

        public double ComparisonLetterSpacing
        {
            get => _comparisonLetterSpacing;
            set
            {
                if (_comparisonLetterSpacing != value)
                {
                    _comparisonLetterSpacing = value;
                    _comparisonSettings.LetterSpacing = value;
                    OnPropertyChanged(nameof(ComparisonLetterSpacing));
                }
            }
        }

        public UIFontViewModel(
            FontCategoryService categoryService,
            FontFavoriteService favoriteService)
        {
            TM.App.Log("[UIFont] ViewModel初始化");

            _categoryService = categoryService;
            _favoriteService = favoriteService;

            var config = FontManager.LoadConfiguration();
            _currentSettings = config.UIFont.Clone();

            _selectedFontFamily = _currentSettings.FontFamily;
            _selectedFontSize = _currentSettings.FontSize;
            _selectedFontWeight = _currentSettings.FontWeight;
            _selectedLineHeight = _currentSettings.LineHeight;
            _selectedLetterSpacing = _currentSettings.LetterSpacing;

            Categories = new ObservableCollection<FontCategory>(
                Enum.GetValues(typeof(FontCategory)).Cast<FontCategory>()
            );

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var fonts = FontManager.GetSystemFonts();
                var favoriteSet = new System.Collections.Generic.HashSet<string>();
                var recentFonts = new System.Collections.Generic.List<string>();
                var items = BuildFontItemsCore(fonts, FontCategory.All, string.Empty, SortMode.Name, favoriteSet, recentFonts);
                var treeNodes = BuildFontTreeNodes(items);
                return () =>
                {
                    _cachedAllFonts = fonts;
                    AvailableFonts = new ObservableCollection<string>(fonts);
                    FilteredFonts.ReplaceAll(items);
                    FontTree.ReplaceAll(treeNodes);
                    TM.App.Log($"[UIFont] 已加载 {fonts.Count} 个系统字体");
                };
            }, "UIFont");

            ApplyCommand = new RelayCommand(ApplySettings);
            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            ToggleFavoriteCommand = new RelayCommand<string>(ToggleFavorite);
            ClearRecentCommand = new RelayCommand(ClearRecent);
            ToggleComparisonModeCommand = new RelayCommand(ToggleComparisonMode);
            ApplyMainFontCommand = new AsyncRelayCommand(ApplyMainFontAsync);
            ApplyComparisonFontCommand = new AsyncRelayCommand(ApplyComparisonFontAsync);
            ExportConfigCommand = new AsyncRelayCommand(ExportConfigAsync);
            ImportConfigCommand = new AsyncRelayCommand(ImportConfigAsync);
            ShareConfigCommand = new AsyncRelayCommand(ShareConfigAsync);
            SelectFontCommand = new TM.Framework.Common.Helpers.MVVM.RelayCommand(SelectFontFromTree);

            _comparisonSettings = new FontSettings
            {
                FontFamily = "Consolas",
                FontSize = 14,
                FontWeight = "Normal",
                LineHeight = 1.5,
                LetterSpacing = 0
            };
            _comparisonFontFamily = "Consolas";
            _comparisonFontSize = 14;
            _comparisonFontWeight = "Normal";
            _comparisonLineHeight = 1.5;
            _comparisonLetterSpacing = 0;
        }

        private void ScheduleFilter()
        {
            if (_filterDebounceTimer == null)
            {
                _filterDebounceTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _filterDebounceTimer.Tick += (_, _) => { _filterDebounceTimer.Stop(); ApplyFiltersAndSort(); };
            }
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private async void ApplyFiltersAndSort()
        {
            if (_cachedAllFonts.Count == 0) return;

            var fonts = _cachedAllFonts;
            var category = SelectedCategory;
            var searchText = SearchText;
            var sortMode = CurrentSortMode;
            var favoriteSet = new System.Collections.Generic.HashSet<string>(_favoriteService.GetFavorites(), StringComparer.OrdinalIgnoreCase);
            var recentFonts = sortMode == SortMode.RecentUsed ? _favoriteService.GetRecentFonts() : new System.Collections.Generic.List<string>();

            try
            {
                var (items, treeNodes) = await Task.Run(() =>
                {
                    var fontItems = BuildFontItemsCore(fonts, category, searchText, sortMode, favoriteSet, recentFonts);
                    return (fontItems, BuildFontTreeNodes(fontItems));
                });
                FilteredFonts.ReplaceAll(items);
                FontTree.ReplaceAll(treeNodes);
                TM.App.Log($"[UIFont] 筛选完成: {items.Count}个字体 (分类:{category}, 排序:{sortMode})");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 筛选字体失败: {ex.Message}");
            }
        }

        private List<FontItem> BuildFontItemsCore(
            System.Collections.Generic.List<string> fonts,
            FontCategory category, string searchText, SortMode sortMode,
            System.Collections.Generic.HashSet<string> favoriteSet,
            System.Collections.Generic.List<string> recentFonts)
        {
            var fontItems = fonts.Select(fontName =>
            {
                var cat = _categoryService.ClassifyFont(fontName);
                var isMono = _categoryService.IsMonospace(fontName);
                return new FontItem
                {
                    FontName = fontName,
                    Category = cat,
                    IsFavorite = favoriteSet.Contains(fontName),
                    IsMonospace = isMono,
                    Tags = _categoryService.GenerateTags(fontName, cat, isMono)
                };
            }).ToList();

            if (category != FontCategory.All)
                fontItems = fontItems.Where(f => f.Category == category).ToList();

            if (!string.IsNullOrWhiteSpace(searchText))
                fontItems = fontItems.Where(f => f.FontName.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            fontItems = sortMode switch
            {
                SortMode.Name => fontItems.OrderBy(f => f.FontName).ToList(),
                SortMode.RecentUsed => fontItems.OrderByDescending(f =>
                {
                    int index = recentFonts.IndexOf(f.FontName);
                    return index >= 0 ? recentFonts.Count - index : -1;
                }).ThenBy(f => f.FontName).ToList(),
                SortMode.FavoriteFirst => fontItems.OrderByDescending(f => f.IsFavorite).ThenBy(f => f.FontName).ToList(),
                _ => fontItems
            };

            return fontItems;
        }

        private static System.Collections.Generic.List<TM.Framework.Common.Controls.TreeNodeItem> BuildFontTreeNodes(
            System.Collections.Generic.IEnumerable<FontItem> fontItems)
        {
            var newItems = new System.Collections.Generic.List<TM.Framework.Common.Controls.TreeNodeItem>();
            foreach (var font in fontItems)
            {
                var icon = font.IsFavorite ? IconHelper.TryGet("Icon.Star") : IconHelper.TryGet("Icon.Font");
                newItems.Add(new TM.Framework.Common.Controls.TreeNodeItem
                {
                    Name = font.FontName,
                    Icon = icon,
                    Tag = font,
                    IsExpanded = false,
                    ShowChildCount = false
                });
            }
            return newItems;
        }

        private void SelectFontFromTree(object? parameter)
        {
            if (parameter is TM.Framework.Common.Controls.TreeNodeItem node && node.Tag is FontItem font)
            {
                if (_selectedFontNode != null)
                    _selectedFontNode.IsSelected = false;
                node.IsSelected = true;
                _selectedFontNode = node;
                SelectedFontFamily = font.FontName;
            }
        }

        private void ToggleFavorite(string? fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return;

            try
            {
                bool isFavorite = _favoriteService.ToggleFavorite(fontName);
                ApplyFiltersAndSort();

                string message = isFavorite ? $"已添加到收藏: {fontName}" : $"已从收藏移除: {fontName}";
                GlobalToast.Info("收藏", message);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 切换收藏失败: {ex.Message}");
                StandardDialog.ShowError($"切换收藏失败\n\n错误详情：{ex.Message}", "操作失败");
            }
        }

        private void ClearRecent()
        {
            try
            {
                _favoriteService.ClearRecent();
                ApplyFiltersAndSort();
                GlobalToast.Success("清除成功", "最近使用记录已清除");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 清除最近使用失败: {ex.Message}");
                StandardDialog.ShowError($"清除最近使用失败\n\n错误详情：{ex.Message}", "操作失败");
            }
        }

        private void ApplySettings()
        {
            try
            {
                FontManager.ApplyUIFont(_currentSettings);
                _favoriteService.RecordUsage(_currentSettings.FontFamily);
                TM.App.Log($"[UIFont] 字体设置已应用");
                GlobalToast.Success("应用成功", "UI字体设置已生效");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 应用字体失败: {ex.Message}");
                StandardDialog.ShowError($"应用字体失败\n\n错误详情：{ex.Message}", "应用失败");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = FontManager.LoadConfiguration();
                config.UIFont = _currentSettings.Clone();
                FontManager.SaveConfiguration(config);
                FontManager.ApplyUIFont(_currentSettings);
                TM.App.Log($"[UIFont] 字体设置已保存");
                GlobalToast.Success("保存成功", "UI字体设置已保存并应用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 保存字体失败: {ex.Message}");
                StandardDialog.ShowError($"保存字体失败\n\n错误详情：{ex.Message}", "保存失败");
            }
        }

        private void ResetSettings()
        {
            try
            {
                var defaultConfig = FontConfiguration.GetDefault();
                _currentSettings = defaultConfig.UIFont.Clone();

                SelectedFontFamily = _currentSettings.FontFamily;
                SelectedFontSize = _currentSettings.FontSize;
                SelectedFontWeight = _currentSettings.FontWeight;
                SelectedLineHeight = _currentSettings.LineHeight;
                SelectedLetterSpacing = _currentSettings.LetterSpacing;

                TM.App.Log($"[UIFont] 字体设置已重置为默认值");
                GlobalToast.Info("重置成功", "UI字体设置已恢复默认");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 重置字体失败: {ex.Message}");
                StandardDialog.ShowError($"重置字体失败\n\n错误详情：{ex.Message}", "重置失败");
            }
        }

        private void ToggleComparisonMode()
        {
            try
            {
                IsComparisonMode = !IsComparisonMode;
                TM.App.Log($"[UIFont] 对比模式已{(IsComparisonMode ? "启用" : "关闭")}");
                GlobalToast.Info("对比模式", IsComparisonMode ? "已启用" : "已关闭");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 切换对比模式失败: {ex.Message}");
                StandardDialog.ShowError($"切换对比模式失败\n\n错误详情：{ex.Message}", "操作失败");
            }
        }

        private async Task ApplyMainFontAsync()
        {
            try
            {
                TM.App.Log($"[UIFont] A/B测试: 应用主字体 {SelectedFontFamily}");

                var config = FontManager.LoadConfiguration();
                var originalSettings = config.UIFont.Clone();

                _currentSettings.FontFamily = SelectedFontFamily;
                _currentSettings.FontSize = SelectedFontSize;
                _currentSettings.FontWeight = SelectedFontWeight;
                _currentSettings.LineHeight = SelectedLineHeight;
                _currentSettings.LetterSpacing = SelectedLetterSpacing;

                FontManager.ApplyUIFont(_currentSettings);
                GlobalToast.Info("A/B测试", $"已应用主字体 {SelectedFontFamily}，5秒后自动恢复");

                await Task.Delay(5000);
                FontManager.ApplyUIFont(originalSettings);
                TM.App.Log("[UIFont] A/B测试: 已恢复原设置");
                GlobalToast.Info("A/B测试", "已自动恢复原字体");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] A/B测试失败: {ex.Message}");
                StandardDialog.ShowError($"A/B测试失败\n\n错误详情：{ex.Message}", "测试失败");
            }
        }

        private async Task ApplyComparisonFontAsync()
        {
            try
            {
                TM.App.Log($"[UIFont] A/B测试: 应用对比字体 {ComparisonFontFamily}");

                var config = FontManager.LoadConfiguration();
                var originalSettings = config.UIFont.Clone();

                FontManager.ApplyUIFont(_comparisonSettings);
                GlobalToast.Info("A/B测试", $"已应用对比字体 {ComparisonFontFamily}，5秒后自动恢复");

                await Task.Delay(5000);
                FontManager.ApplyUIFont(originalSettings);
                TM.App.Log("[UIFont] A/B测试: 已恢复原设置");
                GlobalToast.Info("A/B测试", "已自动恢复原字体");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] A/B测试失败: {ex.Message}");
                StandardDialog.ShowError($"A/B测试失败\n\n错误详情：{ex.Message}", "测试失败");
            }
        }

        private async System.Threading.Tasks.Task ExportConfigAsync()
        {
            try
            {
                await FontManager.ExportConfigurationAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 导出配置失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败\n\n错误详情：{ex.Message}", "导出失败");
            }
        }

        private async System.Threading.Tasks.Task ImportConfigAsync()
        {
            try
            {
                if (await FontManager.ImportConfigurationAsync())
                {
                    var config = FontManager.LoadConfiguration();
                    _currentSettings = config.UIFont.Clone();
                    SelectedFontFamily = _currentSettings.FontFamily;
                    SelectedFontSize = _currentSettings.FontSize;
                    SelectedFontWeight = _currentSettings.FontWeight;
                    SelectedLineHeight = _currentSettings.LineHeight;
                    SelectedLetterSpacing = _currentSettings.LetterSpacing;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 导入配置失败: {ex.Message}");
                StandardDialog.ShowError($"导入失败\n\n错误详情：{ex.Message}", "导入失败");
            }
        }

        private async System.Threading.Tasks.Task ShareConfigAsync()
        {
            try
            {
                await FontManager.ExportAsShareableAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIFont] 分享配置失败: {ex.Message}");
                StandardDialog.ShowError($"分享失败\n\n错误详情：{ex.Message}", "分享失败");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
