using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        private static bool IsLikelyChapterWritingQuestion(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return false;
            var t = userText.Replace(" ", string.Empty);
            return t.Contains("怎么写", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("如何写", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("写法", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("写作思路", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("写作建议", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("写作要点", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("应该怎么写", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("怎么展开", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("怎么设计", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("大纲", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("提纲", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("梗概", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("概要", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("写什么", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("怎么安排", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("怎么衔接", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("如何衔接", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("怎么用", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("如何用", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("用法", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("格式", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEditModeExplicitGenerationIntent(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return false;
            var t = userText.Replace(" ", string.Empty);

            if (ChapterDirectiveParser.HasContinueDirective(t)) return true;
            if (ChapterDirectiveParser.HasRewriteDirective(t)) return true;
            if (t.Contains("@chapter", StringComparison.OrdinalIgnoreCase) || t.Contains("@章节", StringComparison.OrdinalIgnoreCase)) return true;

            var hasStrongVerb = t.Contains("生成", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("续写", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("创作", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("开始生成", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("帮我生成", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("请生成", StringComparison.OrdinalIgnoreCase)
                                || t.Contains("直接生成", StringComparison.OrdinalIgnoreCase);

            if (!hasStrongVerb) return false;

            return ChapterParserHelper.ContainsChapterReference(t)
                   || ChapterParserHelper.ParseChapterRange(t).HasValue
                   || (ChapterParserHelper.ParseChapterRanges(t)?.Count ?? 0) > 0
                   || (ChapterParserHelper.ParseChapterNumberList(t)?.Count ?? 0) > 0
                   || ChapterParserHelper.ParseFromNaturalLanguage(t).chapter.HasValue;
        }

        private async Task<string?> TryBuildEditModeGenerationRedirectMessageAsync(string userText)
        {
            if (!IsEditModeExplicitGenerationIntent(userText)) return null;
            if (IsLikelyChapterWritingQuestion(userText)) return null;

            int? explicitVolume = null;
            var (volFromNl, chFromNl) = ChapterParserHelper.ParseFromNaturalLanguage(userText);
            if (volFromNl.HasValue && volFromNl.Value > 0)
                explicitVolume = volFromNl.Value;
            else
            {
                var extractedVol = ChapterParserHelper.ExtractVolumeNumber(userText);
                if (extractedVol > 0)
                    explicitVolume = extractedVol;
            }

            var requestedNumbers = new SortedSet<int>();
            var ranges = ChapterParserHelper.ParseChapterRanges(userText);
            var range = ChapterParserHelper.ParseChapterRange(userText);
            var list = ChapterParserHelper.ParseChapterNumberList(userText);

            if (ranges != null && ranges.Count > 0)
            {
                foreach (var (start, end) in ranges)
                {
                    for (var i = start; i <= end; i++)
                    {
                        requestedNumbers.Add(i);
                        if (requestedNumbers.Count >= 500) break;
                    }
                    if (requestedNumbers.Count >= 500) break;
                }
            }
            else if (range.HasValue)
            {
                for (var i = range.Value.start; i <= range.Value.end; i++)
                {
                    requestedNumbers.Add(i);
                    if (requestedNumbers.Count >= 500) break;
                }
            }
            else if (list != null && list.Count > 0)
            {
                foreach (var n in list)
                {
                    requestedNumbers.Add(n);
                    if (requestedNumbers.Count >= 500) break;
                }
            }
            else if (chFromNl.HasValue && chFromNl.Value > 0)
            {
                requestedNumbers.Add(chFromNl.Value);
            }

            var validateError = await ValidateRequestedChaptersAsync(requestedNumbers, explicitVolume, userText);
            var redirect = "当前为 Edit 模式（查询与知识库编辑，不执行生成）。请切换到 Plan/Agent 模式后再执行生成。";
            if (string.IsNullOrWhiteSpace(validateError))
                return redirect;

            return redirect + "\n\n" + validateError;
        }

        private static (int? Volume, int Chapter) TryExtractStepChapterRequest(string? title, string? detail)
        {
            var exactChapterId = ExtractChapterIdFromDetail(detail);
            if (!string.IsNullOrWhiteSpace(exactChapterId))
            {
                var parsedId = ChapterParserHelper.ParseChapterId(exactChapterId);
                if (parsedId.HasValue)
                    return (parsedId.Value.volumeNumber, parsedId.Value.chapterNumber);
            }

            var (volFromTitle, chFromTitle) = ChapterParserHelper.ParseFromNaturalLanguage(title ?? string.Empty);
            if (chFromTitle.HasValue && chFromTitle.Value > 0)
            {
                var explicitVol = volFromTitle;
                if (!explicitVol.HasValue || explicitVol.Value <= 0)
                {
                    var extractedVol = ChapterParserHelper.ExtractVolumeNumber(title ?? string.Empty);
                    if (extractedVol > 0)
                        explicitVol = extractedVol;
                }
                return (explicitVol, chFromTitle.Value);
            }

            var (volFromDetail, chFromDetail) = ChapterParserHelper.ParseFromNaturalLanguage(detail ?? string.Empty);
            if (chFromDetail.HasValue && chFromDetail.Value > 0)
            {
                var explicitVol = volFromDetail;
                if (!explicitVol.HasValue || explicitVol.Value <= 0)
                {
                    var extractedVol = ChapterParserHelper.ExtractVolumeNumber(detail ?? string.Empty);
                    if (extractedVol > 0)
                        explicitVol = extractedVol;
                }
                return (explicitVol, chFromDetail.Value);
            }

            return (null, 0);
        }

        private async Task<string?> ValidateRequestedChaptersAsync(
            SortedSet<int> requestedNumbers,
            int? explicitVolume,
            string userText)
        {
            if (requestedNumbers.Count == 0)
            {
                return null;
            }

            var contentService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();

            if (!ChapterDirectiveParser.HasContinueDirective(userText))
            {
                try
                {
                    var generatedChapters = await contentService.GetGeneratedChaptersAsync();
                    var minRequested = requestedNumbers.Min();
                    var maxRequested = requestedNumbers.Max();

                    if (maxRequested > minRequested)
                    {
                        var expectedCount = maxRequested - minRequested + 1;
                        if (expectedCount != requestedNumbers.Count)
                        {
                            var missingCount = expectedCount - requestedNumbers.Count;
                            var missing = new List<int>();
                            for (var i = minRequested; i <= maxRequested && missing.Count < 20; i++)
                            {
                                if (!requestedNumbers.Contains(i))
                                    missing.Add(i);
                            }

                            var missingList = missing.Count > 0
                                ? string.Join("、", missing.Select(n => n.ToString()))
                                : string.Empty;
                            var suffix = missingCount > missing.Count ? $"...（共缺 {missingCount} 章）" : string.Empty;
                            GlobalToast.Warning("检测到跨章生成", "本次请求章节不连续，请先补齐中间章节");
                            return $"⚠ 跨章警告：本次请求章节不连续（第 {minRequested}～{maxRequested} 章之间存在缺口）。\n" +
                                   (string.IsNullOrWhiteSpace(missingList) ? string.Empty : $"缺失：{missingList}{suffix}\n") +
                                   "请按章节顺序连续生成，保证剧情连贯性。";
                        }
                    }

                    if (generatedChapters.Count == 0 && minRequested > 1)
                    {
                        GlobalToast.Warning("检测到跨章生成", $"尚无章节，但请求从第 {minRequested} 章开始，请先从第 1 章开始");
                        return $"⚠ 跨章警告：当前尚未生成任何章节，但请求从第 {minRequested} 章开始生成。\n" +
                               $"请先从第 1 章开始，保证章节连贯性。";
                    }
                    else if (generatedChapters.Count > 0)
                    {
                        var frontier = generatedChapters
                            .OrderBy(c => c.ChapterNumber)
                            .ThenBy(c => c.VolumeNumber)
                            .Last();

                        var maxGenerated = frontier.ChapterNumber;
                        var generatedNums = generatedChapters
                            .Select(c => c.ChapterNumber)
                            .Where(n => n > 0)
                            .ToHashSet();

                        if (maxGenerated > 0)
                        {
                            var missingHistory = new List<int>();
                            for (var i = 1; i <= maxGenerated && missingHistory.Count < 20; i++)
                            {
                                if (!generatedNums.Contains(i))
                                    missingHistory.Add(i);
                            }

                            if (missingHistory.Count > 0 && minRequested >= maxGenerated + 1)
                            {
                                var missingList = string.Join("、", missingHistory.Select(n => n.ToString()));
                                GlobalToast.Warning("检测到章节缺口", "历史章节存在缺口，请先补齐后再继续向后生成");
                                return $"⚠ 连贯性警告：当前虽然已生成到第 {maxGenerated} 章（{frontier.Title}），但历史章节存在缺口。\n" +
                                       $"缺失章节（部分）：{missingList}{(missingHistory.Count >= 20 ? "..." : string.Empty)}\n\n" +
                                       "请先补齐缺失章节，再继续生成后续章节。";
                            }

                            if (minRequested > maxGenerated + 1)
                            {
                                var missingStart = maxGenerated + 1;
                                var missingEnd = minRequested - 1;
                                var missingDesc = missingStart == missingEnd
                                    ? $"第 {missingStart} 章"
                                    : $"第 {missingStart}～{missingEnd} 章";

                                GlobalToast.Warning("检测到跨章生成",
                                    $"当前最新章节为第 {maxGenerated} 章，目标第 {minRequested} 章存在跳跃，请先补全中间章节");
                                return $"⚠ 跨章警告：当前已生成到第 {maxGenerated} 章（{frontier.Title}），" +
                                       $"但请求生成第 {minRequested} 章，跳过了 {missingDesc}（共 {missingEnd - missingStart + 1} 章未生成）。\n\n" +
                                       $"为保证章节连贯性，请先生成 {missingDesc}。\n" +
                                       $"如确需跳过中间章节，请先手动在生成内容界面删除对应章节预规划，或使用 @重写 指令覆盖已有章节。";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKConversationViewModel] 跨章检测失败（非致命，继续执行）: {ex.Message}");
                }
            }

            var existing = new List<string>();
            var ambiguousExisting = new List<string>();

            List<int>? availableVolumes = null;
            IList<TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData>? designs = null;

            if (!explicitVolume.HasValue)
            {
                try
                {
                    var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                    await volumeService.InitializeAsync();
                    var all = volumeService.GetAllVolumeDesigns();

                    designs = all;
                    availableVolumes = all
                        .Where(v => v.VolumeNumber > 0)
                        .Select(v => v.VolumeNumber)
                        .Distinct()
                        .OrderBy(v => v)
                        .ToList();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SKConversationViewModel] 预校验加载分卷设计失败: {ex.Message}");
                }
            }

            foreach (var chapterNumber in requestedNumbers)
            {
                if (chapterNumber <= 0)
                {
                    continue;
                }

                if (explicitVolume.HasValue && explicitVolume.Value > 0)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(explicitVolume.Value, chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (designs == null || availableVolumes == null || availableVolumes.Count == 0)
                {
                    continue;
                }

                var matches = designs
                    .Where(v => v.VolumeNumber > 0)
                    .Where(v => v.StartChapter > 0)
                    .Where(v => v.EndChapter <= 0
                        ? chapterNumber >= v.StartChapter
                        : chapterNumber >= v.StartChapter && chapterNumber <= v.EndChapter)
                    .Select(v => v.VolumeNumber)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                if (matches.Count == 1)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(matches[0], chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (matches.Count == 0 && availableVolumes.Count == 1)
                {
                    var chapterId = ChapterParserHelper.BuildChapterId(availableVolumes[0], chapterNumber);
                    if (contentService.ChapterExists(chapterId))
                    {
                        existing.Add(chapterId);
                    }
                    continue;
                }

                if (matches.Count > 0)
                {
                    foreach (var vol in matches)
                    {
                        var candidateId = ChapterParserHelper.BuildChapterId(vol, chapterNumber);
                        if (contentService.ChapterExists(candidateId))
                        {
                            ambiguousExisting.Add(candidateId);
                        }
                    }
                }
            }

            if (existing.Count > 0)
            {
                var list = string.Join("、", existing.Distinct().Take(6));
                var suffix = existing.Distinct().Count() > 6 ? "..." : string.Empty;
                var first = existing[0];
                return $"检测到目标章节已存在：{list}{suffix}。\n" +
                       $"如需重新生成请使用 @重写:{first}；\n" +
                       $"如需生成新章，请从未生成的章节开始，或明确卷号（如“生成第X卷第Y章”）。";
            }

            if (ambiguousExisting.Count > 0)
            {
                var list = string.Join("、", ambiguousExisting.Distinct().Take(6));
                var suffix = ambiguousExisting.Distinct().Count() > 6 ? "..." : string.Empty;
                return $"检测到请求的章节在多个卷中可能已生成：{list}{suffix}。\n" +
                       $"为避免误生成，请明确卷号（如“生成第X卷第Y章”），或使用 @重写:volN_chM。";
            }

            return null;
        }

        private async Task<string?> ValidateChapterGenerationRequestBeforeExecutionAsync(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                return null;
            }

            if (ChapterDirectiveParser.HasRewriteDirective(userText) || userText.Contains("重写", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!IsExplicitChapterGenerationRequest(userText))
            {
                return null;
            }

            var ranges = ChapterParserHelper.ParseChapterRanges(userText);
            var range = ChapterParserHelper.ParseChapterRange(userText);
            var chapterList = ChapterParserHelper.ParseChapterNumberList(userText);

            int? explicitVolume = null;
            var (volFromNl, chFromNl) = ChapterParserHelper.ParseFromNaturalLanguage(userText);
            if (volFromNl.HasValue && volFromNl.Value > 0)
            {
                explicitVolume = volFromNl.Value;
            }
            else
            {
                var extractedVol = ChapterParserHelper.ExtractVolumeNumber(userText);
                if (extractedVol > 0)
                {
                    explicitVolume = extractedVol;
                }
            }

            var requestedNumbers = new SortedSet<int>();
            if (ranges != null && ranges.Count > 0)
            {
                foreach (var (start, end) in ranges)
                {
                    for (var i = start; i <= end; i++)
                    {
                        requestedNumbers.Add(i);
                        if (requestedNumbers.Count >= 500)
                        {
                            break;
                        }
                    }
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (range.HasValue)
            {
                for (var i = range.Value.start; i <= range.Value.end; i++)
                {
                    requestedNumbers.Add(i);
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (chapterList != null && chapterList.Count > 0)
            {
                foreach (var n in chapterList)
                {
                    requestedNumbers.Add(n);
                    if (requestedNumbers.Count >= 500)
                    {
                        break;
                    }
                }
            }
            else if (chFromNl.HasValue && chFromNl.Value > 0)
            {
                requestedNumbers.Add(chFromNl.Value);
            }
            return await ValidateRequestedChaptersAsync(requestedNumbers, explicitVolume, userText);
        }

        private static bool IsExplicitChapterGenerationRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!ChapterParserHelper.ContainsChapterReference(text)
                && ChapterParserHelper.ParseChapterRange(text) == null
                && ChapterParserHelper.ParseChapterRanges(text) == null
                && ChapterParserHelper.ParseChapterNumberList(text) == null)
            {
                return false;
            }

            var t = text.Replace(" ", string.Empty);
            if (t.Contains("重写", StringComparison.OrdinalIgnoreCase) || t.Contains("改写", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return t.Contains("生成", StringComparison.OrdinalIgnoreCase)
                || t.Contains('写')
                || t.Contains("创作", StringComparison.OrdinalIgnoreCase)
                || t.Contains("续写", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeChapterHint(string userInput, string rawContent)
        {
            var (start, end) = ChapterParserHelper.ParseChapterRange(userInput) ?? (0, 0);
            var ranges = ChapterParserHelper.ParseChapterRanges(userInput);
            var list = ChapterParserHelper.ParseChapterNumberList(userInput);
            var (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(userInput);

            if (!vol.HasValue && !ch.HasValue && start <= 0 && (ranges == null || ranges.Count == 0) && (list == null || list.Count == 0))
            {
                (start, end) = ChapterParserHelper.ParseChapterRange(rawContent) ?? (0, 0);
                ranges = ChapterParserHelper.ParseChapterRanges(rawContent);
                list = ChapterParserHelper.ParseChapterNumberList(rawContent);
                (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(rawContent);
            }

            if (ranges != null && ranges.Count > 0)
            {
                var parts = new List<string>();
                foreach (var (rangeStart, rangeEnd) in ranges)
                {
                    if (rangeStart > 0 && rangeEnd >= rangeStart)
                    {
                        parts.Add($"第{rangeStart}到{rangeEnd}章");
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join("和", parts);
                }
            }

            if (start > 0 && end >= start)
            {
                return $"第{start}到{end}章";
            }

            if (list != null && list.Count > 0)
            {
                return $"第{string.Join("、", list)}章";
            }

            if (vol.HasValue && ch.HasValue)
            {
                return $"第{vol.Value}卷第{ch.Value}章";
            }

            if (ch.HasValue)
            {
                return $"第{ch.Value}章";
            }

            return userInput;
        }

        private static string? ExtractChapterIdFromDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return null;

            var match = ChapterIdFromDetailRegex.Match(detail);
            if (!match.Success)
                return null;

            var candidate = match.Groups[1].Value.Trim();

            var parsed = ChapterParserHelper.ParseChapterId(candidate);
            if (parsed.HasValue)
                return candidate;

            return null;
        }

        private void SyncSessionFromServiceAfterPersist()
        {
            var sessionId = _chatService.Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            _currentSessionId = sessionId;
            HasDraftConversation = false;

            var sessions = _chatService.Sessions.GetAllSessions();
            var current = sessions.Find(s => s.Id == sessionId);
            if (current != null)
            {
                SessionTitle = current.Title;
            }
        }
    }
}
