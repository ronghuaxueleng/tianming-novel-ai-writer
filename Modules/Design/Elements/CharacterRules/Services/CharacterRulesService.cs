using System;
using System.Collections.Generic;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Design.Characters;

namespace TM.Modules.Design.Elements.CharacterRules.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CharacterRulesService : ModuleServiceBase<CharacterRulesCategory, CharacterRulesData>
    {
        public CharacterRulesService()
            : base(
                modulePath: "Design/Elements/CharacterRules",
                categoriesFileName: "categories.json",
                dataFileName: "character_rules.json")
        {
        }

        protected override string? GetEntityTypeKeyForPropagation() => "characters";

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
            NotifyEntryChanged(data);
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
            await AddDataAsync(data).ConfigureAwait(false);
            NotifyEntryChanged(data);
        }

        public void UpdateCharacterRule(CharacterRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            UpdateData(data);
            NotifyEntryChanged(data);
        }

        public async System.Threading.Tasks.Task UpdateCharacterRuleAsync(CharacterRulesData data)
        {
            if (data == null) return;
            data.UpdatedAt = DateTime.Now;
            await UpdateDataAsync(data).ConfigureAwait(false);
            NotifyEntryChanged(data);
        }

        private static void NotifyEntryChanged(CharacterRulesData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Id)) return;
            try
            {
                ServiceLocator.Get<GuideManager>().RaiseEntryChanged(
                    data.Id,
                    data.Name ?? string.Empty,
                    data.Identity ?? string.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharacterRulesService] RaiseEntryChanged 失败（非致命）: {ex.Message}");
            }
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
