using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.CreativeMaterials.Services
{
    public class CreativeMaterialsService : ModuleServiceBase<CreativeMaterialCategory, CreativeMaterialData>
    {
        public CreativeMaterialsService()
            : base(
                modulePath: "Design/Templates/CreativeMaterials",
                categoriesFileName: "categories.json",
                dataFileName: "creative_materials.json")
        {
        }

        public List<CreativeMaterialData> GetAllMaterials() => GetAllData();

        public void AddMaterial(CreativeMaterialData data)
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

        public async System.Threading.Tasks.Task AddMaterialAsync(CreativeMaterialData data)
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

        public void UpdateMaterial(CreativeMaterialData data)
        {
            if (data == null) return;
            data.ModifiedTime = DateTime.Now;
            UpdateData(data);
        }

        public async System.Threading.Tasks.Task UpdateMaterialAsync(CreativeMaterialData data)
        {
            if (data == null) return;
            data.ModifiedTime = DateTime.Now;
            await UpdateDataAsync(data);
        }

        public void DeleteMaterial(string id)
        {
            DeleteData(id);
        }

        public int ClearAllMaterials()
        {
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            return count;
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(m => m.Id == dataId);
        }
    }
}
