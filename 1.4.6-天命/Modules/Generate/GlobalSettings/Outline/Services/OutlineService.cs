using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;

namespace TM.Modules.Generate.GlobalSettings.Outline.Services
{
    public class OutlineService : ModuleServiceBase<OutlineCategory, OutlineData>
    {
        public OutlineService()
            : base(
                modulePath: "Generate/GlobalSettings/Outline",
                categoriesFileName: "categories.json",
                dataFileName: "outline_data.json")
        {
        }

        public List<OutlineData> GetAllOutlines() => GetAllData();

        public void AddOutline(OutlineData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            AddData(data);
        }

        public async System.Threading.Tasks.Task AddOutlineAsync(OutlineData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            await AddDataAsync(data);
        }

        public void UpdateOutline(OutlineData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateOutlineAsync(OutlineData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteOutline(string id)
        {
            DeleteData(id);
        }

        public int ClearAllOutlines()
        {
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            return count;
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(d => d.Id == dataId);
        }

        protected override bool HasContent(OutlineData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineOutline) ||
                   !string.IsNullOrWhiteSpace(data.Theme) ||
                   !string.IsNullOrWhiteSpace(data.OutlineOverview) ||
                   !string.IsNullOrWhiteSpace(data.VolumeDivision) ||
                   !string.IsNullOrWhiteSpace(data.EstimatedWordCount);
        }
    }
}
