using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.UI.Workspace.Services
{
    public class ChapterVersionService
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, ChapterVersionHistory> _histories = new();
        private readonly HashSet<string> _loadedFromDisk = new();
        private const int MaxVersions = 50;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public ChapterVersionService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    lock (_lock)
                    {
                        _histories.Clear();
                        _loadedFromDisk.Clear();
                    }
                    TM.App.Log("[ChapterVersionService] 项目切换，已清除版本历史内存缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterVersionService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public void SaveVersion(string chapterId, string content, string? description = null)
        {
            if (string.IsNullOrEmpty(chapterId) || string.IsNullOrEmpty(content))
                return;

            ChapterVersionHistory history;
            int versionCount;
            lock (_lock)
            {
                history = GetOrCreateHistory(chapterId);

                if (history.CurrentVersion != null &&
                    history.CurrentVersion.Content == content)
                {
                    return;
                }

                history.RedoStack.Clear();

                var version = new ChapterVersion
                {
                    Id = ShortIdGenerator.New("D"),
                    Content = content,
                    Timestamp = DateTime.Now,
                    Description = description ?? "自动保存",
                    WordCount = CountWords(content)
                };

                history.Versions.Add(version);
                history.CurrentIndex = history.Versions.Count - 1;

                if (history.Versions.Count > MaxVersions)
                {
                    var removeCount = history.Versions.Count - MaxVersions;
                    history.Versions.RemoveRange(0, removeCount);
                    history.CurrentIndex -= removeCount;
                }

                versionCount = history.Versions.Count;
            }

            TM.App.Log($"[ChapterVersionService] 保存版本: {chapterId}, 版本数: {versionCount}");
            _ = SaveHistoryToFileAsync(chapterId);
        }

        public string? Undo(string chapterId)
        {
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out var history))
                    return null;

                if (history.CurrentIndex <= 0)
                    return null;

                if (history.CurrentVersion != null)
                {
                    history.RedoStack.Push(history.CurrentVersion);
                }

                history.CurrentIndex--;
                TM.App.Log($"[ChapterVersionService] 撤销: {chapterId}, 当前索引: {history.CurrentIndex}");
                return history.CurrentVersion?.Content;
            }
        }

        public string? Redo(string chapterId)
        {
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out var history))
                    return null;

                if (history.RedoStack.Count == 0)
                    return null;

                var version = history.RedoStack.Pop();
                history.Versions.Add(version);
                history.CurrentIndex = history.Versions.Count - 1;

                TM.App.Log($"[ChapterVersionService] 重做: {chapterId}, 当前索引: {history.CurrentIndex}");

                return version.Content;
            }
        }

        public List<ChapterVersionInfo> GetVersionList(string chapterId)
        {
            ChapterVersion[] versions;
            int currentIndex;
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out var history))
                    return new List<ChapterVersionInfo>();

                versions = history.Versions.ToArray();
                currentIndex = history.CurrentIndex;
            }

            return versions
                .Select((v, i) => new ChapterVersionInfo
                {
                    Index = i,
                    Id = v.Id,
                    Timestamp = v.Timestamp,
                    Description = v.Description,
                    WordCount = v.WordCount,
                    IsCurrent = i == currentIndex
                })
                .OrderByDescending(v => v.Timestamp)
                .ToList();
        }

        public string? RevertToVersion(string chapterId, int index)
        {
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out var history))
                    return null;

                if (index < 0 || index >= history.Versions.Count)
                    return null;

                history.CurrentIndex = index;
                history.RedoStack.Clear();

                TM.App.Log($"[ChapterVersionService] 回退到版本: {chapterId}, 索引: {index}");

                return history.CurrentVersion?.Content;
            }
        }

        public string? GetVersionContent(string chapterId, int index)
        {
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out var history))
                    return null;

                if (index < 0 || index >= history.Versions.Count)
                    return null;

                return history.Versions[index].Content;
            }
        }

        public VersionDiff? CompareVersions(string chapterId, int index1, int index2)
        {
            ChapterVersionHistory? history;
            ChapterVersion v1;
            ChapterVersion v2;
            lock (_lock)
            {
                if (!_histories.TryGetValue(chapterId, out history))
                    return null;

                if (index1 < 0 || index1 >= history.Versions.Count ||
                    index2 < 0 || index2 >= history.Versions.Count)
                    return null;

                v1 = history.Versions[index1];
                v2 = history.Versions[index2];
            }

            return new VersionDiff
            {
                OldVersion = new ChapterVersionInfo
                {
                    Index = index1,
                    Id = v1.Id,
                    Timestamp = v1.Timestamp,
                    Description = v1.Description,
                    WordCount = v1.WordCount
                },
                NewVersion = new ChapterVersionInfo
                {
                    Index = index2,
                    Id = v2.Id,
                    Timestamp = v2.Timestamp,
                    Description = v2.Description,
                    WordCount = v2.WordCount
                },
                OldContent = v1.Content,
                NewContent = v2.Content,
                WordCountDiff = v2.WordCount - v1.WordCount
            };
        }

        public bool CanUndo(string chapterId)
        {
            lock (_lock)
                return _histories.TryGetValue(chapterId, out var history) && history.CurrentIndex > 0;
        }

        public bool CanRedo(string chapterId)
        {
            lock (_lock)
                return _histories.TryGetValue(chapterId, out var history) && history.RedoStack.Count > 0;
        }

        public void ClearHistory(string chapterId)
        {
            lock (_lock)
                _histories.Remove(chapterId);
        }

        public async Task SaveHistoryToFileAsync(string chapterId)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ChapterVersionHistory? history;
                lock (_lock)
                {
                    if (!_histories.TryGetValue(chapterId, out history))
                        return;
                }

                var historyDir = Path.Combine(StoragePathHelper.GetProjectHistoryPath(), chapterId);
                if (!Directory.Exists(historyDir))
                {
                    Directory.CreateDirectory(historyDir);
                }
                var path = Path.Combine(historyDir, "version_history.json");

                string json;
                lock (_lock)
                    json = JsonSerializer.Serialize(history, JsonOptions);
                var tmpPath = path + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterVersionService] 保存历史失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task LoadHistoryFromFileAsync(string chapterId)
        {
            try
            {
                var path = Path.Combine(StoragePathHelper.GetProjectHistoryPath(), chapterId, "version_history.json");

                if (!File.Exists(path))
                    return;

                var json = await File.ReadAllTextAsync(path);
                var history = JsonSerializer.Deserialize<ChapterVersionHistory>(json, JsonOptions);

                if (history != null)
                {
                    lock (_lock)
                        _histories[chapterId] = history;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterVersionService] 加载历史失败: {ex.Message}");
            }
        }

        private ChapterVersionHistory GetOrCreateHistory(string chapterId)
        {
            ChapterVersionHistory? history;
            var shouldLoad = false;

            if (!_histories.TryGetValue(chapterId, out history))
            {
                shouldLoad = _loadedFromDisk.Add(chapterId);
            }
            else
            {
                _loadedFromDisk.Add(chapterId);
                return history;
            }

            if (shouldLoad)
                TryLoadFromDiskSync(chapterId);

            if (!_histories.TryGetValue(chapterId, out history))
            {
                history = new ChapterVersionHistory { ChapterId = chapterId };
                _histories[chapterId] = history;
            }

            return history;
        }

        private void TryLoadFromDiskSync(string chapterId)
        {
            try
            {
                var path = Path.Combine(StoragePathHelper.GetProjectHistoryPath(), chapterId, "version_history.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var h = JsonSerializer.Deserialize<ChapterVersionHistory>(json, JsonOptions);
                if (h != null)
                {
                    lock (_lock)
                        _histories[chapterId] = h;
                    TM.App.Log($"[ChapterVersionService] 从磁盘恢复版本历史: {chapterId}, {h.Versions.Count} 条");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterVersionService] 磁盘加载历史失败 {chapterId}: {ex.Message}");
            }
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int count = 0;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    count++;
            }
            return count;
        }
    }

    public class ChapterVersionHistory
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Versions")] public List<ChapterVersion> Versions { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("CurrentIndex")] public int CurrentIndex { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("RedoStack")] public Stack<ChapterVersion> RedoStack { get; set; } = new();

        public ChapterVersion? CurrentVersion => 
            CurrentIndex >= 0 && CurrentIndex < Versions.Count ? Versions[CurrentIndex] : null;
    }

    public class ChapterVersion
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Content")] public string Content { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("WordCount")] public int WordCount { get; set; }
    }

    public class ChapterVersionInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Index")] public int Index { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("WordCount")] public int WordCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsCurrent")] public bool IsCurrent { get; set; }
    }

    public class VersionDiff
    {
        [System.Text.Json.Serialization.JsonPropertyName("OldVersion")] public ChapterVersionInfo OldVersion { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("NewVersion")] public ChapterVersionInfo NewVersion { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("OldContent")] public string OldContent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("NewContent")] public string NewContent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("WordCountDiff")] public int WordCountDiff { get; set; }
    }
}
