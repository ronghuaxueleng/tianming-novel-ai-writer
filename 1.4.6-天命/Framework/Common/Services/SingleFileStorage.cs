using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Common.Services
{
    public class SingleFileStorage<TData> : IDataStorageStrategy<TData>
    {
        private readonly string _filePath;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SingleFileStorage] {key}: {ex.Message}");
        }

        public SingleFileStorage(string filePath) => _filePath = filePath;

        public List<TData> Load()
        {
            if (!File.Exists(_filePath)) return new List<TData>();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<TData>>(json, JsonHelper.Default) ?? new List<TData>();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(Load), ex);
                return new List<TData>();
            }
        }

        public async System.Threading.Tasks.Task<List<TData>> LoadAsync()
        {
            if (!File.Exists(_filePath)) return new List<TData>();
            try
            {
                var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<TData>>(json, JsonHelper.Default) ?? new List<TData>();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(LoadAsync), ex);
                return new List<TData>();
            }
        }

        public void Save(List<TData> items)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(items, JsonHelper.Default);
            var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }

        public async System.Threading.Tasks.Task SaveAsync(List<TData> items)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(items, JsonHelper.Default);
            var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _filePath, overwrite: true);
        }
    }
}
