using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.Factions;

namespace TM.Modules.Design.Elements.FactionRules.Services
{
    public class FactionRulesService : ModuleServiceBase<FactionRulesCategory, FactionRulesData>
    {
        public FactionRulesService()
            : base(
                modulePath: "Design/Elements/FactionRules",
                categoriesFileName: "categories.json",
                dataFileName: "faction_rules.json")
        {
        }

        public List<FactionRulesData> GetAllFactionRules() => GetAllData();

        public void AddFactionRule(FactionRulesData data)
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

        public async System.Threading.Tasks.Task AddFactionRuleAsync(FactionRulesData data)
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

        public void UpdateFactionRule(FactionRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateFactionRuleAsync(FactionRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteFactionRule(string id)
        {
            DeleteData(id);
        }

        public int ClearAllFactionRules()
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

        protected override bool HasContent(FactionRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Goal) ||
                   !string.IsNullOrWhiteSpace(data.FactionType);
        }
    }
}
