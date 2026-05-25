using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Modules.Design.SmartParsing.BookAnalysis.Crawler;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public class EssenceChapterSelectionService
    {
        private readonly IAITextGenerationService _aiTextGenerationService;

        private readonly IBookWebSearchProvider _webSearchProvider;

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

            System.Diagnostics.Debug.WriteLine($"[EssenceChapterSelectionService] {key}: {ex.Message}");
        }

        public EssenceChapterSelectionService(IAITextGenerationService aiTextGenerationService, IBookWebSearchProvider webSearchProvider)
        {
            _aiTextGenerationService = aiTextGenerationService;
            _webSearchProvider = webSearchProvider;
        }

        public async Task<EssenceChapterSelectionResult> SelectEssenceChaptersAsync(
            string bookTitle,
            string author,
            IReadOnlyList<ChapterInfo> chapters,
            int targetCount = 10,
            bool skipVipChapters = true,
            System.Threading.CancellationToken ct = default)
        {
            var result = new EssenceChapterSelectionResult();

            try
            {
                if (chapters == null || chapters.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "章节列表为空";
                    return result;
                }

                targetCount = Math.Max(10, targetCount);

                var candidates = chapters
                    .Where(c => !skipVipChapters || !c.IsVip)
                    .OrderBy(c => c.Index)
                    .ToList();

                if (candidates.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "无可抓取章节（可能全为VIP）";
                    return result;
                }

                if (candidates.Count <= targetCount)
                {
                    result.Success = true;
                    result.Strategy = "fallback-all";
                    result.SelectedChapters = candidates;
                    return result;
                }

                var searchSummary = string.Empty;
                try
                {
                    var queryTitle = string.IsNullOrWhiteSpace(bookTitle) ? string.Empty : bookTitle.Trim();
                    var queryAuthor = string.IsNullOrWhiteSpace(author) ? string.Empty : author.Trim();
                    var baseQuery = string.IsNullOrWhiteSpace(queryAuthor) ? queryTitle : $"{queryTitle} {queryAuthor}";

                    var query = string.IsNullOrWhiteSpace(baseQuery)
                        ? "小说 精华章节 名场面 高能章节"
                        : $"{baseQuery} 精华章节 名场面 高能章节";

                    var search = await _webSearchProvider.SearchAsync(query, timeoutSeconds: 10);
                    if (search.Success && !string.IsNullOrWhiteSpace(search.Summary))
                    {
                        searchSummary = search.Summary.Trim();
                        if (searchSummary.Length > 2000)
                        {
                            searchSummary = searchSummary.Substring(0, 2000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EssenceChapterSelectionService] 搜索摘要获取失败: {ex.Message}");
                }

                var catalog = BuildCatalogText(candidates);
                var prompt = BuildPrompt(bookTitle, author, targetCount, searchSummary, catalog);

                var aiResult = await _aiTextGenerationService.GenerateAsync(prompt, ct);
                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    result.Success = false;
                    result.ErrorMessage = string.IsNullOrWhiteSpace(aiResult.ErrorMessage)
                        ? "AI未返回有效内容"
                        : aiResult.ErrorMessage;
                    return result;
                }

                result.RawAiContent = aiResult.Content;

                if (!TryParseAiJson(aiResult.Content, out var aiOutput, out var parseError))
                {
                    result.Success = false;
                    result.ErrorMessage = string.IsNullOrWhiteSpace(parseError) ? "AI输出解析失败" : parseError;
                    return result;
                }

                var desiredCount = Math.Max(targetCount, aiOutput.TargetCount ?? targetCount);
                var selected = MapToChapters(aiOutput, candidates, desiredCount);

                if (selected.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "未能从AI输出映射到有效章节";
                    return result;
                }

                result.Success = true;
                result.Strategy = string.IsNullOrWhiteSpace(searchSummary) ? "ai-only" : "search+ai";
                result.SelectedChapters = selected;
                foreach (var item in aiOutput.Chapters)
                {
                    if (item.Index <= 0) continue;
                    if (string.IsNullOrWhiteSpace(item.Reason)) continue;
                    if (!result.ReasonsByIndex.ContainsKey(item.Index))
                    {
                        result.ReasonsByIndex[item.Index] = item.Reason.Trim();
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EssenceChapterSelectionService] 选择精华章失败: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static string BuildCatalogText(IReadOnlyList<ChapterInfo> chapters)
        {
            var ordered = chapters.OrderBy(c => c.Index).ToList();
            var total = ordered.Count;

            IEnumerable<ChapterInfo> picked;
            if (total <= 200)
            {
                picked = ordered;
            }
            else
            {
                const int frontCount = 120;
                const int backCount = 30;
                var step = total <= 600 ? 10 : 20;

                var list = new List<ChapterInfo>();

                list.AddRange(ordered.Take(Math.Min(frontCount, total)));

                var midStart = Math.Min(frontCount, total);
                var midEndExclusive = Math.Max(midStart, total - backCount);
                for (var i = midStart; i < midEndExclusive; i += step)
                {
                    list.Add(ordered[i]);
                }

                if (total > backCount)
                {
                    list.AddRange(ordered.Skip(total - backCount));
                }

                picked = list
                    .GroupBy(c => c.Index)
                    .Select(g => g.First())
                    .OrderBy(c => c.Index)
                    .ToList();
            }

            var sb = new StringBuilder();
            foreach (var ch in picked)
            {
                sb.AppendLine($"{ch.Index}. {ch.Title}");
            }
            return sb.ToString();
        }

        private static string BuildPrompt(string bookTitle, string author, int targetCount, string searchSummary, string catalog)
        {
            var title = string.IsNullOrWhiteSpace(bookTitle) ? "(未知书名)" : bookTitle.Trim();
            var authorText = string.IsNullOrWhiteSpace(author) ? "" : $"（作者：{author.Trim()}）";

            var sb = new StringBuilder();
            sb.AppendLine("<role>Senior novel editor and book analysis specialist. Task: select from the chapter catalog the chapters that best represent the book's full content and structure as 'essence chapters'.</role>");
            sb.AppendLine();
            sb.AppendLine("<book_info>");
            sb.AppendLine($"书名：{title}{authorText}");
            sb.AppendLine("</book_info>");
            sb.AppendLine();
            sb.AppendLine($"<task>从目录中挑选 {targetCount} 个精华章节（必须>=10，尽量覆盖：开篇/世界观/关键转折/人物成长/高潮/结局）。</task>");
            sb.AppendLine();
            sb.AppendLine("<strict_rules>");
            sb.AppendLine("1) 只能从给定目录中选择，index必须是目录里的章节序号（整数）。");
            sb.AppendLine("2) 只输出JSON，禁止输出任何解释、Markdown、代码块标记、前后缀文字。");
            sb.AppendLine("3) chapters数组中每项必须包含：index, titleKeyword, reason。");
            sb.AppendLine("</strict_rules>");
            sb.AppendLine();
            sb.AppendLine("<output_format type=\"json\">");
            sb.AppendLine("{\n  \"targetCount\": 10,\n  \"chapters\": [\n    {\"index\": 1, \"titleKeyword\": \"开篇\", \"reason\": \"世界观与主角登场\"}\n  ]\n}");
            sb.AppendLine("</output_format>");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(searchSummary))
            {
                sb.AppendLine("<search_summary>");
                sb.AppendLine(searchSummary);
                sb.AppendLine("</search_summary>");
                sb.AppendLine();
            }

            sb.AppendLine("<chapter_catalog>");
            sb.AppendLine(catalog);
            sb.AppendLine("</chapter_catalog>");
            return sb.ToString();
        }

        private static bool TryParseAiJson(string raw, out AiEssenceOutput output, out string error)
        {
            output = new AiEssenceOutput();
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    error = "AI返回为空";
                    return false;
                }

                var text = raw.Trim();

                if (text.StartsWith("```", StringComparison.Ordinal))
                {
                    text = text.Trim('`');
                    text = text.Replace("json", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }

                var start = text.IndexOf('{');
                var end = text.LastIndexOf('}');
                if (start < 0 || end <= start)
                {
                    error = "未找到JSON对象";
                    return false;
                }

                var json = text.Substring(start, end - start + 1);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "JSON根节点不是对象";
                    return false;
                }

                if (root.TryGetProperty("targetCount", out var targetCountProp))
                {
                    output.TargetCount = ParseInt(targetCountProp);
                }

                if (root.TryGetProperty("chapters", out var chaptersProp) && chaptersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in chaptersProp.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;

                        var ch = new AiEssenceChapter();

                        if (item.TryGetProperty("index", out var idxProp))
                        {
                            ch.Index = ParseInt(idxProp) ?? 0;
                        }

                        if (item.TryGetProperty("titleKeyword", out var kwProp) && kwProp.ValueKind == JsonValueKind.String)
                        {
                            ch.TitleKeyword = kwProp.GetString() ?? string.Empty;
                        }

                        if (item.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                        {
                            ch.Reason = reasonProp.GetString() ?? string.Empty;
                        }

                        output.Chapters.Add(ch);
                    }
                }

                if (output.Chapters.Count == 0)
                {
                    error = "chapters为空";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int? ParseInt(JsonElement element)
        {
            try
            {
                return element.ValueKind switch
                {
                    JsonValueKind.Number => element.GetInt32(),
                    JsonValueKind.String => ParseIntFromText(element.GetString()),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ParseInt), ex);
                return null;
            }
        }

        private static int? ParseIntFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits)) return null;

            return int.TryParse(digits, out var value) ? value : null;
        }

        private static List<ChapterInfo> MapToChapters(AiEssenceOutput aiOutput, List<ChapterInfo> candidates, int desiredCount)
        {
            desiredCount = Math.Max(10, desiredCount);

            var mapByIndex = candidates
                .GroupBy(c => c.Index)
                .ToDictionary(g => g.Key, g => g.First());

            var selected = new List<ChapterInfo>();
            var used = new HashSet<int>();

            foreach (var item in aiOutput.Chapters)
            {
                if (item.Index <= 0) continue;

                if (mapByIndex.TryGetValue(item.Index, out var chapter))
                {
                    if (used.Add(chapter.Index))
                    {
                        selected.Add(chapter);
                    }
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.TitleKeyword))
                {
                    var match = candidates.FirstOrDefault(c => c.Title.Contains(item.TitleKeyword, StringComparison.OrdinalIgnoreCase));
                    if (match != null && used.Add(match.Index))
                    {
                        selected.Add(match);
                    }
                }
            }

            if (selected.Count < desiredCount)
            {
                foreach (var chapter in candidates)
                {
                    if (selected.Count >= desiredCount) break;
                    if (used.Add(chapter.Index))
                    {
                        selected.Add(chapter);
                    }
                }
            }

            return selected
                .OrderBy(c => c.Index)
                .ToList();
        }

        public class EssenceChapterSelectionResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public List<ChapterInfo> SelectedChapters { get; set; } = new();
            public string Strategy { get; set; } = string.Empty;
            public string RawAiContent { get; set; } = string.Empty;
            public Dictionary<int, string> ReasonsByIndex { get; set; } = new();
        }

        private class AiEssenceOutput
        {
            public int? TargetCount { get; set; }
            public List<AiEssenceChapter> Chapters { get; } = new();
        }

        private class AiEssenceChapter
        {
            public int Index { get; set; }
            public string TitleKeyword { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }
    }
}
