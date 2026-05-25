using System.Reflection;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class BookAnalysisViewModel : DataManagementViewModelBase<BookAnalysisData, BookAnalysisCategory, BookAnalysisService>
    {
        private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        private readonly IPromptRepository _promptRepository;
        private readonly NovelCrawlerService _crawlerService;
        private readonly EssenceChapterSelectionService _essenceChapterSelectionService;

        public BookAnalysisViewModel(IPromptRepository promptRepository, NovelCrawlerService crawlerService, EssenceChapterSelectionService essenceChapterSelectionService)
        {
            _promptRepository = promptRepository;
            _crawlerService = crawlerService;
            _essenceChapterSelectionService = essenceChapterSelectionService;
            LoadUrlHistory();

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(IsCrawling) or nameof(IsAIGenerating) or nameof(IsBatchGenerating))
                {
                    OnPropertyChanged(nameof(IsWebViewHidden));
                    OnPropertyChanged(nameof(IsWebViewVisible));
                }
                if (e.PropertyName == nameof(IsBatchCancelRequested) && IsBatchCancelRequested && IsCrawling)
                {
                    CancelCrawl();
                }
            };
        }

    }
}
