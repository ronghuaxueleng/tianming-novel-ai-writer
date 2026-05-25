using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Modules.Design.GlobalSettings.WorldRules.Services
{
    public class WorldRulesService : ModuleServiceBase<WorldRulesCategory, WorldRulesData>
    {
        public WorldRulesService()
            : base(
                modulePath: "Design/GlobalSettings/WorldRules",
                categoriesFileName: "categories.json",
                dataFileName: "world_rules.json")
        {
        }

        public List<WorldRulesData> GetAllWorldRules() => GetAllData();

        public void AddWorldRule(WorldRulesData data)
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

        public async System.Threading.Tasks.Task AddWorldRuleAsync(WorldRulesData data)
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

        public void UpdateWorldRule(WorldRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateWorldRuleAsync(WorldRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteWorldRule(string id)
        {
            DeleteData(id);
        }

        public int ClearAllWorldRules()
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

        protected override bool HasContent(WorldRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.PowerSystem) ||
                   !string.IsNullOrWhiteSpace(data.HardRules);
        }
    }
}
