using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;
using TM.Modules.Design.SmartParsing.BookAnalysis.Models;
using TM.Modules.Design.SmartParsing.BookAnalysis.Crawler;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class NovelCrawlerService
    {
        private readonly string _crawledBasePath;
        private readonly Dictionary<string, INovelParser> _parsers = new();

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

            System.Diagnostics.Debug.WriteLine($"[NovelCrawlerService] {key}: {ex.Message}");
        }

        public NovelCrawlerService()
        {
            _crawledBasePath = StoragePathHelper.GetModulesStoragePath("Design/SmartParsing/BookAnalysis/CrawledBooks");
            StoragePathHelper.EnsureDirectoryExists(_crawledBasePath);

            RegisterParser(new ShuqutaParser());
            RegisterParser(new XheiyanParser());
            RegisterParser(new BqgdeParser());
        }

        private static readonly JsonSerializerOptions _bookInfoJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions _bookInfoJsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private void RegisterParser(INovelParser parser)
        {
            foreach (var domain in parser.SupportedDomains)
            {
                _parsers[domain.ToLower()] = parser;
            }
        }

        public async Task<string> GetPageHtmlAsync(WebView2 webView)
        {
            try
            {
                var html = await webView.ExecuteScriptAsync("document.documentElement.outerHTML");
                return JsonSerializer.Deserialize<string>(html) ?? string.Empty;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 获取页面 HTML 失败: {ex.Message}");
                return string.Empty;
            }
        }

        public INovelParser? GetParser(string url)
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLower();

                if (_parsers.TryGetValue(host, out var parser))
                {
                    return parser;
                }

                if (host.StartsWith("www."))
                {
                    var withoutWww = host.Substring(4);
                    if (_parsers.TryGetValue(withoutWww, out parser))
                    {
                        return parser;
                    }
                }

                foreach (var (domain, p) in _parsers)
                {
                    if (host.Contains(domain) || domain.Contains(host))
                    {
                        return p;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetParser), ex);
                return null;
            }
        }

        public NovelInfo? ParseNovelPage(string html, string url)
        {
            var parser = GetParser(url);
            if (parser == null)
            {
                TM.App.Log($"[NovelCrawlerService] 找不到适配 {url} 的解析器");
                return null;
            }

            try
            {
                return parser.ParseNovelInfo(html);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 解析页面失败: {ex.Message}");
                return null;
            }
        }

        public async Task<CrawledContent?> CrawlWholeBookAsync(
            WebView2 webView, 
            string catalogUrl, 
            string bookId,
            IProgress<CrawlProgress>? progress = null)
        {
            var parser = GetParser(catalogUrl);
            if (parser == null)
            {
                TM.App.Log($"[NovelCrawlerService] 找不到适配 {catalogUrl} 的解析器");
                return null;
            }

            try
            {
                progress?.Report(new CrawlProgress { Message = "正在获取目录页...", Percentage = 0 });
                var catalogHtml = await GetPageHtmlAsync(webView);

                var catalog = parser.ParseCatalog(catalogHtml);
                if (catalog == null || catalog.Chapters.Count == 0)
                {
                    TM.App.Log("[NovelCrawlerService] 解析目录失败或章节为空");
                    return null;
                }

                var result = new CrawledContent
                {
                    BookId = bookId,
                    SourceUrl = catalogUrl,
                    SourceSite = parser.SiteName,
                    TotalChapters = catalog.Chapters.Count,
                    CrawledAt = DateTime.Now
                };

                int totalChapters = catalog.Chapters.Count;
                int totalWords = 0;
                var crawlerService = new WebCrawlerService(webView);

                for (int i = 0; i < totalChapters; i++)
                {
                    var chapterInfo = catalog.Chapters[i];
                    progress?.Report(new CrawlProgress
                    {
                        Message = $"正在抓取: {chapterInfo.Title}",
                        Percentage = (int)((i + 1) * 100.0 / totalChapters),
                        CurrentChapter = i + 1,
                        TotalChapters = totalChapters
                    });

                    try
                    {
                        webView.Source = new Uri(chapterInfo.Url);
                        await Task.Delay(2000);

                        var contentScript = ContentExtractor.GetContentScript();
                        var scriptResult = await webView.ExecuteScriptAsync(contentScript);
                        var jsonResult = JsonSerializer.Deserialize<string>(scriptResult);

                        if (!string.IsNullOrEmpty(jsonResult))
                        {
                            using var doc = JsonDocument.Parse(jsonResult);
                            var root = doc.RootElement;
                            var success = root.TryGetProperty("success", out var s) && s.GetBoolean();

                            if (success)
                            {
                                var titleRaw = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                                var title = string.IsNullOrWhiteSpace(titleRaw) ? chapterInfo.Title : titleRaw;
                                var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                                var wordCount = root.TryGetProperty("wordCount", out var w) ? w.GetInt32() : content.Length;

                                var chapter = new CrawledChapter
                                {
                                    Index = i + 1,
                                    Title = title,
                                    Url = chapterInfo.Url,
                                    Content = content,
                                    WordCount = wordCount
                                };

                                result.Chapters.Add(chapter);
                                totalWords += chapter.WordCount;

                                TM.App.Log($"[NovelCrawlerService] 抓取成功: {title} ({wordCount} 字)");
                            }
                            else
                            {
                                TM.App.Log($"[NovelCrawlerService] 章节内容提取失败: {chapterInfo.Title}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[NovelCrawlerService] 抓取章节异常: {chapterInfo.Title} - {ex.Message}");
                    }

                    await Task.Delay(500);
                }

                result.TotalWords = totalWords;

                await SaveCrawledContentAsync(bookId, result);

                progress?.Report(new CrawlProgress
                {
                    Message = "抓取完成",
                    Percentage = 100,
                    CurrentChapter = totalChapters,
                    TotalChapters = totalChapters
                });

                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 抓取失败: {ex.Message}");
                return null;
            }
        }

        public async Task SaveCrawledContentAsync(string bookId, CrawledContent content)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return;

            var bookDir = Path.Combine(_crawledBasePath, bookId);
            var tempDir = bookDir + ".tmp";

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }

                Directory.CreateDirectory(tempDir);
                var chaptersDir = Path.Combine(tempDir, "chapters");
                Directory.CreateDirectory(chaptersDir);

                var chapters = content.Chapters.OrderBy(c => c.Index).ToList();
                var pad = Math.Max(3, Math.Max(chapters.Count, 1).ToString().Length);

                foreach (var chapter in chapters)
                {
                    var safeTitle = SanitizeFileNamePart(chapter.Title);
                    var fileName = $"{chapter.Index.ToString($"D{pad}")}_{safeTitle}.txt";
                    chapter.FileName = fileName;

                    var chapterPath = Path.Combine(chaptersDir, fileName);
                    await File.WriteAllTextAsync(chapterPath, chapter.Content ?? string.Empty);
                }

                var info = new BookInfoFile
                {
                    BookId = bookId,
                    Title = content.BookTitle,
                    Author = content.Author,
                    SourceUrl = content.SourceUrl,
                    SourceSite = content.SourceSite,
                    CrawledAt = content.CrawledAt,
                    ChapterCount = chapters.Count,
                    TotalWords = content.TotalWords,
                    Chapters = chapters
                        .Select(c => new BookInfoChapterFile
                        {
                            Index = c.Index,
                            Title = c.Title,
                            FileName = c.FileName,
                            WordCount = c.WordCount,
                            Url = c.Url
                        })
                        .ToList()
                };

                var bookInfoPath = Path.Combine(tempDir, "book_info.json");
                var json = JsonSerializer.Serialize(info, _bookInfoJsonOptions);
                await File.WriteAllTextAsync(bookInfoPath, json);

                if (Directory.Exists(bookDir))
                {
                    Directory.Delete(bookDir, true);
                }

                Directory.Move(tempDir, bookDir);
                TM.App.Log($"[NovelCrawlerService] 已保存爬取内容: {bookDir}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 保存爬取内容失败: {ex.Message}");

                static bool TryDeleteDirectory(string path)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("TryDeleteDirectory", ex);
                        return false;
                    }
                }

                _ = TryDeleteDirectory(tempDir);

                throw;
            }
        }

        public async Task SaveEssenceChapterSelectionAsync(
            string bookId,
            string bookTitle,
            string author,
            IReadOnlyList<int> selectedIndexes,
            int targetCount,
            string strategy,
            IReadOnlyList<int>? goldenIndexes = null,
            IReadOnlyDictionary<string, int>? anchorIndexes = null,
            IReadOnlyDictionary<int, string>? reasonsByIndex = null,
            string? rawAiContent = null)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return;

            try
            {
                var bookDir = Path.Combine(_crawledBasePath, bookId);
                StoragePathHelper.EnsureDirectoryExists(bookDir);

                var file = new EssenceChapterSelectionFile
                {
                    BookId = bookId,
                    BookTitle = bookTitle ?? string.Empty,
                    Author = author ?? string.Empty,
                    TargetCount = Math.Max(10, targetCount),
                    Strategy = strategy ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    SelectedIndexes = selectedIndexes
                        .Where(i => i > 0)
                        .Distinct()
                        .OrderBy(i => i)
                        .ToList()
                    ,
                    GoldenIndexes = (goldenIndexes ?? Array.Empty<int>())
                        .Where(i => i > 0)
                        .Distinct()
                        .OrderBy(i => i)
                        .ToList(),
                    AnchorIndexes = anchorIndexes == null
                        ? new Dictionary<string, int>()
                        : anchorIndexes
                            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                            .GroupBy(kv => kv.Key)
                            .ToDictionary(g => g.Key, g => g.First().Value),
                    ReasonsByIndex = reasonsByIndex == null
                        ? new Dictionary<int, string>()
                        : reasonsByIndex
                            .Where(kv => kv.Key > 0 && !string.IsNullOrWhiteSpace(kv.Value))
                            .GroupBy(kv => kv.Key)
                            .ToDictionary(g => g.Key, g => g.First().Value),
                    RawAiContent = rawAiContent ?? string.Empty
                };

                var filePath = Path.Combine(bookDir, "essence_chapters.json");
                var tempPath = filePath + ".tmp";

                var json = JsonSerializer.Serialize(file, _bookInfoJsonOptions);
                await File.WriteAllTextAsync(tempPath, json);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.Move(tempPath, filePath);
                TM.App.Log($"[NovelCrawlerService] 已保存精华章配置: {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 保存精华章配置失败: {ex.Message}");
            }
        }

        private async Task<EssenceChapterSelectionFile?> LoadEssenceChapterSelectionFileAsync(string bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return null;

            var bookDir = Path.Combine(_crawledBasePath, bookId);
            var filePath = Path.Combine(bookDir, "essence_chapters.json");
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<EssenceChapterSelectionFile>(json, _bookInfoJsonReadOptions);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 加载精华章配置失败: {ex.Message}");
                return null;
            }
        }

        public async Task<CrawledContent?> LoadCrawledContentAsync(string bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return null;

            var bookDir = Path.Combine(_crawledBasePath, bookId);
            var bookInfoPath = Path.Combine(bookDir, "book_info.json");
            if (!File.Exists(bookInfoPath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(bookInfoPath);
                var info = JsonSerializer.Deserialize<BookInfoFile>(json, _bookInfoJsonReadOptions);
                if (info == null)
                {
                    return null;
                }

                var content = new CrawledContent
                {
                    BookId = string.IsNullOrWhiteSpace(info.BookId) ? bookId : info.BookId,
                    BookTitle = info.Title,
                    Author = info.Author,
                    SourceUrl = info.SourceUrl,
                    SourceSite = info.SourceSite,
                    CrawledAt = info.CrawledAt,
                    TotalChapters = info.ChapterCount,
                    TotalWords = info.TotalWords
                };

                foreach (var chapter in info.Chapters.OrderBy(c => c.Index))
                {
                    content.Chapters.Add(new CrawledChapter
                    {
                        Index = chapter.Index,
                        Title = chapter.Title,
                        FileName = chapter.FileName,
                        WordCount = chapter.WordCount,
                        Url = chapter.Url
                    });
                }

                return content;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 加载爬取内容失败: {ex.Message}");
                return null;
            }
        }

        public async Task<string> LoadChapterContentAsync(string bookId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(bookId) || string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var bookDir = Path.Combine(_crawledBasePath, bookId);
            var chapterPath = Path.Combine(bookDir, "chapters", fileName);
            if (!File.Exists(chapterPath))
            {
                return string.Empty;
            }

            try
            {
                return await File.ReadAllTextAsync(chapterPath);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 读取章节文件失败: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<string> LoadCrawledExcerptAsync(
            string bookId, 
            int maxChapters = 5, 
            int maxCharsPerChapter = 2000,
            int maxTotalChars = 8000)
        {
            var content = await LoadCrawledContentAsync(bookId);
            if (content == null || content.Chapters.Count == 0)
            {
                return string.Empty;
            }

            IEnumerable<CrawledChapter> chapters = content.Chapters.OrderBy(c => c.Index).Take(maxChapters);
            var essence = await LoadEssenceChapterSelectionFileAsync(bookId);
            if (essence?.SelectedIndexes != null && essence.SelectedIndexes.Count > 0)
            {
                var selected = new List<CrawledChapter>();
                var chapterMap = content.Chapters
                    .GroupBy(c => c.Index)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var idx in essence.SelectedIndexes)
                {
                    if (idx <= 0) continue;
                    if (chapterMap.TryGetValue(idx, out var ch))
                    {
                        selected.Add(ch);
                    }
                }

                if (selected.Count > 0)
                {
                    if (selected.Count < maxChapters)
                    {
                        var exists = new HashSet<int>(selected.Select(c => c.Index));
                        foreach (var ch in content.Chapters.OrderBy(c => c.Index))
                        {
                            if (selected.Count >= maxChapters) break;
                            if (exists.Add(ch.Index))
                            {
                                selected.Add(ch);
                            }
                        }
                    }

                    chapters = selected.Take(maxChapters);
                    TM.App.Log($"[NovelCrawlerService] 使用精华章生成AI上下文: {Math.Min(selected.Count, maxChapters)}/{maxChapters}");
                }
            }
            var excerpts = new List<string>();
            var totalChars = 0;

            foreach (var chapter in chapters)
            {
                if (totalChars >= maxTotalChars)
                {
                    TM.App.Log($"[NovelCrawlerService] 上下文已达 {totalChars} 字，停止加载更多章节");
                    break;
                }

                var text = await LoadChapterContentAsync(bookId, chapter.FileName);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.Length > maxCharsPerChapter)
                {
                    text = text.Substring(0, maxCharsPerChapter) + "\n\n...[内容截断]...";
                }

                var chapterText = $"### {chapter.Title}\n\n{text}";
                if (totalChars + chapterText.Length > maxTotalChars)
                {
                    var remaining = maxTotalChars - totalChars;
                    if (remaining > 200)
                    {
                        chapterText = chapterText.Substring(0, remaining) + "\n\n...[上下文截断]...";
                        excerpts.Add(chapterText);
                    }
                    break;
                }

                excerpts.Add(chapterText);
                totalChars += chapterText.Length;
            }

            var result = string.Join("\n\n---\n\n", excerpts);
            TM.App.Log($"[NovelCrawlerService] 生成AI上下文摘录: {excerpts.Count} 章, {result.Length} 字符");
            return result;
        }

        public void DeleteCrawledContent(string bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return;

            try
            {
                var bookDir = Path.Combine(_crawledBasePath, bookId);
                if (Directory.Exists(bookDir))
                {
                    Directory.Delete(bookDir, true);
                    TM.App.Log($"[NovelCrawlerService] 已删除爬取内容目录: {bookDir}");
                }

                var v1StorageDir = StoragePathHelper.GetModulesStoragePath("Design/SmartParsing/BookAnalysis/Crawled");
                var v1StorageFile = Path.Combine(v1StorageDir, $"{bookId}.json");
                if (File.Exists(v1StorageFile))
                {
                    File.Delete(v1StorageFile);
                    TM.App.Log($"[NovelCrawlerService] 已删除历史存储文件: {v1StorageFile}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 删除爬取内容失败: {ex.Message}");
            }
        }

        private static string SanitizeFileNamePart(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "章节";
            }

            var sanitized = string.Join("", text.Split(Path.GetInvalidFileNameChars()))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "章节";
            }

            return sanitized.Length > 80 ? sanitized.Substring(0, 80) : sanitized;
        }

        private class BookInfoFile
        {
            [System.Text.Json.Serialization.JsonPropertyName("BookId")] public string BookId { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("Author")] public string Author { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("SourceUrl")] public string SourceUrl { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("SourceSite")] public string SourceSite { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("CrawledAt")] public DateTime CrawledAt { get; set; } = DateTime.Now;
            [System.Text.Json.Serialization.JsonPropertyName("ChapterCount")] public int ChapterCount { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("TotalWords")] public int TotalWords { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Chapters")] public List<BookInfoChapterFile> Chapters { get; set; } = new();
        }

        private class EssenceChapterSelectionFile
        {
            [System.Text.Json.Serialization.JsonPropertyName("BookId")] public string BookId { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("BookTitle")] public string BookTitle { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("Author")] public string Author { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("TargetCount")] public int TargetCount { get; set; } = 10;
            [System.Text.Json.Serialization.JsonPropertyName("Strategy")] public string Strategy { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
            [System.Text.Json.Serialization.JsonPropertyName("SelectedIndexes")] public List<int> SelectedIndexes { get; set; } = new();
            [System.Text.Json.Serialization.JsonPropertyName("GoldenIndexes")] public List<int> GoldenIndexes { get; set; } = new();
            [System.Text.Json.Serialization.JsonPropertyName("AnchorIndexes")] public Dictionary<string, int> AnchorIndexes { get; set; } = new();
            [System.Text.Json.Serialization.JsonPropertyName("ReasonsByIndex")] public Dictionary<int, string> ReasonsByIndex { get; set; } = new();
            [System.Text.Json.Serialization.JsonPropertyName("RawAiContent")] public string RawAiContent { get; set; } = string.Empty;
        }

        public async Task SaveStructureBlueprintAsync(string bookId, object blueprint)
        {
            if (string.IsNullOrWhiteSpace(bookId) || blueprint == null) return;

            try
            {
                var bookDir = Path.Combine(_crawledBasePath, bookId);
                StoragePathHelper.EnsureDirectoryExists(bookDir);

                var filePath = Path.Combine(bookDir, "structure_blueprint.json");
                var tempPath = filePath + ".tmp";

                var json = JsonSerializer.Serialize(blueprint, _bookInfoJsonOptions);
                await File.WriteAllTextAsync(tempPath, json);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.Move(tempPath, filePath);
                TM.App.Log($"[NovelCrawlerService] 已保存结构蓝图: {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 保存结构蓝图失败: {ex.Message}");
            }
        }

        private class BookInfoChapterFile
        {
            [System.Text.Json.Serialization.JsonPropertyName("Index")] public int Index { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("FileName")] public string FileName { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("WordCount")] public int WordCount { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Url")] public string Url { get; set; } = string.Empty;
        }
    }

    public class CrawlProgress
    {
        public string Message { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public int CurrentChapter { get; set; }
        public int TotalChapters { get; set; }
    }

    public class NovelInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
    }

    public class NovelCatalog
    {
        public string Title { get; set; } = string.Empty;
        public List<ChapterLink> Chapters { get; set; } = new();
    }

    public class ChapterLink
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
