using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    public partial class VolumeDesignViewModel
    {
        protected override string DefaultDataIcon => "Icon.Books";

        protected override VolumeDesignData? CreateNewData(string? categoryName = null)
        {
            return new VolumeDesignData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新分卷设计",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override async Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(
                EnsureServiceInitializedAsync(Service),
                EnsureServiceInitializedAsync(_characterService),
                EnsureServiceInitializedAsync(_factionService),
                EnsureServiceInitializedAsync(_locationService));

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        RefreshEntityOptions();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    RefreshEntityOptions();
                }
            }
            catch
            {
                RefreshEntityOptions();
            }
        }

        protected override async Task ResolveEntityReferencesBeforeSaveAsync()
        {
            FormReferencedCharacterNames = await VolumeResolveNamesAsync(FormReferencedCharacterNames, "character");
            FormReferencedFactionNames = await VolumeResolveNamesAsync(FormReferencedFactionNames, "faction");
            FormReferencedLocationNames = await VolumeResolveNamesAsync(FormReferencedLocationNames, "location");
        }

        private Task<string> VolumeResolveNamesAsync(string rawNames, string entityType)
        {
            if (string.IsNullOrWhiteSpace(rawNames)) return Task.FromResult(rawNames);
            var parts = rawNames.Split(new[] { ',', '，', '、', '\n', '\r', ' ', '\t', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                switch (entityType)
                {
                    case "character":
                        if (_characterService.GetAllCharacterRules().Any(c => c.IsEnabled && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：角色 '{n}' 在上游不存在，已忽略");
                        break;
                    case "faction":
                        if (_factionService.GetAllFactionRules().Any(f => f.IsEnabled && string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：势力 '{n}' 在上游不存在，已忽略");
                        break;
                    case "location":
                        if (_locationService.GetAllLocationRules().Any(l => l.IsEnabled && string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase)))
                        { resolved.Add(n); break; }
                        TM.App.Log($"[VolumeDesignViewModel] 实体引用：地点 '{n}' 在上游不存在，已忽略");
                        break;
                }
            }
            return Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllVolumeDesigns();

        protected override List<VolumeDesignCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<VolumeDesignData> GetAllDataItems()
        {
            return Service.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .ToList();
        }

        protected override string GetDataCategory(VolumeDesignData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(VolumeDesignData data)
        {
            var vol = data.VolumeNumber > 0 ? $"第{data.VolumeNumber}卷" : "未编号";
            var rawTitle = string.IsNullOrWhiteSpace(data.VolumeTitle) ? data.Name : data.VolumeTitle;
            var title = System.Text.RegularExpressions.Regex.Replace(rawTitle.Trim(), @"^第\s*\d+\s*卷\s*[：:]\s*", string.Empty);
            return new TreeNodeItem
            {
                Name = $"{vol} {title}",
                Icon = IconHelper.Get("Icon.Books"),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(VolumeDesignData data)
        {
            return new[] { data.VolumeTitle, data.StageGoal, data.VolumeTheme };
        }

        protected override string GetModuleNameForVersionTracking() => "VolumeDesign";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateVolumeDesign(_currentEditingData);
        }
    }
}
