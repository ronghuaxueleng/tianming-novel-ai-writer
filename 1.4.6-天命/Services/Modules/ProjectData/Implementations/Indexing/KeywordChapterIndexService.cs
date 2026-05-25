using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class KeywordChapterIndexService
    {
        private Dictionary<string, List<string>>? _index;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _dirty;
        private volatile bool _pendingInvalidation;

        public KeywordChapterIndexService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private const int MaxChaptersPerKeyword = 50;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false
        };

        #region 公开方法

        public async Task IndexChapterAsync(string chapterId, ChapterChanges changes)
        {
            if (string.IsNullOrEmpty(chapterId) || changes == null) return;

            var keywords = ExtractKeywords(changes);
            if (keywords.Count == 0) return;

            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();

                foreach (var kw in keywords)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!_index!.TryGetValue(key, out var chapters))
                    {
                        chapters = new List<string>();
                        _index[key] = chapters;
                    }

                    if (!chapters.Contains(chapterId))
                    {
                        chapters.Add(chapterId);
                        if (chapters.Count > MaxChaptersPerKeyword)
                            chapters.RemoveRange(0, chapters.Count - MaxChaptersPerKeyword);
                    }
                }

                _dirty = true;
                await SaveAsync();
            }
            finally
            {
                _lock.Release();
            }

            TM.App.Log($"[KeywordIndex] 已索引 {chapterId}: {keywords.Count}个关键词");
        }

        public async Task<List<string>> SearchAsync(IEnumerable<string> keywords, int topK = 5)
        {
            var kwList = keywords?.ToList() ?? new List<string>();
            if (kwList.Count == 0) return new List<string>();

            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();

                var hitCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kw in kwList)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!_index!.TryGetValue(key, out var chapters)) continue;

                    foreach (var chapId in chapters)
                    {
                        hitCount[chapId] = hitCount.GetValueOrDefault(chapId) + 1;
                    }
                }

                return hitCount
                    .OrderByDescending(kv => kv.Value)
                    .Take(topK)
                    .Select(kv => kv.Key)
                    .ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveChapterAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();

                var modified = false;
                foreach (var chapters in _index!.Values)
                {
                    if (chapters.Remove(chapterId))
                        modified = true;
                }

                if (modified)
                {
                    _dirty = true;
                    await SaveAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<HashSet<string>> GetIndexedChapterIdsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var chapters in _index!.Values)
                    result.UnionWith(chapters);
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task IndexChapterFromKeywordsAsync(string chapterId, IEnumerable<string> keywords)
        {
            if (string.IsNullOrEmpty(chapterId) || keywords == null) return;

            var kwList = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            if (kwList.Count == 0) return;

            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();

                foreach (var kw in kwList)
                {
                    var key = NormalizeKeyword(kw);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!_index!.TryGetValue(key, out var chapters))
                    {
                        chapters = new List<string>();
                        _index[key] = chapters;
                    }

                    if (!chapters.Contains(chapterId))
                    {
                        chapters.Add(chapterId);
                        if (chapters.Count > MaxChaptersPerKeyword)
                            chapters.RemoveRange(0, chapters.Count - MaxChaptersPerKeyword);
                    }
                }

                _dirty = true;
                await SaveAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public void InvalidateCache()
        {
            _pendingInvalidation = true;
            _dirty = false;
        }

        #endregion

        #region 私有方法

        private static List<string> ExtractKeywords(ChapterChanges changes)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in changes.CharacterStateChanges ?? new())
                if (!string.IsNullOrWhiteSpace(c.CharacterId))
                    result.Add(c.CharacterId);

            foreach (var p in changes.NewPlotPoints ?? new())
                foreach (var kw in p.Keywords ?? new())
                    if (!string.IsNullOrWhiteSpace(kw))
                        result.Add(kw);

            foreach (var f in changes.ForeshadowingActions ?? new())
                if (!string.IsNullOrWhiteSpace(f.ForeshadowId))
                    result.Add(f.ForeshadowId);

            foreach (var item in changes.ItemTransfers ?? new())
                if (!string.IsNullOrWhiteSpace(item.ItemName))
                    result.Add(item.ItemName);

            return result.ToList();
        }

        private static string NormalizeKeyword(string kw)
        {
            return kw.Trim().ToLowerInvariant();
        }

        private async Task EnsureLoadedAsync()
        {
            if (_pendingInvalidation)
            {
                _index = null;
                _pendingInvalidation = false;
            }
            if (_index != null) return;

            var path = GetIndexFilePath();
            if (!File.Exists(path))
            {
                _index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, _jsonOptions);
                _index = raw != null
                    ? new Dictionary<string, List<string>>(raw, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 加载失败，使用空索引: {ex.Message}");
                _index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task SaveAsync()
        {
            if (!_dirty) return;

            var dir = Path.GetDirectoryName(GetIndexFilePath())!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetIndexFilePath();
            var tmpPath = path + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(_index, _jsonOptions);
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
                _dirty = false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[KeywordIndex] 保存失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        private static string GetIndexFilePath()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "keyword_index.json");
        }

        #endregion
    }
}
