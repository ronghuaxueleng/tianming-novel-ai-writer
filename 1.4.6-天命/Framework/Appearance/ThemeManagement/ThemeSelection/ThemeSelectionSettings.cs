using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Appearance.ThemeManagement.ThemeSelection
{
    public class ThemeSelectionSettings : BaseSettings<ThemeSelectionSettings, ThemeSelectionData>
    {
        public ThemeSelectionSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Appearance/ThemeManagement/ThemeSelection", "theme_selection_data.json");

        protected override ThemeSelectionData CreateDefaultData() => _objectFactory.Create<ThemeSelectionData>();

        private readonly object _lock = new object();

        public ThemeSelectionData GetData() { lock (_lock) { return Data; } }

        public HashSet<string> GetFavoriteIds() { lock (_lock) { return new HashSet<string>(Data.FavoriteIds); } }

        public void AddFavorite(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) throw new ArgumentNullException(nameof(themeId));
            lock (_lock) { if (Data.FavoriteIds.Add(themeId)) { SaveData(); TM.App.Log($"[ThemeSelectionSettings] 添加收藏: {themeId}"); } }
        }

        public void RemoveFavorite(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) throw new ArgumentNullException(nameof(themeId));
            lock (_lock) { if (Data.FavoriteIds.Remove(themeId)) { SaveData(); TM.App.Log($"[ThemeSelectionSettings] 移除收藏: {themeId}"); } }
        }

        public bool ToggleFavorite(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) throw new ArgumentNullException(nameof(themeId));
            lock (_lock)
            {
                bool isFavorite = Data.FavoriteIds.Contains(themeId);
                if (isFavorite) Data.FavoriteIds.Remove(themeId); else Data.FavoriteIds.Add(themeId);
                SaveData();
                TM.App.Log($"[ThemeSelectionSettings] 切换收藏 {themeId}: {!isFavorite}");
                return !isFavorite;
            }
        }

        public bool IsFavorite(string themeId) { lock (_lock) { return Data.FavoriteIds.Contains(themeId); } }

        public void RecordRecentTheme(string themeId, string themeName)
        {
            if (string.IsNullOrEmpty(themeId)) throw new ArgumentNullException(nameof(themeId));
            lock (_lock)
            {
                Data.RecentThemes.RemoveAll(r => r.ThemeId == themeId);
                Data.RecentThemes.Insert(0, new RecentThemeRecord { ThemeId = themeId, ThemeName = themeName, LastUsedTime = DateTime.Now });
                if (Data.RecentThemes.Count > 20) Data.RecentThemes = Data.RecentThemes.Take(20).ToList();
                SaveData();
                TM.App.Log($"[ThemeSelectionSettings] 记录最近使用: {themeName}");
            }
        }

        public List<RecentThemeRecord> GetRecentThemes(int count = 10) { lock (_lock) { return Data.RecentThemes.Take(count).ToList(); } }

        public void AddSearchHistory(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return;
            lock (_lock)
            {
                Data.SearchHistory.Remove(searchText);
                Data.SearchHistory.Insert(0, searchText);
                if (Data.SearchHistory.Count > 50) Data.SearchHistory = Data.SearchHistory.Take(50).ToList();
                SaveData();
            }
        }

        public List<string> GetSearchHistory(int count = 10) { lock (_lock) { return Data.SearchHistory.Take(count).ToList(); } }

        public void ClearSearchHistory() { lock (_lock) { Data.SearchHistory.Clear(); SaveData(); TM.App.Log("[ThemeSelectionSettings] 已清空搜索历史"); } }

        public void UpdatePreferences(ThemeSelectionPreferences preferences) { lock (_lock) { Data.Preferences = preferences ?? new ThemeSelectionPreferences(); SaveData(); } }

        public ThemeSelectionPreferences GetPreferences() { lock (_lock) { return Data.Preferences; } }
    }

    public class ThemeSelectionData
    {
        [System.Text.Json.Serialization.JsonPropertyName("FavoriteIds")] public HashSet<string> FavoriteIds { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("RecentThemes")] public List<RecentThemeRecord> RecentThemes { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("SearchHistory")] public List<string> SearchHistory { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Preferences")] public ThemeSelectionPreferences Preferences { get; set; } = new();
    }

    public class RecentThemeRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("ThemeId")] public string ThemeId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ThemeName")] public string ThemeName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastUsedTime")] public DateTime LastUsedTime { get; set; }
    }

    public class ThemeSelectionPreferences
    {
        [System.Text.Json.Serialization.JsonPropertyName("LastSearchText")] public string LastSearchText { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastSelectedCategory")] public string LastSelectedCategory { get; set; } = "全部";
        [System.Text.Json.Serialization.JsonPropertyName("LastSortMode")] public string LastSortMode { get; set; } = "默认排序";
        [System.Text.Json.Serialization.JsonPropertyName("ShowOnlyFavorites")] public bool ShowOnlyFavorites { get; set; } = false;
    }
}
