using System;
using System.Collections.Generic;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Modules.Design.Elements.PlotRules.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class PlotRulesService : ModuleServiceBase<PlotRulesCategory, PlotRulesData>
    {
        public PlotRulesService()
            : base(
                modulePath: "Design/Elements/PlotRules",
                categoriesFileName: "categories.json",
                dataFileName: "plot_rules.json")
        {
        }

        protected override string? GetEntityTypeKeyForPropagation() => "plotrules";

        public List<PlotRulesData> GetAllPlotRules() => GetAllData();

        public void AddPlotRule(PlotRulesData data)
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

        public async System.Threading.Tasks.Task AddPlotRuleAsync(PlotRulesData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedAt = DateTime.Now;
            data.UpdatedAt = DateTime.Now;
            await AddDataAsync(data).ConfigureAwait(false);
        }

        public void UpdatePlotRule(PlotRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdatePlotRuleAsync(PlotRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data).ConfigureAwait(false);
        }

        public void DeletePlotRule(string id)
        {
            DeleteData(id);
        }

        public int ClearAllPlotRules()
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

        protected override bool HasContent(PlotRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.Goal);
        }
    }
}
