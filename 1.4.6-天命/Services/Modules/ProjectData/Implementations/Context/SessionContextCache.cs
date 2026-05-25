using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class SessionContextCache
    {
        public SessionContextCache() { }

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly HashSet<string> _invalidatedIds = new();
        private readonly object _cacheLock = new();

        private class CacheEntry
        {
            public object Data { get; set; } = default!;
            public DateTime CachedAt { get; set; }
            public long DataVersion { get; set; }
        }

        public async Task<T?> GetOrLoadAsync<T>(string id, Func<Task<T>> loader) where T : class
        {
            lock (_cacheLock)
            {
                if (_invalidatedIds.Contains(id))
                {
                    _cache.Remove(id);
                    _invalidatedIds.Remove(id);
                }

                if (_cache.TryGetValue(id, out var entry))
                {
                    return entry.Data as T;
                }
            }

            var data = await loader();

            lock (_cacheLock)
            {
                _cache[id] = new CacheEntry
                {
                    Data = data,
                    CachedAt = DateTime.UtcNow,
                    DataVersion = DateTime.UtcNow.Ticks
                };
            }

            return data;
        }

        public T? TryGet<T>(string id) where T : class
        {
            lock (_cacheLock)
            {
                if (_invalidatedIds.Contains(id))
                {
                    _cache.Remove(id);
                    _invalidatedIds.Remove(id);
                    return null;
                }

                if (_cache.TryGetValue(id, out var entry))
                {
                    return entry.Data as T;
                }
            }
            return null;
        }

        public void Set<T>(string id, T data) where T : class
        {
            lock (_cacheLock)
            {
                _cache[id] = new CacheEntry
                {
                    Data = data,
                    CachedAt = DateTime.UtcNow,
                    DataVersion = DateTime.UtcNow.Ticks
                };
                _invalidatedIds.Remove(id);
            }
        }

        public void InvalidateEntity(string id)
        {
            lock (_cacheLock)
            {
                _invalidatedIds.Add(id);
            }
            TM.App.Log($"[SessionCache] 已标记失效: {id}");
        }

        public void InvalidateLayer(string layer)
        {
            lock (_cacheLock)
            {
                var keysToInvalidate = new List<string>();
                foreach (var key in _cache.Keys)
                {
                    if (key.StartsWith($"{layer}_"))
                    {
                        keysToInvalidate.Add(key);
                    }
                }

                foreach (var key in keysToInvalidate)
                {
                    _invalidatedIds.Add(key);
                }

                TM.App.Log($"[SessionCache] 已标记层级失效: {layer}, 影响 {keysToInvalidate.Count} 条");
            }
        }

        public void Clear()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
                _invalidatedIds.Clear();
            }
            TM.App.Log("[SessionCache] 缓存已清空");
        }

        public (int CachedCount, int InvalidatedCount) GetStats()
        {
            lock (_cacheLock)
            {
                return (_cache.Count, _invalidatedIds.Count);
            }
        }

        public void Reset()
        {
            Clear();
            TM.App.Log("[SessionCache] 缓存已重置");
        }
    }
}
