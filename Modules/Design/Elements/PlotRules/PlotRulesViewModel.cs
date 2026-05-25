using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Modules.Design.Elements.PlotRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class PlotRulesViewModel : DataManagementViewModelBase<PlotRulesData, PlotRulesCategory, PlotRulesService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterService;
        private readonly LocationRulesService _locationService;
        private List<string> _availableCharacters = new();
        private List<string> _availableLocations = new();
        private Dictionary<string, string> _charIdToName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _charNameToId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _locIdToName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _locNameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly TM.Framework.Common.Services.LazyListCache<CharacterRulesData> _allCharsCache = new();
        private readonly TM.Framework.Common.Services.LazyListCache<LocationRulesData> _allLocsCache = new();

        public List<string> AvailableCharacters
        {
            get => _availableCharacters;
            set { _availableCharacters = value; OnPropertyChanged(); }
        }

        public List<string> AvailableLocations
        {
            get => _availableLocations;
            set { _availableLocations = value; OnPropertyChanged(); }
        }

        public PlotRulesViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterService, LocationRulesService locationService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterService = characterService;
            _locationService = locationService;
        }

        protected override void OnAfterInitializeRefresh()
        {
            RefreshRelationshipOptions();
        }

        private void InvalidateRelationshipCache() { _allCharsCache.Invalidate(); _allLocsCache.Invalidate(); }

        private void RefreshRelationshipOptions()
        {
            _charIdToName = new(StringComparer.OrdinalIgnoreCase);
            _charNameToId = new(StringComparer.OrdinalIgnoreCase);
            _locIdToName = new(StringComparer.OrdinalIgnoreCase);
            _locNameToId = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                var allChars = _allCharsCache.Get(() => _characterService.GetAllCharacterRules().Where(c => c.IsEnabled).ToList());
                foreach (var c in allChars)
                {
                    if (!string.IsNullOrWhiteSpace(c.Id)) _charIdToName[c.Id] = c.Name;
                    if (!string.IsNullOrWhiteSpace(c.Name)) _charNameToId[c.Name] = c.Id;
                }
                AvailableCharacters = allChars.Select(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 加载角色列表失败: {ex.Message}");
                AvailableCharacters = new List<string>();
            }

            try
            {
                var allLocs = _allLocsCache.Get(() => _locationService.GetAllLocationRules().Where(l => l.IsEnabled).ToList());
                foreach (var l in allLocs)
                {
                    if (!string.IsNullOrWhiteSpace(l.Id)) _locIdToName[l.Id] = l.Name;
                    if (!string.IsNullOrWhiteSpace(l.Name)) _locNameToId[l.Name] = l.Id;
                }
                AvailableLocations = allLocs.Select(l => l.Name).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotRulesViewModel] 加载位置列表失败: {ex.Message}");
                AvailableLocations = new List<string>();
            }
        }

        private string CharIdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_charIdToName.TryGetValue(idOrName, out var n)) return n;
            if (_charNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string CharIdsToNames(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
            return string.Join("、", ids.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(CharIdToName).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string CharNameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (_charNameToId.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            return string.Empty;
        }

        private string CharNamesToIds(string names)
        {
            if (string.IsNullOrWhiteSpace(names)) return string.Empty;
            return string.Join("、", names.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(CharNameToId).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string LocIdToName(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (_locIdToName.TryGetValue(idOrName, out var n)) return n;
            if (_locNameToId.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        private string LocNameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            if (_locNameToId.TryGetValue(name, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(name)) return name;
            return string.Empty;
        }

    }
}
