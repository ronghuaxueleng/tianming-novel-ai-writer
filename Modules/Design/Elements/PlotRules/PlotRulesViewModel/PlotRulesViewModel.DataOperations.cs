using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Modules.Design.Elements.PlotRules
{
    public partial class PlotRulesViewModel
    {
        protected override string DefaultDataIcon => "Icon.Book";

        protected override PlotRulesData? CreateNewData(string? categoryName = null)
        {
            return new PlotRulesData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新剧情规则",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var (mainChars, keyNpcs, location) = NormalizePlotReferences(FormMainCharacters, FormKeyNpcs, FormLocation);
            FormMainCharacters = mainChars;
            FormKeyNpcs = keyNpcs;
            FormLocation = location;

            return Task.CompletedTask;
        }

        private (string MainCharacters, string KeyNpcs, string Location) NormalizePlotReferences(
            string rawMainChars,
            string rawKeyNpcs,
            string rawLocation)
        {
            return (
                NormalizeCharRefList(rawMainChars),
                NormalizeCharRefList(rawKeyNpcs),
                NormalizeLocationRef(rawLocation)
            );
        }

        private string NormalizeCharRefList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return string.Join("、", raw
                .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s) && !EntityNameNormalizeHelper.IsIgnoredValue(s))
                .Where(n => ShortIdGenerator.IsLikelyId(n)
                    ? _charIdToName.ContainsKey(n)
                    : AvailableCharacters.Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase))));
        }

        private string NormalizeLocationRef(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var loc = raw.Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(loc)) return string.Empty;
            if (ShortIdGenerator.IsLikelyId(loc))
                return _locIdToName.ContainsKey(loc) ? loc : string.Empty;
            return AvailableLocations.Any(l => string.Equals(l, loc, StringComparison.OrdinalIgnoreCase))
                ? loc
                : string.Empty;
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
                EnsureServiceInitializedAsync(_locationService));

            try
            {
                InvalidateRelationshipCache();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        RefreshRelationshipOptions();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    RefreshRelationshipOptions();
                }
            }
            catch
            {
                InvalidateRelationshipCache();
                RefreshRelationshipOptions();
            }
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllPlotRules();

        protected override string GetModuleNameForVersionTracking() => "PlotRules";

        protected override void ApplyPrefilledFields(Dictionary<string, string> fields)
        {
            if (fields.TryGetValue("TargetVolume", out var vol)) FormTargetVolume = vol;
        }

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdatePlotRule(_currentEditingData);
        }

        protected override List<PlotRulesCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<PlotRulesData> GetAllDataItems() => Service.GetAllPlotRules();

        protected override string GetDataCategory(PlotRulesData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(PlotRulesData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = IconHelper.Get("Icon.Book"),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(PlotRulesData data)
        {
            return new[] { data.OneLineSummary, data.EventType, data.Goal };
        }
    }
}
