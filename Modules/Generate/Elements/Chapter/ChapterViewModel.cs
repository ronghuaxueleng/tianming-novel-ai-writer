using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;

namespace TM.Modules.Generate.Elements.Chapter
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ChapterViewModel : DataManagementViewModelBase<ChapterData, ChapterCategory, ChapterService>, IDisposable
    {
        private static readonly Regex ChNormLeadingPrefixRegex = new(@"^.{0,30}?[-_—–\s]+(?=第\s*[\d一二三四五六七八九十百千零]+\s*章)", RegexOptions.Compiled);
        private static readonly Regex ChNormArabicRegex = new(@"^\s*第\s*\d+\s*章\s*[：:、\-—–_]*\s*", RegexOptions.Compiled);
        private static readonly Regex ChNormChineseRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*章\s*[：:、\-—–_]*\s*", RegexOptions.Compiled);

        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly CharacterRulesService _characterService;
        private readonly LocationRulesService _locationService;
        private readonly FactionRulesService _factionService;

        public ChapterViewModel(
            IPromptRepository promptRepository,
            ContextService contextService,
            VolumeDesignService volumeDesignService,
            CharacterRulesService characterService,
            LocationRulesService locationService,
            FactionRulesService factionService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _volumeDesignService = volumeDesignService;
            _characterService = characterService;
            _locationService = locationService;
            _factionService = factionService;

            _volumeDesignService.DataChanged += OnVolumeDataChanged;
        }
        private void OnVolumeDataChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RefreshTreeAndCategorySelection();
                    UpdateBulkToggleState();

                    if (!string.IsNullOrWhiteSpace(FormCategory))
                    {
                        var categories = GetAllCategoriesFromService() ?? new List<ChapterCategory>();
                        if (!categories.Any(c => string.Equals(c.Name, FormCategory, StringComparison.Ordinal)))
                        {
                            FormCategory = string.Empty;
                            FormVolume = string.Empty;
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 同步分卷数据变更失败: {ex.Message}");
            }
        }
        public override void Dispose()
        {
            _volumeDesignService.DataChanged -= OnVolumeDataChanged;
            base.Dispose();
        }
    }
}
