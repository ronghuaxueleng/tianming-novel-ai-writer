using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.Characters;

namespace TM.Modules.Design.Elements.CharacterRules.Services
{
    public class CharacterRulesService : ModuleServiceBase<CharacterRulesCategory, CharacterRulesData>
    {
        public CharacterRulesService()
            : base(
                modulePath: "Design/Elements/CharacterRules",
                categoriesFileName: "categories.json",
                dataFileName: "character_rules.json")
        {
        }

        public List<CharacterRulesData> GetAllCharacterRules() => GetAllData();

        public void AddCharacterRule(CharacterRulesData data)
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

        public async System.Threading.Tasks.Task AddCharacterRuleAsync(CharacterRulesData data)
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

        public void UpdateCharacterRule(CharacterRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateCharacterRuleAsync(CharacterRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteCharacterRule(string id)
        {
            DeleteData(id);
        }

        public int ClearAllCharacterRules()
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

        protected override bool HasContent(CharacterRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Identity) ||
                   !string.IsNullOrWhiteSpace(data.Want) ||
                   !string.IsNullOrWhiteSpace(data.Need);
        }
    }
}
