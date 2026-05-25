using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;

namespace TM.Modules.Generate.Elements.Blueprint
{
    public partial class BlueprintViewModel
    {
        protected override string DefaultDataIcon => "Icon.Clapper";

        protected override BlueprintData? CreateNewData(string? categoryName = null)
        {
            return new BlueprintData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新蓝图",
                Category = categoryName ?? string.Empty,
                IsEnabled = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        protected override async System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
        {
            var castEmpty = string.IsNullOrWhiteSpace(FormCast) || EntityNameNormalizeHelper.IsIgnoredValue(FormCast.Trim());
            var locationsEmpty = string.IsNullOrWhiteSpace(FormLocations) || EntityNameNormalizeHelper.IsIgnoredValue(FormLocations.Trim());
            var factionsEmpty = string.IsNullOrWhiteSpace(FormFactions) || EntityNameNormalizeHelper.IsIgnoredValue(FormFactions.Trim());

            if (castEmpty || locationsEmpty || factionsEmpty)
            {
                try
                {
                    var volMatch = VolChIdRegex.Match(FormChapterId ?? string.Empty);
                    if (volMatch.Success)
                    {
                        var volNum = int.Parse(volMatch.Groups[1].Value);
                        var chNum = int.Parse(volMatch.Groups[2].Value);
                        await _chapterService.InitializeAsync();
                        var chapter = _chapterService.GetAllChapters()
                            .FirstOrDefault(c => c.IsEnabled && c.ChapterNumber == chNum
                                && ExtractVolumeNumber(c.Category) == volNum);
                        if (chapter != null)
                        {
                            if (castEmpty && chapter.ReferencedCharacterNames?.Count > 0)
                            { FormCast = string.Join("、", chapter.ReferencedCharacterNames); castEmpty = false; }
                            if (locationsEmpty && chapter.ReferencedLocationNames?.Count > 0)
                            { FormLocations = string.Join("、", chapter.ReferencedLocationNames); locationsEmpty = false; }
                            if (factionsEmpty && chapter.ReferencedFactionNames?.Count > 0)
                            { FormFactions = string.Join("、", chapter.ReferencedFactionNames); factionsEmpty = false; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BlueprintViewModel] 从章节设计继承实体引用失败: {ex.Message}");
                }

                if (castEmpty || locationsEmpty || factionsEmpty)
                {
                    try
                    {
                        await _volumeDesignService.InitializeAsync();
                        var volume = _volumeDesignService.GetAllVolumeDesigns()
                            .FirstOrDefault(v => v.IsEnabled
                                && (string.Equals(v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name, FormCategory, StringComparison.Ordinal)
                                    || string.Equals(v.Name, FormCategory, StringComparison.Ordinal)));
                        if (volume != null)
                        {
                            if (castEmpty && volume.ReferencedCharacterNames?.Count > 0)
                                FormCast = string.Join("、", volume.ReferencedCharacterNames);
                            if (locationsEmpty && volume.ReferencedLocationNames?.Count > 0)
                                FormLocations = string.Join("、", volume.ReferencedLocationNames);
                            if (factionsEmpty && volume.ReferencedFactionNames?.Count > 0)
                                FormFactions = string.Join("、", volume.ReferencedFactionNames);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BlueprintViewModel] 从分卷设计继承实体引用失败: {ex.Message}");
                    }
                }
            }

            var (resolvedCast, resolvedLocs, resolvedFacs, resolvedPov) = await NormalizeBlueprintEntitiesAsync(
                FormChapterId, FormCategory,
                FormCast, FormLocations, FormFactions, FormPovCharacter);

            FormCast = resolvedCast;
            FormLocations = resolvedLocs;
            FormFactions = resolvedFacs;
            FormPovCharacter = resolvedPov;
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveCharacterAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var name = raw.Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(name)) return System.Threading.Tasks.Task.FromResult(string.Empty);
            if (_characterService.GetAllCharacterRules().Any(c => c.IsEnabled &&
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return System.Threading.Tasks.Task.FromResult(name);
            TM.App.Log($"[BlueprintViewModel] 实体引用：角色 '{name}' 在上游不存在，已忽略");
            return System.Threading.Tasks.Task.FromResult(string.Empty);
        }

        private async System.Threading.Tasks.Task<string> BlueprintResolveCharactersAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts) resolved.Add(await BlueprintResolveCharacterAsync(n));
            return string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveLocationsAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_locationService.GetAllLocationRules().Any(l => l.IsEnabled &&
                    string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase))) { resolved.Add(n); continue; }
                TM.App.Log($"[BlueprintViewModel] 实体引用：地点 '{n}' 在上游不存在，已忽略");
            }
            return System.Threading.Tasks.Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        private System.Threading.Tasks.Task<string> BlueprintResolveFactionsAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return System.Threading.Tasks.Task.FromResult(raw);
            var parts = raw.Split(new[] { ',', '，', '、', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
            var resolved = new List<string>();
            foreach (var n in parts)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_factionService.GetAllFactionRules().Any(f => f.IsEnabled &&
                    string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))) { resolved.Add(n); continue; }
                TM.App.Log($"[BlueprintViewModel] 实体引用：势力 '{n}' 在上游不存在，已忽略");
            }
            return System.Threading.Tasks.Task.FromResult(string.Join("、", resolved.Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        protected override async System.Threading.Tasks.Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(
                EnsureServiceInitializedAsync(Service),
                EnsureServiceInitializedAsync(_characterService),
                EnsureServiceInitializedAsync(_locationService),
                EnsureServiceInitializedAsync(_factionService),
                EnsureServiceInitializedAsync(_chapterService),
                EnsureServiceInitializedAsync(_volumeDesignService));

            try
            {
                InvalidateEntityCache();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        LoadAvailableEntities();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    LoadAvailableEntities();
                }
            }
            catch
            {
                LoadAvailableEntities();
            }
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems() => Service.ClearAllBlueprints();

        protected override List<BlueprintCategory> GetAllCategoriesFromService()
        {
            return Service.GetAllCategories();
        }

        protected override List<BlueprintData> GetAllDataItems()
            => Service.GetAllBlueprints().OrderBy(b => b.SceneNumber).ToList();

        protected override string GetDataCategory(BlueprintData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(BlueprintData data)
        {
            var chapterNo = TryParseChapterNumberFromChapterId(data.ChapterId);
            var chapterTitle = TryResolveChapterTitle(chapterNo);
            var fallbackTitle = string.IsNullOrWhiteSpace(chapterTitle)
                ? CleanBlueprintSceneTitle(string.IsNullOrWhiteSpace(data.SceneTitle) ? data.Name : data.SceneTitle)
                : chapterTitle;
            var titlePart = string.IsNullOrWhiteSpace(fallbackTitle) ? string.Empty : $" {fallbackTitle}";
            return new TreeNodeItem
            {
                Name = chapterNo > 0 ? $"第{chapterNo}章蓝图{titlePart}" : $"蓝图{titlePart}".Trim(),
                Icon = IconHelper.Get("Icon.Clapper"),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(BlueprintData data)
        {
            return new[] { data.SceneTitle, data.OneLineStructure, data.PovCharacter };
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: BlueprintData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                }
                else if (param is TreeNodeItem { Tag: BlueprintCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(BlueprintData data)
        {
            FormName = data.Name;
            FormIcon = "Icon.Clapper";
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;

            FormChapterId = MatchChapterId(data.ChapterId);
            FormOneLineStructure = data.OneLineStructure;
            FormPacingCurve = data.PacingCurve;

            FormSceneNumber = data.SceneNumber;
            FormSceneTitle = data.SceneTitle;
            FormPovCharacter = data.PovCharacter;
            FormOpening = data.Opening;
            FormDevelopment = data.Development;
            FormTurning = data.Turning;
            FormEnding = data.Ending;
            FormInfoDrop = data.InfoDrop;

            FormCast = data.Cast;
            FormLocations = data.Locations;
            FormFactions = data.Factions;
            FormItemsClues = data.ItemsClues;
        }

        private void LoadCategoryToForm(BlueprintCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.Name;
            ResetBusinessFields();
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = DefaultDataIcon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ResetBusinessFields();
        }

        private void ResetBusinessFields()
        {
            FormChapterId = GetDefaultChapterId();
            FormOneLineStructure = string.Empty;
            FormPacingCurve = string.Empty;

            FormSceneNumber = 0;
            FormSceneTitle = string.Empty;
            FormPovCharacter = string.Empty;
            FormOpening = string.Empty;
            FormDevelopment = string.Empty;
            FormTurning = string.Empty;
            FormEnding = string.Empty;
            FormInfoDrop = string.Empty;

            FormCast = string.Empty;
            FormLocations = string.Empty;
            FormFactions = string.Empty;
            FormItemsClues = string.Empty;
        }

    }
}
