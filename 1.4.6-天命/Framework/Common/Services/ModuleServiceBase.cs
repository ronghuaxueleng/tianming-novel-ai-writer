using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.VersionTracking;

namespace TM.Framework.Common.Services
{
    public interface IClearAllService
    {
        int ClearAllData();
    }

    public interface ICascadeDeleteCategoryService
    {
        (int categoriesDeleted, int dataDeleted) CascadeDeleteCategory(string categoryName);
    }

    public abstract class ModuleServiceBase<TCategory, TData> : IAsyncInitializable, ICategorySaver, IClearAllService, ICascadeDeleteCategoryService
        where TCategory : ICategory
        where TData : class, IDataItem
    {
        private string _categoriesFile;
        private string _builtInCategoriesFile;
        private string _dataFile;
        private readonly string _modulePath;
        private IDataStorageStrategy<TData>? _storage;
        private readonly bool _delayDataLoading;
        private bool _versionIncrementPending = false;

        private readonly object _saveDataQueueLock = new();
        private int _saveDataQueueVersion;
        private System.Threading.Tasks.Task _saveDataQueueTask = System.Threading.Tasks.Task.CompletedTask;

        private readonly object _saveCategoriesQueueLock = new();
        private int _saveCategoriesQueueVersion;
        private System.Threading.Tasks.Task _saveCategoriesQueueTask = System.Threading.Tasks.Task.CompletedTask;

        private readonly object _saveBuiltInCategoriesQueueLock = new();
        private int _saveBuiltInCategoriesQueueVersion;
        private System.Threading.Tasks.Task _saveBuiltInCategoriesQueueTask = System.Threading.Tasks.Task.CompletedTask;

        private readonly IWorkScopeService _workScopeService;
        private readonly VersionTrackingService _versionTrackingService;

        private readonly object _initLock = new();
        private System.Threading.Tasks.Task? _initializeTask;

        protected List<TCategory> Categories { get; set; }
        protected List<TData> DataItems { get; set; }

        protected ModuleServiceBase(string modulePath, string categoriesFileName, string dataFileName, bool delayDataLoading = false)
        {
            _modulePath = modulePath;
            _categoriesFile = StoragePathHelper.GetFilePath("Modules", modulePath, categoriesFileName);
            _builtInCategoriesFile = StoragePathHelper.GetFilePath("Modules", modulePath, "built_in_categories.json");
            _dataFile = StoragePathHelper.GetFilePath("Modules", modulePath, dataFileName);
            _delayDataLoading = delayDataLoading;

            _workScopeService = ServiceLocator.Get<IWorkScopeService>();
            _versionTrackingService = ServiceLocator.Get<VersionTrackingService>();

            Categories = new List<TCategory>();
            DataItems = new List<TData>();

            _storage = new SingleFileStorage<TData>(_dataFile);

            StoragePathHelper.CurrentProjectChanged += (_, _) =>
            {
                lock (_initLock)
                {
                    _initializeTask = null;
                    Categories = new List<TCategory>();
                    DataItems = new List<TData>();
                }
                TM.App.Log($"[{GetType().Name}] 项目切换，已重置数据，等待下次访问时重新加载");
            };
        }

        protected void OverrideCategoriesFile(string path) { _categoriesFile = path; }

        protected void OverrideBuiltInCategoriesFile(string path) { _builtInCategoriesFile = path; }

        protected void OverrideDataFile(string path) { _dataFile = path; }

        protected void SetStorageStrategy(IDataStorageStrategy<TData> strategy)
        {
            _storage = strategy;
        }

        public bool IsInitialized
        {
            get
            {
                lock (_initLock)
                {
                    return _initializeTask?.IsCompletedSuccessfully ?? false;
                }
            }
        }

        public System.Threading.Tasks.Task InitializeAsync()
        {
            lock (_initLock)
            {
                return _initializeTask ??= InitializeCoreAsync();
            }
        }

        public void EnsureInitialized()
        {
            if (IsInitialized) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                _ = InitializeAsync();
                return;
            }

            System.Threading.Tasks.Task.Run(() => InitializeAsync()).GetAwaiter().GetResult();
        }

