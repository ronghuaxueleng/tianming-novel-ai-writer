using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TM.Framework.Common.Helpers
{
    public static class InfoLogDedup
    {
        private static readonly object _debugLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        public static void DebugLogOnce(string key, Exception ex, string? prefix = null)
        {
            if (!TM.App.IsDebugMode) return;
            lock (_debugLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key)) return;
            }
            System.Diagnostics.Debug.WriteLine($"[{prefix ?? "Debug"}] {key}: {ex.Message}");
        }
        private static readonly ConcurrentDictionary<string, DateTime> _cache = new();
        private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(3);
        private const int MaxEntries = 3000;

        public static bool ShouldLog(string key)
        {
            if (TM.App.IsDebugMode)
            {
                return true;
            }

            var now = DateTime.UtcNow;

            if (_cache.Count > MaxEntries)
            {
                _cache.Clear();
            }

            if (_cache.TryGetValue(key, out var last) && now - last < DedupWindow)
            {
                return false;
            }

            _cache[key] = now;
            return true;
        }
    }
}
