using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.Templates.CreativeMaterials;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    public partial class ShortStoryBlueprintViewModel
    {
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

        private string _formBookAnalysisId = string.Empty;
        private string _formSourceBookName = string.Empty;
        private string _formGenre = string.Empty;
        private string _formSynopsis = string.Empty;
        private string _formTotalChapters = string.Empty;
        private string _formWordsPerChapter = string.Empty;
        private string _formToneGuide = string.Empty;
        private bool _genreManuallySet;
        private bool _suppressGenreManualMark;

        public string FormBookAnalysisId
        {
            get => _formBookAnalysisId;
            set
            {
                if (_formBookAnalysisId != value)
                {
                    _formBookAnalysisId = value;
                    OnPropertyChanged();
                    UpdateSourceBookName();
                    UpdateAIGenerateEnabledState();
                }
            }
        }
        public string FormSourceBookName { get => _formSourceBookName; set { _formSourceBookName = value; OnPropertyChanged(); } }

        public string FormGenre
        {
            get => _formGenre;
            set
            {
                if (_formGenre != value)
                {
                    _formGenre = value;
                    if (!_suppressGenreManualMark)
                        _genreManuallySet = !string.IsNullOrWhiteSpace(_formGenre);
                    OnPropertyChanged();
                }
            }
        }

        public string FormSynopsis { get => _formSynopsis; set { _formSynopsis = value; OnPropertyChanged(); } }
        public string FormWordsPerChapter { get => _formWordsPerChapter; set { _formWordsPerChapter = value; OnPropertyChanged(); } }
        public string FormToneGuide { get => _formToneGuide; set { _formToneGuide = value; OnPropertyChanged(); } }

        private List<BookAnalysisOption> _bookOptions = new();
        public List<BookAnalysisOption> BookOptions { get => _bookOptions; set { _bookOptions = value; OnPropertyChanged(); } }

        private List<GenreInfo> _genreOptions = new();
        public List<GenreInfo> GenreOptions { get => _genreOptions; set { _genreOptions = value; OnPropertyChanged(); } }

        public bool IsTotalChaptersLocked => _currentEditingData != null && ChapterBlueprints.Count > 0;

        public string FormTotalChapters
        {
            get => _formTotalChapters;
            set
            {
                if (_formTotalChapters != value)
                {
                    _formTotalChapters = value;
                    OnPropertyChanged();
                    SyncBlueprintsFromTotalChapters();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                        System.Windows.Threading.DispatcherPriority.Background);
                    UpdateAIGenerateEnabledState();
                }
            }
        }

        public RangeObservableCollection<ShortStoryChapterBlueprintVM> ChapterBlueprints { get; } = new();

        private int _currentChapterIndex;
        public int CurrentChapterIndex
        {
            get => _currentChapterIndex;
            set
            {
                if (value < 0 || value >= ChapterBlueprints.Count) return;
                _currentChapterIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentChapterBlueprint));
                OnPropertyChanged(nameof(CurrentChapterLabel));
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }

        public ShortStoryChapterBlueprintVM? CurrentChapterBlueprint
            => ChapterBlueprints.ElementAtOrDefault(_currentChapterIndex);

        public string CurrentChapterLabel
            => ChapterBlueprints.Count > 0
                ? $"第{_currentChapterIndex + 1}章  /  共{ChapterBlueprints.Count}章"
                : "暂无章节";

        public bool CanGoPrev => _currentChapterIndex > 0;
        public bool CanGoNext => _currentChapterIndex < ChapterBlueprints.Count - 1;

        public List<string> StatusOptions { get; } = new() { "已禁用", "已启用" };
    }
}
