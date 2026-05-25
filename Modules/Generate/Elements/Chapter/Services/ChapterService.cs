using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;

namespace TM.Modules.Generate.Elements.Chapter.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterService : ModuleServiceBase<ChapterCategory, ChapterData>
    {
        private readonly VolumeDesignService _volumeDesignService;

        public event EventHandler<EventArgs>? DataChanged;

        private int _dataChangePending;

        private void RaiseDataChanged()
        {
            if (System.Threading.Interlocked.Exchange(ref _dataChangePending, 1) == 1) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        DataChanged?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ChapterService] RaiseDataChanged 异常: {ex.Message}");
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dataChangePending, 0);
                    }
                }));
            }
            else
            {
                try
                {
                    DataChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ChapterService] RaiseDataChanged 异常: {ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _dataChangePending, 0);
                }
            }
        }

        public ChapterService(VolumeDesignService volumeDesignService)
            : base(
                modulePath: "Generate/Elements/Chapter",
                categoriesFileName: "categories.json",
                dataFileName: "chapter_data.json")
        {
            _volumeDesignService = volumeDesignService;

            _volumeDesignService.CategoryDeleted += OnVolumeCategoryDeleted;
        }

        protected override string? GetEntityTypeKeyForPropagation() => "chapter";

        private void OnVolumeCategoryDeleted(object? sender, CategoryDeletedEventArgs e)
        {
            try
            {
                var categoryName = e.CategoryName;
                var categoryId = e.CategoryId;
                var dataToDelete = DataItems.Where(d =>
                    (!string.IsNullOrWhiteSpace(categoryId) && d.CategoryId == categoryId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName)).ToList();

                if (dataToDelete.Count > 0)
                {
                    foreach (var item in dataToDelete)
                    {
                        DataItems.Remove(item);
                    }
                    SaveData();
                    RaiseDataChanged();
                    TM.App.Log($"[ChapterService] 级联删除: 分类'{categoryName}'下的 {dataToDelete.Count} 条数据已删除");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterService] 级联删除失败: {ex.Message}");
            }
        }

        public new List<ChapterCategory> GetAllCategories()
        {
            var subscribed = GetSubscribedCategories();
            var rewrite = GetRewriteCategories();
            return subscribed
                .Concat(rewrite.Where(r => !subscribed.Any(s => string.Equals(s.Name, r.Name, StringComparison.Ordinal))))
                .OrderBy(c => c.Order)
                .ToList();
        }

        public List<ChapterCategory> GetSubscribedCategories()
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .Select(v => new ChapterCategory
                {
                    Id = v.Id,
                    Name = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name,
                    Icon = "Icon.Books",
                    Order = v.VolumeNumber,
                    IsBuiltIn = true,
                    IsEnabled = v.IsEnabled
                }).ToList();
        }

        public List<ChapterCategory> GetRewriteCategories()
        {
            return Categories
                .Where(c => !c.IsBuiltIn)
                .OrderBy(c => c.Order)
                .ToList();
        }

        public async System.Threading.Tasks.Task<bool> AddRewriteCategoryAsync(ChapterCategory category)
        {
            if (category == null) return false;

            if (string.IsNullOrWhiteSpace(category.Id))
                category.Id = ShortIdGenerator.New("C");

            category.IsBuiltIn = false;

            var subscribed = GetSubscribedCategories();
            if (subscribed.Any(c => string.Equals(c.Name, category.Name, StringComparison.Ordinal)))
            {
                TM.App.Log($"[ChapterService] 仿写分类名称与订阅分卷冲突，禁止添加: {category.Name}");
                return false;
            }

            var added = await base.AddCategoryAsync(category);
            if (added)
                RaiseDataChanged();
            return added;
        }

        public async System.Threading.Tasks.Task DeleteRewriteCategoryAsync(string categoryName)
        {
            await base.DeleteCategoryAsync(categoryName);
            RaiseDataChanged();
        }

        public override int SetCategoriesEnabled(IEnumerable<string> categoryNames, bool enabled)
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.SetCategoriesEnabled(categoryNames, enabled);
        }

        public List<ChapterData> GetAllChapters() => GetAllData();

        public void AddChapter(ChapterData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            EnsureCategoryIdFromVolumeDesign(data);
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            AddData(data);
            RaiseDataChanged();
        }

        public async System.Threading.Tasks.Task AddChapterAsync(ChapterData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            EnsureCategoryIdFromVolumeDesign(data);
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            await AddDataAsync(data);
            RaiseDataChanged();
        }

        public void UpdateChapter(ChapterData data)
        {
            if (data == null) return;
            EnsureCategoryIdFromVolumeDesign(data);
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
            RaiseDataChanged();
        }

        public async System.Threading.Tasks.Task UpdateChapterAsync(ChapterData data)
        {
            if (data == null) return;
            EnsureCategoryIdFromVolumeDesign(data);
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
            RaiseDataChanged();
        }

        public void DeleteChapter(string id)
        {
            DeleteData(id);
            RaiseDataChanged();
        }

        public int ClearAllChapters()
        {
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            RaiseDataChanged();
            return count;
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            try
            {
                await _volumeDesignService.InitializeAsync();
                var allCategories = GetAllCategories();

                Categories.Clear();
                Categories.AddRange(allCategories);

                bool updated = false;
                foreach (var data in DataItems)
                {
                    if (!string.IsNullOrWhiteSpace(data.Category) && string.IsNullOrWhiteSpace(data.CategoryId))
                    {
                        var matchedCategory = allCategories.FirstOrDefault(c =>
                            string.Equals(c.Name, data.Category, StringComparison.Ordinal));
                        if (matchedCategory != null && !string.IsNullOrWhiteSpace(matchedCategory.Id))
                        {
                            data.CategoryId = matchedCategory.Id;
                            updated = true;
                            TM.App.Log($"[ChapterService] 补全CategoryId: {data.Name} -> {matchedCategory.Id}");
                        }
                    }
                }

                if (updated)
                {
                    await SaveDataAsync();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterService] 同步分类失败: {ex.Message}");
            }
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(d => d.Id == dataId);
        }

        private void EnsureCategoryIdFromVolumeDesign(ChapterData data)
        {
            if (!string.IsNullOrWhiteSpace(data.CategoryId)) return;
            if (string.IsNullOrWhiteSpace(data.Category)) return;

            try
            {
                _volumeDesignService.EnsureInitialized();
                var volumeDesigns = _volumeDesignService.GetAllVolumeDesigns()
                    .ToList();

                var matchedVolume = volumeDesigns.FirstOrDefault(v =>
                {
                    var expectedName = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name;
                    return string.Equals(expectedName, data.Category, StringComparison.Ordinal) ||
                           string.Equals(v.Name, data.Category, StringComparison.Ordinal);
                });

                if (matchedVolume != null && !string.IsNullOrWhiteSpace(matchedVolume.Id))
                {
                    data.CategoryId = matchedVolume.Id;
                    if (TM.App.IsDebugMode)
                        TM.App.Log($"[ChapterService] 主动补全CategoryId: {data.Name} -> {matchedVolume.Id}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterService] EnsureCategoryIdFromVolumeDesign 失败: {ex.Message}");
            }
        }

        protected override bool HasContent(ChapterData data)
        {
            return !string.IsNullOrWhiteSpace(data.ChapterTitle) ||
                   !string.IsNullOrWhiteSpace(data.MainGoal) ||
                   !string.IsNullOrWhiteSpace(data.ChapterTheme);
        }
    }
}
