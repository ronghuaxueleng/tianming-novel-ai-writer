using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public class WebCrawlerService
    {
        private readonly WebView2 _webView;
        private readonly Random _random = new();

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[WebCrawlerService] {key}: {ex.Message}");
        }

        public int PageLoadTimeout { get; set; } = 30;

        public WebCrawlerService(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        }

        #region 章节目录提取

        public async Task<List<ChapterInfo>> ExtractChapterListAsync()
        {
            try
            {
                var script = ContentExtractor.GetChapterListScript();
                var result = await _webView.ExecuteScriptAsync(script);

                var json = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrEmpty(json))
                {
                    TM.App.Log("[WebCrawlerService] 章节提取结果为空");
                    return new List<ChapterInfo>();
                }

                var chapters = JsonSerializer.Deserialize<List<ChapterInfo>>(json,
                    JsonHelper.Default);

                TM.App.Log($"[WebCrawlerService] 提取到 {chapters?.Count ?? 0} 个章节");
                if (chapters != null && chapters.Count > 0)
                {
                    foreach (var ch in chapters)
                        ch.Title = CleanChapterTitle(ch.Title);

                    for (int i = 0; i < Math.Min(3, chapters.Count); i++)
                    {
                        TM.App.Log($"[WebCrawlerService] 章节样本[{i}]: title='{chapters[i].Title}' url='{chapters[i].Url}'");
                    }
                }

                if (chapters != null && chapters.Count > 0 && chapters.Count < 50)
                {
                    var expanded = await TryExpandChapterListAsync(chapters, script);
                    if (expanded.Count > chapters.Count)
                    {
                        chapters = expanded;
                    }
                }

                return chapters ?? new List<ChapterInfo>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WebCrawlerService] 章节提取失败: {ex.Message}");
                return new List<ChapterInfo>();
            }
        }

        public async Task<(string title, string author, string genre, string tags)> ExtractBookInfoAsync()
        {
            try
            {
                var script = ContentExtractor.GetBookInfoScript();
                var result = await _webView.ExecuteScriptAsync(script);

                var json = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrEmpty(json))
                    return (string.Empty, string.Empty, string.Empty, string.Empty);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                var genre = root.TryGetProperty("genre", out var g) ? g.GetString() ?? "" : "";
                var tags = root.TryGetProperty("tags", out var tg) ? tg.GetString() ?? "" : "";

                return (title, author, genre, tags);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WebCrawlerService] 书籍信息提取失败: {ex.Message}");
                return (string.Empty, string.Empty, string.Empty, string.Empty);
            }
        }

        #endregion

        private async Task<(bool success, string title, string content, int wordCount, string? error)> ExtractChapterContentWithPaginationAsync(
            CancellationToken cancellationToken, string? chapterBaseUrl = null, string? catalogChapterTitle = null)
        {
            const int maxPages = 8;
            const int maxChapterChars = 30000;
            var mergedSb = new System.Text.StringBuilder();
            string? firstPageTitle = null;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int firstPageLen = 0;

            for (var page = 0; page < maxPages; page++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await Dispatcher.Yield(DispatcherPriority.Input);

                var currentUrl = _webView.Source?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(currentUrl) && !visited.Add(currentUrl)) break;

                var content = await ExtractChapterContentAsync();

                if (!content.success && page == 0)
                {
                    int[] retryDelays = { 1000, 2500, 5000 };
                    for (int r = 0; r < retryDelays.Length; r++)
                    {
                        await Task.Delay(retryDelays[r], cancellationToken);
                        content = await ExtractChapterContentAsync();
                        if (content.success) break;
                    }
                }

                if (!content.success) return content;

                if (string.IsNullOrWhiteSpace(firstPageTitle) && !string.IsNullOrWhiteSpace(content.title))
                    firstPageTitle = content.title;

                if (page > 0)
                {
                    var referenceTitle = catalogChapterTitle ?? firstPageTitle;
                    if (!string.IsNullOrWhiteSpace(content.title) && !string.IsNullOrWhiteSpace(referenceTitle))
                    {
                        if (!IsSameChapterTitle(referenceTitle, content.title))
                        {
                            TM.App.Log($"[WebCrawlerService] 跨章防御(标题): 基准='{referenceTitle}' → 新页='{content.title}'");
                            break;
                        }
                    }

                    if (firstPageLen > 0 && mergedSb.Length > firstPageLen * 5)
                    {
                        TM.App.Log($"[WebCrawlerService] 跨章防御(膨胀): 内容已达首页{mergedSb.Length / firstPageLen}倍({mergedSb.Length}字)，停止拼接");
                        break;
                    }
                }

                var contentPart = (content.content ?? string.Empty).Replace("\r\n", "\n").Trim();
                if (!string.IsNullOrWhiteSpace(contentPart))
                {
                    var merged = mergedSb.ToString();
                    if (!merged.Contains(contentPart, StringComparison.Ordinal))
                    {
                        if (mergedSb.Length > 0) mergedSb.Append("\n\n");
                        mergedSb.Append(contentPart);
                    }

                    if (page == 0) firstPageLen = contentPart.Length;
                }

                if (mergedSb.Length >= maxChapterChars)
                {
                    TM.App.Log($"[WebCrawlerService] 跨章防御(上限): 已达{mergedSb.Length}字符，停止拼接");
                    break;
                }

                var nextUrl = await TryGetNextPageUrlAsync();
                if (string.IsNullOrWhiteSpace(nextUrl)) break;

                if (!string.IsNullOrWhiteSpace(currentUrl))
                {
                    if (string.Equals(nextUrl, currentUrl, StringComparison.OrdinalIgnoreCase)) break;

                    if (IsPaginationUrl(chapterBaseUrl ?? currentUrl, nextUrl))
                    {
                    }
                    else if (IsLikelyDifferentChapterUrl(currentUrl, nextUrl))
                    {
                        TM.App.Log($"[WebCrawlerService] 跨章防御(URL): '{currentUrl}' → '{nextUrl}'");
                        break;
                    }
                }

                await NavigateAndWaitAsync(nextUrl, cancellationToken);
            }

            var finalContent = System.Text.RegularExpressions.Regex.Replace(mergedSb.ToString(), "\n{3,}", "\n\n").Trim();
            return (true, firstPageTitle ?? string.Empty, finalContent, finalContent.Length, null);
        }

        private static bool IsSameChapterTitle(string firstTitle, string currentTitle)
        {
            if (string.IsNullOrWhiteSpace(firstTitle) || string.IsNullOrWhiteSpace(currentTitle))
                return true;

            var chapterPattern = new System.Text.RegularExpressions.Regex(@"第[\d一二三四五六七八九十百千万零〇]+[章节回篇]");
            var m1 = chapterPattern.Match(firstTitle);
            var m2 = chapterPattern.Match(currentTitle);

            if (m1.Success && m2.Success)
                return m1.Value == m2.Value;

            return string.Equals(firstTitle.Trim(), currentTitle.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPaginationUrl(string baseUrl, string nextUrl)
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                var nextUri = new Uri(nextUrl);

                if (!string.Equals(baseUri.Host, nextUri.Host, StringComparison.OrdinalIgnoreCase))
                    return false;

                var baseFile = System.IO.Path.GetFileNameWithoutExtension(baseUri.AbsolutePath);
                var nextFile = System.IO.Path.GetFileNameWithoutExtension(nextUri.AbsolutePath);

                if (System.Text.RegularExpressions.Regex.IsMatch(nextFile,
                    @"^" + System.Text.RegularExpressions.Regex.Escape(baseFile) + @"[_\-]p?\d+$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;

                var query = nextUri.Query;
                if (System.Text.RegularExpressions.Regex.IsMatch(query, @"[?&]p(age)?=\d+",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;

                var basePath = baseUri.AbsolutePath.TrimEnd('/');
                var nextPath = nextUri.AbsolutePath.TrimEnd('/');
                if (nextPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        nextPath.Substring(basePath.Length), @"^/\d+$"))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLikelyDifferentChapterUrl(string currentUrl, string nextUrl)
        {
            try
            {
                var curUri = new Uri(currentUrl);
                var nextUri = new Uri(nextUrl);

                if (!string.Equals(curUri.Host, nextUri.Host, StringComparison.OrdinalIgnoreCase))
                    return true;

                var curFile = System.IO.Path.GetFileNameWithoutExtension(curUri.AbsolutePath);
                var nextFile = System.IO.Path.GetFileNameWithoutExtension(nextUri.AbsolutePath);

                if (long.TryParse(curFile, out var curId) && long.TryParse(nextFile, out var nextId))
                {
                    if (curId != nextId)
                        return true;
                }

                if (!string.IsNullOrWhiteSpace(curFile) && !string.IsNullOrWhiteSpace(nextFile)
                    && !string.Equals(curFile, nextFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (!nextFile.StartsWith(curFile, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> TryGetNextPageUrlAsync()
        {
            try
            {
                var script = ContentExtractor.GetNextPageScript();
                var result = await _webView.ExecuteScriptAsync(script);
                var json = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrWhiteSpace(json)) return string.Empty;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var next = root.TryGetProperty("nextUrl", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                return next?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetNextPageUrlAsync), ex);
                return string.Empty;
            }
        }

        #region 批量抓取

        public async Task<CrawlResult> CrawlChaptersAsync(
            List<ChapterInfo> chapters,
            CrawlOptions options,
            IProgress<CrawlProgress>? progress,
            CancellationToken cancellationToken)
        {
            var result = new CrawlResult
            {
                SourceUrl = _webView.Source?.ToString() ?? ""
            };

            var (title, author, genre, tags) = await ExtractBookInfoAsync();
            result.BookTitle = title;
            result.Author = author;

            var targetChapters = FilterChapters(chapters, options)
                .OrderBy(c => c.Index)
                .ToList();
            TM.App.Log($"[WebCrawlerService] 开始抓取 {targetChapters.Count} 个章节");

            var crawledChapters = new List<ChapterContent>();
            var totalWords = 0;
            const int maxWordsPerChapter = 30000;
            const int maxTotalWords = 500000;

            for (int i = 0; i < targetChapters.Count; i++)
            {
                await Dispatcher.Yield(DispatcherPriority.Input);

                if (cancellationToken.IsCancellationRequested)
                {
                    TM.App.Log("[WebCrawlerService] 用户取消抓取");
                    result.ErrorMessage = "用户取消";
                    break;
                }

                var chapter = targetChapters[i];

                progress?.Report(new CrawlProgress
                {
                    Current = i + 1,
                    Total = targetChapters.Count,
                    CurrentChapter = chapter.Title,
                    StatusMessage = $"正在抓取: {chapter.Title}",
                    IsCrawling = true
                });

                try
                {
                    await NavigateAndWaitAsync(chapter.Url, cancellationToken);

                    var content = await ExtractChapterContentWithPaginationAsync(cancellationToken, chapter.Url, chapter.Title);

                    if (content.success)
                    {
                        TM.App.Log($"[WebCrawlerService] 内容提取title='{content.title}' chapterTitle='{chapter.Title}'");
                        if (string.IsNullOrWhiteSpace(content.content) || content.wordCount < 50)
                        {
                            TM.App.Log($"[WebCrawlerService] 正文过短，跳过不保存: {chapter.Title} ({chapter.Url}) len={content.wordCount}");
                        }
                        else if (string.IsNullOrWhiteSpace(content.title) && content.wordCount < 300)
                        {
                            TM.App.Log($"[WebCrawlerService] 疑似目录页/专题页，跳过不保存: {chapter.Title} ({chapter.Url}) len={content.wordCount}");
                        }
                        else
                        {
                            var chapterContent = content.content;
                            var chapterWordCount = content.wordCount;

                            if (chapterWordCount > maxWordsPerChapter)
                            {
                                TM.App.Log($"[WebCrawlerService] 单章字数超限({chapterWordCount}>{maxWordsPerChapter})，截断: {chapter.Title}");
                                chapterContent = chapterContent.Substring(0, maxWordsPerChapter);
                                chapterWordCount = maxWordsPerChapter;
                            }

                            crawledChapters.Add(new ChapterContent
                            {
                                Index = chapter.Index,
                                Title = string.IsNullOrEmpty(content.title) ? chapter.Title : content.title,
                                Url = chapter.Url,
                                Content = chapterContent,
                                WordCount = chapterWordCount
                            });

                            totalWords += chapterWordCount;
                            chapter.IsCrawled = true;

                            TM.App.Log($"[WebCrawlerService] 抓取成功: {chapter.Title} ({chapterWordCount} 字)");

                            if (totalWords >= maxTotalWords)
                            {
                                TM.App.Log($"[WebCrawlerService] 总字数已达上限({totalWords}>={maxTotalWords})，停止抓取");
                                break;
                            }
                        }
                    }
                    else
                    {
                        TM.App.Log($"[WebCrawlerService] 抓取失败: {chapter.Title} ({chapter.Url}) - {content.error}");
                    }

                    if (i < targetChapters.Count - 1)
                    {
                        var delay = _random.Next(options.MinDelayMs, options.MaxDelayMs);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    TM.App.Log("[WebCrawlerService] 抓取被取消");
                    break;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[WebCrawlerService] 抓取异常: {chapter.Title} - {ex.Message}");
                }
            }

            result.Chapters = crawledChapters;
            result.TotalWords = totalWords;
            result.Success = crawledChapters.Count > 0;

            progress?.Report(new CrawlProgress
            {
                Current = targetChapters.Count,
                Total = targetChapters.Count,
                CurrentChapter = "",
                StatusMessage = $"抓取完成: {crawledChapters.Count}/{targetChapters.Count} 章",
                IsCrawling = false
            });

            TM.App.Log($"[WebCrawlerService] 抓取完成: {crawledChapters.Count} 章, {totalWords} 字");
            return result;
        }

        private List<ChapterInfo> FilterChapters(List<ChapterInfo> chapters, CrawlOptions options)
        {
            var filtered = chapters.AsEnumerable();

            if (options.SkipVipChapters)
            {
                filtered = filtered.Where(c => !c.IsVip);
            }

            var list = filtered.ToList();

            switch (options.Mode)
            {
                case CrawlMode.FirstN:
                    return list.Take(options.FirstNCount).ToList();

                case CrawlMode.Range:
                    return list
                        .Where(c => c.Index >= options.RangeStart && c.Index <= options.RangeEnd)
                        .ToList();

                case CrawlMode.All:
                default:
                    return list;
            }
        }

        #endregion

        #region 目录扩展导航

        private async Task<List<ChapterInfo>> TryExpandChapterListAsync(
            List<ChapterInfo> currentChapters, string extractScript)
        {
            try
            {
                TM.App.Log($"[WebCrawlerService] 当前页面仅提取到 {currentChapters.Count} 章，尝试扩展目录");

                var navScript = ContentExtractor.GetExpandCatalogScript();
                var navResult = await _webView.ExecuteScriptAsync(navScript);
                var navJson = JsonSerializer.Deserialize<string>(navResult);

                List<ChapterInfo>? expandedChapters = null;

                if (!string.IsNullOrWhiteSpace(navJson))
                {
                    using var doc = JsonDocument.Parse(navJson);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    var value = root.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";

                    if (type == "url" && !string.IsNullOrWhiteSpace(value))
                    {
                        TM.App.Log($"[WebCrawlerService] 导航到完整目录: {value}");
                        await NavigateSpaAsync(value);
                        expandedChapters = await TryExtractChaptersAsync(extractScript);
                    }
                    else if (type == "clicked")
                    {
                        TM.App.Log("[WebCrawlerService] 已点击'查看更多章节'，等待页面更新");
                        await Task.Delay(3000);
                        expandedChapters = await TryExtractChaptersAsync(extractScript);
                    }
                }

                if (expandedChapters != null && expandedChapters.Count > currentChapters.Count)
                {
                    TM.App.Log($"[WebCrawlerService] 阶段1成功，提取到 {expandedChapters.Count} 章（原 {currentChapters.Count} 章）");
                    return NormalizeMobileChapterUrls(expandedChapters);
                }

                var desktopUrl = TryBuildDesktopUrl();
                if (!string.IsNullOrWhiteSpace(desktopUrl))
                {
                    TM.App.Log($"[WebCrawlerService] 阶段1未增加章节，fallback 到桌面版: {desktopUrl}");
                    await NavigateAndWaitAsync(desktopUrl, CancellationToken.None);
                    var desktopChapters = await TryExtractChaptersAsync(extractScript);
                    if (desktopChapters != null && desktopChapters.Count > currentChapters.Count)
                    {
                        TM.App.Log($"[WebCrawlerService] 桌面版提取到 {desktopChapters.Count} 章（原 {currentChapters.Count} 章）");
                        return NormalizeMobileChapterUrls(desktopChapters);
                    }
                }

                TM.App.Log($"[WebCrawlerService] 所有策略均未增加章节，保留原结果");
                return NormalizeMobileChapterUrls(currentChapters);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WebCrawlerService] 扩展目录失败: {ex.Message}");
                return NormalizeMobileChapterUrls(currentChapters);
            }
        }

        private async Task<List<ChapterInfo>?> TryExtractChaptersAsync(string extractScript)
        {
            try
            {
                var result = await _webView.ExecuteScriptAsync(extractScript);
                var json = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonSerializer.Deserialize<List<ChapterInfo>>(json, JsonHelper.Default);
            }
            catch
            {
                return null;
            }
        }

        private string? TryBuildDesktopUrl()
        {
            var source = _webView.Source?.ToString() ?? "";
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return null;
            var host = uri.Host.ToLowerInvariant();
            if (!host.StartsWith("m.", StringComparison.Ordinal)) return null;
            var desktopHost = "www." + host.Substring(2);
            var builder = new UriBuilder(uri) { Host = desktopHost };
            return builder.Uri.ToString();
        }

        private static List<ChapterInfo> NormalizeMobileChapterUrls(List<ChapterInfo> chapters)
        {
            foreach (var ch in chapters)
            {
                if (string.IsNullOrWhiteSpace(ch.Url)) continue;
                if (!Uri.TryCreate(ch.Url, UriKind.Absolute, out var uri)) continue;
                var host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("m.", StringComparison.Ordinal))
                {
                    var desktopHost = "www." + host.Substring(2);
                    var builder = new UriBuilder(uri) { Host = desktopHost };
                    ch.Url = builder.Uri.ToString();
                }
            }
            return chapters;
        }

        private async Task NavigateSpaAsync(string url)
        {
            var currentUrl = _webView.Source?.ToString() ?? "";

            bool isHashOnly = false;
            if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var current) &&
                Uri.TryCreate(url, UriKind.Absolute, out var target))
            {
                isHashOnly = current.Scheme == target.Scheme &&
                             current.Host == target.Host &&
                             current.Port == target.Port &&
                             current.AbsolutePath == target.AbsolutePath;
            }

            if (isHashOnly)
            {
                var escapedUrl = JsonSerializer.Serialize(url);
                await _webView.ExecuteScriptAsync($"window.location.href = {escapedUrl}");
                await Task.Delay(2500);
            }
            else
            {
                await NavigateAndWaitAsync(url, CancellationToken.None);
            }
        }

        #endregion

        #region 页面导航

        private async Task NavigateAndWaitAsync(string url, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
            {
                tcs.TrySetResult(e.IsSuccess);
            }

            _webView.NavigationCompleted += OnNavigationCompleted;

            try
            {
                _webView.Source = new Uri(url);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(PageLoadTimeout));

                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(Timeout.Infinite, cts.Token)
                );

                if (completedTask != tcs.Task)
                {
                    throw new TimeoutException($"页面加载超时: {url}");
                }

                await Task.Delay(500, cancellationToken);
                await Dispatcher.Yield(DispatcherPriority.Input);
            }
            finally
            {
                _webView.NavigationCompleted -= OnNavigationCompleted;
            }
        }

        private async Task<(bool success, string title, string content, int wordCount, string? error)> ExtractChapterContentAsync()
        {
            try
            {
                var script = ContentExtractor.GetContentScript();
                var result = await _webView.ExecuteScriptAsync(script);

                var json = JsonSerializer.Deserialize<string>(result);
                if (string.IsNullOrEmpty(json))
                    return (false, "", "", 0, "提取结果为空");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                if (!success)
                {
                    var error = root.TryGetProperty("error", out var e) ? e.GetString() : "未知错误";
                    TM.App.Log($"[WebCrawlerService] 正文提取失败: {error} url={_webView.Source}");
                    return (false, "", "", 0, error);
                }
                if (TM.App.IsDebugMode)
                {
                    var dbgWc = root.TryGetProperty("wordCount", out var wcEl) ? wcEl.GetInt32() : 0;
                    TM.App.Log($"[WebCrawlerService] 正文提取成功 wordCount={dbgWc} url={_webView.Source}");
                }

                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                title = CleanChapterTitle(title);
                var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                content = CleanNovelContent(content);
                var wordCount = content.Length;

                return (true, title, content, wordCount, null);
            }
            catch (Exception ex)
            {
                return (false, "", "", 0, ex.Message);
            }
        }

        #endregion

        private static readonly System.Text.RegularExpressions.Regex _puaRegex =
            new(@"[\uE000-\uF8FF]", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string CleanChapterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;

            var cleaned = _puaRegex.Replace(title, string.Empty);

            cleaned = cleaned
                .Replace("\u25A1", "")
                .Replace("\u25A0", "")
                .Replace("\u25AA", "")
                .Replace("\u25AB", "")
                .Replace("\u25FD", "")
                .Replace("\u25FE", "")
                .Replace("\uFFFD", "")
                .Replace("\u0000", "");

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

            return string.IsNullOrWhiteSpace(cleaned) ? title.Trim() : cleaned;
        }

        private static string StripPuaCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return _puaRegex.Replace(text, string.Empty);
        }

        private static string CleanNovelContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;

            content = StripPuaCharacters(content);

            var adLineKeywords = new[]
            {
                "最新网址", "最新章节", "请记住本书首发域名", "手机版阅读网址",
                "本章未完", "点击下一页", "笔趣阁", "顶点小说", "全本免费",
                "天才一秒", "记住地址", "shuquta.com", "xheiyan.info",
                "bqgde.de", "bqg", "17k.com", "qidian.com",
                "推荐阅读", "相关推荐", "更多精彩", "手机用户请浏览",
                "温馨提示：", "投票推荐", "加入书签"
            };

            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder(content.Length);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var isAd = false;
                foreach (var kw in adLineKeywords)
                {
                    if (trimmed.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        isAd = true;
                        break;
                    }
                }
                if (!isAd)
                    sb.AppendLine(trimmed);
            }

            var result = System.Text.RegularExpressions.Regex.Replace(
                sb.ToString().Trim(), @"\n{3,}", "\n\n");
            return result;
        }
    }
}
