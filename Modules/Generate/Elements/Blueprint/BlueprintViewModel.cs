using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.RegularExpressions;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.Generate.Elements.Blueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class BlueprintViewModel : DataManagementViewModelBase<BlueprintData, BlueprintCategory, BlueprintService>, IDisposable
    {
        #region 预编译正则

        private static readonly Regex VolNumCnRegex = new(@"第\s*(\d+)\s*卷", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolChIdRegex = new(@"vol(\d+)_ch(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolChIdPartialRegex = new(@"vol(\d+)_ch\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CategoryVolNumRegex = new(@"第(\d+)卷", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChapterSuffixNumRegex = new(@"_ch(?<ch>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolInputParseRegex = new(@"vol\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChInputParseRegex = new(@"ch\s*(\d+)|第\s*(\d+)\s*章|章\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NormChVolArabicRegex = new(@"^\s*第\s*\d+\s*卷\s*[：:、\-—–]*\s*第\s*\d+\s*章\s*[：:、\-—–]*\s*", RegexOptions.Compiled);
        private static readonly Regex NormChVolChineseRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*卷\s*[：:、\-—–]*\s*第\s*[一二三四五六七八九十百千零]+\s*章\s*[：:、\-—–]*\s*", RegexOptions.Compiled);
        private static readonly Regex NormChArabicRegex = new(@"^\s*第\s*\d+\s*章\s*[：:、\-—–]*\s*", RegexOptions.Compiled);
        private static readonly Regex NormChChineseRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*章\s*[：:、\-—–]*\s*", RegexOptions.Compiled);

        private static readonly Regex CleanNumRangeRegex = new(@"^\s*\d+\s*[-_－—–]\s*\d+\s*", RegexOptions.Compiled);
        private static readonly Regex CleanVolChStrRegex = new(@"^\s*vol\s*\d+\s*[_-]?ch\s*\d+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanChPrefixRegex = new(@"^\s*ch\s*\d+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanSceneBpRegex = new(@"^\s*场景蓝图[-_]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanSceneNumRegex = new(@"^\s*场景\s*[-_]?\d+(?:-\d+)?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanVolChArabicRegex = new(@"^\s*第\s*\d+\s*卷\s*[-_\s]*第\s*\d+\s*章\s*[-_\s]*", RegexOptions.Compiled);
        private static readonly Regex CleanVolChChineseRegex = new(@"^\s*第\s*[一二三四五六七八九十百千零]+\s*卷\s*[-_\s]*第\s*[一二三四五六七八九十百千零]+\s*章\s*[-_\s]*", RegexOptions.Compiled);
        private static readonly Regex CleanSceneRefRegex = new(@"(^|[-_\s])scene\s*[-_]?\d+(?:-\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanVolRefRegex = new(@"(^|[-_\s])vol\d+(_?ch\d+)?(-\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanVolNumRegex = new(@"(^|[-_\s])卷\d+[-_\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CleanChNumRegex = new(@"(^|[-_\s])章\d+[-_\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        private readonly IPromptRepository _promptRepository;
        private readonly ContextService _contextService;
        private readonly CharacterRulesService _characterService;
        private readonly LocationRulesService _locationService;
        private readonly FactionRulesService _factionService;
        private readonly ChapterService _chapterService;
        private readonly VolumeDesignService _volumeDesignService;
        private string _formName = string.Empty;
        private string _formIcon = "Icon.Clapper";
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
                    LoadAvailableEntities();
                }
            }
        }

        private string _formChapterId = string.Empty;
        private string _formOneLineStructure = string.Empty;
        private string _formPacingCurve = string.Empty;

        public string FormChapterId
        {
            get => _formChapterId;
            set
            {
                if (_formChapterId != value)
                {
                    _formChapterId = value;
                    OnPropertyChanged();
                    LoadAvailableEntities();
                }
            }
        }
        public string FormOneLineStructure { get => _formOneLineStructure; set { _formOneLineStructure = value; OnPropertyChanged(); } }
        public string FormPacingCurve { get => _formPacingCurve; set { _formPacingCurve = value; OnPropertyChanged(); } }

        private int _formSceneNumber = 0;
        private string _formSceneTitle = string.Empty;
        private string _formPovCharacter = string.Empty;
        private string _formOpening = string.Empty;
        private string _formDevelopment = string.Empty;
        private string _formTurning = string.Empty;
        private string _formEnding = string.Empty;
        private string _formInfoDrop = string.Empty;

        public int FormSceneNumber { get => _formSceneNumber; set { _formSceneNumber = value; OnPropertyChanged(); } }
        public string FormSceneTitle { get => _formSceneTitle; set { _formSceneTitle = value; OnPropertyChanged(); } }
        public string FormPovCharacter { get => _formPovCharacter; set { _formPovCharacter = value; OnPropertyChanged(); } }
        public string FormOpening { get => _formOpening; set { _formOpening = value; OnPropertyChanged(); } }
        public string FormDevelopment { get => _formDevelopment; set { _formDevelopment = value; OnPropertyChanged(); } }
        public string FormTurning { get => _formTurning; set { _formTurning = value; OnPropertyChanged(); } }
        public string FormEnding { get => _formEnding; set { _formEnding = value; OnPropertyChanged(); } }
        public string FormInfoDrop { get => _formInfoDrop; set { _formInfoDrop = value; OnPropertyChanged(); } }

        private string _formItemsClues = string.Empty;
        public string FormItemsClues { get => _formItemsClues; set { _formItemsClues = value; OnPropertyChanged(); } }

        private string _formCast = string.Empty;
        private string _formLocations = string.Empty;
        private string _formFactions = string.Empty;

        public string FormCast { get => _formCast; set { _formCast = value; OnPropertyChanged(); } }
        public string FormLocations { get => _formLocations; set { _formLocations = value; OnPropertyChanged(); } }
        public string FormFactions { get => _formFactions; set { _formFactions = value; OnPropertyChanged(); } }

        public ObservableCollection<string> AvailableCharacters { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();
        public ObservableCollection<string> AvailableLocations { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();
        public ObservableCollection<string> AvailableFactions { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();
        private readonly TM.Framework.Common.Services.LazyListCache<string> _globalCharsCache = new();
        private readonly TM.Framework.Common.Services.LazyListCache<string> _globalLocsCache = new();
        private readonly TM.Framework.Common.Services.LazyListCache<string> _globalFactionsCache = new();
        private void InvalidateEntityCache() { _globalCharsCache.Invalidate(); _globalLocsCache.Invalidate(); _globalFactionsCache.Invalidate(); }

        public ObservableCollection<string> AvailableChapterIds { get; } = new();

        private List<string> _availablePovCharacters = new();
        public List<string> AvailablePovCharacters => _availablePovCharacters;

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        public BlueprintViewModel(IPromptRepository promptRepository, ContextService contextService, CharacterRulesService characterService, LocationRulesService locationService, FactionRulesService factionService, ChapterService chapterService, VolumeDesignService volumeDesignService)
        {
            _promptRepository = promptRepository;
            _contextService = contextService;
            _characterService = characterService;
            _locationService = locationService;
            _factionService = factionService;
            _chapterService = chapterService;
            _volumeDesignService = volumeDesignService;

            _volumeDesignService.DataChanged += OnVolumeDataChanged;
            _chapterService.DataChanged += OnChapterDataChanged;
        }

    }
}
