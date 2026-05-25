using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Generate.Elements.Chapter
{
    public partial class ChapterViewModel
    {
        private static readonly char[] EntityNameSeparators =
            new[] { ',', '，', '、', '\n', '\r', ' ', '\t', ';', '；' };

        private string _formName = string.Empty;
        private string _formIcon = "Icon.Document";
        private string _formStatus = "已启用";
        private string _formCategory = string.Empty;

        public string FormName { get => _formName; set { _formName = value; OnPropertyChanged(); } }
        public string FormIcon { get => _formIcon; set { _formIcon = value; OnPropertyChanged(); } }
        public string FormStatus { get => _formStatus; set { _formStatus = value; OnPropertyChanged(); } }

        public string FormCategory
        {
            get => _formCategory;
            set
            {
                if (_formCategory != value)
                {
                    _formCategory = value;
                    OnPropertyChanged();
                    OnCategoryValueChanged(_formCategory);
                }
            }
        }

        private string _formChapterTitle = string.Empty;
        private int _formChapterNumber = 0;
        private string _formVolume = string.Empty;
        private string _formChapterTheme = string.Empty;
        private string _formReaderExperienceGoal = string.Empty;
        private string _formMainGoal = string.Empty;

        public string FormChapterTitle { get => _formChapterTitle; set { _formChapterTitle = value; OnPropertyChanged(); } }
        public int FormChapterNumber { get => _formChapterNumber; set { _formChapterNumber = value; OnPropertyChanged(); } }
        public string FormVolume { get => _formVolume; set { _formVolume = value; OnPropertyChanged(); } }
        public string FormChapterTheme { get => _formChapterTheme; set { _formChapterTheme = value; OnPropertyChanged(); } }
        public string FormReaderExperienceGoal { get => _formReaderExperienceGoal; set { _formReaderExperienceGoal = value; OnPropertyChanged(); } }
        public string FormMainGoal { get => _formMainGoal; set { _formMainGoal = value; OnPropertyChanged(); } }

        private string _formResistanceSource = string.Empty;
        private string _formKeyTurn = string.Empty;
        private string _formHook = string.Empty;

        public string FormResistanceSource { get => _formResistanceSource; set { _formResistanceSource = value; OnPropertyChanged(); } }
        public string FormKeyTurn { get => _formKeyTurn; set { _formKeyTurn = value; OnPropertyChanged(); } }
        public string FormHook { get => _formHook; set { _formHook = value; OnPropertyChanged(); } }

        private string _formWorldInfoDrop = string.Empty;
        private string _formCharacterArcProgress = string.Empty;
        private string _formMainPlotProgress = string.Empty;
        private string _formForeshadowing = string.Empty;

        public string FormWorldInfoDrop { get => _formWorldInfoDrop; set { _formWorldInfoDrop = value; OnPropertyChanged(); } }
        public string FormCharacterArcProgress { get => _formCharacterArcProgress; set { _formCharacterArcProgress = value; OnPropertyChanged(); } }
        public string FormMainPlotProgress { get => _formMainPlotProgress; set { _formMainPlotProgress = value; OnPropertyChanged(); } }
        public string FormForeshadowing { get => _formForeshadowing; set { _formForeshadowing = value; OnPropertyChanged(); } }

        private string _formReferencedCharacterNames = string.Empty;
        private string _formReferencedFactionNames = string.Empty;
        private string _formReferencedLocationNames = string.Empty;

        public string FormReferencedCharacterNames { get => _formReferencedCharacterNames; set { _formReferencedCharacterNames = value; OnPropertyChanged(); } }
        public string FormReferencedFactionNames { get => _formReferencedFactionNames; set { _formReferencedFactionNames = value; OnPropertyChanged(); } }
        public string FormReferencedLocationNames { get => _formReferencedLocationNames; set { _formReferencedLocationNames = value; OnPropertyChanged(); } }

        private static string ToCommaSeparated(List<string> list)
            => list == null ? string.Empty : string.Join("、", list.Where(s => !string.IsNullOrWhiteSpace(s)));

        private static List<string> FromCommaSeparated(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        public RangeObservableCollection<string> AvailableCharacters { get; } = new();
        public RangeObservableCollection<string> AvailableFactions { get; } = new();
        public RangeObservableCollection<string> AvailableLocations { get; } = new();

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

                characters = volume.ReferencedCharacterNames?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                locations = volume.ReferencedLocationNames?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                factions = volume.ReferencedFactionNames?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            }
            catch (Exception ex) { TM.App.Log($"[ChapterViewModel] 获取分卷实体池失败: {ex.Message}"); }
        }

        private void RefreshEntityPool(string volumeCategory)
        {
            TryGetVolumeEntityPool(volumeCategory, out var chars, out var locs, out var facs);
            AvailableCharacters.ReplaceAll(chars);
            AvailableFactions.ReplaceAll(facs);
            AvailableLocations.ReplaceAll(locs);
        }

        private (string Characters, string Locations, string Factions) NormalizeChapterReferences(
            string? volumeCategory,
            string rawChars,
            string rawLocs,
            string rawFacs)
        {
            var chars = FilterToGlobalCharacters(rawChars);
            var locs = FilterToGlobalLocations(rawLocs);
            var facs = FilterToGlobalFactions(rawFacs);

            TryGetVolumeEntityPool(volumeCategory, out var scopedChars, out var scopedLocs, out var scopedFacs);

            if (scopedChars.Count > 0 && !string.IsNullOrWhiteSpace(chars))
                chars = EntityNameNormalizeHelper.FilterToCandidates(chars, scopedChars);
            if (scopedLocs.Count > 0 && !string.IsNullOrWhiteSpace(locs))
                locs = EntityNameNormalizeHelper.FilterToCandidates(locs, scopedLocs);
            if (scopedFacs.Count > 0 && !string.IsNullOrWhiteSpace(facs))
                facs = EntityNameNormalizeHelper.FilterToCandidates(facs, scopedFacs);

            return (chars, locs, facs);
        }

        private string FilterToGlobalCharacters(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var resolved = new List<string>();
            foreach (var n in raw.Split(EntityNameSeparators, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_characterService.GetAllCharacterRules().Any(c => c.IsEnabled &&
                    string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved.Add(n);
                    continue;
                }
                TM.App.Log($"[ChapterViewModel] 实体引用：角色 '{n}' 在上游不存在，已忽略");
            }
            return string.Join("、", resolved.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string FilterToGlobalLocations(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var resolved = new List<string>();
            foreach (var n in raw.Split(EntityNameSeparators, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_locationService.GetAllLocationRules().Any(l => l.IsEnabled &&
                    string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved.Add(n);
                    continue;
                }
                TM.App.Log($"[ChapterViewModel] 实体引用：地点 '{n}' 在上游不存在，已忽略");
            }
            return string.Join("、", resolved.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string FilterToGlobalFactions(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var resolved = new List<string>();
            foreach (var n in raw.Split(EntityNameSeparators, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (EntityNameNormalizeHelper.IsIgnoredValue(n)) continue;
                if (_factionService.GetAllFactionRules().Any(f => f.IsEnabled &&
                    string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
                {
                    resolved.Add(n);
                    continue;
                }
                TM.App.Log($"[ChapterViewModel] 实体引用：势力 '{n}' 在上游不存在，已忽略");
            }
            return string.Join("、", resolved.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
    }
}
