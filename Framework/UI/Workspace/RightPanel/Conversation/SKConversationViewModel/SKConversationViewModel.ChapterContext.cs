using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 章节上下文解析

        private async Task<string?> ResolveChapterIdFromTextAsync(string userText)
        {
            ContentGuide? guide = null;
            try
            {
                guide = await _guideContextService.GetContentGuideAsync();
            }
            catch (Exception ex)
            {
                DebugLogOnce("LoadContentGuide", ex);
            }

            var referenceParser = ServiceLocator.Get<ReferenceParser>();
            var references = referenceParser.ParseReferences(userText);
            var chapterRef = references.FirstOrDefault(r => r.Type == "chapter" || r.Type == "rewrite");

            if (!string.IsNullOrEmpty(chapterRef?.Name))
            {
                if (ChapterExistsInGuide(guide, chapterRef.Name))
                {
                    TM.App.Log($"[SKConversationViewModel] 从 @{chapterRef.Type} 引用解析到章节: {chapterRef.Name}");
                    return chapterRef.Name;
                }
                TM.App.Log($"[SKConversationViewModel] @{chapterRef.Type} 引用的章节不存在: {chapterRef.Name}");
            }

            var chapterIdFromNL = await TryParseChapterIdFromNaturalLanguageAsync(userText, guide);
            if (!string.IsNullOrEmpty(chapterIdFromNL))
            {
                if (ChapterExistsInGuide(guide, chapterIdFromNL))
                {
                    TM.App.Log($"[SKConversationViewModel] 从自然语言解析到章节: {chapterIdFromNL}");
                    return chapterIdFromNL;
                }
                else
                {
                    TM.App.Log($"[SKConversationViewModel] 自然语言解析到的章节不存在: {chapterIdFromNL}");
                }
            }

            return null;
        }

        private async Task<string?> ResolveChapterIdTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var trimmed = token.Trim();

            ContentGuide? guide = null;
            try
            {
                guide = await _guideContextService.GetContentGuideAsync();
            }
            catch (Exception ex)
            {
                DebugLogOnce("LoadContentGuide_Token", ex);
            }

            var parsed = ChapterParserHelper.ParseChapterId(trimmed);
            if (parsed.HasValue)
            {
                var chapterId = ChapterParserHelper.BuildChapterId(parsed.Value.volumeNumber, parsed.Value.chapterNumber);
                return ChapterExistsInGuide(guide, chapterId) ? chapterId : null;
            }

            var nlChapterId = await TryParseChapterIdFromNaturalLanguageAsync(trimmed, guide);
            if (!string.IsNullOrEmpty(nlChapterId))
            {
                return ChapterExistsInGuide(guide, nlChapterId) ? nlChapterId : null;
            }

            return null;
        }

        private async Task<string?> TryParseChapterIdFromNaturalLanguageAsync(string text, ContentGuide? guide = null)
        {
            var (volume, chapter) = ChapterParserHelper.ParseFromNaturalLanguage(text);

            if (volume.HasValue && chapter.HasValue)
            {
                return ChapterParserHelper.BuildChapterId(volume.Value, chapter.Value);
            }

            if (chapter.HasValue)
            {
                return await FindUniqueChapterAcrossVolumesAsync(chapter.Value.ToString(), guide);
            }

            return null;
        }

        private async Task<string?> FindUniqueChapterAcrossVolumesAsync(string chapterNumber, ContentGuide? guide = null)
        {
            var matchedChapterIds = new List<string>();

            var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
            await volumeService.InitializeAsync();
            var volumeNumbers = volumeService.GetAllVolumeDesigns()
                .Select(v => v.VolumeNumber)
                .Where(v => v > 0)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            foreach (var vol in volumeNumbers)
            {
                var candidateId = ChapterParserHelper.BuildChapterId(vol, int.Parse(chapterNumber));
                if (ChapterExistsInGuide(guide, candidateId))
                {
                    matchedChapterIds.Add(candidateId);
                }
            }

            if (matchedChapterIds.Count == 1)
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章唯一匹配: {matchedChapterIds[0]}");
                return matchedChapterIds[0];
            }
            else if (matchedChapterIds.Count > 1)
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章存在多卷匹配: {string.Join(", ", matchedChapterIds)}，需要用户指定卷号");
                return null;
            }
            else
            {
                TM.App.Log($"[SKConversationViewModel] 第{chapterNumber}章未找到任何匹配");
                return null;
            }
        }

        private static string CleanChapterReferences(string userText)
        {
            var cleaned = ChapterReferencePrefixRegex.Replace(userText, string.Empty).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? userText : cleaned;
        }

        private static bool ChapterExistsInGuide(ContentGuide? guide, string chapterId)
        {
            if (guide?.Chapters == null) return false;
            if (guide.Chapters.TryGetValue(chapterId, out var entry))
            {
                return !string.IsNullOrEmpty(entry.Title) || entry.ChapterNumber > 0;
            }
            return false;
        }

        #endregion
    }
}
