using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.Blueprint.Services
{
    public class BlueprintService : ModuleServiceBase<BlueprintCategory, BlueprintData>
    {
        private readonly VolumeDesignService _volumeDesignService;

        public BlueprintService(VolumeDesignService volumeDesignService)
            : base(
                modulePath: "Generate/Elements/Blueprint",
                categoriesFileName: "categories.json",
                dataFileName: "blueprint_data.json")
        {
            _volumeDesignService = volumeDesignService;

            _volumeDesignService.CategoryDeleted += OnVolumeCategoryDeleted;
        }

        private void OnVolumeCategoryDeleted(object? sender, CategoryDeletedEventArgs e)
        {
            try
            {
                var categoryName = e.CategoryName;
                var dataToDelete = DataItems.Where(d => d.Category == categoryName).ToList();

                if (dataToDelete.Count > 0)
                {
                    foreach (var item in dataToDelete)
                    {
                        DataItems.Remove(item);
                    }
                    SaveData();
                    TM.App.Log($"[BlueprintService] 级联删除: 分类'{categoryName}'下的 {dataToDelete.Count} 条数据已删除");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintService] 级联删除失败: {ex.Message}");
            }
        }

        public new List<BlueprintCategory> GetAllCategories()
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .Select(v => new BlueprintCategory
                {
                    Id = v.Id,
                    Name = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name,
                    Icon = "📚",
                    Order = v.VolumeNumber,
                    IsBuiltIn = false,
                    IsEnabled = v.IsEnabled
                }).ToList();
        }

        public override int SetCategoriesEnabled(IEnumerable<string> categoryNames, bool enabled)
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.SetCategoriesEnabled(categoryNames, enabled);
        }

        public List<BlueprintData> GetAllBlueprints() => GetAllData();

        public void AddBlueprint(BlueprintData data)
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
        }

        public async System.Threading.Tasks.Task AddBlueprintAsync(BlueprintData data)
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
        }

        public void UpdateBlueprint(BlueprintData data)
        {
            if (data == null) return;
            EnsureCategoryIdFromVolumeDesign(data);
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateBlueprintAsync(BlueprintData data)
        {
            if (data == null) return;
            EnsureCategoryIdFromVolumeDesign(data);
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteBlueprint(string id)
        {
            DeleteData(id);
        }

        public int ClearAllBlueprints()
        {
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            return count;
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            try
            {
                await _volumeDesignService.InitializeAsync();
                var volumeCategories = GetAllCategories();

                Categories.Clear();
                Categories.AddRange(volumeCategories);

                bool updated = false;
                foreach (var data in DataItems)
                {
                    if (!string.IsNullOrWhiteSpace(data.Category) && string.IsNullOrWhiteSpace(data.CategoryId))
                    {
                        var matchedCategory = Categories.FirstOrDefault(c =>
                            string.Equals(c.Name, data.Category, StringComparison.Ordinal));
                        if (matchedCategory != null && !string.IsNullOrWhiteSpace(matchedCategory.Id))
                        {
                            data.CategoryId = matchedCategory.Id;
                            updated = true;
                            TM.App.Log($"[BlueprintService] 补全CategoryId: {data.Name} -> {matchedCategory.Id}");
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
                TM.App.Log($"[BlueprintService] 同步分类失败: {ex.Message}");
            }
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(d => d.Id == dataId);
        }

        private void EnsureCategoryIdFromVolumeDesign(BlueprintData data)
        {
            if (!string.IsNullOrWhiteSpace(data.CategoryId)) return;
            if (string.IsNullOrWhiteSpace(data.Category)) return;

            try
            {
                _volumeDesignService.EnsureInitialized();
                var volumeDesigns = _volumeDesignService.GetAllVolumeDesigns();

                var matchedVolume = volumeDesigns.FirstOrDefault(v =>
                {
                    var expectedName = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name;
                    return string.Equals(expectedName, data.Category, StringComparison.Ordinal) ||
                           string.Equals(v.Name, data.Category, StringComparison.Ordinal);
                });

                if (matchedVolume != null && !string.IsNullOrWhiteSpace(matchedVolume.Id))
                {
                    data.CategoryId = matchedVolume.Id;
                    TM.App.Log($"[BlueprintService] 主动补全CategoryId: {data.Name} -> {matchedVolume.Id}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintService] EnsureCategoryIdFromVolumeDesign 失败: {ex.Message}");
            }
        }

        protected override bool HasContent(BlueprintData data)
        {
            return !string.IsNullOrWhiteSpace(data.ChapterId) ||
                   !string.IsNullOrWhiteSpace(data.OneLineStructure) ||
                   !string.IsNullOrWhiteSpace(data.SceneTitle);
        }
    }
}
