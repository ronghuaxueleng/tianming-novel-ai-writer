using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.Services
{
    public abstract partial class ModuleServiceBase<TCategory, TData>
        where TCategory : ICategory
        where TData : class, IDataItem
    {
        public bool IsCategoryOperationAllowed(string categoryName)
        {
            var category = Categories.FirstOrDefault(c => c.Name == categoryName);
            return category == null || !category.IsBuiltIn;
        }

        public bool IsCategoryBuiltIn(string categoryName)
        {
            var category = Categories.FirstOrDefault(c => c.Name == categoryName);
            return category?.IsBuiltIn ?? false;
        }

        #region 数据管理

        [Obfuscation(Exclude = true)]
        public List<TData> GetAllData()
        {
            return DataItems.ToList();
        }

        public void AddData(TData data)
        {
            if (data == null) return;

            EnsureDataIdentifiers(data);
            DataItems.Add(data);
            SaveData();
            if (!string.IsNullOrWhiteSpace(data.Id))
                SetSnapshotName(data.Id!, data.Name ?? string.Empty);
            if (TM.App.IsDebugMode)
                TM.App.Log($"[{GetType().Name}] 添加数据");
        }

        [Obfuscation(Exclude = true)]
        public async System.Threading.Tasks.Task AddDataAsync(TData data)
        {
            if (data == null) return;

            EnsureDataIdentifiers(data);
            DataItems.Add(data);
            await SaveDataAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(data.Id))
                SetSnapshotName(data.Id!, data.Name ?? string.Empty);
            if (TM.App.IsDebugMode)
                TM.App.Log($"[{GetType().Name}] 异步添加数据");
        }

        public void UpdateData(TData data)
        {
            if (data == null) return;

            var oldName = !string.IsNullOrWhiteSpace(data.Id) ? GetSnapshotName(data.Id!) : null;
            SyncCategoryId(data);
            SaveData();
            if (!string.IsNullOrWhiteSpace(data.Id))
                SetSnapshotName(data.Id!, data.Name ?? string.Empty);
            TryTriggerRenamePropagation(data.Id ?? string.Empty, oldName, data.Name);
            if (TM.App.IsDebugMode)
                TM.App.Log($"[{GetType().Name}] 更新数据");
        }

        [Obfuscation(Exclude = true)]
        public async System.Threading.Tasks.Task UpdateDataAsync(TData data)
        {
            if (data == null) return;

            var oldName = !string.IsNullOrWhiteSpace(data.Id) ? GetSnapshotName(data.Id!) : null;
            SyncCategoryId(data);
            await SaveDataAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(data.Id))
                SetSnapshotName(data.Id!, data.Name ?? string.Empty);
            TryTriggerRenamePropagation(data.Id ?? string.Empty, oldName, data.Name);
            if (TM.App.IsDebugMode)
                TM.App.Log($"[{GetType().Name}] 异步更新数据");
        }

        public void DeleteData(string dataId)
        {
            int removedCount = OnBeforeDeleteData(dataId);
            if (removedCount > 0)
            {
                SaveData();
                RemoveSnapshotName(dataId);
                TM.App.Log($"[{GetType().Name}] 删除数据: ID={dataId}");
            }
        }

        [Obfuscation(Exclude = true)]
        public async System.Threading.Tasks.Task DeleteDataAsync(string dataId)
        {
            int removedCount = OnBeforeDeleteData(dataId);
            if (removedCount > 0)
            {
                await SaveDataAsync().ConfigureAwait(false);
                RemoveSnapshotName(dataId);
                TM.App.Log($"[{GetType().Name}] 异步删除数据: ID={dataId}");
            }
        }

        protected abstract int OnBeforeDeleteData(string dataId);

        #endregion

        #region 防噪音机制（过滤空分类/空数据）

        public virtual List<TCategory> GetNonEmptyCategories()
        {
            var dataCategories = DataItems
                .Where(d => IsDataEnabled(d) && !string.IsNullOrEmpty(GetDataCategory(d)))
                .Select(d => GetDataCategory(d))
                .Distinct()
                .ToHashSet(StringComparer.Ordinal);

            return Categories
                .Where(c => c.IsEnabled && dataCategories.Contains(c.Name))
                .OrderBy(c => c.Order)
                .ToList();
        }

        public virtual List<TData> GetNonEmptyData()
        {
            return DataItems
                .Where(d => IsDataEnabled(d) && HasContent(d))
                .ToList();
        }

        protected virtual bool HasContent(TData data)
        {
            return data != null && IsDataEnabled(data);
        }

        private string GetDataCategory(TData data)
        {
            return data?.Category ?? string.Empty;
        }

        private bool IsDataEnabled(TData data)
        {
            return data?.IsEnabled ?? false;
        }

        #endregion

        #region 批量启用/禁用

        public virtual int SetCategoriesEnabled(IEnumerable<string> categoryNames, bool enabled)
        {
            if (categoryNames == null) return 0;
            var set = new HashSet<string>(categoryNames, StringComparer.Ordinal);
            int count = 0;
            foreach (var category in Categories)
            {
                if (set.Contains(category.Name) && category.IsEnabled != enabled)
                {
                    category.IsEnabled = enabled;
                    count++;
                }
            }
            if (count > 0) SaveCategories();
            return count;
        }

        public virtual async System.Threading.Tasks.Task<int> SetCategoriesEnabledAsync(IEnumerable<string> categoryNames, bool enabled)
        {
            if (categoryNames == null) return 0;
            var set = new HashSet<string>(categoryNames, StringComparer.Ordinal);
            int count = 0;
            foreach (var category in Categories)
            {
                if (set.Contains(category.Name) && category.IsEnabled != enabled)
                {
                    category.IsEnabled = enabled;
                    count++;
                }
            }
            if (count > 0) await SaveCategoriesAsync().ConfigureAwait(false);
            return count;
        }

        public int SetDataEnabledByCategories(IEnumerable<string> categoryNames, bool enabled)
        {
            if (categoryNames == null) return 0;
            var set = new HashSet<string>(categoryNames, StringComparer.Ordinal);
            int count = 0;
            foreach (var item in DataItems)
            {
                if (set.Contains(item.Category) && item.IsEnabled != enabled)
                {
                    item.IsEnabled = enabled;
                    count++;
                }
            }
            if (count > 0) SaveData();
            return count;
        }

        public async System.Threading.Tasks.Task<int> SetDataEnabledByCategoriesAsync(IEnumerable<string> categoryNames, bool enabled)
        {
            if (categoryNames == null) return 0;
            var set = new HashSet<string>(categoryNames, StringComparer.Ordinal);
            int count = 0;
            foreach (var item in DataItems)
            {
                if (set.Contains(item.Category) && item.IsEnabled != enabled)
                {
                    item.IsEnabled = enabled;
                    count++;
                }
            }
            if (count > 0) await SaveDataAsync().ConfigureAwait(false);
            return count;
        }

        #endregion

        private void EnqueueWriteCategoriesFile(string destPath, List<TCategory> data, string logTag)
        {
            lock (_saveCategoriesQueueLock)
            {
                var version = ++_saveCategoriesQueueVersion;
                _saveCategoriesQueueTask = _saveCategoriesQueueTask.ContinueWith(async _ =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
                        bool shouldWrite;
                        lock (_saveCategoriesQueueLock)
                        {
                            shouldWrite = (version == _saveCategoriesQueueVersion);
                        }
                        if (!shouldWrite) return;

                        var dir = Path.GetDirectoryName(destPath);
                        var tmp = destPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        await using (var stream = File.Create(tmp))
                        {
                            await JsonSerializer.SerializeAsync(stream, data, JsonHelper.Default).ConfigureAwait(false);
                        }
                        File.Move(tmp, destPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{logTag}] 后台写文件失败: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }
        }

        private void EnqueueWriteBuiltInCategoriesFile(string destPath, List<TCategory> data, string logTag)
        {
            lock (_saveBuiltInCategoriesQueueLock)
            {
                var version = ++_saveBuiltInCategoriesQueueVersion;
                _saveBuiltInCategoriesQueueTask = _saveBuiltInCategoriesQueueTask.ContinueWith(async _ =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
                        bool shouldWrite;
                        lock (_saveBuiltInCategoriesQueueLock)
                        {
                            shouldWrite = (version == _saveBuiltInCategoriesQueueVersion);
                        }
                        if (!shouldWrite) return;

                        var dir = Path.GetDirectoryName(destPath);
                        var tmp = destPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        await using (var stream = File.Create(tmp))
                        {
                            await JsonSerializer.SerializeAsync(stream, data, JsonHelper.Default).ConfigureAwait(false);
                        }
                        File.Move(tmp, destPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{logTag}] 后台写文件失败: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }
        }

        #region 数据持久化

        private List<TCategory> MergeCategories(List<TCategory> userCategories, List<TCategory> builtInCategories)
        {
            var result = new List<TCategory>();
            var userCategoryNames = new HashSet<string>(userCategories.Select(c => c.Name), StringComparer.Ordinal);

            result.AddRange(userCategories);

            foreach (var builtIn in builtInCategories)
            {
                if (!userCategoryNames.Contains(builtIn.Name))
                {
                    result.Add(builtIn);
                }
                else
                {
                    TM.App.Log($"[{GetType().Name}] 用户分类覆盖系统内置: {builtIn.Name}");
                }
            }

            return result.OrderBy(c => c.Order).ToList();
        }

        protected virtual List<TCategory> CreateDefaultCategories()
        {
            return new List<TCategory>();
        }

        private void SaveCategories()
        {
            try
            {
                var userCategories = Categories.Where(c => !c.IsBuiltIn).ToList();
                EnqueueWriteCategoriesFile(_categoriesFile, userCategories, GetType().Name + ".SaveCategories");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 保存分类失败: {ex.Message}");
            }
        }

        private void SaveBuiltInCategories()
        {
            try
            {
                var builtInCategories = Categories.Where(c => c.IsBuiltIn).ToList();
                EnqueueWriteBuiltInCategoriesFile(_builtInCategoriesFile, builtInCategories, GetType().Name + ".SaveBuiltInCategories");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 保存系统内置分类失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveCategoriesAsync()
        {
            try
            {
                var userCategories = Categories.Where(c => !c.IsBuiltIn).ToList();

                var dir = Path.GetDirectoryName(_categoriesFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var tmp = _categoriesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmp))
                {
                    await JsonSerializer.SerializeAsync(stream, userCategories, JsonHelper.Default).ConfigureAwait(false);
                }
                File.Move(tmp, _categoriesFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 异步保存分类失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveBuiltInCategoriesAsync()
        {
            try
            {
                var builtInCategories = Categories.Where(c => c.IsBuiltIn).ToList();

                var dir = Path.GetDirectoryName(_builtInCategoriesFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var tmp = _builtInCategoriesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmp))
                {
                    await JsonSerializer.SerializeAsync(stream, builtInCategories, JsonHelper.Default).ConfigureAwait(false);
                }
                File.Move(tmp, _builtInCategoriesFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 异步保存系统内置分类失败: {ex.Message}");
            }
        }

        public void SaveAllCategories()
        {
            SaveBuiltInCategories();
            SaveCategories();
        }

        public async System.Threading.Tasks.Task SaveAllCategoriesAsync()
        {
            await Task.WhenAll(
                SaveBuiltInCategoriesAsync(),
                SaveCategoriesAsync()).ConfigureAwait(false);
        }

        protected void SaveData()
        {
            var storage = _storage!;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                var immediateSnapshot = DataItems.ToList();
                lock (_saveDataQueueLock)
                {
                    var version = ++_saveDataQueueVersion;
                    _saveDataQueueTask = _saveDataQueueTask.ContinueWith(async _ =>
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
                            lock (_saveDataQueueLock)
                            {
                                if (version != _saveDataQueueVersion) return;
                            }
                            await storage.SaveAsync(immediateSnapshot).ConfigureAwait(false);
                            ModuleDataNotifier.RaiseDataSaved();
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[{GetType().Name}] 后台保存数据失败: {ex.Message}");
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
                }
            }
            else
            {
                var snapshot = DataItems.ToList();
                storage.SaveAsync(snapshot).SafeFireAndForget(ex => TM.App.Log($"[{GetType().Name}] 非UI保存失败: {ex.Message}"));
                ModuleDataNotifier.RaiseDataSaved();
            }

            TriggerVersionIncrement();
        }

        protected async System.Threading.Tasks.Task SaveDataAsync()
        {
            await _storage!.SaveAsync(DataItems.ToList()).ConfigureAwait(false);
            ModuleDataNotifier.RaiseDataSaved();
            TriggerVersionIncrement();
        }

        private bool _batchSaveInProgress;
        private bool _batchSaveDirty;

        public void BeginBatchSave()
        {
            _batchSaveInProgress = true;
            _batchSaveDirty = false;
        }

        public void EndBatchSave()
        {
            var dirty = _batchSaveDirty;
            _batchSaveInProgress = false;
            _batchSaveDirty = false;
            if (dirty)
                TriggerVersionIncrement();
        }

        private void TriggerVersionIncrement()
        {
            if (_batchSaveInProgress)
            {
                _batchSaveDirty = true;
                return;
            }

            if (!_versionIncrementPending)
            {
                _versionIncrementPending = true;
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_versionIncrementPending)
                    {
                        var moduleName = GetModuleName();
                        _versionTrackingService.IncrementModuleVersion(moduleName);
                        _versionIncrementPending = false;
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public string ModuleName => _moduleName;

        protected string GetModuleName() => _moduleName;

        #endregion
    }
}

