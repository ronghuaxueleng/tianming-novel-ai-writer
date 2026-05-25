using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontUsageData
    {
        [System.Text.Json.Serialization.JsonPropertyName("FavoriteFonts")] public List<string> FavoriteFonts { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("RecentFonts")] public List<FontUsageEntry> RecentFonts { get; set; } = new();
    }

    public class FontUsageEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("FontName")] public string FontName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastUsed")] public DateTime LastUsed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UsageCount")] public int UsageCount { get; set; }
    }

    public class FontFavoriteService
    {
        private readonly string _dataFilePath;
        private FontUsageData _data = null!;
        private const int MaxRecentFonts = 20;

        public FontFavoriteService()
        {
            _dataFilePath = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetFilePath(
                "Framework",
                "Appearance/Font",
                "favorites.json"
            );

            LoadData();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    _data = JsonSerializer.Deserialize<FontUsageData>(json) ?? new FontUsageData();
                    TM.App.Log($"[FontFavoriteService] 成功加载收藏数据，收藏字体:{_data.FavoriteFonts.Count}个，最近使用:{_data.RecentFonts.Count}个");
                }
                else
                {
                    _data = new FontUsageData();
                    TM.App.Log("[FontFavoriteService] 收藏数据文件不存在，创建新数据");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFavoriteService] 加载收藏数据失败: {ex.Message}");
                _data = new FontUsageData();
            }
        }

        private void SaveData()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_dataFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                string json = JsonSerializer.Serialize(_data, JsonHelper.Default);
                var tmpFf = _dataFilePath + ".tmp";
                File.WriteAllText(tmpFf, json);
                File.Move(tmpFf, _dataFilePath, overwrite: true);
                TM.App.Log("[FontFavoriteService] 收藏数据已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFavoriteService] 保存收藏数据失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveDataAsync()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_dataFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                string json = JsonSerializer.Serialize(_data, JsonHelper.Default);
                var tmpFfA = _dataFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpFfA, json);
                File.Move(tmpFfA, _dataFilePath, overwrite: true);
                TM.App.Log("[FontFavoriteService] 收藏数据已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFavoriteService] 异步保存收藏数据失败: {ex.Message}");
            }
        }

        public void AddToFavorites(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return;

            if (!_data.FavoriteFonts.Contains(fontName))
            {
                _data.FavoriteFonts.Add(fontName);
                SaveData();
                TM.App.Log($"[FontFavoriteService] 添加到收藏: {fontName}");
            }
        }

        public void RemoveFromFavorites(string fontName)
        {
            if (_data.FavoriteFonts.Remove(fontName))
            {
                SaveData();
                TM.App.Log($"[FontFavoriteService] 从收藏中移除: {fontName}");
            }
        }

        public bool ToggleFavorite(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return false;

            if (_data.FavoriteFonts.Contains(fontName))
            {
                RemoveFromFavorites(fontName);
                return false;
            }
            else
            {
                AddToFavorites(fontName);
                return true;
            }
        }

        public bool IsFavorite(string fontName)
        {
            return _data.FavoriteFonts.Contains(fontName);
        }

        public List<string> GetFavorites()
        {
            return new List<string>(_data.FavoriteFonts);
        }

        public void RecordUsage(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return;

            var existing = _data.RecentFonts.FirstOrDefault(f => f.FontName == fontName);
            if (existing != null)
            {
                existing.LastUsed = DateTime.Now;
                existing.UsageCount++;
            }
            else
            {
                _data.RecentFonts.Add(new FontUsageEntry
                {
                    FontName = fontName,
                    LastUsed = DateTime.Now,
                    UsageCount = 1
                });
            }

            if (_data.RecentFonts.Count > MaxRecentFonts)
            {
                _data.RecentFonts = _data.RecentFonts
                    .OrderByDescending(f => f.LastUsed)
                    .Take(MaxRecentFonts)
                    .ToList();
            }

            SaveData();
        }

        public List<string> GetRecentFonts()
        {
            return _data.RecentFonts
                .OrderByDescending(f => f.LastUsed)
                .Select(f => f.FontName)
                .ToList();
        }

        public int GetUsageCount(string fontName)
        {
            var entry = _data.RecentFonts.FirstOrDefault(f => f.FontName == fontName);
            return entry?.UsageCount ?? 0;
        }

        public void ClearRecent()
        {
            _data.RecentFonts.Clear();
            SaveData();
            TM.App.Log("[FontFavoriteService] 已清除最近使用记录");
        }

        public void ClearFavorites()
        {
            _data.FavoriteFonts.Clear();
            SaveData();
            TM.App.Log("[FontFavoriteService] 已清除所有收藏");
        }
    }
}

