using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Services;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class DataIndexService
    {
        private readonly GuideContextService _guideContextService;

        public DataIndexService(GuideContextService guideContextService)
        {
            _guideContextService = guideContextService;

            try
            {
                GuideContextService.CacheInvalidated += (_, _) => Reset();
                TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectChanged += (_, _) => Reset();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataIndexService] 订阅缓存失效事件失败: {ex.Message}");
            }
        }

        private void Reset()
        {
            lock (_lock)
            {
                _nameToEntries.Clear();
                _idToEntry.Clear();
                _isInitialized = false;
            }
            Interlocked.Increment(ref _initEpoch);
            TM.App.Log("[DataIndexService] 项目切换/缓存失效，已重置实体索引");
        }

        private readonly Dictionary<string, List<IndexEntry>> _nameToEntries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IndexEntry> _idToEntry = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _isInitialized;
        private readonly object _lock = new();
        private int _initEpoch;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public bool IsInitialized => _isInitialized;

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                var epoch = Volatile.Read(ref _initEpoch);

                lock (_lock)
                {
                    if (_isInitialized)
                        return;

                    _nameToEntries.Clear();
                    _idToEntry.Clear();
                }

                await _guideContextService.InitializeCacheAsync();
                if (epoch != Volatile.Read(ref _initEpoch))
                    return;

                var characters = await _guideContextService.GetAllCharactersAsync();
                var locations = await _guideContextService.GetAllLocationsAsync();
                var factions = await _guideContextService.GetAllFactionsAsync();
                var plotRules = await _guideContextService.GetAllPlotRulesAsync();
                var worldRules = await _guideContextService.GetAllWorldRulesAsync();

                lock (_lock)
                {
                    if (_isInitialized)
                        return;
                    if (epoch != Volatile.Read(ref _initEpoch))
                        return;

                    foreach (var c in characters)
                        AddEntry(c.Id, c.Name, EntityCategory.Character, c);

                    foreach (var l in locations)
                        AddEntry(l.Id, l.Name, EntityCategory.Location, l);

                    foreach (var f in factions)
                        AddEntry(f.Id, f.Name, EntityCategory.Faction, f);

                    foreach (var p in plotRules)
                        AddEntry(p.Id, p.Name, EntityCategory.PlotRule, p);

                    foreach (var w in worldRules)
                        AddEntry(w.Id, w.Name, EntityCategory.WorldRule, w);

                    _isInitialized = true;
                }
            }
            finally
            {
                _initLock.Release();
            }

            TM.App.Log($"[DataIndexService] 索引初始化完成: {_idToEntry.Count} 个实体");
        }

        private void AddEntry(string id, string name, EntityCategory category, object data)
        {
            if (string.IsNullOrEmpty(id))
                return;

            var entry = new IndexEntry
            {
                Id = id,
                Name = name ?? string.Empty,
                Category = category,
                Data = data
            };

            _idToEntry[id] = entry;

            if (!string.IsNullOrEmpty(name))
            {
                if (!_nameToEntries.TryGetValue(name, out var list))
                {
                    list = new List<IndexEntry>();
                    _nameToEntries[name] = list;
                }
                list.Add(entry);
            }
        }

        public List<IndexEntry> FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return new List<IndexEntry>();

            lock (_lock)
            {
                return _nameToEntries.TryGetValue(name, out var entries)
                    ? new List<IndexEntry>(entries)
                    : new List<IndexEntry>();
            }
        }

        public List<IndexEntry> SearchByName(string keyword, int maxResults = 20)
        {
            if (string.IsNullOrEmpty(keyword))
                return new List<IndexEntry>();

            lock (_lock)
            {
                return _nameToEntries
                    .Where(kv => kv.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(kv => kv.Value)
                    .Take(maxResults)
                    .ToList();
            }
        }

        public IndexEntry? FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            lock (_lock)
            {
                return _idToEntry.TryGetValue(id, out var entry) ? entry : null;
            }
        }

        public List<string> ListIdsByCategory(EntityCategory category)
        {
            lock (_lock)
            {
                return _idToEntry.Values
                    .Where(e => e.Category == category)
                    .Select(e => e.Id)
                    .ToList();
            }
        }

        public List<IndexEntry> SearchByCategory(EntityCategory category, string keyword, int maxResults = 20)
        {
            lock (_lock)
            {
                var query = _idToEntry.Values.Where(e => e.Category == category);

                if (!string.IsNullOrEmpty(keyword))
                {
                    query = query.Where(e => 
                        e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        e.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                return query.Take(maxResults).ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _nameToEntries.Clear();
                _idToEntry.Clear();
                _isInitialized = false;
            }
            Interlocked.Increment(ref _initEpoch);
            TM.App.Log("[DataIndexService] 索引已清除");
        }

        public void UpdateEntry(string id, string name, EntityCategory category, object data)
        {
            lock (_lock)
            {
                if (_idToEntry.TryGetValue(id, out var oldEntry))
                {
                    if (!string.IsNullOrEmpty(oldEntry.Name) && 
                        _nameToEntries.TryGetValue(oldEntry.Name, out var oldList))
                    {
                        oldList.RemoveAll(e => e.Id == id);
                        if (oldList.Count == 0)
                            _nameToEntries.Remove(oldEntry.Name);
                    }
                }

                AddEntry(id, name, category, data);
            }
        }
    }

    public enum EntityCategory
    {
        Character,
        Location,
        Faction,
        PlotRule,
        WorldRule
    }

    public class IndexEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public EntityCategory Category { get; set; }
        public object? Data { get; set; }
    }
}
