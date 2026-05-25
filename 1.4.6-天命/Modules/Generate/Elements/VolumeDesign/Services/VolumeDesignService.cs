using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.VolumeDesign.Services
{
    public class CategoryDeletedEventArgs : EventArgs
    {
        public string CategoryName { get; }
        public CategoryDeletedEventArgs(string categoryName) => CategoryName = categoryName;
    }

    public class VolumeDesignService : ModuleServiceBase<VolumeDesignCategory, VolumeDesignData>
    {
        public event EventHandler<EventArgs>? DataChanged;

        public event EventHandler<CategoryDeletedEventArgs>? CategoryDeleted;

        private void RaiseDataChanged()
        {
            try
            {
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignService] 通知数据变更事件失败: {ex.Message}");
            }
        }

        public void RaiseCategoryDeleted(string categoryName)
        {
            try
            {
                CategoryDeleted?.Invoke(this, new CategoryDeletedEventArgs(categoryName));
                TM.App.Log($"[VolumeDesignService] 分类删除事件已触发: {categoryName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignService] 触发分类删除事件失败: {ex.Message}");
            }
        }

        public VolumeDesignService()
            : base(
                modulePath: "Generate/Elements/VolumeDesign",
                categoriesFileName: "categories.json",
                dataFileName: "volume_design_data.json")
        {
        }

        public List<VolumeDesignData> GetAllVolumeDesigns() => GetAllData();

        public void AddVolumeDesign(VolumeDesignData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            AddData(data);
            RaiseDataChanged();
        }

        public async System.Threading.Tasks.Task AddVolumeDesignAsync(VolumeDesignData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            await AddDataAsync(data);
            RaiseDataChanged();
        }

        public void UpdateVolumeDesign(VolumeDesignData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
            RaiseDataChanged();
        }

        public async System.Threading.Tasks.Task UpdateVolumeDesignAsync(VolumeDesignData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
            RaiseDataChanged();
        }

        public void DeleteVolumeDesign(string id)
        {
            var item = DataItems.FirstOrDefault(d => d.Id == id);
            if (item != null)
            {
                RaiseCategoryDeleted(GetDerivedCategoryName(item));
            }
            DeleteData(id);
            RaiseDataChanged();
        }

        public int ClearAllVolumeDesigns()
        {
            foreach (var item in DataItems.ToList())
            {
                RaiseCategoryDeleted(GetDerivedCategoryName(item));
            }
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            RaiseDataChanged();
            return count;
        }

        private static string GetDerivedCategoryName(VolumeDesignData item)
        {
            return item.VolumeNumber > 0
                ? $"第{item.VolumeNumber}卷 {item.VolumeTitle}".Trim()
                : item.Name;
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(d => d.Id == dataId);
        }

        protected override bool HasContent(VolumeDesignData data)
        {
            return !string.IsNullOrWhiteSpace(data.VolumeTitle) ||
                   !string.IsNullOrWhiteSpace(data.VolumeTheme) ||
                   !string.IsNullOrWhiteSpace(data.StageGoal);
        }
    }
}
