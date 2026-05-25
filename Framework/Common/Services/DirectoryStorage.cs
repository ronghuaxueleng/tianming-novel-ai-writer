using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace TM.Framework.Common.Services
{
    public class DirectoryStorage<TData> : IDataStorageStrategy<TData>
    {
        private readonly string _rootDir;
        private readonly Func<TData, string> _filePathResolver;
        private readonly Func<TData, string>? _idResolver;
        private readonly Func<TData, bool>? _saveFilter;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public DirectoryStorage(string rootDir, Func<TData, string> filePathResolver, Func<TData, string>? idResolver = null, Func<TData, bool>? saveFilter = null)
        {
            _rootDir = rootDir;
            _filePathResolver = filePathResolver;
            _idResolver = idResolver;
            _saveFilter = saveFilter;
        }

        public async System.Threading.Tasks.Task<List<TData>> LoadAsync()
        {
            var result = new List<TData>();
            if (!Directory.Exists(_rootDir)) return result;

            var loadedIds = new HashSet<string>();

            foreach (var jsonFile in Directory.GetFiles(_rootDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    await using var stream = File.OpenRead(jsonFile);
                    var items = await JsonSerializer.DeserializeAsync<List<TData>>(stream, JsonHelper.Default).ConfigureAwait(false);
                    if (items != null)
                    {
                        if (_idResolver == null)
                        {
                            result.AddRange(items);
                        }
                        else
                        {
                            foreach (var item in items)
                            {
                                var id = _idResolver(item);
                                if (string.IsNullOrEmpty(id) || !loadedIds.Contains(id))
                                {
                                    result.Add(item);
                                    if (!string.IsNullOrEmpty(id)) loadedIds.Add(id);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[DirectoryStorage] 异步加载失败 {jsonFile}: {ex.Message}");
                }
            }
            return result;
        }

        public async System.Threading.Tasks.Task SaveAsync(List<TData> items)
        {
            if (items == null) return;

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var itemsToSave = _saveFilter != null ? items.Where(_saveFilter).ToList() : items;

                var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (itemsToSave.Count > 0)
                {
                    var grouped = itemsToSave.GroupBy(item => _filePathResolver(item));
                    foreach (var group in grouped)
                    {
                        try
                        {
                            var filePath = Path.GetFullPath(group.Key);
                            writtenFiles.Add(filePath);
                            var dir = Path.GetDirectoryName(filePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            await using (var stream = File.Create(tmp))
                            {
                                await JsonSerializer.SerializeAsync(stream, group.ToList(), JsonHelper.Default).ConfigureAwait(false);
                            }
                            File.Move(tmp, filePath, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[DirectoryStorage] 异步保存失败: {ex.Message}");
                        }
                    }
                }

                await CleanupStaleFilesAsync(writtenFiles).ConfigureAwait(false);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async System.Threading.Tasks.Task CleanupStaleFilesAsync(HashSet<string> writtenFiles)
        {
            try
            {
                if (!Directory.Exists(_rootDir)) return;

                var files = await System.Threading.Tasks.Task.Run(() => Directory.GetFiles(_rootDir, "*.json", SearchOption.AllDirectories)).ConfigureAwait(false);
                foreach (var file in files)
                {
                    var fullPath = Path.GetFullPath(file);
                    if (!writtenFiles.Contains(fullPath))
                    {
                        try
                        {
                            File.Delete(fullPath);
                            TM.App.Log($"[DirectoryStorage] 清理残留文件: {fullPath}");
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[DirectoryStorage] 清理残留文件失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DirectoryStorage] 扫描残留文件失败: {ex.Message}");
            }
        }
    }
}
