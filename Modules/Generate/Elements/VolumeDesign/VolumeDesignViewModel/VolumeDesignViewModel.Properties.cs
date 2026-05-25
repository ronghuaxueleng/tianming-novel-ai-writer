using System.Collections.Generic;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    public partial class VolumeDesignViewModel
    {
        private string _formName = string.Empty;
        private string _formIcon = "Icon.Books";
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

        private int _formVolumeNumber;
        private string _formVolumeTitle = string.Empty;
        private string _formVolumeTheme = string.Empty;
        private string _formStageGoal = string.Empty;
        private int _formStartChapter;
        private int _formEndChapter;

        public int FormVolumeNumber { get => _formVolumeNumber; set { _formVolumeNumber = value; OnPropertyChanged(); } }
        public string FormVolumeTitle { get => _formVolumeTitle; set { _formVolumeTitle = value; OnPropertyChanged(); } }
        public string FormVolumeTheme { get => _formVolumeTheme; set { _formVolumeTheme = value; OnPropertyChanged(); } }
        public string FormStageGoal { get => _formStageGoal; set { _formStageGoal = value; OnPropertyChanged(); } }
        public int FormStartChapter { get => _formStartChapter; set { _formStartChapter = value; OnPropertyChanged(); } }
        public int FormEndChapter { get => _formEndChapter; set { _formEndChapter = value; OnPropertyChanged(); } }

        private string _formMainConflict = string.Empty;
        private string _formPressureSource = string.Empty;
        private string _formKeyEvents = string.Empty;
        private string _formOpeningState = string.Empty;
        private string _formEndingState = string.Empty;

        public string FormMainConflict { get => _formMainConflict; set { _formMainConflict = value; OnPropertyChanged(); } }
        public string FormPressureSource { get => _formPressureSource; set { _formPressureSource = value; OnPropertyChanged(); } }
        public string FormKeyEvents { get => _formKeyEvents; set { _formKeyEvents = value; OnPropertyChanged(); } }
        public string FormOpeningState { get => _formOpeningState; set { _formOpeningState = value; OnPropertyChanged(); } }
        public string FormEndingState { get => _formEndingState; set { _formEndingState = value; OnPropertyChanged(); } }

        private string _formChapterAllocationOverview = string.Empty;
        private string _formPlotAllocation = string.Empty;
        private string _formChapterGenerationHints = string.Empty;

        public string FormChapterAllocationOverview { get => _formChapterAllocationOverview; set { _formChapterAllocationOverview = value; OnPropertyChanged(); } }
        public string FormPlotAllocation { get => _formPlotAllocation; set { _formPlotAllocation = value; OnPropertyChanged(); } }
        public string FormChapterGenerationHints { get => _formChapterGenerationHints; set { _formChapterGenerationHints = value; OnPropertyChanged(); } }

        private string _formReferencedCharacterNames = string.Empty;
        private string _formReferencedFactionNames = string.Empty;
        private string _formReferencedLocationNames = string.Empty;

        public string FormReferencedCharacterNames { get => _formReferencedCharacterNames; set { _formReferencedCharacterNames = value; OnPropertyChanged(); } }
        public string FormReferencedFactionNames { get => _formReferencedFactionNames; set { _formReferencedFactionNames = value; OnPropertyChanged(); } }
        public string FormReferencedLocationNames { get => _formReferencedLocationNames; set { _formReferencedLocationNames = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };

        private List<string> _availableCharacters = new();
        private List<string> _availableFactions = new();
        private List<string> _availableLocations = new();

        public List<string> AvailableCharacters { get => _availableCharacters; private set { _availableCharacters = value; OnPropertyChanged(); } }
        public List<string> AvailableFactions { get => _availableFactions; private set { _availableFactions = value; OnPropertyChanged(); } }
        public List<string> AvailableLocations { get => _availableLocations; private set { _availableLocations = value; OnPropertyChanged(); } }
    }
}
