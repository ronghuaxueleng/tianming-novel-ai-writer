using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class VolumeFactArchiveStore
    {
        private readonly ConcurrentDictionary<int, VolumeFactArchive> _cache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public VolumeFactArchiveStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _cache.Clear();
                    TM.App.Log("[FactArchiveStore] 项目切换，已清除卷存档缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        #region 公开方法

        public void InvalidateCache() => _cache.Clear();

        public async Task ArchiveVolumeAsync(int volumeNumber, FactSnapshot snapshot, string lastChapterId)
        {
            if (volumeNumber <= 0 || snapshot == null) return;

            var archive = new VolumeFactArchive
            {
                VolumeNumber = volumeNumber,
                LastChapterId = lastChapterId,
                ArchivedAt = DateTime.Now,
                CharacterStates = snapshot.CharacterStates ?? new(),
                ConflictProgress = snapshot.ConflictProgress ?? new(),
                ForeshadowingStatus = snapshot.ForeshadowingStatus ?? new(),
                LocationStates = snapshot.LocationStates ?? new(),
                FactionStates = snapshot.FactionStates ?? new(),
                ItemStates = snapshot.ItemStates ?? new(),
                Timeline = snapshot.Timeline ?? new(),
                CharacterLocations = snapshot.CharacterLocations ?? new()
            };

            await _writeLock.WaitAsync();
            try
            {
                await SaveArchiveAsync(volumeNumber, archive);
                _cache[volumeNumber] = archive;
            }
            finally
            {
                _writeLock.Release();
            }

            TM.App.Log($"[FactArchiveStore] 第{volumeNumber}卷存档完成: {lastChapterId}，角色{archive.CharacterStates.Count}条");
        }

        public async Task DeleteArchiveIfLastChapterAsync(int volumeNumber, string chapterId)
        {
            if (volumeNumber <= 0 || string.IsNullOrWhiteSpace(chapterId)) return;

            var archive = await LoadArchiveAsync(volumeNumber);
            if (archive == null) return;
            if (!string.Equals(archive.LastChapterId, chapterId, StringComparison.OrdinalIgnoreCase)) return;

            var path = GetArchiveFilePath(volumeNumber);
            await _writeLock.WaitAsync();
            try
            {
                _cache.TryRemove(volumeNumber, out _);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    TM.App.Log($"[FactArchiveStore] 第{volumeNumber}卷存档已删除（LastChapterId={chapterId} 被删除）");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<List<VolumeFactArchive>> GetPreviousArchivesAsync(int currentVolumeNumber)
        {
            var result = new List<VolumeFactArchive>();
            if (currentVolumeNumber <= 1) return result;

            var dir = GetArchivesDir();
            if (!Directory.Exists(dir)) return result;

            var maxVols = LayeredContextConfig.ArchiveMaxPreviousVolumes;
            var startVol = System.Math.Max(1, currentVolumeNumber - maxVols);
            for (int vol = startVol; vol < currentVolumeNumber; vol++)
            {
                var archive = await LoadArchiveAsync(vol);
                if (archive != null)
                    result.Add(archive);
            }

            return result;
        }

        #endregion

        #region 私有方法

        private string GetArchivesDir()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
        }

        private string GetArchiveFilePath(int volumeNumber)
        {
            return Path.Combine(GetArchivesDir(), $"vol{volumeNumber}.json");
        }

        private async Task<VolumeFactArchive?> LoadArchiveAsync(int volumeNumber)
        {
            if (_cache.TryGetValue(volumeNumber, out var cached))
                return cached;

            var path = GetArchiveFilePath(volumeNumber);
            if (!File.Exists(path)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var archive = JsonSerializer.Deserialize<VolumeFactArchive>(json, _jsonOptions);
                if (archive != null)
                    _cache[volumeNumber] = archive;
                return archive;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
                return null;
            }
        }

        private async Task SaveArchiveAsync(int volumeNumber, VolumeFactArchive archive)
        {
            var dir = GetArchivesDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetArchiveFilePath(volumeNumber);
            var tmpPath = path + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(archive, _jsonOptions);
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 保存 vol{volumeNumber} 失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        #endregion
    }
}