        #region 统一ID补全

        private bool EnsureCategoryIdsForLoadedCategories(out bool builtInUpdated)
        {
            bool updated = false;
            builtInUpdated = false;
            foreach (var category in Categories)
            {
                if (!EnsureCategoryId(category, category.IsBuiltIn))
                {
                    continue;
                }

                if (category.IsBuiltIn)
                {
                    builtInUpdated = true;
                }
                else
                {
                    updated = true;
                }
            }

            return updated;
        }

        private bool EnsureCategoryId(TCategory category, bool deterministic)
        {
            if (!string.IsNullOrWhiteSpace(category.Id))
            {
                return false;
            }

            var seed = $"{_modulePath}|{category.ParentCategory}|{category.Name}";
            var newId = deterministic
                ? ShortIdGenerator.NewDeterministic("C", seed)
                : ShortIdGenerator.New("C");
            category.Id = newId;
            TM.App.Log($"[{GetType().Name}] 自动分配分类ID: {category.Name} -> {newId}");
            return true;
        }

        private void EnsureDataIdentifiers(TData data)
        {
            try
            {
                EnsureDataId(data);
                EnsureDataCategoryId(data);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 自动补全数据ID失败: {ex.Message}");
            }
        }

        private void EnsureDataId(TData data)
        {
            if (!string.IsNullOrWhiteSpace(data.Id))
            {
                return;
            }

            var newId = ShortIdGenerator.New("D");
            data.Id = newId;
            TM.App.Log($"[{GetType().Name}] 自动分配数据ID: {newId}");
        }

        private void EnsureDataCategoryId(TData data)
        {
            if (!string.IsNullOrWhiteSpace(data.CategoryId))
            {
                return;
            }

            var categoryName = data.Category;
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return;
            }

