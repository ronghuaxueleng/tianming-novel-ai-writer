using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

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
        private readonly object _lock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _dataVersion;
        private const int MaxRecentFonts = 20;

        public FontFavoriteService()
        {
            _dataFilePath = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetFilePath(
                "Framework",
                "Appearance/Font",
                "favorites.json"
            );
            _data = new FontUsageData();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await LoadDataAsync().ConfigureAwait(false); }
                catch (Exception ex) { TM.App.Log($"[FontFavoriteService] 初始化加载失败: {ex.Message}"); }
            });
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            var loadVersion = Volatile.Read(ref _dataVersion);
            try
            {
                FontUsageData loaded;
                if (File.Exists(_dataFilePath))
                {
                    var json = await File.ReadAllTextAsync(_dataFilePath).ConfigureAwait(false);
                    loaded = JsonSerializer.Deserialize<FontUsageData>(json) ?? new FontUsageData();
                    TM.App.Log($"[FontFavoriteService] 成功加载收藏数据，收藏字体:{loaded.FavoriteFonts.Count}个，最近使用:{loaded.RecentFonts.Count}个");
                }
                else
                {
                    loaded = new FontUsageData();
                    TM.App.Log("[FontFavoriteService] 收藏数据文件不存在，创建新数据");
                }

                lock (_lock)
                {
                    if (loadVersion == Volatile.Read(ref _dataVersion))
                    {
                        _data = loaded;
                        return;
                    }

                    foreach (var f in loaded.FavoriteFonts)
                    {
                        if (!_data.FavoriteFonts.Contains(f))
                            _data.FavoriteFonts.Add(f);
                    }

                    var existingRecent = _data.RecentFonts
                        .GroupBy(r => r.FontName)
                        .ToDictionary(g => g.Key, g => g.First());
                    foreach (var r in loaded.RecentFonts)
                    {
                        if (string.IsNullOrWhiteSpace(r.FontName))
                            continue;
                        if (!existingRecent.TryGetValue(r.FontName, out var cur))
                        {
                            _data.RecentFonts.Add(r);
                            existingRecent[r.FontName] = r;
                        }
                        else
                        {
                            if (r.LastUsed > cur.LastUsed)
                                cur.LastUsed = r.LastUsed;
                            if (r.UsageCount > cur.UsageCount)
                                cur.UsageCount = r.UsageCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFavoriteService] 加载收藏数据失败: {ex.Message}");
                if (loadVersion != Volatile.Read(ref _dataVersion))
                    return;
                lock (_lock)
                {
                    if (loadVersion != Volatile.Read(ref _dataVersion))
                        return;
                    _data = new FontUsageData();
                }
            }
        }

        private async System.Threading.Tasks.Task SaveDataAsync()
        {
            int saveVersion;
            string json;
            lock (_lock)
            {
                saveVersion = _dataVersion;
                json = JsonSerializer.Serialize(_data, JsonHelper.Default);
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _dataVersion))
                    return;
                string? directory = Path.GetDirectoryName(_dataFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                var tmpFfA = _dataFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpFfA, json).ConfigureAwait(false);
                File.Move(tmpFfA, _dataFilePath, overwrite: true);
                TM.App.Log("[FontFavoriteService] 收藏数据已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFavoriteService] 异步保存收藏数据失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void AddToFavorites(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return;

            if (!_data.FavoriteFonts.Contains(fontName))
            {
                lock (_lock)
                {
                    if (_data.FavoriteFonts.Contains(fontName))
                        return;
                    Interlocked.Increment(ref _dataVersion);
                    _data.FavoriteFonts.Add(fontName);
                }
                _ = SaveDataAsync();
                TM.App.Log($"[FontFavoriteService] 添加到收藏: {fontName}");
            }
        }

        public void RemoveFromFavorites(string fontName)
        {
            bool removed;
            lock (_lock)
            {
                removed = _data.FavoriteFonts.Remove(fontName);
                if (removed)
                    Interlocked.Increment(ref _dataVersion);
            }

            if (removed)
            {
                _ = SaveDataAsync();
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
            lock (_lock) return _data.FavoriteFonts.Contains(fontName);
        }

        public List<string> GetFavorites()
        {
            lock (_lock) return new List<string>(_data.FavoriteFonts);
        }

        public void RecordUsage(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return;

            lock (_lock)
            {
                Interlocked.Increment(ref _dataVersion);
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
                    _data.RecentFonts.Sort((a, b) => b.LastUsed.CompareTo(a.LastUsed));
                    _data.RecentFonts.RemoveRange(MaxRecentFonts, _data.RecentFonts.Count - MaxRecentFonts);
                }
            }

            _ = SaveDataAsync();
        }

        public List<string> GetRecentFonts()
        {
            lock (_lock)
            {
                return _data.RecentFonts
                    .OrderByDescending(f => f.LastUsed)
                    .Select(f => f.FontName)
                    .ToList();
            }
        }

        public int GetUsageCount(string fontName)
        {
            lock (_lock)
            {
                var entry = _data.RecentFonts.FirstOrDefault(f => f.FontName == fontName);
                return entry?.UsageCount ?? 0;
            }
        }

        public void ClearRecent()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _dataVersion);
                _data.RecentFonts.Clear();
            }
            _ = SaveDataAsync();
            TM.App.Log("[FontFavoriteService] 已清除最近使用记录");
        }

        public void ClearFavorites()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _dataVersion);
                _data.FavoriteFonts.Clear();
            }
            _ = SaveDataAsync();
            TM.App.Log("[FontFavoriteService] 已清除所有收藏");
        }
    }
}

