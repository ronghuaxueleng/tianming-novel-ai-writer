using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    public partial class BookAnalysisViewModel
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

        private static List<Crawler.ChapterInfo> BuildEssenceChaptersAPlusB(
            IReadOnlyList<Crawler.ChapterInfo> chapters,
            IReadOnlyList<Crawler.ChapterInfo>? aiSelected,
            int targetCount,
            out List<int> goldenIndexes,
            out Dictionary<string, int> anchorIndexes)
        {
            targetCount = Math.Max(10, targetCount);

            var golden = new List<int>();
            var anchors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var candidates = chapters
                .Where(c => !c.IsVip)
                .OrderBy(c => c.Index)
                .ToList();

            if (candidates.Count == 0)
            {
                goldenIndexes = golden;
                anchorIndexes = anchors;
                return new List<Crawler.ChapterInfo>();
            }

            var mapByIndex = candidates
                .GroupBy(c => c.Index)
                .ToDictionary(g => g.Key, g => g.First());

            var selected = new List<Crawler.ChapterInfo>();
            var used = new HashSet<int>();

            var maxIndex = chapters.Count == 0 ? 0 : chapters.Max(c => c.Index);

            void AddByIndex(int index)
            {
                if (index <= 0) return;
                if (mapByIndex.TryGetValue(index, out var chapter) && used.Add(chapter.Index))
                {
                    selected.Add(chapter);
                }
            }

            AddByIndex(1);
            AddByIndex(2);
            AddByIndex(3);

            golden.AddRange(new[] { 1, 2, 3 });

            AddAnchor("p10", 0.10);
            AddAnchor("p50", 0.50);
            AddAnchor("p80", 0.80);

            var ending = PickEndingAnchor(candidates);
            if (ending != null && used.Add(ending.Index))
            {
                selected.Add(ending);
                anchors["ending"] = ending.Index;
            }

            if (aiSelected != null)
            {
                foreach (var ch in aiSelected.OrderBy(c => c.Index))
                {
                    if (selected.Count >= targetCount) break;
                    if (ch.IsVip) continue;
                    if (mapByIndex.TryGetValue(ch.Index, out var normalized) && used.Add(normalized.Index))
                    {
                        selected.Add(normalized);
                    }
                }
            }

            if (selected.Count < targetCount)
            {
                foreach (var ch in candidates)
                {
                    if (selected.Count >= targetCount) break;
                    if (used.Add(ch.Index))
                    {
                        selected.Add(ch);
                    }
                }
            }

            var result = selected
                .OrderBy(c => c.Index)
                .ToList();

            goldenIndexes = golden;
            anchorIndexes = anchors;
            return result;

            void AddAnchor(string key, double ratio)
            {
                if (ratio <= 0) return;
                if (ratio >= 1) return;

                if (maxIndex > 0)
                {
                    var desiredIndex = (int)Math.Round(maxIndex * ratio);
                    desiredIndex = Math.Max(1, Math.Min(maxIndex, desiredIndex));

                    var nearest = FindNearestNonVipByIndex(desiredIndex);
                    if (nearest != null && used.Add(nearest.Index))
                    {
                        selected.Add(nearest);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            anchors[key] = nearest.Index;
                        }
                        return;
                    }
                }

                if (candidates.Count == 0) return;

                var pos = (int)Math.Round((candidates.Count - 1) * ratio);
                pos = Math.Max(0, Math.Min(candidates.Count - 1, pos));
                var ch = candidates[pos];
                if (used.Add(ch.Index))
                {
                    selected.Add(ch);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        anchors[key] = ch.Index;
                    }
                }
            }

            Crawler.ChapterInfo? FindNearestNonVipByIndex(int desiredIndex)
            {
                if (candidates.Count == 0) return null;

                var best = candidates[0];
                var bestDistance = Math.Abs(best.Index - desiredIndex);
                for (var i = 1; i < candidates.Count; i++)
                {
                    var ch = candidates[i];
                    var dist = Math.Abs(ch.Index - desiredIndex);
                    if (dist < bestDistance)
                    {
                        best = ch;
                        bestDistance = dist;

                        if (bestDistance == 0)
                        {
                            break;
                        }
                    }
                    else if (ch.Index > desiredIndex && dist > bestDistance)
                    {
                        break;
                    }
                }

                return best;
            }

            static Crawler.ChapterInfo? PickEndingAnchor(IReadOnlyList<Crawler.ChapterInfo> nonVipChapters)
            {
                if (nonVipChapters == null || nonVipChapters.Count == 0) return null;

                var badKeywords = new[]
                {
                    "后记",
                    "完本",
                    "完结",
                    "完结感言",
                    "完本感言",
                    "感言",
                    "作者的话",
                    "作者有话说",
                    "公告",
                    "请假",
                    "新书",
                    "番外"
                };

                var start = Math.Max(0, nonVipChapters.Count - 5);
                for (var i = nonVipChapters.Count - 1; i >= start; i--)
                {
                    var ch = nonVipChapters[i];
                    var title = ch.Title ?? string.Empty;
                    var isBad = badKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && title.Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (!isBad)
                    {
                        return ch;
                    }
                }

                return nonVipChapters[^1];
            }
        }

        private static List<Crawler.ChapterInfo> BuildEssenceChaptersAPlusB(
            IReadOnlyList<Crawler.ChapterInfo> chapters,
            IReadOnlyList<Crawler.ChapterInfo>? aiSelected,
            int targetCount)
        {
            return BuildEssenceChaptersAPlusB(chapters, aiSelected, targetCount, out _, out _);
        }

        private List<int> _lastGoldenIndexes = new();
        private Dictionary<string, int> _lastAnchorIndexes = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, string> _lastReasonsByIndex = new();
        private string _lastRawAiContent = string.Empty;
        private string _lastEssenceStrategy = string.Empty;

        private string _formAuthor = string.Empty;
        private string _formGenre = string.Empty;
        private string _formSourceUrl = string.Empty;

        public string FormAuthor { get => _formAuthor; set { _formAuthor = value; OnPropertyChanged(); } }
        public string FormGenre { get => _formGenre; set { _formGenre = value; OnPropertyChanged(); } }
        public string FormSourceUrl { get => _formSourceUrl; set { _formSourceUrl = value; OnPropertyChanged(); } }

        private string _sourceBookTitle = string.Empty;
        private string _sourceAuthor = string.Empty;
        private string _sourceGenre = string.Empty;
        private string _sourceKeywords = string.Empty;
        private string _sourceSite = string.Empty;
        private int _chapterCount = 0;
        private int _totalWordCount = 0;
        private DateTime? _crawledAt;

        public string SourceBookTitle { get => _sourceBookTitle; set { _sourceBookTitle = value; OnPropertyChanged(); } }
        public string SourceAuthor { get => _sourceAuthor; set { _sourceAuthor = value; OnPropertyChanged(); } }
        public string SourceGenre { get => _sourceGenre; set { _sourceGenre = value; OnPropertyChanged(); } }
        public string SourceKeywords { get => _sourceKeywords; set { _sourceKeywords = value; OnPropertyChanged(); } }
        public string SourceSite { get => _sourceSite; set { _sourceSite = value; OnPropertyChanged(); } }
        public int ChapterCount { get => _chapterCount; set { _chapterCount = value; OnPropertyChanged(); } }
        public int TotalWordCount { get => _totalWordCount; set { _totalWordCount = value; OnPropertyChanged(); } }
        public DateTime? CrawledAt { get => _crawledAt; set { _crawledAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(CrawledAtDisplay)); } }
        public string CrawledAtDisplay => CrawledAt?.ToString("yyyy-MM-dd HH:mm") ?? "未爬取";

        private string _currentUrl = string.Empty;
        private string _crawlStatus = "未抓取";
        private bool _isCrawling;
        private string _crawlStatusMessage = string.Empty;
        private string _crawlProgressText = string.Empty;
        private double _crawlProgressPercent;
        private System.Threading.CancellationTokenSource? _crawlCts;
        private List<Crawler.ChapterInfo> _extractedChapters = new();

        public string CurrentUrl { get => _currentUrl; set { _currentUrl = value; OnPropertyChanged(); } }
        public string CrawlStatus { get => _crawlStatus; set { _crawlStatus = value; OnPropertyChanged(); } }
        public bool IsCrawling
        {
            get => _isCrawling;
            set
            {
                _isCrawling = value;
                OnPropertyChanged();
                if (value) IsBatchCancelRequested = false;
                IsBatchGenerating = value;
                if (!value) BatchProgressText = string.Empty;
            }
        }

        public bool IsWebViewHidden => IsCrawling || IsAIGenerating || IsBatchGenerating;

        private bool _isWebViewVisible = false;
        public bool IsWebViewVisible
        {
            get => _isWebViewVisible && !IsWebViewHidden;
            set { _isWebViewVisible = value; OnPropertyChanged(); }
        }
        public string CrawlStatusMessage
        {
            get => _crawlStatusMessage;
            set
            {
                _crawlStatusMessage = value;
                OnPropertyChanged();
                if (IsCrawling) BatchProgressText = value;
            }
        }
        public string CrawlProgressText { get => _crawlProgressText; set { _crawlProgressText = value; OnPropertyChanged(); } }
        public double CrawlProgressPercent { get => _crawlProgressPercent; set { _crawlProgressPercent = value; OnPropertyChanged(); } }

        private Crawler.WebCrawlerService? _webCrawlerService;
        public void SetWebCrawlerService(Crawler.WebCrawlerService service)
        {
            _webCrawlerService = service;
            _extractBookInfoCommand?.RaiseCanExecuteChanged();
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
        }

        private ICommand? _crawlCurrentPageCommand;
        public ICommand CrawlCurrentPageCommand => _crawlCurrentPageCommand ??= new AsyncRelayCommand(async () => await CrawlCurrentPageAsync());

        private ICommand? _crawlWholeBookCommand;
        public ICommand CrawlWholeBookCommand => _crawlWholeBookCommand ??= new AsyncRelayCommand(async () => await CrawlWholeBookAsync());

        private AsyncRelayCommand? _extractBookInfoCommand;
        public ICommand ExtractBookInfoCommand => _extractBookInfoCommand ??= new AsyncRelayCommand(async () => await ExtractBookInfoAsync(), CanExecuteExtractBookInfo);

        private AsyncRelayCommand? _getEssenceChaptersCommand;
        public ICommand GetEssenceChaptersCommand => _getEssenceChaptersCommand ??= new AsyncRelayCommand(async () => await GetEssenceChaptersAsync(), CanExecuteGetEssenceChapters);

        private ICommand? _cancelCrawlCommand;
        public ICommand CancelCrawlCommand => _cancelCrawlCommand ??= new RelayCommand(_ => CancelCrawl());

        private ICommand? _importLocalFileCommand;
        public ICommand ImportLocalFileCommand => _importLocalFileCommand ??= new AsyncRelayCommand(async () => await ImportLocalFileAsync());

        private bool CanExecuteExtractBookInfo()
        {
            return !IsCrawling && _webCrawlerService != null;
        }

    }
}
