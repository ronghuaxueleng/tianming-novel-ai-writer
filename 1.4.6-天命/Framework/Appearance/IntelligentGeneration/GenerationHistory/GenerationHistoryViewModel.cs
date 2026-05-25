using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.IntelligentGeneration.GenerationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class GenerationHistoryViewModel : INotifyPropertyChanged
    {
        private readonly GenerationHistorySettings _historySettings;
        private ObservableCollection<HistoryRecord> _historyRecords = new();
        private HistoryRecord? _selectedRecord = null;
        private string _searchKeyword = string.Empty;
        private string _selectedFilter = "全部";

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

            System.Diagnostics.Debug.WriteLine($"[GenerationHistory] {key}: {ex.Message}");
        }

        public ObservableCollection<HistoryRecord> HistoryRecords
        {
            get => _historyRecords;
            set
            {
                _historyRecords = value;
                OnPropertyChanged(nameof(HistoryRecords));
            }
        }

        public HistoryRecord? SelectedRecord
        {
            get => _selectedRecord;
            set
            {
                _selectedRecord = value;
                OnPropertyChanged(nameof(SelectedRecord));
            }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                _searchKeyword = value;
                OnPropertyChanged(nameof(SearchKeyword));
                ApplyFilter();
            }
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                _selectedFilter = value;
                OnPropertyChanged(nameof(SelectedFilter));
                ApplyFilter();
            }
        }

        public ObservableCollection<string> FilterOptions { get; } = new()
        {
            "全部", "收藏", "图片取色", "AI配色", "今天", "本周", "本月"
        };

        public ICommand RefreshCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ApplyRecordCommand { get; }
        public ICommand DeleteRecordCommand { get; }
        public ICommand ExportRecordCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }

        private List<HistoryRecord> _allRecords = new();

        public GenerationHistoryViewModel(GenerationHistorySettings historySettings)
        {
            _historySettings = historySettings;
            RefreshCommand = new RelayCommand(LoadHistory);
            ClearAllCommand = new RelayCommand(ClearAllHistory);
            ApplyRecordCommand = new RelayCommand(p => ApplyRecord(p as HistoryRecord));
            DeleteRecordCommand = new RelayCommand(p => DeleteRecord(p as HistoryRecord));
            ExportRecordCommand = new RelayCommand(p => ExportRecord(p as HistoryRecord));
            ToggleFavoriteCommand = new RelayCommand(p => ToggleFavorite(p as HistoryRecord));

            LoadHistory();
        }

        private void LoadHistory()
        {
            try
            {
                _allRecords.Clear();

                var records = _historySettings.GetAllRecords();

                TM.App.Log($"[GenerationHistory] 从Settings加载 {records.Count} 条记录");

                foreach (var record in records)
                {
                    _allRecords.Add(new HistoryRecord
                    {
                        Id = record.Id,
                        Type = record.Type,
                        Name = record.Name,
                        Timestamp = record.Timestamp,
                        PrimaryColor = ParseColor(record.PrimaryColor),
                        SecondaryColor = ParseColor(record.SecondaryColor),
                        AccentColor = ParseColor(record.AccentColor),
                        BackgroundColor = ParseColor(record.BackgroundColor),
                        TextColor = ParseColor(record.TextColor),
                        Harmony = record.Harmony,
                        Keywords = record.Keywords,
                        IsFavorite = record.IsFavorite,
                        ApplyCommand = ApplyRecordCommand,
                        DeleteCommand = DeleteRecordCommand,
                        ExportCommand = ExportRecordCommand,
                        FavoriteCommand = ToggleFavoriteCommand
                    });
                }

                TM.App.Log($"[GenerationHistory] 转换完成，_allRecords={_allRecords.Count}");

                ApplyFilter();

                TM.App.Log($"[GenerationHistory] 筛选完成：HistoryRecords={HistoryRecords.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 加载历史失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"加载历史记录失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private void ApplyFilter()
        {
            TM.App.Log($"[GenerationHistory] ApplyFilter开始：_allRecords数量={_allRecords.Count}");

            var filtered = _allRecords.AsEnumerable();

            if (SelectedFilter == "收藏")
            {
                filtered = filtered.Where(r => r.IsFavorite);
            }
            else if (SelectedFilter == "图片取色")
            {
                filtered = filtered.Where(r => r.Type == "图片取色");
            }
            else if (SelectedFilter == "AI配色")
            {
                filtered = filtered.Where(r => r.Type == "AI配色");
            }
            else if (SelectedFilter == "今天")
            {
                filtered = filtered.Where(r => r.Timestamp.Date == DateTime.Now.Date);
            }
            else if (SelectedFilter == "本周")
            {
                var weekStart = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek);
                filtered = filtered.Where(r => r.Timestamp >= weekStart);
            }
            else if (SelectedFilter == "本月")
            {
                filtered = filtered.Where(r => r.Timestamp.Month == DateTime.Now.Month && r.Timestamp.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                filtered = filtered.Where(r =>
                    r.Name.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase) ||
                    r.Keywords.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase));
                TM.App.Log($"[GenerationHistory] 应用关键词搜索: {SearchKeyword}");
            }

            var filteredList = filtered.ToList();
            TM.App.Log($"[GenerationHistory] 筛选后得到 {filteredList.Count} 条记录");

            HistoryRecords.Clear();
            foreach (var record in filteredList)
            {
                HistoryRecords.Add(record);
                TM.App.Log($"[GenerationHistory] 添加记录: {record.Name} ({record.Type}) - {record.Timestamp}");
            }

            TM.App.Log($"[GenerationHistory] ApplyFilter完成：HistoryRecords最终数量={HistoryRecords.Count}");
        }

        private void ApplyRecord(HistoryRecord? record)
        {
            if (record == null) return;

            try
            {
                var app = Application.Current;

                var isLight = GetColorBrightness(record.BackgroundColor) > 128;

                app.Resources["UnifiedBackground"] = new SolidColorBrush(AdjustColorBrightness(record.BackgroundColor, isLight ? -10 : 10));
                app.Resources["ContentBackground"] = new SolidColorBrush(record.BackgroundColor);
                app.Resources["Surface"] = new SolidColorBrush(record.BackgroundColor);
                app.Resources["ContentHighlight"] = new SolidColorBrush(AdjustColorBrightness(record.BackgroundColor, isLight ? 5 : -5));

                app.Resources["WindowBorder"] = new SolidColorBrush(isLight ? Color.FromRgb(203, 213, 225) : Color.FromRgb(51, 65, 85));
                app.Resources["BorderBrush"] = new SolidColorBrush(isLight ? Color.FromRgb(226, 232, 240) : Color.FromRgb(71, 85, 105));

                app.Resources["TextPrimary"] = new SolidColorBrush(record.TextColor);
                app.Resources["TextSecondary"] = new SolidColorBrush(AdjustColorBrightness(record.TextColor, isLight ? 40 : -40));
                app.Resources["TextTertiary"] = new SolidColorBrush(AdjustColorBrightness(record.TextColor, isLight ? 70 : -70));
                app.Resources["TextDisabled"] = new SolidColorBrush(AdjustColorBrightness(record.TextColor, isLight ? 100 : -100));

                app.Resources["HoverBackground"] = new SolidColorBrush(AdjustColorBrightness(record.BackgroundColor, isLight ? -15 : 15));
                app.Resources["ActiveBackground"] = new SolidColorBrush(AdjustColorBrightness(record.BackgroundColor, isLight ? -25 : 25));
                app.Resources["SelectedBackground"] = new SolidColorBrush(BlendColors(record.PrimaryColor, record.BackgroundColor, 0.2));

                app.Resources["PrimaryColor"] = new SolidColorBrush(record.PrimaryColor);
                app.Resources["PrimaryHover"] = new SolidColorBrush(AdjustColorBrightness(record.PrimaryColor, isLight ? -15 : 15));
                app.Resources["PrimaryActive"] = new SolidColorBrush(AdjustColorBrightness(record.PrimaryColor, isLight ? -30 : 30));

                app.Resources["SuccessColor"] = new SolidColorBrush(isLight ? Color.FromRgb(16, 185, 129) : Color.FromRgb(52, 211, 153));
                app.Resources["WarningColor"] = new SolidColorBrush(isLight ? Color.FromRgb(245, 158, 11) : Color.FromRgb(251, 191, 36));
                app.Resources["DangerColor"] = new SolidColorBrush(isLight ? Color.FromRgb(239, 68, 68) : Color.FromRgb(248, 113, 113));
                app.Resources["DangerHover"] = new SolidColorBrush(isLight ? Color.FromRgb(220, 38, 38) : Color.FromRgb(239, 68, 68));
                app.Resources["InfoColor"] = new SolidColorBrush(record.AccentColor);

                TM.App.Log($"[GenerationHistory] 已应用历史记录: {record.Name}");

                ToastNotification.ShowSuccess("应用成功", $"已应用历史配色方案「{record.Name}」");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 应用失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"应用历史记录失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private void DeleteRecord(HistoryRecord? record)
        {
            if (record == null) return;

            try
            {
                _historySettings.DeleteRecord(record.Id);

                _allRecords.Remove(record);
                ApplyFilter();

                TM.App.Log($"[GenerationHistory] 已删除历史记录: {record.Name}");

                ToastNotification.ShowSuccess("删除成功", $"已删除历史记录「{record.Name}」");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 删除失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"删除历史记录失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private async void ExportRecord(HistoryRecord? record)
        {
            if (record == null) return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var themeName = $"History_{record.Name}_{timestamp}";

                var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
                StoragePathHelper.EnsureDirectoryExists(themesPath);

                var fileName = string.Join("", themeName.Split(Path.GetInvalidFileNameChars())) + ".xaml";
                var filePath = Path.Combine(themesPath, fileName);

                var xamlContent = GenerateThemeXaml(record, themeName);
                var tmpGhv = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpGhv, xamlContent, Encoding.UTF8);
                File.Move(tmpGhv, filePath, overwrite: true);

                TM.App.Log($"[GenerationHistory] 已导出历史记录为主题: {themeName}");

                ToastNotification.ShowSuccess("导出成功", $"已导出为主题「{themeName}」");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 导出失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"导出历史记录失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private void ClearAllHistory()
        {
            try
            {
                _historySettings.ClearAll();

                _allRecords.Clear();
                ApplyFilter();

                TM.App.Log("[GenerationHistory] 已清空所有历史记录");

                ToastNotification.ShowSuccess("清空成功", "已清空所有历史记录");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 清空失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"清空历史记录失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private string GenerateThemeXaml(HistoryRecord record, string themeName)
        {
            var isLight = GetColorBrightness(record.BackgroundColor) > 128;

            var unifiedBg = GetColorHex(AdjustColorBrightness(record.BackgroundColor, isLight ? -10 : 10));
            var contentBg = GetColorHex(record.BackgroundColor);
            var surface = GetColorHex(record.BackgroundColor);
            var contentHighlight = GetColorHex(AdjustColorBrightness(record.BackgroundColor, isLight ? 5 : -5));

            var windowBorder = isLight ? "#CBD5E1" : "#334155";
            var borderBrush = isLight ? "#E2E8F0" : "#475569";

            var textPrimary = GetColorHex(record.TextColor);
            var textSecondary = GetColorHex(AdjustColorBrightness(record.TextColor, isLight ? 40 : -40));
            var textTertiary = GetColorHex(AdjustColorBrightness(record.TextColor, isLight ? 70 : -70));
            var textDisabled = GetColorHex(AdjustColorBrightness(record.TextColor, isLight ? 100 : -100));

            var hoverBg = GetColorHex(AdjustColorBrightness(record.BackgroundColor, isLight ? -15 : 15));
            var activeBg = GetColorHex(AdjustColorBrightness(record.BackgroundColor, isLight ? -25 : 25));
            var selectedBg = GetColorHex(BlendColors(record.PrimaryColor, record.BackgroundColor, 0.2));

            var primaryColor = GetColorHex(record.PrimaryColor);
            var primaryHover = GetColorHex(AdjustColorBrightness(record.PrimaryColor, isLight ? -15 : 15));
            var primaryActive = GetColorHex(AdjustColorBrightness(record.PrimaryColor, isLight ? -30 : 30));

            var successColor = isLight ? "#10B981" : "#34D399";
            var warningColor = isLight ? "#F59E0B" : "#FBBF24";
            var dangerColor = isLight ? "#EF4444" : "#F87171";
            var dangerHover = isLight ? "#DC2626" : "#EF4444";
            var infoColor = GetColorHex(record.AccentColor);

            return $@"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

    <!-- 历史记录导出主题: {themeName} -->
    <!-- 原始记录: {record.Type} - {record.Name} -->
    <!-- 生成时间: {record.Timestamp:yyyy-MM-dd HH:mm:ss} -->
    <!-- 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->

    <!-- 背景颜色 -->
    <SolidColorBrush x:Key=""UnifiedBackground"" Color=""{unifiedBg}""/>
    <SolidColorBrush x:Key=""ContentBackground"" Color=""{contentBg}""/>
    <SolidColorBrush x:Key=""Surface"" Color=""{surface}""/>
    <SolidColorBrush x:Key=""ContentHighlight"" Color=""{contentHighlight}""/>

    <!-- 边框颜色 -->
    <SolidColorBrush x:Key=""WindowBorder"" Color=""{windowBorder}""/>
    <SolidColorBrush x:Key=""BorderBrush"" Color=""{borderBrush}""/>

    <!-- 文字颜色 -->
    <SolidColorBrush x:Key=""TextPrimary"" Color=""{textPrimary}""/>
    <SolidColorBrush x:Key=""TextSecondary"" Color=""{textSecondary}""/>
    <SolidColorBrush x:Key=""TextTertiary"" Color=""{textTertiary}""/>
    <SolidColorBrush x:Key=""TextDisabled"" Color=""{textDisabled}""/>

    <!-- 交互状态颜色 -->
    <SolidColorBrush x:Key=""HoverBackground"" Color=""{hoverBg}""/>
    <SolidColorBrush x:Key=""ActiveBackground"" Color=""{activeBg}""/>
    <SolidColorBrush x:Key=""SelectedBackground"" Color=""{selectedBg}""/>

    <!-- 主题色 -->
    <SolidColorBrush x:Key=""PrimaryColor"" Color=""{primaryColor}""/>
    <SolidColorBrush x:Key=""PrimaryHover"" Color=""{primaryHover}""/>
    <SolidColorBrush x:Key=""PrimaryActive"" Color=""{primaryActive}""/>

    <!-- 功能色 -->
    <SolidColorBrush x:Key=""SuccessColor"" Color=""{successColor}""/>
    <SolidColorBrush x:Key=""WarningColor"" Color=""{warningColor}""/>
    <SolidColorBrush x:Key=""DangerColor"" Color=""{dangerColor}""/>
    <SolidColorBrush x:Key=""DangerHover"" Color=""{dangerHover}""/>
    <SolidColorBrush x:Key=""InfoColor"" Color=""{infoColor}""/>

</ResourceDictionary>";
        }

        #region 辅助方法

        private Color ParseColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                return Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ParseColor), ex);
                return Colors.White;
            }
        }

        private string GetColorHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private double GetColorBrightness(Color color)
        {
            return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114);
        }

        private Color AdjustColorBrightness(Color color, int amount)
        {
            return Color.FromRgb(
                (byte)Math.Clamp(color.R + amount, 0, 255),
                (byte)Math.Clamp(color.G + amount, 0, 255),
                (byte)Math.Clamp(color.B + amount, 0, 255)
            );
        }

        private Color BlendColors(Color c1, Color c2, double ratio)
        {
            var r = (byte)(c1.R * ratio + c2.R * (1 - ratio));
            var g = (byte)(c1.G * ratio + c2.G * (1 - ratio));
            var b = (byte)(c1.B * ratio + c2.B * (1 - ratio));
            return Color.FromRgb(r, g, b);
        }

        #endregion

        private void ToggleFavorite(HistoryRecord? record)
        {
            if (record == null) return;

            try
            {
                record.IsFavorite = !record.IsFavorite;

                _historySettings.UpdateFavorite(record.Id, record.IsFavorite);

                TM.App.Log($"[GenerationHistory] 切换收藏状态: {record.Name} -> {(record.IsFavorite ? "已收藏" : "取消收藏")}");

                ToastNotification.ShowSuccess(
                    record.IsFavorite ? "已收藏" : "已取消收藏",
                    record.Name
                );
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GenerationHistory] 切换收藏失败: {ex.Message}");
                StandardDialog.ShowError(
                    $"切换收藏失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class HistoryRecord : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Color PrimaryColor { get; set; }
        public Color SecondaryColor { get; set; }
        public Color AccentColor { get; set; }
        public Color BackgroundColor { get; set; }
        public Color TextColor { get; set; }
        public string Harmony { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;

        private bool _isFavorite = false;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                OnPropertyChanged(nameof(IsFavorite));
                OnPropertyChanged(nameof(FavoriteIcon));
            }
        }

        public SolidColorBrush PrimaryBrush => new(PrimaryColor);
        public SolidColorBrush SecondaryBrush => new(SecondaryColor);
        public SolidColorBrush AccentBrush => new(AccentColor);
        public SolidColorBrush BackgroundBrush => new(BackgroundColor);
        public SolidColorBrush TextBrush => new(TextColor);

        public string PrimaryHex => $"#{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}";
        public string FormattedTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string TypeIcon => Type == "图片取色" ? "🖼️" : "🎨";
        public string FavoriteIcon => IsFavorite ? "⭐" : "☆";

        public ICommand? ApplyCommand { get; set; }
        public ICommand? DeleteCommand { get; set; }
        public ICommand? ExportCommand { get; set; }
        public ICommand? FavoriteCommand { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HistoryRecordData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PrimaryColor")] public string PrimaryColor { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SecondaryColor")] public string SecondaryColor { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("AccentColor")] public string AccentColor { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BackgroundColor")] public string BackgroundColor { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("TextColor")] public string TextColor { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Harmony")] public string Harmony { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Keywords")] public string Keywords { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsFavorite")] public bool IsFavorite { get; set; } = false;
    }
}

