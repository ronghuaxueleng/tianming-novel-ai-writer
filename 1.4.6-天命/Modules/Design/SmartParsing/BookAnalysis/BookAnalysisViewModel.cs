using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Modules.Design.SmartParsing.BookAnalysis.Models;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class BookAnalysisViewModel : DataManagementViewModelBase<BookAnalysisData, BookAnalysisCategory, BookAnalysisService>
    {
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
                }
            };
        }

        private string _formName = string.Empty;
        private string _formIcon = "📖";
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
            targetCount = Math.Max(12, targetCount);

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

        private string _currentUrl = "http://www.shuquta.com/";
        private string _crawlStatus = "未抓取";
        private bool _isCrawling;
        private string _crawlStatusMessage = string.Empty;
        private string _crawlProgressText = string.Empty;
        private double _crawlProgressPercent;
        private System.Threading.CancellationTokenSource? _crawlCts;
        private List<Crawler.ChapterInfo> _extractedChapters = new();

        public string CurrentUrl { get => _currentUrl; set { _currentUrl = value; OnPropertyChanged(); } }
        public string CrawlStatus { get => _crawlStatus; set { _crawlStatus = value; OnPropertyChanged(); } }
        public bool IsCrawling { get => _isCrawling; set { _isCrawling = value; OnPropertyChanged(); } }

        public bool IsWebViewHidden => IsCrawling || IsAIGenerating || IsBatchGenerating;
        public string CrawlStatusMessage { get => _crawlStatusMessage; set { _crawlStatusMessage = value; OnPropertyChanged(); } }
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

        private bool CanExecuteExtractBookInfo()
        {
            return !IsCrawling && _webCrawlerService != null;
        }

        private async Task ExtractBookInfoAsync()
        {
            if (_webCrawlerService == null)
            {
                GlobalToast.Warning("提示", "爬虫服务未初始化");
                return;
            }

            try
            {
                CrawlStatus = "正在提取书籍信息...";

                var (title, author, genre, tags) = await _webCrawlerService.ExtractBookInfoAsync();
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author))
                {
                    CrawlStatus = "未提取到书名";
                    GlobalToast.Warning("提示", "未提取到书名/作者，请确认当前页面是书籍页或目录页");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    SourceBookTitle = title;

                    if (string.IsNullOrWhiteSpace(FormName))
                    {
                        FormName = title;
                    }

                    try
                    {
                        var dataName = title.Trim();
                        if (!string.IsNullOrWhiteSpace(dataName))
                        {
                            var existing = Service.GetAllAnalysis()
                                .FirstOrDefault(d => string.Equals(d.Name, dataName, StringComparison.Ordinal));

                            if (existing == null)
                            {
                                var targetCategoryName = _currentEditingCategory?.Name
                                    ?? _currentEditingData?.Category;

                                if (string.IsNullOrWhiteSpace(targetCategoryName))
                                {
                                    GlobalToast.Info("已提取书名", $"书名：{dataName}\n请先在左侧选中一个分类，再提取书名即可自动创建");
                                    return;
                                }

                                var confirm = StandardDialog.ShowConfirm(
                                    $"已提取书名：{dataName}\n\n是否在『{targetCategoryName}』中自动创建同名书籍分析数据？\n（后续爬取/AI分析都会归档到该数据项下）",
                                    "创建数据确认");

                                if (confirm)
                                {
                                    var data = new BookAnalysisData
                                    {
                                        Id = ShortIdGenerator.New("D"),
                                        Name = dataName,
                                        Category = targetCategoryName,
                                        Icon = "📖",
                                        IsEnabled = true,
                                        Author = author,
                                        Genre = genre,
                                        SourceUrl = CurrentUrl,
                                        SourceBookTitle = title,
                                        SourceAuthor = author,
                                        SourceGenre = genre,
                                        SourceKeywords = tags
                                    };

                                    Service.AddAnalysis(data);
                                    _ = ServiceLocator.Get<IWorkScopeService>().SetCurrentScopeAsync(data.Id);
                                    RefreshTreeAndCategorySelection();
                                    ApplyCategorySelection(data.Category);

                                    _currentEditingData = data;
                                    _currentEditingCategory = null;
                                    LoadDataToForm(data);
                                    EnterEditMode();
                                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                                    FocusOnDataItem(data);
                                }
                            }
                            else
                            {
                                _currentEditingData = existing;
                                _currentEditingCategory = null;
                                LoadDataToForm(existing);
                                EnterEditMode();
                                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                                ApplyCategorySelection(existing.Category);
                                FocusOnDataItem(existing);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BookAnalysisViewModel] 自动创建数据失败: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(author))
                {
                    SourceAuthor = author;
                }

                if (!string.IsNullOrWhiteSpace(genre))
                {
                    SourceGenre = genre;
                }

                if (!string.IsNullOrWhiteSpace(tags))
                {
                    SourceKeywords = tags;
                }

                CrawlStatus = "已提取书籍信息";
                GlobalToast.Success("提取完成", $"书名：{SourceBookTitle}，作者：{SourceAuthor}");
            }
            catch (Exception ex)
            {
                CrawlStatus = "提取失败";
                GlobalToast.Error("错误", ex.Message);
                TM.App.Log($"[BookAnalysisViewModel] 提取书籍信息失败: {ex.Message}");
            }
        }

        private bool CanExecuteGetEssenceChapters()
        {
            return !IsCrawling
                   && _webCrawlerService != null
                   && _currentEditingData != null
                   && !string.IsNullOrWhiteSpace(_currentEditingData.Id);
        }

        private async Task GetEssenceChaptersAsync()
        {
            if (_webCrawlerService == null)
            {
                GlobalToast.Warning("提示", "爬虫服务未初始化");
                return;
            }

            if (_currentEditingData == null || string.IsNullOrWhiteSpace(_currentEditingData.Id))
            {
                GlobalToast.Warning("提示", "请先创建或选择书籍分析");
                return;
            }

            IsCrawling = true;
            _extractBookInfoCommand?.RaiseCanExecuteChanged();
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            CrawlStatusMessage = "正在识别章节...";
            CrawlProgressText = string.Empty;
            CrawlProgressPercent = 0;

            try
            {
                CrawlStatus = "正在识别章节...";
                var chapters = await _webCrawlerService.ExtractChapterListAsync();
                if (chapters.Count == 0)
                {
                    CrawlStatus = "未识别到章节";
                    IsCrawling = false;
                    _extractBookInfoCommand?.RaiseCanExecuteChanged();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    GlobalToast.Warning("提示", "未识别到章节目录，请确认当前页面是书籍目录页");
                    return;
                }

                CrawlStatus = $"已识别 {chapters.Count} 章";
                CrawlStatusMessage = $"已识别 {chapters.Count} 章，正在提取书籍信息...";

                var (pageTitle, pageAuthor, pageGenre, pageTags) = await _webCrawlerService.ExtractBookInfoAsync();
                if (!string.IsNullOrWhiteSpace(pageTitle) && string.IsNullOrWhiteSpace(SourceBookTitle))
                {
                    SourceBookTitle = pageTitle;
                    if (string.IsNullOrWhiteSpace(FormName))
                    {
                        FormName = pageTitle;
                    }
                }

                if (!string.IsNullOrWhiteSpace(pageAuthor) && string.IsNullOrWhiteSpace(SourceAuthor))
                {
                    SourceAuthor = pageAuthor;
                }

                if (!string.IsNullOrWhiteSpace(pageGenre) && string.IsNullOrWhiteSpace(SourceGenre))
                {
                    SourceGenre = pageGenre;
                }

                if (!string.IsNullOrWhiteSpace(pageTags) && string.IsNullOrWhiteSpace(SourceKeywords))
                {
                    SourceKeywords = pageTags;
                }

                var titleForAi = string.IsNullOrWhiteSpace(SourceBookTitle) ? pageTitle : SourceBookTitle;
                var authorForAi = string.IsNullOrWhiteSpace(SourceAuthor) ? pageAuthor : SourceAuthor;

                const int targetCount = 12;

                CrawlStatus = "正在选择精华章...";
                CrawlStatusMessage = "正在调用AI选择精华章...";
                var selection = await _essenceChapterSelectionService.SelectEssenceChaptersAsync(
                    titleForAi,
                    authorForAi,
                    chapters,
                    targetCount: targetCount,
                    skipVipChapters: true,
                    ct: _crawlCts?.Token ?? System.Threading.CancellationToken.None);

                if (!selection.Success)
                {
                    TM.App.Log($"[BookAnalysisViewModel] 精华章选择失败，回退A策略: {selection.ErrorMessage}");

                    _extractedChapters = BuildEssenceChaptersAPlusB(chapters, aiSelected: null, targetCount: targetCount, out _lastGoldenIndexes, out _lastAnchorIndexes);
                    _lastReasonsByIndex = new Dictionary<int, string>();
                    _lastRawAiContent = string.Empty;
                    _lastEssenceStrategy = "A+B:fallback-A-only";

                    if (_extractedChapters.Count == 0)
                    {
                        CrawlStatus = "无可抓取章节";
                        IsCrawling = false;
                        _extractBookInfoCommand?.RaiseCanExecuteChanged();
                        _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                        GlobalToast.Warning("提示", "未找到可抓取章节（可能全部为VIP章节）");
                        return;
                    }

                    CrawlStatus = $"精华章选择失败，回退 { _extractedChapters.Count } 章";
                    CrawlStatusMessage = $"回退抓取 {_extractedChapters.Count} 章...";
                    GlobalToast.Warning("提示", $"精华章选择失败，已回退抓取 {_extractedChapters.Count} 章");

                    await _crawlerService.SaveEssenceChapterSelectionAsync(
                        _currentEditingData.Id,
                        titleForAi,
                        authorForAi,
                        _extractedChapters.Select(c => c.Index).ToList(),
                        targetCount: targetCount,
                        strategy: "A+B:fallback-A-only",
                        goldenIndexes: _lastGoldenIndexes,
                        anchorIndexes: _lastAnchorIndexes,
                        reasonsByIndex: _lastReasonsByIndex,
                        rawAiContent: _lastRawAiContent);

                    await StartCrawlAsync(new Crawler.CrawlOptions
                    {
                        Mode = Crawler.CrawlMode.All,
                        SkipVipChapters = true,
                        MinDelayMs = 1000,
                        MaxDelayMs = 3000
                    });

                    return;
                }

                _extractedChapters = BuildEssenceChaptersAPlusB(chapters, selection.SelectedChapters, targetCount, out _lastGoldenIndexes, out _lastAnchorIndexes);
                _lastReasonsByIndex = selection.ReasonsByIndex ?? new Dictionary<int, string>();
                _lastRawAiContent = selection.RawAiContent ?? string.Empty;
                _lastEssenceStrategy = $"A+B:golden3+anchors+{selection.Strategy}";
                if (_extractedChapters.Count == 0)
                {
                    CrawlStatus = "无可抓取章节";
                    IsCrawling = false;
                    _extractBookInfoCommand?.RaiseCanExecuteChanged();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    GlobalToast.Warning("提示", "未找到可抓取章节（可能全部为VIP章节）");
                    return;
                }
                CrawlStatus = $"已选出 {_extractedChapters.Count} 章";
                CrawlStatusMessage = $"已选出 {_extractedChapters.Count} 章，准备抓取...";
                GlobalToast.Success("精华章已选出", $"已选出 {_extractedChapters.Count} 章");

                await _crawlerService.SaveEssenceChapterSelectionAsync(
                    _currentEditingData.Id,
                    titleForAi,
                    authorForAi,
                    _extractedChapters.Select(c => c.Index).ToList(),
                    targetCount: targetCount,
                    strategy: _lastEssenceStrategy,
                    goldenIndexes: _lastGoldenIndexes,
                    anchorIndexes: _lastAnchorIndexes,
                    reasonsByIndex: _lastReasonsByIndex,
                    rawAiContent: _lastRawAiContent);

                await StartCrawlAsync(new Crawler.CrawlOptions
                {
                    Mode = Crawler.CrawlMode.All,
                    SkipVipChapters = true,
                    MinDelayMs = 1000,
                    MaxDelayMs = 3000
                });
            }
            catch (Exception ex)
            {
                CrawlStatus = "获取精华章失败";
                IsCrawling = false;
                _extractBookInfoCommand?.RaiseCanExecuteChanged();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                GlobalToast.Error("错误", ex.Message);
                TM.App.Log($"[BookAnalysisViewModel] 获取精华章失败: {ex.Message}");
            }
        }

        private async Task CrawlCurrentPageAsync()
        {
            if (_webCrawlerService == null)
            {
                GlobalToast.Warning("提示", "爬虫服务未初始化");
                return;
            }

            try
            {
                CrawlStatus = "正在识别章节...";
                _extractedChapters = await _webCrawlerService.ExtractChapterListAsync();

                if (_extractedChapters.Count == 0)
                {
                    CrawlStatus = "未识别到章节";
                    GlobalToast.Warning("提示", "未识别到章节目录，请确认当前页面是书籍目录页");
                    return;
                }

                CrawlStatus = $"已识别 {_extractedChapters.Count} 章";
                GlobalToast.Success("识别完成", $"已识别到 {_extractedChapters.Count} 个章节");

                await ShowCrawlOptionsAndStartAsync();
            }
            catch (Exception ex)
            {
                CrawlStatus = "识别失败";
                GlobalToast.Error("错误", ex.Message);
                TM.App.Log($"[BookAnalysisViewModel] 章节识别失败: {ex.Message}");
            }
        }

        private async Task CrawlWholeBookAsync()
        {
            if (_webCrawlerService == null)
            {
                GlobalToast.Warning("提示", "爬虫服务未初始化");
                return;
            }

            if (_extractedChapters.Count == 0)
            {
                await CrawlCurrentPageAsync();
                if (_extractedChapters.Count == 0) return;
            }
            else
            {
                await ShowCrawlOptionsAndStartAsync();
            }
        }

        private async Task ShowCrawlOptionsAndStartAsync()
        {
            var options = Dialogs.CrawlOptionsDialog.Show(null, _extractedChapters);

            if (options == null)
            {
                TM.App.Log("[BookAnalysisViewModel] 用户取消抓取");
                return;
            }

            await StartCrawlAsync(options);
        }

        private async Task StartCrawlAsync(Crawler.CrawlOptions options)
        {
            if (_webCrawlerService == null) return;

            var filtered = options.SkipVipChapters
                ? _extractedChapters.Where(c => !c.IsVip)
                : _extractedChapters.AsEnumerable();

            var filteredList = filtered.ToList();
            var expectedCount = options.Mode switch
            {
                Crawler.CrawlMode.FirstN => Math.Min(options.FirstNCount, filteredList.Count),
                Crawler.CrawlMode.Range => filteredList.Count(c => c.Index >= options.RangeStart && c.Index <= options.RangeEnd),
                _ => filteredList.Count
            };

            _crawlCts = new System.Threading.CancellationTokenSource();
            IsCrawling = true;
            _extractBookInfoCommand?.RaiseCanExecuteChanged();
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            CrawlStatusMessage = "正在抓取...";

            var progress = new Progress<Crawler.CrawlProgress>(p =>
            {
                CrawlProgressPercent = p.Percentage;
                CrawlProgressText = $"{p.Current}/{p.Total} - {p.CurrentChapter}";
                CrawlStatusMessage = p.StatusMessage;
            });

            try
            {
                var result = await _webCrawlerService.CrawlChaptersAsync(
                    _extractedChapters, options, progress, _crawlCts.Token);

                var isCanceled = (!string.IsNullOrWhiteSpace(result.ErrorMessage) && result.ErrorMessage.Contains("用户取消"))
                                 || (_crawlCts.IsCancellationRequested && result.Chapters.Count < expectedCount);

                if (result.Success)
                {
                    SourceBookTitle = result.BookTitle;
                    SourceAuthor = result.Author;
                    ChapterCount = result.Chapters.Count;
                    TotalWordCount = result.TotalWords;
                    CrawledAt = result.CrawlTime;

                    var saved = false;
                    CrawledContent? crawled = null;
                    if (!isCanceled)
                    {
                        if (_currentEditingData != null && !string.IsNullOrWhiteSpace(_currentEditingData.Id))
                        {
                            crawled = new CrawledContent
                            {
                                BookId = _currentEditingData.Id,
                                BookTitle = result.BookTitle,
                                Author = result.Author,
                                TotalChapters = result.Chapters.Count,
                                TotalWords = result.TotalWords,
                                CrawledAt = result.CrawlTime,
                                SourceUrl = result.SourceUrl,
                                SourceSite = !string.IsNullOrEmpty(result.SourceUrl) && Uri.TryCreate(result.SourceUrl, UriKind.Absolute, out var uri)
                                    ? uri.Host
                                    : string.Empty
                            };

                            foreach (var chapter in result.Chapters)
                            {
                                crawled.Chapters.Add(new CrawledChapter
                                {
                                    Index = chapter.Index,
                                    Title = chapter.Title,
                                    Content = chapter.Content,
                                    WordCount = chapter.WordCount,
                                    Url = chapter.Url
                                });
                            }

                            DisplayCrawledPreviewFromContent(crawled);
                            saved = await SaveCrawledContentAsync(_currentEditingData.Id, crawled);
                        }
                        else
                        {
                            TM.App.Log("[BookAnalysisViewModel] 当前数据未持久化，跳过爬取结果存储");
                        }
                    }

                    CrawlStatus = isCanceled
                        ? $"已取消（已抓取 {result.Chapters.Count} 章，未保存）"
                        : saved
                            ? $"已抓取 {result.Chapters.Count} 章（已保存）"
                            : $"已抓取 {result.Chapters.Count} 章（未保存）";

                    if (isCanceled)
                    {
                        GlobalToast.Info("已取消", $"已抓取 {result.Chapters.Count} 章（未保存）");
                    }
                    else
                    {
                        if (saved)
                        {
                            GlobalToast.Success("抓取完成", $"成功抓取 {result.Chapters.Count} 章，共 {result.TotalWords} 字（已自动保存）");
                        }
                        else
                        {
                            GlobalToast.Warning("抓取完成", $"成功抓取 {result.Chapters.Count} 章，共 {result.TotalWords} 字（未保存）");
                        }
                    }
                }
                else
                {
                    if (isCanceled)
                    {
                        CrawlStatus = "已取消";
                        GlobalToast.Info("已取消", "未保存章节");
                    }
                    else
                    {
                        CrawlStatus = result.ErrorMessage ?? "抓取失败";
                        GlobalToast.Warning("提示", result.ErrorMessage ?? "抓取失败");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CrawlStatus = "已取消";
                GlobalToast.Info("提示", "抓取已取消");
            }
            catch (Exception ex)
            {
                CrawlStatus = "抓取出错";
                GlobalToast.Error("错误", ex.Message);
                TM.App.Log($"[BookAnalysisViewModel] 抓取失败: {ex.Message}");
            }
            finally
            {
                IsCrawling = false;
                _extractBookInfoCommand?.RaiseCanExecuteChanged();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                _crawlCts?.Dispose();
                _crawlCts = null;
            }
        }

        private async Task<bool> SaveCrawledContentAsync(string bookId, CrawledContent crawled)
        {
            if (string.IsNullOrWhiteSpace(bookId) || crawled == null)
            {
                return false;
            }

            try
            {
                CrawlStatus = "正在保存...";
                await _crawlerService.SaveCrawledContentAsync(bookId, crawled);

                try
                {
                    var blueprint = new Models.StructureBlueprint
                    {
                        BookId = bookId,
                        BookTitle = crawled.BookTitle,
                        Author = crawled.Author,
                        CreatedAt = DateTime.Now,
                        Strategy = _lastEssenceStrategy,
                        TargetCount = Math.Max(12, _lastGoldenIndexes.Count + _lastAnchorIndexes.Count),
                        GoldenIndexes = _lastGoldenIndexes
                            .Where(i => i > 0)
                            .Distinct()
                            .OrderBy(i => i)
                            .ToList(),
                        AnchorIndexes = _lastAnchorIndexes
                            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                            .GroupBy(kv => kv.Key)
                            .ToDictionary(g => g.Key, g => g.First().Value),
                        SelectedIndexes = crawled.Chapters
                            .Select(c => c.Index)
                            .Where(i => i > 0)
                            .Distinct()
                            .OrderBy(i => i)
                            .ToList(),
                        ReasonsByIndex = _lastReasonsByIndex
                            .Where(kv => kv.Key > 0 && !string.IsNullOrWhiteSpace(kv.Value))
                            .GroupBy(kv => kv.Key)
                            .ToDictionary(g => g.Key, g => g.First().Value),
                        RawAiContent = _lastRawAiContent ?? string.Empty,
                        TotalChapters = crawled.TotalChapters,
                        TotalWords = crawled.TotalWords
                    };

                    await _crawlerService.SaveStructureBlueprintAsync(bookId, blueprint);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisViewModel] 保存结构蓝图失败: {ex.Message}");
                }

                CrawlStatus = "已保存";
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 自动保存爬取内容失败: {ex.Message}");
                CrawlStatus = "保存失败";
                GlobalToast.Error("保存失败", ex.Message);
                return false;
            }
        }

        private void DisplayCrawledPreviewFromContent(CrawledContent content)
        {
            try
            {
                ChapterList.Clear();
                SelectedChapter = null;
                SelectedChapterContent = string.Empty;

                foreach (var chapter in content.Chapters.OrderBy(c => c.Index))
                {
                    ChapterList.Add(new Crawler.ChapterContent
                    {
                        Index = chapter.Index,
                        Title = chapter.Title,
                        FileName = chapter.FileName ?? string.Empty,
                        WordCount = chapter.WordCount,
                        Url = chapter.Url,
                        Content = chapter.Content ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 刷新章节预览失败: {ex.Message}");
            }
        }

        private void CancelCrawl()
        {
            _crawlCts?.Cancel();
            CrawlStatusMessage = "正在取消...";
        }

        private static readonly string _urlHistoryPath = StoragePathHelper.GetFilePath(
            "Modules",
            "Design/SmartParsing/BookAnalysis",
            "url_history.json");

        private static readonly string[] DefaultNovelSites =
        {
            "http://www.shuquta.com/",
            "http://www.xheiyan.info/",
            "https://m.bqgde.de/",
        };

        public System.Collections.ObjectModel.ObservableCollection<string> UrlHistory { get; } = new();

        public event Action<string>? NavigateRequested;

        private ICommand? _deleteUrlCommand;
        public ICommand DeleteUrlCommand => _deleteUrlCommand ??= new RelayCommand(param =>
        {
            if (param is string url && UrlHistory.Contains(url))
            {
                UrlHistory.Remove(url);
                SaveUrlHistory();
            }
        });

        private void LoadUrlHistory()
        {
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                List<string> urls = new();
                string? error = null;

                try
                {
                    if (System.IO.File.Exists(_urlHistoryPath))
                    {
                        var json = System.IO.File.ReadAllText(_urlHistoryPath);
                        urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                return () =>
                {
                    try
                    {
                        UrlHistory.Clear();

                        if (urls.Count > 0)
                        {
                            foreach (var url in urls)
                            {
                                if (IsAllowedNovelUrl(url))
                                {
                                    UrlHistory.Add(url);
                                }
                            }
                            TM.App.Log($"[BookAnalysisViewModel] 已加载 {urls.Count} 条URL历史记录");
                        }

                        foreach (var url in DefaultNovelSites)
                        {
                            if (!UrlHistory.Contains(url))
                            {
                                UrlHistory.Add(url);
                            }
                        }

                        const string defaultUrl = "http://www.shuquta.com/";
                        if (UrlHistory.Contains(defaultUrl))
                        {
                            UrlHistory.Remove(defaultUrl);
                        }
                        UrlHistory.Insert(0, defaultUrl);

                        if (!string.IsNullOrWhiteSpace(UrlHistory.FirstOrDefault()))
                        {
                            CurrentUrl = UrlHistory[0];
                        }

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            TM.App.Log($"[BookAnalysisViewModel] 加载URL历史记录失败: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BookAnalysisViewModel] 加载URL历史记录失败: {ex.Message}");
                    }
                };
            }, "BookAnalysis.UrlHistory");
        }

        private static bool IsAllowedNovelUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host?.ToLowerInvariant() ?? string.Empty;
            return host == "www.shuquta.com" || host == "shuquta.com" ||
                   host == "www.xheiyan.info" || host == "xheiyan.info" ||
                   host == "m.bqgde.de" || host == "www.bqgde.de" || host == "bqgde.de" ||
                   host == "m.bqg78.com" || host == "www.bqg78.com" || host == "bqg78.com";
        }

        private async void SaveUrlHistory()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_urlHistoryPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(UrlHistory.ToList(), options);
                var tmpBav = _urlHistoryPath + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmpBav, json);
                System.IO.File.Move(tmpBav, _urlHistoryPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 保存URL历史失败: {ex.Message}");
            }
        }

        private void AddUrlToHistory(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (UrlHistory.Contains(url))
            {
                UrlHistory.Remove(url);
            }

            UrlHistory.Insert(0, url);

            while (UrlHistory.Count > 30)
            {
                UrlHistory.RemoveAt(UrlHistory.Count - 1);
            }

            SaveUrlHistory();
        }

        private System.Collections.ObjectModel.ObservableCollection<Crawler.ChapterContent> _chapterList = new();
        private Crawler.ChapterContent? _selectedChapter;
        private string _selectedChapterContent = string.Empty;

        public System.Collections.ObjectModel.ObservableCollection<Crawler.ChapterContent> ChapterList { get => _chapterList; set { _chapterList = value; OnPropertyChanged(); } }
        public Crawler.ChapterContent? SelectedChapter 
        { 
            get => _selectedChapter; 
            set 
            { 
                _selectedChapter = value; 
                OnPropertyChanged(); 
                _ = LoadSelectedChapterContentAsync();
            } 
        }
        public string SelectedChapterContent { get => _selectedChapterContent; set { _selectedChapterContent = value; OnPropertyChanged(); } }

        private async Task LoadSelectedChapterContentAsync()
        {
            if (_currentEditingData == null || SelectedChapter == null)
            {
                SelectedChapterContent = string.Empty;
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(SelectedChapter.Content))
                {
                    SelectedChapterContent = SelectedChapter.Content;
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedChapter.FileName))
                {
                    SelectedChapterContent = string.Empty;
                    return;
                }

                SelectedChapterContent = string.Empty;
                var text = await _crawlerService.LoadChapterContentAsync(_currentEditingData.Id, SelectedChapter.FileName);
                SelectedChapter.Content = text;
                SelectedChapterContent = text;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 加载章节内容失败: {ex.Message}");
                SelectedChapterContent = string.Empty;
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

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        public List<string> GenreOptions { get; } = new();

        public List<string> StatusOptions { get; } = new()
        {
            "已禁用", "已启用"
        };

        protected override string DefaultDataIcon => "📖";

        protected override int GetMaxCategoryCount() => 1;
        protected override int GetMaxDataCountPerCategory() => 1;
        protected override string GetCategoryLimitMessage()
            => "智能拆书仅支持系统内置唯一分类，不允许新建分类。";
        protected override string GetDataLimitMessage()
            => "当前拆书分类已有数据，请先删除旧数据，再创建新的拆书内容。";

        protected override BookAnalysisData? CreateNewData(string? categoryName = null)
        {
            return new BookAnalysisData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新书籍",
                Category = categoryName ?? string.Empty,
                Icon = DefaultDataIcon,
                IsEnabled = true,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems()
        {
            try
            {
                var all = Service.GetAllAnalysis();
                foreach (var item in all)
                {
                    if (!string.IsNullOrWhiteSpace(item.Id))
                    {
                        _crawlerService.DeleteCrawledContent(item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 清空前删除爬取内容失败: {ex.Message}");
            }

            return Service.ClearAllAnalysis();
        }

        protected override List<BookAnalysisCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<BookAnalysisData> GetAllDataItems() => Service.GetAllAnalysis();

        protected override string GetDataCategory(BookAnalysisData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(BookAnalysisData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = data.Icon,
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(BookAnalysisData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.Genre.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SourceBookTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SourceAuthor.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.SourceKeywords.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new AsyncRelayCommand(async param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: BookAnalysisData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);

                    OnDataItemLoaded();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();

                    if (!string.IsNullOrWhiteSpace(data.Id))
                    {
                        await ServiceLocator.Get<IWorkScopeService>().SetCurrentScopeAsync(data.Id);
                        await LoadCrawledContent(data.Id);
                    }
                }
                else if (param is TreeNodeItem { Tag: BookAnalysisCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(BookAnalysisData data)
        {
            FormName = data.Name;
            FormIcon = data.Icon;
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;
            FormAuthor = data.Author;
            FormGenre = data.Genre;
            FormSourceUrl = data.SourceUrl;
            SourceBookTitle = data.SourceBookTitle;
            SourceAuthor = data.SourceAuthor;
            SourceGenre = data.SourceGenre;
            SourceKeywords = data.SourceKeywords;
            SourceSite = data.SourceSite;
            ChapterCount = data.ChapterCount;
            TotalWordCount = data.TotalWordCount;
            CrawledAt = data.CrawledAt;
            FormWorldBuildingMethod = data.WorldBuildingMethod;
            FormPowerSystemDesign = data.PowerSystemDesign;
            FormEnvironmentDescription = data.EnvironmentDescription;
            FormFactionDesign = data.FactionDesign;
            FormWorldviewHighlights = data.WorldviewHighlights;
            FormProtagonistDesign = data.ProtagonistDesign;
            FormSupportingRoles = data.SupportingRoles;
            FormCharacterRelations = data.CharacterRelations;
            FormGoldenFingerDesign = data.GoldenFingerDesign;
            FormCharacterHighlights = data.CharacterHighlights;
            FormPlotStructure = data.PlotStructure;
            FormConflictDesign = data.ConflictDesign;
            FormClimaxArrangement = data.ClimaxArrangement;
            FormForeshadowingTechnique = data.ForeshadowingTechnique;
            FormPlotHighlights = data.PlotHighlights;
        }

        private async Task LoadCrawledContent(string dataId)
        {
            try
            {
                ChapterList.Clear();
                SelectedChapter = null;
                SelectedChapterContent = string.Empty;

                var content = await _crawlerService.LoadCrawledContentAsync(dataId);
                if (content != null)
                {
                    foreach (var chapter in content.Chapters)
                    {
                        ChapterList.Add(new Crawler.ChapterContent
                        {
                            Index = chapter.Index,
                            Title = chapter.Title,
                            FileName = chapter.FileName,
                            WordCount = chapter.WordCount,
                            Url = chapter.Url
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 加载爬取内容失败: {ex.Message}");
            }
        }

        private void LoadCategoryToForm(BookAnalysisCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.ParentCategory ?? string.Empty;
            ClearBusinessFields();
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = DefaultDataIcon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ClearBusinessFields();
        }

        private void ClearBusinessFields()
        {
            FormAuthor = string.Empty;
            FormGenre = string.Empty;
            FormSourceUrl = string.Empty;
            CurrentUrl = "http://www.shuquta.com/";
            CrawlStatus = "未抓取";
            SourceBookTitle = string.Empty;
            SourceAuthor = string.Empty;
            SourceGenre = string.Empty;
            SourceKeywords = string.Empty;
            SourceSite = string.Empty;
            ChapterCount = 0;
            TotalWordCount = 0;
            CrawledAt = null;
            ChapterList.Clear();
            SelectedChapter = null;
            SelectedChapterContent = string.Empty;
            FormWorldBuildingMethod = string.Empty;
            FormPowerSystemDesign = string.Empty;
            FormEnvironmentDescription = string.Empty;
            FormFactionDesign = string.Empty;
            FormWorldviewHighlights = string.Empty;
            FormProtagonistDesign = string.Empty;
            FormSupportingRoles = string.Empty;
            FormCharacterRelations = string.Empty;
            FormGoldenFingerDesign = string.Empty;
            FormCharacterHighlights = string.Empty;
            FormPlotStructure = string.Empty;
            FormConflictDesign = string.Empty;
            FormClimaxArrangement = string.Empty;
            FormForeshadowingTechnique = string.Empty;
            FormPlotHighlights = string.Empty;
        }

        protected override string NewItemTypeName => "书籍分析";
        private ICommand? _addCommand;
        public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
        {
            try
            {
                _currentEditingData = null;
                _currentEditingCategory = null;
                ResetForm();
                ExecuteAddWithCreateMode();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 新建失败: {ex.Message}");
                GlobalToast.Error("新建失败", ex.Message);
            }
        });

        private ICommand? _saveCommand;
        public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
        {
            try
            {
                ExecuteSaveWithCreateEditMode(
                    validateForm: ValidateFormCore,
                    createCategoryCore: CreateCategoryCore,
                    createDataCore: CreateAnalysisCore,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCore,
                    updateDataCore: UpdateAnalysisCore);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", ex.Message);
            }
        });

        private bool ValidateFormCore()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                GlobalToast.Warning("保存失败", "请输入书名");
                return false;
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或书籍分析");
                return false;
            }

            return true;
        }

        private void CreateCategoryCore()
        {
            var parentCategoryName = string.Empty;
            var level = 1;

            if (!string.IsNullOrWhiteSpace(FormCategory))
            {
                parentCategoryName = FormCategory;
                var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
                level = parentCategory != null ? parentCategory.Level + 1 : 1;
            }

            var categoryIcon = GetCategoryIconForSave(FormIcon);

            var newCategory = new BookAnalysisCategory
            {
                Id = ShortIdGenerator.New("C"),
                Name = FormName,
                Icon = categoryIcon,
                ParentCategory = parentCategoryName,
                Level = level,
                Order = Service.GetAllCategories().Count + 1
            };

            if (!Service.AddCategory(newCategory))
            {
                GlobalToast.Warning("创建失败", "分类名已存在，请改名");
                return;
            }

            string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
            GlobalToast.Success("保存成功", $"{levelDesc}『{newCategory.Name}』已创建");

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
        }

        private void CreateAnalysisCore()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            Service.AddAnalysis(newData);
            _currentEditingData = newData;
            _ = ServiceLocator.Get<IWorkScopeService>().SetCurrentScopeAsync(newData.Id);
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            GlobalToast.Success("保存成功", $"书籍分析『{newData.Name}』已创建");
        }

        private void UpdateCategoryCore()
        {
            if (_currentEditingCategory == null) return;

            var oldName = _currentEditingCategory.Name;
            _currentEditingCategory.Name = FormName;
            _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
            if (!Service.UpdateCategory(_currentEditingCategory))
            {
                _currentEditingCategory.Name = oldName;
                GlobalToast.Warning("保存失败", "分类名已存在，请改名");
                return;
            }
            GlobalToast.Success("保存成功", $"分类『{_currentEditingCategory.Name}』已更新");
        }

        private void UpdateAnalysisCore()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            Service.UpdateAnalysis(_currentEditingData);
            GlobalToast.Success("保存成功", $"书籍分析『{_currentEditingData.Name}』已更新");
        }

        private void UpdateDataFromForm(BookAnalysisData data)
        {
            var newIsEnabled = (FormStatus == "已启用");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckScopeBeforeEnable(data.SourceBookId, data.Name))
                {
                    FormStatus = "已禁用";
                    return;
                }
            }

            data.Name = FormName;
            data.Icon = GetDataIconForSave(FormIcon);
            data.Category = FormCategory;
            data.IsEnabled = newIsEnabled;
            data.ModifiedTime = DateTime.Now;
            data.Author = FormAuthor;
            data.Genre = FormGenre;
            data.SourceUrl = FormSourceUrl;
            data.SourceBookTitle = SourceBookTitle;
            data.SourceAuthor = SourceAuthor;
            data.SourceGenre = SourceGenre;
            data.SourceKeywords = SourceKeywords;
            data.SourceSite = SourceSite;
            data.ChapterCount = ChapterCount;
            data.TotalWordCount = TotalWordCount;
            data.CrawledAt = CrawledAt;
            data.WorldBuildingMethod = FormWorldBuildingMethod;
            data.PowerSystemDesign = FormPowerSystemDesign;
            data.EnvironmentDescription = FormEnvironmentDescription;
            data.FactionDesign = FormFactionDesign;
            data.WorldviewHighlights = FormWorldviewHighlights;
            data.ProtagonistDesign = FormProtagonistDesign;
            data.SupportingRoles = FormSupportingRoles;
            data.CharacterRelations = FormCharacterRelations;
            data.GoldenFingerDesign = FormGoldenFingerDesign;
            data.CharacterHighlights = FormCharacterHighlights;
            data.PlotStructure = FormPlotStructure;
            data.ConflictDesign = FormConflictDesign;
            data.ClimaxArrangement = FormClimaxArrangement;
            data.ForeshadowingTechnique = FormForeshadowingTechnique;
            data.PlotHighlights = FormPlotHighlights;
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                var targetCategory = _currentEditingCategory;
                var targetData = _currentEditingData;
                if (_ is TreeNodeItem node)
                {
                    if (node.Tag is BookAnalysisCategory category)
                    {
                        targetCategory = category;
                        targetData = null;
                    }
                    else if (node.Tag is BookAnalysisData data)
                    {
                        targetData = data;
                        targetCategory = null;
                    }
                }

                if (targetCategory != null)
                {
                    var allCategoriesToDelete = CollectCategoryAndChildrenNames(targetCategory.Name);

                    if (allCategoriesToDelete.Any(name => Service.IsCategoryBuiltIn(name)))
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除（含联动删除）。");
                        return;
                    }

                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分类『{targetCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有书籍分析也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    int totalCategoryDeleted = 0;
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        var dataInCategory = Service.GetAllAnalysis()
                            .Where(a => a.Category == categoryName)
                            .ToList();

                        foreach (var analysis in dataInCategory)
                        {
                            _crawlerService.DeleteCrawledContent(analysis.Id);
                            Service.DeleteAnalysis(analysis.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                        if (!Service.IsCategoryBuiltIn(categoryName))
                        {
                            totalCategoryDeleted++;
                        }
                    }

                    if (totalCategoryDeleted == 0)
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除。");
                        return;
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {totalCategoryDeleted} 个分类及其 {totalDataDeleted} 个书籍分析");

                    _currentEditingCategory = null;
                    ResetForm();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    RefreshTreeAndCategorySelection();
                }
                else if (targetData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除书籍分析『{targetData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    _crawlerService.DeleteCrawledContent(targetData.Id);
                    Service.DeleteAnalysis(targetData.Id);
                    GlobalToast.Success("删除成功", $"书籍分析『{targetData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或书籍分析");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        private ICommand? _navigateCommand;
        public ICommand NavigateCommand => _navigateCommand ??= new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(CurrentUrl))
            {
                GlobalToast.Warning("导航失败", "请输入网址");
                return;
            }

            AddUrlToHistory(CurrentUrl);

            NavigateRequested?.Invoke(CurrentUrl);
        });

        private TM.Framework.Common.ViewModels.AIGenerationConfig? _cachedConfig;
        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return _cachedConfig ??= new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "拆书分析师",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "分析书籍",
                ProgressMessage = "正在分析书籍内容，请稍候...",
                CompleteMessage = "AI已生成拆书分析结果，请查看并编辑",
                InputVariables = new()
                {
                    ["书名"] = () => FormName,
                    ["作者"] = () => SourceAuthor,
                    ["类型"] = () => SourceGenre,
                },
                OutputFields = new()
                {
                    ["世界构建手法"] = v => FormWorldBuildingMethod = v,
                    ["力量体系设计"] = v => FormPowerSystemDesign = v,
                    ["环境描写技巧"] = v => FormEnvironmentDescription = v,
                    ["势力设计技巧"] = v => FormFactionDesign = v,
                    ["世界观亮点"] = v => FormWorldviewHighlights = v,
                    ["主角塑造手法"] = v => FormProtagonistDesign = v,
                    ["配角设计技巧"] = v => FormSupportingRoles = v,
                    ["人物关系设计"] = v => FormCharacterRelations = v,
                    ["金手指设计"] = v => FormGoldenFingerDesign = v,
                    ["角色塑造亮点"] = v => FormCharacterHighlights = v,
                    ["情节结构技巧"] = v => FormPlotStructure = v,
                    ["冲突设计手法"] = v => FormConflictDesign = v,
                    ["高潮布局技巧"] = v => FormClimaxArrangement = v,
                    ["伏笔技巧"] = v => FormForeshadowingTechnique = v,
                    ["剧情设计亮点"] = v => FormPlotHighlights = v,
                },
                OutputFieldGetters = new()
                {
                    ["世界构建手法"] = () => FormWorldBuildingMethod,
                    ["力量体系设计"] = () => FormPowerSystemDesign,
                    ["环境描写技巧"] = () => FormEnvironmentDescription,
                    ["势力设计技巧"] = () => FormFactionDesign,
                    ["世界观亮点"] = () => FormWorldviewHighlights,
                    ["主角塑造手法"] = () => FormProtagonistDesign,
                    ["配角设计技巧"] = () => FormSupportingRoles,
                    ["人物关系设计"] = () => FormCharacterRelations,
                    ["金手指设计"] = () => FormGoldenFingerDesign,
                    ["角色塑造亮点"] = () => FormCharacterHighlights,
                    ["情节结构技巧"] = () => FormPlotStructure,
                    ["冲突设计手法"] = () => FormConflictDesign,
                    ["高潮布局技巧"] = () => FormClimaxArrangement,
                    ["伏笔技巧"] = () => FormForeshadowingTechnique,
                    ["剧情设计亮点"] = () => FormPlotHighlights,
                },
                FieldAliases = new()
                {
                    ["世界构建手法"] = new[] { "世界观构建", "世界设定" },
                },
                EnableKeywordExtract = false,
                ContextProvider = async () =>
                {
                    if (_currentEditingData == null) return string.Empty;
                    var excerpt = await _crawlerService.LoadCrawledExcerptAsync(
                        _currentEditingData.Id,
                        maxChapters: 12,
                        maxCharsPerChapter: int.MaxValue,
                        maxTotalChars: int.MaxValue);
                    if (!string.IsNullOrWhiteSpace(excerpt))
                    {
                        return $"<book_excerpt source=\"crawled\" type=\"essence\">\n{excerpt}\n</book_excerpt>";
                    }
                    return string.Empty;
                }
            };
        }

        protected override bool CanExecuteAIGenerate() => _currentEditingData != null;

        protected override bool SupportsBatch(TreeNodeItem categoryNode) => false;
    }
}
