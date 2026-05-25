using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Common.Services
{
    public class DirectoryStorage<TData> : IDataStorageStrategy<TData>
    {
        private readonly string _rootDir;
        private readonly Func<TData, string> _filePathResolver;
        private readonly Func<TData, string>? _idResolver;
        private readonly Func<TData, bool>? _saveFilter;

        public DirectoryStorage(string rootDir, Func<TData, string> filePathResolver, Func<TData, string>? idResolver = null, Func<TData, bool>? saveFilter = null)
        {
            _rootDir = rootDir;
            _filePathResolver = filePathResolver;
            _idResolver = idResolver;
            _saveFilter = saveFilter;
        }

        public List<TData> Load()
        {
            var result = new List<TData>();
            if (!Directory.Exists(_rootDir)) return result;

            var loadedIds = new HashSet<string>();

            foreach (var jsonFile in Directory.GetFiles(_rootDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    var items = JsonSerializer.Deserialize<List<TData>>(json, JsonHelper.Default);
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
                    TM.App.Log($"[DirectoryStorage] 加载失败 {jsonFile}: {ex.Message}");
                }
            }
            return result;
        }

        public void Save(List<TData> items)
        {
            if (items == null) return;

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
                        var json = JsonSerializer.Serialize(group.ToList(), JsonHelper.Default);
                        var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, filePath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[DirectoryStorage] 保存失败: {ex.Message}");
                    }
                }
            }

            CleanupStaleFiles(writtenFiles);
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
                    var json = await File.ReadAllTextAsync(jsonFile).ConfigureAwait(false);
                    var items = JsonSerializer.Deserialize<List<TData>>(json, JsonHelper.Default);
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
                        var json = JsonSerializer.Serialize(group.ToList(), JsonHelper.Default);
                        var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                        File.Move(tmp, filePath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[DirectoryStorage] 异步保存失败: {ex.Message}");
                    }
                }
            }

            CleanupStaleFiles(writtenFiles);
        }

        private void CleanupStaleFiles(HashSet<string> writtenFiles)
        {
            try
            {
                if (!Directory.Exists(_rootDir)) return;

                foreach (var file in Directory.GetFiles(_rootDir, "*.json", SearchOption.AllDirectories))
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
