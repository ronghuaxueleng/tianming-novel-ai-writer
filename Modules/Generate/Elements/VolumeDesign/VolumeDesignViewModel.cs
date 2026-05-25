using System;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class VolumeDesignViewModel : DataManagementViewModelBase<VolumeDesignData, VolumeDesignCategory, VolumeDesignService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterService;
        private readonly FactionRulesService _factionService;
        private readonly LocationRulesService _locationService;

        public VolumeDesignViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterService, FactionRulesService factionService, LocationRulesService locationService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterService = characterService;
            _factionService = factionService;
            _locationService = locationService;
        }

        protected override void OnAfterInitializeRefresh()
        {
            RefreshEntityOptions();
        }
        private void RefreshEntityOptions()
        {
            try { AvailableCharacters = _characterService.GetAllCharacterRules().Where(c => c.IsEnabled).Select(c => c.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载角色列表失败: {ex.Message}"); }

            try { AvailableFactions = _factionService.GetAllFactionRules().Where(f => f.IsEnabled).Select(f => f.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载势力列表失败: {ex.Message}"); }

            try { AvailableLocations = _locationService.GetAllLocationRules().Where(l => l.IsEnabled).Select(l => l.Name).ToList(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 加载地点列表失败: {ex.Message}"); }
        }

    }
}
