using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Modules.Design.SmartParsing.BookAnalysis.Models;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    public partial class BookAnalysisViewModel
    {
        private static readonly System.Text.RegularExpressions.Regex _chapterPattern = new(
            @"^(第[零一二三四五六七八九十百千万\d]+[章节卷][^\n]*)",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);
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
                                        Icon = "Icon.Book",
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
                                    RefreshTreeAndCategorySelection();
                                    ApplyCategorySelection(data.Category);

                                    _currentEditingData = data;
                                    _currentEditingCategory = null;
                                    LoadDataToForm(data);
                                    EnterEditMode();
                                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();

                                    var dataToFocus = data;
                                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                                    {
                                        FocusOnDataItem(dataToFocus);
                                    }, System.Windows.Threading.DispatcherPriority.Loaded);
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
                GlobalToast.Error("提取失败", $"提取失败：{ex.Message}");
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

            _crawlCts?.Dispose();
            _crawlCts = new System.Threading.CancellationTokenSource();
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
                    _crawlCts?.Dispose(); _crawlCts = null;
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

                const int targetCount = 10;

                CrawlStatus = "正在选择精华章...";
                CrawlStatusMessage = "正在调用AI选择精华章...";
                var selection = await _essenceChapterSelectionService.SelectEssenceChaptersAsync(
                    titleForAi,
                    authorForAi,
                    chapters,
                    targetCount: targetCount,
                    skipVipChapters: true,
                    ct: _crawlCts.Token);

                if (_crawlCts.IsCancellationRequested)
                {
                    CrawlStatus = "已取消";
                    IsCrawling = false;
                    _crawlCts?.Dispose(); _crawlCts = null;
                    _extractBookInfoCommand?.RaiseCanExecuteChanged();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    GlobalToast.Info("已取消", "AI选章已取消");
                    return;
                }

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
                        _crawlCts?.Dispose(); _crawlCts = null;
                        _extractBookInfoCommand?.RaiseCanExecuteChanged();
                        _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                        GlobalToast.Warning("提示", "未找到可抓取章节（可能全部为VIP章节）");
                        return;
                    }

                    CrawlStatus = $"精华章选择失败，回退 {_extractedChapters.Count} 章";
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
                    _crawlCts?.Dispose(); _crawlCts = null;
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
            catch (OperationCanceledException)
            {
                CrawlStatus = "已取消";
                IsCrawling = false;
                _crawlCts?.Dispose(); _crawlCts = null;
                _extractBookInfoCommand?.RaiseCanExecuteChanged();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                GlobalToast.Info("已取消", "AI选章已取消");
            }
            catch (Exception ex)
            {
                CrawlStatus = "获取精华章失败";
                IsCrawling = false;
                _crawlCts?.Dispose(); _crawlCts = null;
                _extractBookInfoCommand?.RaiseCanExecuteChanged();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                GlobalToast.Error("获取失败", $"获取失败：{ex.Message}");
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
                GlobalToast.Error("识别失败", $"识别失败：{ex.Message}");
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

            _crawlCts ??= new System.Threading.CancellationTokenSource();
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
                        CrawlStatus = "抓取失败";
                        GlobalToast.Warning("提示", $"抓取失败：{result.ErrorMessage ?? "未知原因"}");
                        TM.App.Log($"[BookAnalysisViewModel] 抓取失败: {result.ErrorMessage}");
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
                GlobalToast.Error("抓取失败", $"抓取失败：{ex.Message}");
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
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
                return false;
            }
        }

        private void DisplayCrawledPreviewFromContent(CrawledContent content)
        {
            try
            {
                SelectedChapter = null;
                SelectedChapterContent = string.Empty;

                ChapterList.ReplaceAll(content.Chapters.OrderBy(c => c.Index).Select(chapter => new Crawler.ChapterContent
                {
                    Index = chapter.Index,
                    Title = chapter.Title,
                    FileName = chapter.FileName ?? string.Empty,
                    WordCount = chapter.WordCount,
                    Url = chapter.Url,
                    Content = chapter.Content ?? string.Empty
                }).ToList());
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

    }
}

