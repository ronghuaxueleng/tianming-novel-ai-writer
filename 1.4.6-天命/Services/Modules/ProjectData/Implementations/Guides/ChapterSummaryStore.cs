using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ChapterSummaryStore
    {
        private readonly ConcurrentDictionary<int, Dictionary<string, string>> _volumeCache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ChapterSummaryStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _volumeCache.Clear();
                    TM.App.Log("[ChapterSummaryStore] 项目切换，已清除摘要分片缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterSummaryStore] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        #region 公开方法

        public async Task SetSummaryAsync(string chapterId, string summary)
        {
            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync();
            try
            {
                var existing = await LoadVolumeInternalAsync(vol);
                var updated = new Dictionary<string, string>(existing) { [chapterId] = summary };
                await SaveVolumeAsync(vol, updated);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task RemoveSummaryAsync(string chapterId)
        {
            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync();
            try
            {
                var existing = await LoadVolumeInternalAsync(vol);
                if (existing.ContainsKey(chapterId))
                {
                    var updated = new Dictionary<string, string>(existing);
                    updated.Remove(chapterId);
                    await SaveVolumeAsync(vol, updated);
                    TM.App.Log($"[SummaryStore] 已移除摘要: {chapterId}");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<string> GetSummaryAsync(string chapterId)
        {
            var vol = GetVolumeNumber(chapterId);
            var summaries = await LoadVolumeAsync(vol);
            return summaries.GetValueOrDefault(chapterId, string.Empty);
        }

        public async Task<Dictionary<string, string>> GetPreviousSummariesAsync(string currentChapterId, int count)
        {
            var parsed = ChapterParserHelper.ParseChapterId(currentChapterId);
            if (!parsed.HasValue)
                return new Dictionary<string, string>();

            var currentVol = parsed.Value.volumeNumber;

            var allSummaries = new Dictionary<string, string>(await LoadVolumeAsync(currentVol));

            var _preloadStart = System.Math.Max(1, currentVol - 5);
            var _preloadTasks = Enumerable.Range(_preloadStart, currentVol - _preloadStart)
                .Select(v => LoadVolumeAsync(v)).ToList();
            if (_preloadTasks.Count > 0) await Task.WhenAll(_preloadTasks);

            var volToLoad = currentVol - 1;
            while (volToLoad >= 1)
            {
                var previousCount = allSummaries
                    .Count(kv => ChapterParserHelper.CompareChapterId(kv.Key, currentChapterId) < 0);
                if (previousCount >= count) break;

                var prevVolSummaries = await LoadVolumeAsync(volToLoad);
                foreach (var kv in prevVolSummaries)
                    allSummaries.TryAdd(kv.Key, kv.Value);

                volToLoad--;
            }

            return allSummaries;
        }

        public async Task<Dictionary<string, string>> GetVolumeSummariesAsync(int volumeNumber)
        {
            return new Dictionary<string, string>(await LoadVolumeAsync(volumeNumber));
        }

        public async Task<Dictionary<string, string>> GetAllSummariesAsync()
        {
            var dir = GetSummariesDir();
            if (!Directory.Exists(dir))
                return new Dictionary<string, string>();

            var result = new Dictionary<string, string>();
            var files = Directory.GetFiles(dir, "vol*.json");
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith("vol") && int.TryParse(fileName.Substring(3), out var vol))
                {
                    var summaries = await LoadVolumeAsync(vol);
                    foreach (var kv in summaries)
                    {
                        result.TryAdd(kv.Key, kv.Value);
                    }
                }
            }

            return result;
        }

        public void InvalidateCache()
        {
            _volumeCache.Clear();
        }

        public async Task BulkSetAsync(Dictionary<string, string> summaries)
        {
            if (summaries == null || summaries.Count == 0) return;

            await _writeLock.WaitAsync();
            try
            {
                var byVolume = new Dictionary<int, Dictionary<string, string>>();
                foreach (var kv in summaries)
                {
                    var vol = GetVolumeNumber(kv.Key);
                    if (!byVolume.TryGetValue(vol, out var volDict))
                    {
                        volDict = new Dictionary<string, string>();
                        byVolume[vol] = volDict;
                    }
                    volDict[kv.Key] = kv.Value;
                }

                var dir = GetSummariesDir();
                Directory.CreateDirectory(dir);

                foreach (var (vol, volSummaries) in byVolume)
                {
                    await SaveVolumeInternalAsync(vol, volSummaries);
                    _volumeCache[vol] = volSummaries;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        #endregion

        #region 私有方法

        private string GetSummariesDir()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "summaries");
        }

        private string GetVolumeFilePath(int volumeNumber)
        {
            return Path.Combine(GetSummariesDir(), $"vol{volumeNumber}.json");
        }

        private static int GetVolumeNumber(string chapterId)
        {
            return ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
        }

        private async Task<Dictionary<string, string>> LoadVolumeAsync(int volumeNumber)
        {
            if (_volumeCache.TryGetValue(volumeNumber, out var cached))
                return cached;

            return await LoadVolumeInternalAsync(volumeNumber);
        }

        private async Task<Dictionary<string, string>> LoadVolumeInternalAsync(int volumeNumber)
        {
            var path = GetVolumeFilePath(volumeNumber);
            Dictionary<string, string> summaries;

            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path);
                    summaries = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                                ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SummaryStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
                    summaries = new Dictionary<string, string>();
                }
            }
            else
            {
                summaries = new Dictionary<string, string>();
            }

            _volumeCache[volumeNumber] = summaries;
            return summaries;
        }

        private async Task SaveVolumeAsync(int volumeNumber, Dictionary<string, string> summaries)
        {
            await SaveVolumeInternalAsync(volumeNumber, summaries);
            _volumeCache[volumeNumber] = summaries;
        }

        private async Task SaveVolumeInternalAsync(int volumeNumber, Dictionary<string, string> summaries)
        {
            var dir = GetSummariesDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetVolumeFilePath(volumeNumber);
            var tmpPath = path + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(summaries, JsonOptions);
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SummaryStore] 保存 vol{volumeNumber} 失败: {ex.Message}");

                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch { }

                throw;
            }
        }

        #endregion
    }
}
