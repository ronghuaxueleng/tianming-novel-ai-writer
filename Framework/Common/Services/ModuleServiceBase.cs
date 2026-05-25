using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
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

    public abstract partial class ModuleServiceBase<TCategory, TData> : IAsyncInitializable, ICategorySaver, IClearAllService, ICascadeDeleteCategoryService
        where TCategory : ICategory
        where TData : class, IDataItem
    {
        private string _categoriesFile;
        private string _builtInCategoriesFile;
        private string _dataFile;
        private readonly string _modulePath;
        private readonly string _moduleName;
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

        private readonly VersionTrackingService _versionTrackingService;

        private readonly object _initLock = new();
        private System.Threading.Tasks.Task? _initializeTask;

        protected List<TCategory> Categories { get; set; }
        protected List<TData> DataItems { get; set; }

        private readonly object _nameSnapshotLock = new();
        private readonly Dictionary<string, string> _nameSnapshot = new(StringComparer.Ordinal);

        protected ModuleServiceBase(string modulePath, string categoriesFileName, string dataFileName, bool delayDataLoading = false)
        {
            _modulePath = modulePath;
            _moduleName = modulePath.Split('/').Last();
            _categoriesFile = StoragePathHelper.GetFilePath("Modules", modulePath, categoriesFileName);
            _builtInCategoriesFile = StoragePathHelper.GetFilePath("Modules", modulePath, "built_in_categories.json");
            _dataFile = StoragePathHelper.GetFilePath("Modules", modulePath, dataFileName);
            _delayDataLoading = delayDataLoading;

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

            StoragePathHelper.ModuleDataIsEnabledChanged += (dirPath, enabled) =>
            {
                var myDir = System.IO.Path.GetDirectoryName(_dataFile);
                if (!string.Equals(myDir, dirPath, StringComparison.OrdinalIgnoreCase)) return;
                lock (_initLock)
                {
                    foreach (var item in DataItems)
                        item.IsEnabled = enabled;
                }
                TM.App.Log($"[{GetType().Name}] 内存 IsEnabled 已同步为 {enabled}");
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

        public System.Threading.Tasks.Task ReloadAsync()
        {
            lock (_initLock)
            {
                _initializeTask = null;
                Categories = new List<TCategory>();
                DataItems = new List<TData>();
            }
            return InitializeAsync();
        }

        public void EnsureInitialized()
        {
            if (IsInitialized) return;
            _ = InitializeAsync();
        }

        public System.Threading.Tasks.Task EnsureInitializedAsync()
        {
            if (IsInitialized) return System.Threading.Tasks.Task.CompletedTask;
            return InitializeAsync();
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

        private void SyncCategoryId(TData data)
        {
            var categoryName = data.Category;
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            var category = Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));
            if (category == null || string.IsNullOrWhiteSpace(category.Id)) return;

            if (data.CategoryId == category.Id) return;

            data.CategoryId = category.Id;
            TM.App.Log($"[{GetType().Name}] 同步CategoryId: {categoryName} -> {category.Id}");
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

            RebuildNameSnapshot();
        }

        #region P0-E: Name 变化追踪（用于 Rename 全局传播）

        protected virtual string? GetEntityTypeKeyForPropagation() => null;

        protected void RebuildNameSnapshot()
        {
            lock (_nameSnapshotLock)
            {
                _nameSnapshot.Clear();
                foreach (var item in DataItems)
                {
                    if (item == null) continue;
                    var id = item.Id;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    _nameSnapshot[id!] = item.Name ?? string.Empty;
                }
            }
        }

        private string? GetSnapshotName(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_nameSnapshotLock)
            {
                return _nameSnapshot.TryGetValue(id, out var name) ? name : null;
            }
        }

        private void SetSnapshotName(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_nameSnapshotLock)
            {
                _nameSnapshot[id] = name ?? string.Empty;
            }
        }

        private void RemoveSnapshotName(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_nameSnapshotLock)
            {
                _nameSnapshot.Remove(id);
            }
        }

        protected void TryTriggerRenamePropagation(string id, string? oldName, string? newName)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

            var entityTypeKey = GetEntityTypeKeyForPropagation();
            if (string.IsNullOrWhiteSpace(entityTypeKey)) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var propagationType = Type.GetType("TM.Services.Framework.AI.EntityPropagationService, 天命", throwOnError: false)
                                           ?? Type.GetType("TM.Services.Framework.AI.EntityPropagationService", throwOnError: false);
                    if (propagationType == null)
                    {
                        TM.App.Log($"[{GetType().Name}] Rename 传播跳过：未能解析 EntityPropagationService 类型");
                        return;
                    }

                    var instance = Activator.CreateInstance(propagationType);
                    var method = propagationType.GetMethod("PropagateRenameAsync");
                    if (instance == null || method == null)
                    {
                        TM.App.Log($"[{GetType().Name}] Rename 传播跳过：未能解析 PropagateRenameAsync 方法");
                        return;
                    }

                    var task = method.Invoke(instance, new object[] { id, entityTypeKey!, oldName!, newName! }) as System.Threading.Tasks.Task;
                    if (task == null) return;
                    await task.ConfigureAwait(false);

                    var resultProp = task.GetType().GetProperty("Result");
                    var result = resultProp?.GetValue(task) as string ?? string.Empty;
                    TM.App.Log($"[{GetType().Name}] Rename 传播完成: {entityTypeKey} {id} '{oldName}' -> '{newName}': {result}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetType().Name}] Rename 传播异常（非致命）: {ex.Message}");
                }
            });
        }

        #endregion

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

        protected virtual string GetBuiltInCategoriesResourceMarker()
        {
            return $".Modules.{_modulePath.Replace('/', '.')}.Resources.built_in_categories.json";
        }

        private async System.Threading.Tasks.Task<List<TCategory>> LoadEmbeddedBuiltInCategoriesAsync()
        {
            var result = new List<TCategory>();
            try
            {
                var asm = GetType().Assembly;
                var marker = GetBuiltInCategoriesResourceMarker();
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(marker, StringComparison.Ordinal));

                if (resourceName == null)
                    return result;

                await using var stream = asm.GetManifestResourceStream(resourceName)!;
                var categories = await JsonSerializer.DeserializeAsync<List<TCategory>>(stream, JsonHelper.Default).ConfigureAwait(false);
                if (categories != null)
                {
                    foreach (var cat in categories)
                        cat.IsBuiltIn = true;
                    result = categories;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 加载嵌入内置分类失败: {ex.Message}");
            }
            return result;
        }

        private static List<TCategory> MergeBuiltInRuntimeCache(List<TCategory> baseline, List<TCategory> runtime)
        {
            var result = baseline.ToList();
            foreach (var item in runtime)
            {
                var index = result.FindIndex(c =>
                    (!string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(item.Id) && string.Equals(c.Id, item.Id, StringComparison.Ordinal)) ||
                    string.Equals(c.Name, item.Name, StringComparison.Ordinal));

                if (index >= 0) result[index] = item;
                else result.Add(item);
            }
            return result.OrderBy(c => c.Order).ToList();
        }

        private async System.Threading.Tasks.Task LoadCategoriesAsync()
        {
            try
            {
                var userCategories = new List<TCategory>();

                if (File.Exists(_categoriesFile))
                {
                    await using var stream = File.OpenRead(_categoriesFile);
                    var categories = await JsonSerializer.DeserializeAsync<List<TCategory>>(stream, JsonHelper.Default).ConfigureAwait(false);
                    if (categories != null)
                    {
                        userCategories = categories;
                    }
                }

                var builtInCategories = await LoadEmbeddedBuiltInCategoriesAsync().ConfigureAwait(false);

                if (File.Exists(_builtInCategoriesFile))
                {
                    await using var builtInStream = File.OpenRead(_builtInCategoriesFile);
                    var storageCategories = await JsonSerializer.DeserializeAsync<List<TCategory>>(builtInStream, JsonHelper.Default).ConfigureAwait(false);
                    if (storageCategories != null && storageCategories.Count > 0)
                    {
                        foreach (var cat in storageCategories)
                            cat.IsBuiltIn = true;

                        builtInCategories = MergeBuiltInRuntimeCache(builtInCategories, storageCategories);
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

                var categoryId = category.Id;
                int dataRemoved = DataItems.RemoveAll(d =>
                    (!string.IsNullOrWhiteSpace(categoryId) && d.CategoryId == categoryId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && string.Equals(d.Category, categoryName, StringComparison.Ordinal)));

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

                var categoryId = category.Id;
                int dataRemoved = DataItems.RemoveAll(d =>
                    (!string.IsNullOrWhiteSpace(categoryId) && d.CategoryId == categoryId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && string.Equals(d.Category, categoryName, StringComparison.Ordinal)));

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
                var builtInId = root.Id;
                int dataRemoved = DataItems.RemoveAll(d =>
                    (!string.IsNullOrWhiteSpace(builtInId) && d.CategoryId == builtInId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && string.Equals(d.Category, categoryName, StringComparison.Ordinal)));
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

            var categoryIdSet = new HashSet<string>(
                Categories
                    .Where(c => allNameSet.Contains(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                    .Select(c => c.Id),
                StringComparer.Ordinal);

            int dataRemoved = DataItems.RemoveAll(d =>
                (!string.IsNullOrWhiteSpace(d.CategoryId) && categoryIdSet.Contains(d.CategoryId)) ||
                (string.IsNullOrWhiteSpace(d.CategoryId) && allNameSet.Contains(d.Category)));
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

        #endregion
    }
}

