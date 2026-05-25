using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Modules.Design.Templates.CreativeMaterials;
using TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ShortStoryBlueprintViewModel : DataManagementViewModelBase<ShortStoryBlueprintData, ShortStoryBlueprintCategory, ShortStoryBlueprintService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly PanelCommunicationService _comm;
        private readonly BookAnalysisService _bookAnalysisService;
        private readonly SpecLoader _specLoader;

        protected override string DefaultDataIcon => "Icon.Book";
        protected override string NewItemTypeName => "蓝图";

        public ShortStoryBlueprintViewModel(IPromptRepository promptRepository, PanelCommunicationService comm, BookAnalysisService bookAnalysisService, SpecLoader specLoader)
        {
            _promptRepository = promptRepository;
            _comm = comm;
            _bookAnalysisService = bookAnalysisService;
            _specLoader = specLoader;
        }

        protected override void OnAfterInitializeRefresh()
        {
            LoadBookOptions();
        }

        public void RefreshBookOptions() => LoadBookOptions();

        private void LoadBookOptions()
        {
            var analyses = _bookAnalysisService.GetAllAnalysis();
            BookOptions = analyses
                .Select(b => new BookAnalysisOption { Id = b.Id, Name = b.Name, Author = b.Author, Genre = b.Genre })
                .ToList();

            GenreOptions = LoadGenresFromSpec();
        }

        private void UpdateSourceBookName()
        {
            var book = BookOptions.FirstOrDefault(b => b.Id == FormBookAnalysisId);
            FormSourceBookName = book?.Name ?? string.Empty;

            if (!_genreManuallySet)
            {
                var genre = book?.Genre;
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    _suppressGenreManualMark = true;
                    FormGenre = genre;
                    _suppressGenreManualMark = false;
                }
            }
        }

        private List<GenreInfo> LoadGenresFromSpec()
        {
            try
            {
                var specTemplates = _promptRepository.GetAllTemplates()
                    .Where(t => t.Tags != null && t.Tags.Contains("Spec") && !string.IsNullOrWhiteSpace(t.Category))
                    .ToList();
                return specTemplates.Select(t => new GenreInfo
                {
                    Name = t.Category,
                    Icon = TM.Framework.Common.Helpers.IconHelper.TryGet(t.Icon),
                    Description = string.Empty,
                }).ToList();
            }
            catch
            {
                return new List<GenreInfo>();
            }
        }

    }
}
