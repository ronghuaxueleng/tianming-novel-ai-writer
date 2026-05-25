using System.Collections.Generic;

namespace TM.Modules.Design.Elements.PlotRules
{
    public partial class PlotRulesViewModel
    {
        protected override string NewItemTypeName => "剧情规则";

        private string _formName = string.Empty;
        private string _formIcon = "Icon.Book";
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

        private string _formTargetVolume = string.Empty;
        private string _formAssignedVolume = string.Empty;
        private string _formOneLineSummary = string.Empty;
        private string _formEventType = string.Empty;
        private string _formStoryPhase = string.Empty;
        private string _formPrerequisitesTrigger = string.Empty;

        public string FormTargetVolume
        {
            get => _formTargetVolume;
            set
            {
                if (_formTargetVolume != value)
                {
                    _formTargetVolume = value;
                    OnPropertyChanged();
                    RefreshAssignedVolumeOptions();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                        System.Windows.Threading.DispatcherPriority.Background);

                    var hasEditingContext = IsCreateMode || _currentEditingData != null || _currentEditingCategory != null;
                    var isValidTotalVolume = int.TryParse(_formTargetVolume?.Trim(), out var n) && n > 0;
                    IsAIGenerateEnabled = hasEditingContext && isValidTotalVolume;
                }
            }
        }
        public string FormAssignedVolume { get => _formAssignedVolume; set { _formAssignedVolume = value; OnPropertyChanged(); } }
        public string FormOneLineSummary { get => _formOneLineSummary; set { _formOneLineSummary = value; OnPropertyChanged(); } }
        public string FormEventType { get => _formEventType; set { _formEventType = value; OnPropertyChanged(); } }
        public string FormStoryPhase { get => _formStoryPhase; set { _formStoryPhase = value; OnPropertyChanged(); } }
        public string FormPrerequisitesTrigger { get => _formPrerequisitesTrigger; set { _formPrerequisitesTrigger = value; OnPropertyChanged(); } }

        private string _formMainCharacters = string.Empty;
        private string _formKeyNpcs = string.Empty;
        private string _formLocation = string.Empty;
        private string _formTimeDuration = string.Empty;

        public string FormMainCharacters { get => _formMainCharacters; set { _formMainCharacters = value; OnPropertyChanged(); } }
        public string FormKeyNpcs { get => _formKeyNpcs; set { _formKeyNpcs = value; OnPropertyChanged(); } }
        public string FormLocation { get => _formLocation; set { _formLocation = value; OnPropertyChanged(); } }
        public string FormTimeDuration { get => _formTimeDuration; set { _formTimeDuration = value; OnPropertyChanged(); } }

        private string _formStepTitle = string.Empty;
        private string _formGoal = string.Empty;
        private string _formConflict = string.Empty;
        private string _formResult = string.Empty;
        private string _formEmotionCurve = string.Empty;

        public string FormStepTitle { get => _formStepTitle; set { _formStepTitle = value; OnPropertyChanged(); } }
        public string FormGoal { get => _formGoal; set { _formGoal = value; OnPropertyChanged(); } }
        public string FormConflict { get => _formConflict; set { _formConflict = value; OnPropertyChanged(); } }
        public string FormResult { get => _formResult; set { _formResult = value; OnPropertyChanged(); } }
        public string FormEmotionCurve { get => _formEmotionCurve; set { _formEmotionCurve = value; OnPropertyChanged(); } }

        private string _formMainPlotPush = string.Empty;
        private string _formCharacterGrowth = string.Empty;
        private string _formWorldReveal = string.Empty;
        private string _formRewardsClues = string.Empty;

        public string FormMainPlotPush { get => _formMainPlotPush; set { _formMainPlotPush = value; OnPropertyChanged(); } }
        public string FormCharacterGrowth { get => _formCharacterGrowth; set { _formCharacterGrowth = value; OnPropertyChanged(); } }
        public string FormWorldReveal { get => _formWorldReveal; set { _formWorldReveal = value; OnPropertyChanged(); } }
        public string FormRewardsClues { get => _formRewardsClues; set { _formRewardsClues = value; OnPropertyChanged(); } }

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
        public List<string> EventTypeOptions { get; } = new() { "主线剧情", "卷主线", "支线剧情", "过渡剧情", "伏笔埋设", "伏笔揭示" };

        private List<string> _assignedVolumeOptions = new() { "全局" };
        public List<string> AssignedVolumeOptions
        {
            get => _assignedVolumeOptions;
            private set { _assignedVolumeOptions = value; OnPropertyChanged(); }
        }

        private void RefreshAssignedVolumeOptions()
        {
            var options = new List<string> { "全局" };
            if (int.TryParse(_formTargetVolume?.Trim(), out var n) && n > 0)
            {
                for (int i = 1; i <= n; i++)
                    options.Add($"第{i}卷");
            }
            AssignedVolumeOptions = options;
        }

        private static List<string> BuildAssignedVolumeOptions(string? totalVolume)
        {
            var options = new List<string> { "全局" };
            if (int.TryParse(totalVolume?.Trim(), out var n) && n > 0)
            {
                for (int i = 1; i <= n; i++)
                    options.Add($"第{i}卷");
            }
            return options;
        }
    }
}
