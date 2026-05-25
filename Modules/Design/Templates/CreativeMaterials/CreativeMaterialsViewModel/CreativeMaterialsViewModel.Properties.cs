using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    public partial class CreativeMaterialsViewModel
    {
        private string _formName = string.Empty;
        private string _formIcon = "Icon.Note";
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
        private string _formOverallIdea = string.Empty;

        private bool _genreManuallySet;
        private bool _suppressGenreManualMark;
        private bool _suppressSelectedGenresSync;
        private bool _suppressSelectedGenresChangeHandling;

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
                    {
                        _genreManuallySet = !string.IsNullOrWhiteSpace(_formGenre);
                    }
                    OnPropertyChanged();
                    if (!_suppressSelectedGenresSync)
                    {
                        SyncSelectedGenresFromFormGenre();
                    }
                }
            }
        }
        public string FormOverallIdea { get => _formOverallIdea; set { _formOverallIdea = value; OnPropertyChanged(); } }

        private string _formNovelSynopsis = string.Empty;
        public string FormNovelSynopsis { get => _formNovelSynopsis; set { _formNovelSynopsis = value; OnPropertyChanged(); } }

        public List<string> GoldenChapterOptions { get; } = new() { "不启用", "黄金三章" };
        private string _formGoldenChapterModeText = "不启用";
        public string FormGoldenChapterModeText
        {
            get => _formGoldenChapterModeText;
            set
            {
                if (_formGoldenChapterModeText != value)
                {
                    _formGoldenChapterModeText = value ?? "不启用";
                    OnPropertyChanged();
                    TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.Save(_formGoldenChapterModeText == "黄金三章");
                }
            }
        }

        private string _formWorldBuildingMethod = string.Empty;
        private string _formPowerSystemDesign = string.Empty;
        private string _formEnvironmentDescription = string.Empty;
        private string _formFactionDesign = string.Empty;
        private string _formWorldviewHighlights = string.Empty;

        public string FormWorldBuildingMethod { get => _formWorldBuildingMethod; set { _formWorldBuildingMethod = value; OnPropertyChanged(); } }
        public string FormPowerSystemDesign { get => _formPowerSystemDesign; set { _formPowerSystemDesign = value; OnPropertyChanged(); } }
        public string FormEnvironmentDescription { get => _formEnvironmentDescription; set { _formEnvironmentDescription = value; OnPropertyChanged(); } }
        public string FormFactionDesign { get => _formFactionDesign; set { _formFactionDesign = value; OnPropertyChanged(); } }
        public string FormWorldviewHighlights { get => _formWorldviewHighlights; set { _formWorldviewHighlights = value; OnPropertyChanged(); } }

        private string _formProtagonistDesign = string.Empty;
        private string _formSupportingRoles = string.Empty;
        private string _formCharacterRelations = string.Empty;
        private string _formGoldenFingerDesign = string.Empty;
        private string _formCharacterHighlights = string.Empty;

        public string FormProtagonistDesign { get => _formProtagonistDesign; set { _formProtagonistDesign = value; OnPropertyChanged(); } }
        public string FormSupportingRoles { get => _formSupportingRoles; set { _formSupportingRoles = value; OnPropertyChanged(); } }
        public string FormCharacterRelations { get => _formCharacterRelations; set { _formCharacterRelations = value; OnPropertyChanged(); } }
        public string FormGoldenFingerDesign { get => _formGoldenFingerDesign; set { _formGoldenFingerDesign = value; OnPropertyChanged(); } }
        public string FormCharacterHighlights { get => _formCharacterHighlights; set { _formCharacterHighlights = value; OnPropertyChanged(); } }

        private string _formPlotStructure = string.Empty;
        private string _formConflictDesign = string.Empty;
        private string _formClimaxArrangement = string.Empty;
        private string _formForeshadowingTechnique = string.Empty;
        private string _formPlotHighlights = string.Empty;

        public string FormPlotStructure { get => _formPlotStructure; set { _formPlotStructure = value; OnPropertyChanged(); } }
        public string FormConflictDesign { get => _formConflictDesign; set { _formConflictDesign = value; OnPropertyChanged(); } }
        public string FormClimaxArrangement { get => _formClimaxArrangement; set { _formClimaxArrangement = value; OnPropertyChanged(); } }
        public string FormForeshadowingTechnique { get => _formForeshadowingTechnique; set { _formForeshadowingTechnique = value; OnPropertyChanged(); } }
        public string FormPlotHighlights { get => _formPlotHighlights; set { _formPlotHighlights = value; OnPropertyChanged(); } }

        private List<BookAnalysisOption> _bookOptions = new();
        public List<BookAnalysisOption> BookOptions { get => _bookOptions; set { _bookOptions = value; OnPropertyChanged(); } }

        private List<GenreInfo> _genreOptions = new();
        public List<GenreInfo> GenreOptions
        {
            get => _genreOptions;
            set
            {
                _genreOptions = value ?? new();
                OnPropertyChanged();
                RemapSelectedGenresToCurrentOptions();
            }
        }

        public List<string> StatusOptions { get; } = new()
        {
            "已禁用", "已启用"
        };

        public List<string> GenreSelectionModeOptions { get; } = new() { "单选", "多选" };

        private string _formGenreSelectionMode = "单选";
        public string FormGenreSelectionMode
        {
            get => _formGenreSelectionMode;
            set
            {
                var newValue = string.IsNullOrWhiteSpace(value) ? "单选" : value;
                if (_formGenreSelectionMode != newValue)
                {
                    _formGenreSelectionMode = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSingleGenreMode));
                    OnPropertyChanged(nameof(IsMultiGenreMode));
                    ApplyGenreSelectionModeChange();
                }
            }
        }

        public bool IsSingleGenreMode => _formGenreSelectionMode == "单选";
        public bool IsMultiGenreMode => _formGenreSelectionMode == "多选";

        public ObservableCollection<GenreInfo> FormSelectedGenres { get; } = new();

        private void OnFormSelectedGenresChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSelectedGenresChangeHandling) return;

            _genreManuallySet = FormSelectedGenres.Count > 0;
            var first = FormSelectedGenres.FirstOrDefault();
            var nextGenre = first?.Name ?? string.Empty;
            if (!string.Equals(FormGenre, nextGenre, StringComparison.Ordinal))
            {
                _suppressSelectedGenresSync = true;
                try { FormGenre = nextGenre; }
                finally { _suppressSelectedGenresSync = false; }
            }
        }

        private void SyncSelectedGenresFromFormGenre()
        {
            _suppressSelectedGenresChangeHandling = true;
            try
            {
                FormSelectedGenres.Clear();
                if (string.IsNullOrWhiteSpace(FormGenre)) return;

                var info = GenreOptions.FirstOrDefault(g => string.Equals(g.Name, FormGenre, StringComparison.OrdinalIgnoreCase));
                if (info != null) FormSelectedGenres.Add(info);
            }
            finally
            {
                _suppressSelectedGenresChangeHandling = false;
            }
        }

        private void RemapSelectedGenresToCurrentOptions()
        {
            var selectedNames = FormSelectedGenres
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => g.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedNames.Count == 0 && !string.IsNullOrWhiteSpace(FormGenre))
            {
                selectedNames.Add(FormGenre);
            }

            _suppressSelectedGenresChangeHandling = true;
            try
            {
                FormSelectedGenres.Clear();
                foreach (var name in selectedNames)
                {
                    var info = GenreOptions.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (info != null) FormSelectedGenres.Add(info);
                }
            }
            finally
            {
                _suppressSelectedGenresChangeHandling = false;
            }
        }

        private void ApplyGenreSelectionModeChange()
        {
            if (IsSingleGenreMode)
            {
                if (FormSelectedGenres.Count > 1)
                {
                    var first = FormSelectedGenres[0];
                    FormSelectedGenres.Clear();
                    FormSelectedGenres.Add(first);
                }
                var g = FormSelectedGenres.FirstOrDefault();
                if (g != null && !string.Equals(FormGenre, g.Name, StringComparison.Ordinal))
                {
                    FormGenre = g.Name;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(FormGenre)
                    && !FormSelectedGenres.Any(s => string.Equals(s.Name, FormGenre, StringComparison.Ordinal)))
                {
                    var info = GenreOptions.FirstOrDefault(g => string.Equals(g.Name, FormGenre, StringComparison.OrdinalIgnoreCase));
                    if (info != null) FormSelectedGenres.Add(info);
                }
            }
        }
    }
}
