using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TM.Framework.Common.Services
{
    public class SingleFileStorage<TData> : IDataStorageStrategy<TData>
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

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

        public async System.Threading.Tasks.Task<List<TData>> LoadAsync()
        {
            if (!File.Exists(_filePath)) return new List<TData>();
            try
            {
                await using var stream = File.OpenRead(_filePath);
                return await JsonSerializer.DeserializeAsync<List<TData>>(stream, JsonHelper.Default).ConfigureAwait(false) ?? new List<TData>();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(LoadAsync), ex);
                return new List<TData>();
            }
        }

        public async System.Threading.Tasks.Task SaveAsync(List<TData> items)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmp))
                {
                    await JsonSerializer.SerializeAsync(stream, items, JsonHelper.Default).ConfigureAwait(false);
                }
                File.Move(tmp, _filePath, overwrite: true);
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
