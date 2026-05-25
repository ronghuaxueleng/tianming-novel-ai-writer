using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    public partial class CreativeMaterialsViewModel
    {
        protected override string DefaultDataIcon => "Icon.Lightbulb";

        protected override int GetMaxCategoryCount() => 1;
        protected override int GetMaxDataCountPerCategory() => 1;
        protected override string GetCategoryLimitMessage()
            => "创作模板仅支持系统内置唯一分类，不允许新建分类。";
        protected override string GetDataLimitMessage()
            => "当前素材库已有创作模板，请先删除旧模板，再构建新的创作模板。";

        protected override CreativeMaterialData? CreateNewData(string? categoryName = null)
        {
            return new CreativeMaterialData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新素材",
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

        protected override int ClearAllDataItems() => Service.ClearAllMaterials();

        protected override string GetModuleNameForVersionTracking() => "CreativeMaterials";

        protected override void ApplyPrefilledFields(Dictionary<string, string> fields)
        {
            if (fields.TryGetValue("Genre", out var genre)) FormGenre = genre;
            if (fields.TryGetValue("SourceBookName", out var book)) FormSourceBookName = book;
            if (fields.TryGetValue("GoldenChapter", out var gc))
                TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.Save(gc == "黄金三章");
        }

        public override Dictionary<string, string> GetPrefilledFieldDefaults(string categoryName)
        {
            var dict = new Dictionary<string, string>();
            try
            {
                var categoryNames = CollectCategoryAndChildrenNames(categoryName);
                var existing = Service.GetAllMaterials()
                    .Where(m => categoryNames.Contains(m.Category) && m.IsEnabled && !string.IsNullOrWhiteSpace(m.Genre))
                    .OrderByDescending(m => m.ModifiedTime)
                    .FirstOrDefault();
                if (existing != null)
                    dict["Genre"] = existing.Genre;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] GetPrefilledFieldDefaults 异常: {ex.Message}");
            }
            try
            {
                var enabled = TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.Load();
                dict["GoldenChapter"] = enabled ? "黄金三章" : "不启用";
            }
            catch { }

            return dict;
        }

        public override Dictionary<string, List<string>> GetExtraFieldOptions()
        {
            var dict = new Dictionary<string, List<string>>();
            try
            {
                var genres = LoadGenresFromSpec();
                if (genres.Count > 0)
                    dict["Genre"] = genres.Select(g => g.Name).ToList();

                LoadBookOptions();
                if (BookOptions.Count > 0)
                    dict["SourceBookName"] = BookOptions.Select(b => b.Name).ToList();

                dict["GoldenChapter"] = new List<string> { "不启用", "黄金三章" };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] GetExtraFieldOptions 异常: {ex.Message}");
            }
            return dict;
        }

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateMaterial(_currentEditingData);
        }

        protected override List<CreativeMaterialCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<CreativeMaterialData> GetAllDataItems() => Service.GetAllMaterials();

        protected override string GetDataCategory(CreativeMaterialData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(CreativeMaterialData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = IconHelper.TryGet(data.Icon),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(CreativeMaterialData data)
        {
            return new[]
            {
                data.SourceBookName, data.Genre, data.OverallIdea,
                data.WorldBuildingMethod, data.PowerSystemDesign, data.EnvironmentDescription,
                data.FactionDesign, data.WorldviewHighlights,
                data.ProtagonistDesign, data.SupportingRoles, data.CharacterRelations,
                data.GoldenFingerDesign, data.CharacterHighlights,
                data.PlotStructure, data.ConflictDesign, data.ClimaxArrangement,
                data.ForeshadowingTechnique, data.PlotHighlights
            };
        }
    }
}
