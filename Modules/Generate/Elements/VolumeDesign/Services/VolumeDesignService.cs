using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.VolumeDesign.Services
{
    public class CategoryDeletedEventArgs : EventArgs
    {
        public string CategoryName { get; }
        public string CategoryId { get; }
        public CategoryDeletedEventArgs(string categoryName, string categoryId)
        {
            CategoryName = categoryName;
            CategoryId = categoryId;
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeDesignService : ModuleServiceBase<VolumeDesignCategory, VolumeDesignData>
    {
        public event EventHandler<EventArgs>? DataChanged;

        public event EventHandler<CategoryDeletedEventArgs>? CategoryDeleted;

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
                        TM.App.Log($"[VolumeDesignService] 通知数据变更事件失败: {ex.Message}");
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
                    TM.App.Log($"[VolumeDesignService] 通知数据变更事件失败: {ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _dataChangePending, 0);
                }
            }
        }

        public void RaiseCategoryDeleted(string categoryName, string categoryId)
        {
            try
            {
                CategoryDeleted?.Invoke(this, new CategoryDeletedEventArgs(categoryName, categoryId));
                TM.App.Log($"[VolumeDesignService] 分类删除事件已触发: {categoryName} (id={categoryId})");
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

        protected override string? GetEntityTypeKeyForPropagation() => "volumedesign";

        public List<VolumeDesignData> GetAllVolumeDesigns() => GetAllData();

        public void AddVolumeDesign(VolumeDesignData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            if (data.VolumeNumber > 0 && GetAllData().Any(v => v.VolumeNumber == data.VolumeNumber))
            {
                TM.App.Log($"[VolumeDesignService] 卷号 {data.VolumeNumber} 已存在，跳过添加");
                return;
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
            if (data.VolumeNumber > 0 && GetAllData().Any(v => v.VolumeNumber == data.VolumeNumber))
            {
                TM.App.Log($"[VolumeDesignService] 卷号 {data.VolumeNumber} 已存在，跳过添加");
                return;
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
                RaiseCategoryDeleted(GetDerivedCategoryName(item), item.Id);
            }
            DeleteData(id);
            RaiseDataChanged();
        }

        public int ClearAllVolumeDesigns()
        {
            foreach (var item in DataItems.ToList())
            {
                RaiseCategoryDeleted(GetDerivedCategoryName(item), item.Id);
            }
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            RaiseDataChanged();
            return count;
        }

        private static string GetDerivedCategoryName(VolumeDesignData item)
        {
            if (item.VolumeNumber <= 0) return item.Name;
            var cleanTitle = System.Text.RegularExpressions.Regex.Replace(
                (item.VolumeTitle ?? string.Empty).Trim(),
                @"^第\s*\d+\s*卷\s*[：:]\s*", string.Empty);
            return $"第{item.VolumeNumber}卷 {cleanTitle}".Trim();
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
