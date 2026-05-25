using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace TM.Modules.Generate.Elements.Blueprint
{
    public partial class BlueprintViewModel
    {
        protected override void OnAfterInitializeRefresh()
        {
            LoadAvailableEntities();
        }

        private void LoadAvailableEntities()
        {
            var globalCharacters = new List<string>();
            var globalLocations = new List<string>();
            var globalFactions = new List<string>();

            try
            {
                globalCharacters = _globalCharsCache.Get(() => _characterService.GetAllCharacterRules()
                    .Where(c => c.IsEnabled)
                    .Select(c => c.Name)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载角色列表失败: {ex.Message}");
            }

            try
            {
                globalLocations = _globalLocsCache.Get(() => _locationService.GetAllLocationRules()
                    .Where(l => l.IsEnabled)
                    .Select(l => l.Name)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载地点列表失败: {ex.Message}");
            }

            try
            {
                globalFactions = _globalFactionsCache.Get(() => _factionService.GetAllFactionRules()
                    .Where(f => f.IsEnabled)
                    .Select(f => f.Name)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 加载势力列表失败: {ex.Message}");
            }

            GetScopedEntityPools(FormChapterId, FormCategory, out var scopedCharacters, out var scopedLocations, out var scopedFactions);

            ReplaceCollection(AvailableCharacters, scopedCharacters.Count > 0 ? scopedCharacters : globalCharacters);
            ReplaceCollection(AvailableLocations, scopedLocations.Count > 0 ? scopedLocations : globalLocations);
            ReplaceCollection(AvailableFactions, scopedFactions.Count > 0 ? scopedFactions : globalFactions);

            ReloadAvailableChapterIds();

            _availablePovCharacters = AvailableCharacters.Prepend(string.Empty).ToList();
            OnPropertyChanged(nameof(AvailablePovCharacters));
        }

        private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            if (target is TM.Framework.Common.ViewModels.RangeObservableCollection<T> range)
            {
                range.ReplaceAll(items is IList<T> list ? list : items.ToList());
                return;
            }

            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private void TryGetVolumeEntityPool(
            string? volumeCategory,
            out List<string> characters,
            out List<string> locations,
            out List<string> factions)
        {
            characters = new List<string>();
            locations = new List<string>();
            factions = new List<string>();

            if (string.IsNullOrWhiteSpace(volumeCategory)) return;

            try
            {
                var volume = _volumeDesignService.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.IsEnabled
                        && (string.Equals(v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name, volumeCategory, StringComparison.Ordinal)
                            || string.Equals(v.Name, volumeCategory, StringComparison.Ordinal)));
                if (volume == null) return;

                characters = volume.ReferencedCharacterNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
                locations = volume.ReferencedLocationNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
                factions = volume.ReferencedFactionNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 获取分卷实体池失败: {ex.Message}");
            }
        }

        private void TryGetChapterEntityPool(
            string? chapterId,
            out List<string> characters,
            out List<string> locations,
            out List<string> factions)
        {
            characters = new List<string>();
            locations = new List<string>();
            factions = new List<string>();

            if (string.IsNullOrWhiteSpace(chapterId)) return;

            try
            {
                var m = VolChIdRegex.Match(chapterId.Trim());
                if (!m.Success) return;
                var volNum = int.Parse(m.Groups[1].Value);
                var chNum = int.Parse(m.Groups[2].Value);

                _chapterService.EnsureInitialized();
                var chapter = _chapterService.GetAllChapters()
                    .FirstOrDefault(c => c.IsEnabled && c.ChapterNumber == chNum
                        && ExtractVolumeNumber(c.Category) == volNum);
                if (chapter == null) return;

                characters = chapter.ReferencedCharacterNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
                locations = chapter.ReferencedLocationNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
                factions = chapter.ReferencedFactionNames?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 获取章节实体池失败: {ex.Message}");
            }
        }

        private void GetScopedEntityPools(
            string? chapterId,
            string? volumeCategory,
            out List<string> characters,
            out List<string> locations,
            out List<string> factions)
        {
            TryGetChapterEntityPool(chapterId, out var chapChars, out var chapLocs, out var chapFacs);
            TryGetVolumeEntityPool(volumeCategory, out var volChars, out var volLocs, out var volFacs);

            characters = chapChars.Count > 0 ? chapChars : volChars;
            locations = chapLocs.Count > 0 ? chapLocs : volLocs;
            factions = chapFacs.Count > 0 ? chapFacs : volFacs;
        }

        private async Task<(string Cast, string Locations, string Factions, string PovCharacter)> NormalizeBlueprintEntitiesAsync(
            string? chapterId,
            string? volumeCategory,
            string rawCast,
            string rawLocations,
            string rawFactions,
            string rawPov)
        {
            var cast = string.IsNullOrWhiteSpace(rawCast) ? string.Empty : await BlueprintResolveCharactersAsync(rawCast);
            var locs = string.IsNullOrWhiteSpace(rawLocations) ? string.Empty : await BlueprintResolveLocationsAsync(rawLocations);
            var facs = string.IsNullOrWhiteSpace(rawFactions) ? string.Empty : await BlueprintResolveFactionsAsync(rawFactions);
            var pov = string.IsNullOrWhiteSpace(rawPov) ? string.Empty : await BlueprintResolveCharacterAsync(rawPov);

            GetScopedEntityPools(chapterId, volumeCategory, out var scopedChars, out var scopedLocs, out var scopedFacs);
            if (scopedChars.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(cast))
                    cast = EntityNameNormalizeHelper.FilterToCandidates(cast, scopedChars);
                if (!string.IsNullOrWhiteSpace(pov))
                    pov = EntityNameNormalizeHelper.FilterToCandidate(pov, scopedChars);
            }
            if (scopedLocs.Count > 0 && !string.IsNullOrWhiteSpace(locs))
                locs = EntityNameNormalizeHelper.FilterToCandidates(locs, scopedLocs);
            if (scopedFacs.Count > 0 && !string.IsNullOrWhiteSpace(facs))
                facs = EntityNameNormalizeHelper.FilterToCandidates(facs, scopedFacs);

            return (cast, locs, facs, pov);
        }

        private static int ExtractVolumeNumber(string? volume)
        {
            if (string.IsNullOrWhiteSpace(volume)) return 0;

            var cnMatch = VolNumCnRegex.Match(volume);
            if (cnMatch.Success && int.TryParse(cnMatch.Groups[1].Value, out var cnNum)) return cnNum;

            if (int.TryParse(volume, out var num)) return num;

            var cleaned = volume.Replace("vol", "").Replace("_", "").Trim();
            if (int.TryParse(cleaned, out var parsed)) return parsed;

            return 0;
        }

    }
}