            var category = Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));
            if (category == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(category.Id))
            {
                return;
            }

            data.CategoryId = category.Id;
            TM.App.Log($"[{GetType().Name}] 自动绑定CategoryId: {categoryName} -> {category.Id}");
        }

        #endregion

        private async System.Threading.Tasks.Task EnsureDataIdentifiersOnLoadAsync()
        {
            bool updated = false;
            foreach (var data in DataItems)
            {
                var hadId = !string.IsNullOrWhiteSpace(data.Id);
                var hadCategoryId = !string.IsNullOrWhiteSpace(data.CategoryId);
                EnsureDataIdentifiers(data);
                if (!hadId && !string.IsNullOrWhiteSpace(data.Id)) updated = true;
                if (!hadCategoryId && !string.IsNullOrWhiteSpace(data.CategoryId)) updated = true;
            }
            if (updated)
            {
                await SaveDataAsync().ConfigureAwait(false);
                TM.App.Log($"[{GetType().Name}] 初始化时补全Id/CategoryId并已回写");
            }
        }

        protected virtual System.Threading.Tasks.Task OnAfterCategoriesLoadedAsync()
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        protected virtual System.Threading.Tasks.Task OnInitializedAsync()
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        protected async System.Threading.Tasks.Task LoadDataInternalAsync()
        {
            if (_storage != null)
            {
                DataItems = await _storage.LoadAsync().ConfigureAwait(false);
                await EnsureDataIdentifiersOnLoadAsync().ConfigureAwait(false);
            }
        }

        private async System.Threading.Tasks.Task InitializeCoreAsync()
        {
            await LoadCategoriesAsync().ConfigureAwait(false);

            await OnAfterCategoriesLoadedAsync().ConfigureAwait(false);

            if (!_delayDataLoading && _storage != null)
            {
                DataItems = await _storage.LoadAsync().ConfigureAwait(false);
                await EnsureDataIdentifiersOnLoadAsync().ConfigureAwait(false);
            }

            await OnInitializedAsync().ConfigureAwait(false);
        }

        private async System.Threading.Tasks.Task LoadCategoriesAsync()
        {
            try
            {
                var userCategories = new List<TCategory>();
                var builtInCategories = new List<TCategory>();

                if (File.Exists(_categoriesFile))
                {
                    var json = await File.ReadAllTextAsync(_categoriesFile).ConfigureAwait(false);
                    var categories = JsonSerializer.Deserialize<List<TCategory>>(json);
                    if (categories != null)
                    {
                        userCategories = categories;
                    }
                }

                if (File.Exists(_builtInCategoriesFile))
                {
                    var json = await File.ReadAllTextAsync(_builtInCategoriesFile).ConfigureAwait(false);
                    var categories = JsonSerializer.Deserialize<List<TCategory>>(json);
                    if (categories != null)
                    {
                        foreach (var cat in categories)
                        {
                            cat.IsBuiltIn = true;
                        }
                        builtInCategories = categories;
                    }
                }

                Categories = MergeCategories(userCategories, builtInCategories);

                var userUpdated = EnsureCategoryIdsForLoadedCategories(out var builtInUpdated);
                if (builtInUpdated)
                {
                    await SaveBuiltInCategoriesAsync().ConfigureAwait(false);
                }
                if (userUpdated)
                {
                    await SaveCategoriesAsync().ConfigureAwait(false);
                }

                if (Categories.Count == 0)
                {
                    Categories = CreateDefaultCategories();
                    if (Categories.Count > 0)
                    {
                        await SaveCategoriesAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 异步加载分类失败: {ex.Message}");
                Categories = CreateDefaultCategories();
            }
        }

        #region 分类管理

        public List<TCategory> GetAllCategories()
        {
            return Categories.ToList();
        }

        protected bool IsCategoryNameAvailable(string? name, TCategory? exclude = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            foreach (var c in Categories)
            {
                if (exclude != null && ReferenceEquals(c, exclude)) continue;
                if (string.Equals(c.Name?.Trim(), trimmed, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public bool AddCategory(TCategory category)
        {
            if (category == null) return false;

            if (!IsCategoryNameAvailable(category.Name))
            {
                TM.App.Log($"[{GetType().Name}] 分类名称已存在，禁止添加: {category.Name}");
                return false;
            }

            AutoAssignBoundPrimaryType(category);

            EnsureCategoryId(category, category.IsBuiltIn);

            Categories.Add(category);
            SaveCategories();
            TM.App.Log($"[{GetType().Name}] 添加分类: {category.Name}");
            return true;
        }

        public async System.Threading.Tasks.Task<bool> AddCategoryAsync(TCategory category)
        {
            if (category == null) return false;

            if (!IsCategoryNameAvailable(category.Name))
            {
                TM.App.Log($"[{GetType().Name}] 分类名称已存在，禁止添加: {category.Name}");
                return false;
            }

            AutoAssignBoundPrimaryType(category);

            EnsureCategoryId(category, category.IsBuiltIn);

            Categories.Add(category);
            await SaveCategoriesAsync().ConfigureAwait(false);
            TM.App.Log($"[{GetType().Name}] 异步添加分类: {category.Name}");
            return true;
        }

        protected virtual void AutoAssignBoundPrimaryType(TCategory category)
        {
            if (category is not IBoundPrimaryTypeHost host) return;

            if (!string.IsNullOrEmpty(host.BoundPrimaryType)) return;

            string? assignedValue = null;

            if (!string.IsNullOrEmpty(category.ParentCategory))
            {
                var parentCategory = Categories.FirstOrDefault(c => c.Name == category.ParentCategory);
                if (parentCategory is IBoundPrimaryTypeHost parentHost)
                {
                    assignedValue = parentHost.BoundPrimaryType;
                }
            }

            if (string.IsNullOrEmpty(assignedValue))
            {
                var mapping = GetNameToPrimaryTypeMapping();
                if (mapping.TryGetValue(category.Name, out var mappedValue))
                {
                    assignedValue = mappedValue;
                }
            }

            if (string.IsNullOrEmpty(assignedValue))
            {
                assignedValue = GetDefaultPrimaryType();
            }

            if (!string.IsNullOrEmpty(assignedValue))
            {
                host.BoundPrimaryType = assignedValue;
                TM.App.Log($"[{GetType().Name}] 自动分配BoundPrimaryType: {category.Name} -> {assignedValue}");
            }
        }

        protected virtual Dictionary<string, string> GetNameToPrimaryTypeMapping()
        {
            return new Dictionary<string, string>();
        }

        protected virtual string GetDefaultPrimaryType()
        {
            return "其他";
        }

        public bool UpdateCategory(TCategory category)
        {
            if (category == null) return false;

            if (category.IsBuiltIn)
            {
                TM.App.Log($"[{GetType().Name}] 系统内置分类不可修改: {category.Name}");
                return false;
            }

            if (!IsCategoryNameAvailable(category.Name, category))
            {
                TM.App.Log($"[{GetType().Name}] 分类名称已存在，禁止改名: {category.Name}");
                return false;
            }

            SaveCategories();
            TM.App.Log($"[{GetType().Name}] 更新分类: {category.Name}");
            return true;
        }

        public async System.Threading.Tasks.Task<bool> UpdateCategoryAsync(TCategory category)
        {
            if (category == null) return false;

            if (category.IsBuiltIn)
            {
                TM.App.Log($"[{GetType().Name}] 系统内置分类不可修改: {category.Name}");
                return false;
            }

            if (!IsCategoryNameAvailable(category.Name, category))
            {
                TM.App.Log($"[{GetType().Name}] 分类名称已存在，禁止改名: {category.Name}");
                return false;
            }

            await SaveCategoriesAsync().ConfigureAwait(false);
            TM.App.Log($"[{GetType().Name}] 异步更新分类: {category.Name}");
            return true;
        }

        public void DeleteCategory(string categoryName)
        {
            var category = Categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null)
            {
                if (category.IsBuiltIn)
                {
                    TM.App.Log($"[{GetType().Name}] 系统内置分类不可删除: {categoryName}");
                    throw new InvalidOperationException($"系统内置分类「{categoryName}」不可删除");
                }

                int dataRemoved = DataItems.RemoveAll(d => 
                    string.Equals(d.Category, categoryName, StringComparison.Ordinal));

                Categories.Remove(category);
                SaveCategories();
                if (dataRemoved > 0) SaveData();

                TM.App.Log($"[{GetType().Name}] 删除分类: {categoryName}, 级联清理数据={dataRemoved}条");
            }
        }

        public async System.Threading.Tasks.Task DeleteCategoryAsync(string categoryName)
        {
            var category = Categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null)
            {
                if (category.IsBuiltIn)
                {
                    TM.App.Log($"[{GetType().Name}] 系统内置分类不可删除: {categoryName}");
                    throw new InvalidOperationException($"系统内置分类「{categoryName}」不可删除");
                }

                int dataRemoved = DataItems.RemoveAll(d => 
                    string.Equals(d.Category, categoryName, StringComparison.Ordinal));

                Categories.Remove(category);
                await SaveCategoriesAsync().ConfigureAwait(false);
                if (dataRemoved > 0) await SaveDataAsync().ConfigureAwait(false);

                TM.App.Log($"[{GetType().Name}] 异步删除分类: {categoryName}, 级联清理数据={dataRemoved}条");
            }
        }

        public virtual (int categoriesDeleted, int dataDeleted) CascadeDeleteCategory(string categoryName)
        {
            var root = Categories.FirstOrDefault(c => c.Name == categoryName);
            if (root != null && root.IsBuiltIn)
            {
                TM.App.Log($"[{GetType().Name}] 内置分类仅删除直属数据，保留分类节点及子树: {categoryName}");
                int dataRemoved = DataItems.RemoveAll(d => string.Equals(d.Category, categoryName, StringComparison.Ordinal));
                if (dataRemoved > 0) SaveData();
                TM.App.Log($"[{GetType().Name}] 内置分类直属数据已清除: {dataRemoved}条");
                return (0, dataRemoved);
            }

            return CascadeDeleteCategoryNames(CollectCategoryTree(categoryName));
        }

        public virtual int ClearAllData()
        {
            var userCatNames = Categories.Where(c => !c.IsBuiltIn).Select(c => c.Name).ToList();
            var (_, dataDeleted) = CascadeDeleteCategoryNames(userCatNames);
            return dataDeleted;
        }

        protected virtual (int categoriesDeleted, int dataDeleted) CascadeDeleteCategoryNames(List<string> categoryNames)
        {
            var allNameSet = new HashSet<string>(categoryNames, StringComparer.Ordinal);

            var builtInNames = new HashSet<string>(
                Categories.Where(c => c.IsBuiltIn && allNameSet.Contains(c.Name)).Select(c => c.Name),
                StringComparer.Ordinal);
            var catDeleteSet = new HashSet<string>(
                allNameSet.Where(n => !builtInNames.Contains(n)),
                StringComparer.Ordinal);

            int dataRemoved = DataItems.RemoveAll(d => allNameSet.Contains(d.Category));
            int catRemoved = Categories.RemoveAll(c => catDeleteSet.Contains(c.Name));

            if (catRemoved > 0) SaveCategories();
            if (dataRemoved > 0) SaveData();

            TM.App.Log($"[{GetType().Name}] 级联删除: 分类={catRemoved}个, 数据={dataRemoved}条");
            return (catRemoved, dataRemoved);
        }

        protected List<string> CollectCategoryTree(string categoryName)
        {
            var result = new List<string>();
            void Collect(string name)
            {
                result.Add(name);
                foreach (var child in Categories.Where(c => 
                    string.Equals(c.ParentCategory, name, StringComparison.Ordinal)))
                {
                    Collect(child.Name);
                }
            }
            if (!string.IsNullOrWhiteSpace(categoryName)) Collect(categoryName);
            return result;
        }

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

        #endregion

        #region 数据管理

        public List<TData> GetAllData()
        {
            return DataItems.ToList();
        }

        public void AddData(TData data)
        {
            if (data == null) return;

            EnsureDataIdentifiers(data);

            SetSourceBookIdIfSupported(data);

            DataItems.Add(data);
            SaveData();
            TM.App.Log($"[{GetType().Name}] 添加数据");
        }

        public async System.Threading.Tasks.Task AddDataAsync(TData data)
        {
            if (data == null) return;

            EnsureDataIdentifiers(data);

            SetSourceBookIdIfSupported(data);

            DataItems.Add(data);
            await SaveDataAsync().ConfigureAwait(false);
            TM.App.Log($"[{GetType().Name}] 异步添加数据");
        }

        public void UpdateData(TData data)
        {
            if (data == null) return;

            SaveData();
            TM.App.Log($"[{GetType().Name}] 更新数据");
        }

        public async System.Threading.Tasks.Task UpdateDataAsync(TData data)
        {
            if (data == null) return;

            await SaveDataAsync().ConfigureAwait(false);
            TM.App.Log($"[{GetType().Name}] 异步更新数据");
        }

        public void DeleteData(string dataId)
        {
            int removedCount = OnBeforeDeleteData(dataId);
            if (removedCount > 0)
            {
                SaveData();
                TM.App.Log($"[{GetType().Name}] 删除数据: ID={dataId}");
            }
        }

        public async System.Threading.Tasks.Task DeleteDataAsync(string dataId)
        {
            int removedCount = OnBeforeDeleteData(dataId);
            if (removedCount > 0)
            {
                await SaveDataAsync().ConfigureAwait(false);
                TM.App.Log($"[{GetType().Name}] 异步删除数据: ID={dataId}");
            }
        }

        protected abstract int OnBeforeDeleteData(string dataId);

        protected void SetSourceBookIdIfSupported(TData data)
        {
            try
            {
                if (data is not ISourceBookBound bound) return;

                if (!string.IsNullOrEmpty(bound.SourceBookId))
                    return;

                var currentSourceBookId = _workScopeService.CurrentSourceBookId;
                if (string.IsNullOrEmpty(currentSourceBookId))
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && dispatcher.CheckAccess())
                    {
                        _ = _workScopeService.GetCurrentScopeAsync();
                        return;
                    }

                    currentSourceBookId = System.Threading.Tasks.Task.Run(
                        () => _workScopeService.GetCurrentScopeAsync()).GetAwaiter().GetResult();
                }

                if (!string.IsNullOrEmpty(currentSourceBookId))
                {
                    bound.SourceBookId = currentSourceBookId;
                    TM.App.Log($"[{GetType().Name}] 自动设置 SourceBookId: {currentSourceBookId}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 设置 SourceBookId 失败: {ex.Message}");
            }
        }

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

        private void EnqueueWriteCategoriesFile(string destPath, string json, string logTag)
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
                        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                        File.Move(tmp, destPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{logTag}] 后台写文件失败: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }
        }

        private void EnqueueWriteBuiltInCategoriesFile(string destPath, string json, string logTag)
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
                        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
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
                var json = JsonSerializer.Serialize(userCategories, JsonHelper.Default);
                EnqueueWriteCategoriesFile(_categoriesFile, json, GetType().Name + ".SaveCategories");
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
                var json = JsonSerializer.Serialize(builtInCategories, JsonHelper.Default);
                EnqueueWriteBuiltInCategoriesFile(_builtInCategoriesFile, json, GetType().Name + ".SaveBuiltInCategories");
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

                var json = JsonSerializer.Serialize(userCategories, JsonHelper.Default);
                var dir = Path.GetDirectoryName(_categoriesFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var tmp = _categoriesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
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

                var json = JsonSerializer.Serialize(builtInCategories, JsonHelper.Default);
                var dir = Path.GetDirectoryName(_builtInCategoriesFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var tmp = _builtInCategoriesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
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
            await SaveBuiltInCategoriesAsync().ConfigureAwait(false);
            await SaveCategoriesAsync().ConfigureAwait(false);
        }

        protected void SaveData()
        {
            PrepareSourceBookIds();

            var storage = _storage!;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                var snapshot = DataItems.ToList();
                lock (_saveDataQueueLock)
                {
                    var version = ++_saveDataQueueVersion;
                    _saveDataQueueTask = _saveDataQueueTask.ContinueWith(async _ =>
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
                            bool shouldWrite;
                            lock (_saveDataQueueLock)
                            {
                                shouldWrite = (version == _saveDataQueueVersion);
                            }
                            if (!shouldWrite) return;

                            await storage.SaveAsync(snapshot).ConfigureAwait(false);
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
                storage.Save(DataItems);
            }

            TriggerVersionIncrement();
        }

        protected async System.Threading.Tasks.Task SaveDataAsync()
        {
            PrepareSourceBookIds();
            await _storage!.SaveAsync(DataItems).ConfigureAwait(false);
            TriggerVersionIncrement();
        }

        private void PrepareSourceBookIds()
        {
            try
            {
                var currentSourceBookId = _workScopeService.CurrentSourceBookId;
                if (string.IsNullOrEmpty(currentSourceBookId)) return;

                foreach (var item in DataItems)
                {
                    if (item is ISourceBookBound bound && string.IsNullOrEmpty(bound.SourceBookId))
                    {
                        bound.SourceBookId = currentSourceBookId;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 批量补写 SourceBookId 失败: {ex.Message}");
            }
        }

        private void TriggerVersionIncrement()
        {
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

        public string ModuleName => GetModuleName();

        protected string GetModuleName()
        {
            return _modulePath.Split('/').Last();
        }

        #endregion
    }
}
