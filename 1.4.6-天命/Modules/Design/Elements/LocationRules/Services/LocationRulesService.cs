using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.Location;

namespace TM.Modules.Design.Elements.LocationRules.Services
{
    public class LocationRulesService : ModuleServiceBase<LocationRulesCategory, LocationRulesData>
    {
        public LocationRulesService()
            : base(
                modulePath: "Design/Elements/LocationRules",
                categoriesFileName: "categories.json",
                dataFileName: "location_rules.json")
        {
        }

        public List<LocationRulesData> GetAllLocationRules() => GetAllData();

        public void AddLocationRule(LocationRulesData data)
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

        public async System.Threading.Tasks.Task AddLocationRuleAsync(LocationRulesData data)
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

        public void UpdateLocationRule(LocationRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateLocationRuleAsync(LocationRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteLocationRule(string id)
        {
            DeleteData(id);
        }

        public int ClearAllLocationRules()
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

        protected override bool HasContent(LocationRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Description) ||
                   !string.IsNullOrWhiteSpace(data.Terrain);
        }
    }
}
