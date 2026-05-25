using System;
using System.Collections.Generic;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    public partial class ShortStoryBlueprintViewModel
    {
        protected override ShortStoryBlueprintData? CreateNewData(string? categoryName = null)
        {
            return new ShortStoryBlueprintData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新蓝图",
                Category = categoryName ?? string.Empty,
                Icon = DefaultDataIcon,
                IsEnabled = true,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllBlueprints();

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateBlueprint(_currentEditingData);
        }

        protected override List<ShortStoryBlueprintCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<ShortStoryBlueprintData> GetAllDataItems() => Service.GetAllBlueprints();

        protected override string GetDataCategory(ShortStoryBlueprintData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(ShortStoryBlueprintData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = IconHelper.TryGet(data.Icon),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(ShortStoryBlueprintData data)
        {
            return new[] { data.Genre, data.SourceBookName, data.Synopsis };
        }
    }
}
