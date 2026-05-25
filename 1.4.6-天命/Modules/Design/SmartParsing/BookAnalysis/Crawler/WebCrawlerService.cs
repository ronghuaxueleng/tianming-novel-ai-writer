using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading;
using System.Threading.Tasks;
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
                    for (int i = 0; i < Math.Min(3, chapters.Count); i++)
                    {
                        TM.App.Log($"[WebCrawlerService] 章节样本[{i}]: title='{chapters[i].Title}' url='{chapters[i].Url}'");
                    }
                }

                if (chapters != null && chapters.Count > 0 && chapters.Count < 50 && IsBqgdeSite())
                {
                    var expanded = await TryNavigateToBqgdeFullListAsync(chapters, script);
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
            CancellationToken cancellationToken)
        {
            var source = _webView.Source?.ToString() ?? string.Empty;
            var isShuquta = !string.IsNullOrWhiteSpace(source)
                          && Uri.TryCreate(source, UriKind.Absolute, out var uri)
                          && (uri.Host.Contains("shuquta.com", StringComparison.OrdinalIgnoreCase));

            if (!isShuquta)
            {
                var content = await ExtractChapterContentAsync();
                if (content.success) return content;

                var isBqgde = !string.IsNullOrWhiteSpace(source)
                              && Uri.TryCreate(source, UriKind.Absolute, out var bqUri)
                              && IsBqgdeHost(bqUri.Host);
                if (isBqgde)
                {
                    int[] retryDelays = { 1500, 3000, 5000 };
                    for (int r = 0; r < retryDelays.Length; r++)
                    {
                        await Task.Delay(retryDelays[r], cancellationToken);
                        content = await ExtractChapterContentAsync();
                        if (content.success) return content;
                    }
                }

                return content;
            }

            const int maxPages = 8;
            var merged = string.Empty;
            string? finalTitle = null;
            var totalWords = 0;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var page = 0; page < maxPages; page++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var currentUrl = _webView.Source?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(currentUrl) && !visited.Add(currentUrl))
                {
                    break;
                }

                var content = await ExtractChapterContentAsync();
                if (!content.success)
                {
                    return content;
                }

                if (string.IsNullOrWhiteSpace(finalTitle) && !string.IsNullOrWhiteSpace(content.title))
                {
                    finalTitle = content.title;
                }

                var part = content.content ?? string.Empty;
                part = part.Replace("\r\n", "\n");
                part = part.Trim();

                if (!string.IsNullOrWhiteSpace(part))
                {
                    if (merged.IndexOf(part, StringComparison.Ordinal) < 0)
                    {
                        if (!string.IsNullOrWhiteSpace(merged))
                        {
                            merged += "\n\n";
                        }
                        merged += part;
                    }
                }

                merged = merged.Replace("\n{3,}", "\n\n");
                totalWords = merged.Length;

                var nextUrl = await TryGetNextPageUrlAsync();
                if (string.IsNullOrWhiteSpace(nextUrl))
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(currentUrl) && string.Equals(nextUrl, currentUrl, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await NavigateAndWaitAsync(nextUrl, cancellationToken);
            }

            merged = merged.Replace("\n{3,}", "\n\n").Trim();

            return (true, finalTitle ?? string.Empty, merged, totalWords, null);
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

            for (int i = 0; i < targetChapters.Count; i++)
            {
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

                    var content = await ExtractChapterContentWithPaginationAsync(cancellationToken);

                    if (content.success)
                    {
                        TM.App.Log($"[WebCrawlerService] 内容提取title='{content.title}' chapterTitle='{chapter.Title}'");
                        if (string.IsNullOrWhiteSpace(content.content) || content.wordCount < 50)
                        {
                            TM.App.Log($"[WebCrawlerService] 正文过短，可能解析到了非正文区域: {chapter.Title} ({chapter.Url}) len={content.wordCount}");
                        }

                        crawledChapters.Add(new ChapterContent
                        {
                            Index = chapter.Index,
                            Title = string.IsNullOrEmpty(content.title) ? chapter.Title : content.title,
                            Url = chapter.Url,
                            Content = content.content,
                            WordCount = content.wordCount
                        });

                        totalWords += content.wordCount;
                        chapter.IsCrawled = true;

                        TM.App.Log($"[WebCrawlerService] 抓取成功: {chapter.Title} ({content.wordCount} 字)");
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

        #region bqgde 完整目录导航

        private bool IsBqgdeSite()
        {
            var source = _webView.Source?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri))
                return false;
            return IsBqgdeHost(uri.Host);
        }

        private static bool IsBqgdeHost(string host)
        {
            host = host.ToLowerInvariant();
            if (host.Contains("bqgde.de")) return true;
            var idx = host.IndexOf("bqg", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var pos = idx + 3;
                while (pos < host.Length && char.IsDigit(host[pos])) pos++;
                if (pos < host.Length && host[pos] == '.') return true;
            }
            return false;
        }

        private async Task<List<ChapterInfo>> TryNavigateToBqgdeFullListAsync(
            List<ChapterInfo> currentChapters, string extractScript)
        {
            try
            {
                TM.App.Log($"[WebCrawlerService] bqgde: 当前页面仅提取到 {currentChapters.Count} 章，尝试导航到完整目录");

                var navScript = ContentExtractor.GetBqgdeFullListNavigationScript();
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
                        TM.App.Log($"[WebCrawlerService] bqgde: 导航到完整目录: {value}");
                        await NavigateSpaAsync(value);
                        expandedChapters = await TryExtractChaptersAsync(extractScript);
                    }
                    else if (type == "clicked")
                    {
                        TM.App.Log("[WebCrawlerService] bqgde: 已点击'查看更多章节'，等待页面更新");
                        await Task.Delay(3000);
                        expandedChapters = await TryExtractChaptersAsync(extractScript);
                    }
                }

                if (expandedChapters != null && expandedChapters.Count > currentChapters.Count)
                {
                    TM.App.Log($"[WebCrawlerService] bqgde: 阶段1成功，提取到 {expandedChapters.Count} 章（原 {currentChapters.Count} 章）");
                    return NormalizeBqgdeChapterUrls(expandedChapters);
                }

                var desktopUrl = TryBuildBqgdeDesktopUrl();
                if (!string.IsNullOrWhiteSpace(desktopUrl))
                {
                    TM.App.Log($"[WebCrawlerService] bqgde: 阶段1未增加章节，fallback 到桌面版: {desktopUrl}");
                    await NavigateAndWaitAsync(desktopUrl, CancellationToken.None);
                    var desktopChapters = await TryExtractChaptersAsync(extractScript);
                    if (desktopChapters != null && desktopChapters.Count > currentChapters.Count)
                    {
                        TM.App.Log($"[WebCrawlerService] bqgde: 桌面版提取到 {desktopChapters.Count} 章（原 {currentChapters.Count} 章）");
                        return NormalizeBqgdeChapterUrls(desktopChapters);
                    }
                }

                TM.App.Log($"[WebCrawlerService] bqgde: 所有策略均未增加章节，保留原结果并归一化URL");
                return NormalizeBqgdeChapterUrls(currentChapters);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WebCrawlerService] bqgde: 导航完整目录失败: {ex.Message}");
                return NormalizeBqgdeChapterUrls(currentChapters);
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

        private string? TryBuildBqgdeDesktopUrl()
        {
            var source = _webView.Source?.ToString() ?? "";
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return null;
            var host = uri.Host.ToLowerInvariant();
            if (!host.StartsWith("m.")) return null;
            var desktopHost = "www." + host.Substring(2);
            var builder = new UriBuilder(uri) { Host = desktopHost };
            return builder.Uri.ToString();
        }

        private static List<ChapterInfo> NormalizeBqgdeChapterUrls(List<ChapterInfo> chapters)
        {
            foreach (var ch in chapters)
            {
                if (string.IsNullOrWhiteSpace(ch.Url)) continue;
                if (!Uri.TryCreate(ch.Url, UriKind.Absolute, out var uri)) continue;
                var host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("m."))
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
                    return (false, "", "", 0, error);
                }

                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                var wordCount = root.TryGetProperty("wordCount", out var w) ? w.GetInt32() : content.Length;

                return (true, title, content, wordCount, null);
            }
            catch (Exception ex)
            {
                return (false, "", "", 0, ex.Message);
            }
        }

        #endregion
    }
}
