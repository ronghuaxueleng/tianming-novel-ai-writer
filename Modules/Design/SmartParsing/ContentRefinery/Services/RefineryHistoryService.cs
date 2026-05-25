using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.Design.SmartParsing.ContentRefinery.Models;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Services
{
    public class RefineryHistoryService
    {
        private const int MaxRecords = 50;
        private List<RefineryHistoryRecord> _records = new();
        private readonly object _lock = new();
        private bool _loaded;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _saveVersion;

        public List<RefineryHistoryRecord> GetAll()
        {
            EnsureLoaded();
            lock (_lock)
                return _records.OrderByDescending(r => r.CreatedAt).ToList();
        }

        public void Add(RefineryHistoryRecord record)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(record.Id))
                record.Id = ShortIdGenerator.New("ref");
            if (record.CreatedAt == default)
                record.CreatedAt = DateTime.Now;

            lock (_lock)
            {
                _records.Add(record);

                if (_records.Count > MaxRecords)
                {
                    _records = _records
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(MaxRecords)
                        .ToList();
                }
            }

            Save();
        }

        public void MarkCommitted(string recordId)
        {
            EnsureLoaded();
            bool changed;
            lock (_lock)
            {
                var record = _records.FirstOrDefault(r => r.Id == recordId);
                changed = record != null;
                if (record != null)
                    record.IsCommitted = true;
            }
            if (changed)
                Save();
        }

        public void ClearAll()
        {
            lock (_lock)
                _records.Clear();
            Save();
        }

        public Task PreWarmAsync()
        {
            if (_loaded) return Task.CompletedTask;
            return Task.Run(async () =>
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    var path = StoragePathHelper.GetFilePath("Modules", "Design/SmartParsing/ContentRefinery", "history.json");
                    if (File.Exists(path))
                    {
                        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                        var loaded = JsonSerializer.Deserialize<List<RefineryHistoryRecord>>(json) ?? new();
                        lock (_lock)
                        {
                            var existingIds = new System.Collections.Generic.HashSet<string>(_records.Select(r => r.Id));
                            foreach (var r in loaded.Where(r => !existingIds.Contains(r.Id)))
                                _records.Add(r);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentRefinery] 加载历史记录失败: {ex.Message}");
                    lock (_lock)
                        _records = new();
                }
            });
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            PreWarmAsync().SafeFireAndForget(ex => TM.App.Log($"[ContentRefinery] 预热失败: {ex.Message}"));
        }

        private void Save()
        {
            List<RefineryHistoryRecord> snapshot;
            lock (_lock)
                snapshot = _records.ToList();
            var saveVersion = Interlocked.Increment(ref _saveVersion);
            _ = SaveCoreAsync(snapshot, saveVersion);
        }

        private async Task SaveCoreAsync(List<RefineryHistoryRecord> snapshot, int saveVersion)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _saveVersion))
                    return;
                var path = StoragePathHelper.GetFilePath("Modules", "Design/SmartParsing/ContentRefinery", "history.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(snapshot, JsonHelper.Default);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 保存历史记录失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
