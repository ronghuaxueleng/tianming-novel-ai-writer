using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Modules.Design.SmartParsing.BookAnalysis.Models;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class NovelCrawlerService
    {
        private readonly string _crawledBasePath;

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
                await using (var infoStream = File.Create(bookInfoPath))
                {
                    await JsonSerializer.SerializeAsync(infoStream, info, _bookInfoJsonOptions);
                }

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
                var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                await using (var writeStream = File.Create(tempPath))
                {
                    await JsonSerializer.SerializeAsync(writeStream, file, _bookInfoJsonOptions);
                }

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
                await using var stream = File.OpenRead(filePath);
                return await JsonSerializer.DeserializeAsync<EssenceChapterSelectionFile>(stream, _bookInfoJsonReadOptions);
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
                await using var stream = File.OpenRead(bookInfoPath);
                var info = await JsonSerializer.DeserializeAsync<BookInfoFile>(stream, _bookInfoJsonReadOptions);
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
                return await File.ReadAllTextAsync(chapterPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelCrawlerService] 读取章节文件失败: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<string> LoadCrawledExcerptAsync(
            string bookId,
            int maxChapters = 10,
            int maxCharsPerChapter = 2000,
            int maxTotalChars = 12000)
        {
            var contentTask = LoadCrawledContentAsync(bookId);
            var essenceTask = LoadEssenceChapterSelectionFileAsync(bookId);
            await Task.WhenAll(contentTask, essenceTask);

            var content = await contentTask;
            if (content == null || content.Chapters.Count == 0)
            {
                return string.Empty;
            }

            IEnumerable<CrawledChapter> chapters = content.Chapters.OrderBy(c => c.Index).Take(maxChapters);
            var essence = await essenceTask;
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
            var chapterLabels = new Dictionary<int, string>();
            if (essence != null)
            {
                var goldenSet = new HashSet<int>(essence.GoldenIndexes ?? new List<int>());
                var anchorFriendly = new Dictionary<string, string>
                {
                    ["p10"] = "结构锚点·开篇10%",
                    ["p50"] = "结构锚点·中段50%",
                    ["p80"] = "结构锚点·高潮80%",
                    ["ending"] = "结构锚点·结尾"
                };
                var anchorByIndex = new Dictionary<int, string>();
                foreach (var kv in (essence.AnchorIndexes ?? new Dictionary<string, int>()))
                {
                    if (kv.Value > 0)
                        anchorByIndex[kv.Value] = anchorFriendly.TryGetValue(kv.Key, out var fn) ? fn : $"结构锚点·{kv.Key}";
                }

                var selectedSet = new HashSet<int>(essence.SelectedIndexes ?? new List<int>());
                foreach (var idx in selectedSet)
                {
                    if (goldenSet.Contains(idx))
                        chapterLabels[idx] = "黄金章";
                    else if (anchorByIndex.TryGetValue(idx, out var anchorLabel))
                        chapterLabels[idx] = anchorLabel;
                    else
                    {
                        var reason = (essence.ReasonsByIndex != null && essence.ReasonsByIndex.TryGetValue(idx, out var r))
                            ? $"·{r}" : string.Empty;
                        chapterLabels[idx] = $"AI精华章{reason}";
                    }
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

                var label = chapterLabels.TryGetValue(chapter.Index, out var lbl) ? $" [{lbl}]" : string.Empty;
                var chapterText = $"### {chapter.Title}{label}\n\n{text}";
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
                var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                await using (var writeStream = File.Create(tempPath))
                {
                    await JsonSerializer.SerializeAsync(writeStream, blueprint, _bookInfoJsonOptions);
                }

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

}
