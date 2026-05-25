using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ShortStoryBlueprintService : ModuleServiceBase<ShortStoryBlueprintCategory, ShortStoryBlueprintData>
    {
        public ShortStoryBlueprintService()
            : base(
                modulePath: "Design/Templates/OneClickGenerate/ShortStoryBlueprint",
                categoriesFileName: "categories.json",
                dataFileName: "short_story_blueprints.json")
        {
        }

        public List<ShortStoryBlueprintData> GetAllBlueprints() => GetAllData();

        public void AddBlueprint(ShortStoryBlueprintData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;
            AddData(data);
        }

        public async System.Threading.Tasks.Task AddBlueprintAsync(ShortStoryBlueprintData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;
            await AddDataAsync(data);
        }

        public void UpdateBlueprint(ShortStoryBlueprintData data)
        {
            if (data == null) return;
            data.ModifiedTime = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateBlueprintAsync(ShortStoryBlueprintData data)
        {
            if (data == null) return;
            data.ModifiedTime = DateTime.Now;
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

        public ShortStoryBlueprintData? GetBlueprintById(string blueprintId)
        {
            if (string.IsNullOrWhiteSpace(blueprintId)) return null;
            return DataItems.FirstOrDefault(d => d.Id == blueprintId);
        }

        public ShortStoryBlueprintData? GetBlueprintByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return DataItems.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(m => m.Id == dataId);
        }
    }
}
