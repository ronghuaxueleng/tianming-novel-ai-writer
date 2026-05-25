using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GeneratedContentService : IGeneratedContentService
    {
        private string ChaptersDirectory => StoragePathHelper.GetProjectChaptersPath();

        private static readonly Dictionary<string, (ChapterInfo Info, DateTime Modified)> _metaCache = new();
        private static readonly object _metaCacheLock = new();

        private static string[] _cachedFiles = Array.Empty<string>();
        private static DateTime _cachedFilesAt = DateTime.MinValue;
        private static string _cachedFilesDir = string.Empty;

        public GeneratedContentService()
        {
            _ = ChaptersDirectory;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateStaticCaches();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public void InvalidateStaticCaches()
        {
            lock (_metaCacheLock)
            {
                _metaCache.Clear();
                _cachedFiles = Array.Empty<string>();
                _cachedFilesAt = DateTime.MinValue;
                _cachedFilesDir = string.Empty;
            }
        }

        public Task SaveChapterAsync(string chapterId, string content)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new ArgumentException("章节ID不能为空", nameof(chapterId));

            var callback = ServiceLocator.Get<ContentGenerationCallback>();
            return callback.OnExternalContentSavedAsync(chapterId, content);
        }

        public async Task<string?> GetChapterAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return null;

            var filePath = GetChapterPath(chapterId);

            if (!File.Exists(filePath))
                return null;

            return await File.ReadAllTextAsync(filePath);
        }

        public async Task<List<ChapterInfo>> GetGeneratedChaptersAsync()
        {
            var chapters = new List<ChapterInfo>();

            if (!Directory.Exists(ChaptersDirectory))
                return chapters;

            var dir = ChaptersDirectory;
            var dirModified = Directory.Exists(dir)
                ? Directory.GetLastWriteTimeUtc(dir)
                : DateTime.MinValue;
            string[] files;
            lock (_metaCacheLock)
            {
                if (_cachedFilesDir == dir && _cachedFilesAt == dirModified && _cachedFiles.Length > 0)
                    files = _cachedFiles;
                else
                    files = null!;
            }
            if (files == null)
            {
                files = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly);
                lock (_metaCacheLock)
                {
                    _cachedFiles = files;
                    _cachedFilesAt = dirModified;
                    _cachedFilesDir = dir;
                }
            }

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var chapterId = Path.GetFileNameWithoutExtension(file);
                var modified = fileInfo.LastWriteTime;

                ChapterInfo? cached = null;
                lock (_metaCacheLock)
                {
                    if (_metaCache.TryGetValue(file, out var entry) && entry.Modified == modified)
                        cached = entry.Info;
                }

                if (cached != null)
                {
                    chapters.Add(cached);
                    continue;
                }

                var (volumeNumber, chapterNumber) = ParseChapterId(chapterId);
                var (title, wordCount) = await ReadChapterMetaAsync(file).ConfigureAwait(false);

                var info = new ChapterInfo
                {
                    Id = chapterId,
                    Title = title,
                    VolumeNumber = volumeNumber,
                    ChapterNumber = chapterNumber,
                    WordCount = wordCount,
                    CreatedTime = fileInfo.CreationTime,
                    ModifiedTime = modified,
                    FilePath = file
                };

                lock (_metaCacheLock) { _metaCache[file] = (info, modified); }
                chapters.Add(info);
            }

            return chapters
                .OrderBy(c => c.VolumeNumber)
                .ThenBy(c => c.ChapterNumber)
                .ToList();
        }

        public async Task<bool> DeleteChapterAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return false;

            var filePath = GetChapterPath(chapterId);

            if (!File.Exists(filePath))
                return false;

            try
            {
                try
                {
                    await ServiceLocator.Get<ContentGenerationCallback>().OnChapterDeletedAsync(chapterId);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GeneratedContentService] 章节清理失败（非致命，继续删除MD）: {chapterId}, {ex.Message}");
                }

                File.Delete(filePath);
                TM.App.Log($"[GeneratedContentService] 删除章节: {chapterId}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 删除章节失败: {chapterId}, {ex.Message}");
                return false;
            }
        }

        public bool ChapterExists(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return false;

            return File.Exists(GetChapterPath(chapterId));
        }

        public string GetChapterPath(string chapterId)
        {
            return Path.Combine(ChaptersDirectory, $"{chapterId}.md");
        }

        private static (int volumeNumber, int chapterNumber) ParseChapterId(string chapterId)
        {
            return ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
        }

        private static string NormalizeChapterTitle(string title)
        {
            return ChapterParserHelper.NormalizeChapterTitle(title);
        }

        private static async Task<(string Title, int WordCount)> ReadChapterMetaAsync(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? firstNonEmptyLine = null;
                string? title = null;

                var chineseCount = 0;
                var englishWords = 0;
                var lineIndex = 0;

                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                        break;

                    var trimmed = line.Trim();
                    if (firstNonEmptyLine == null && !string.IsNullOrWhiteSpace(trimmed))
                        firstNonEmptyLine = trimmed;

                    if (title == null && lineIndex < 10)
                    {
                        if (trimmed.StartsWith("# "))
                            title = NormalizeChapterTitle(trimmed.Substring(2).Trim());
                        else if (trimmed.StartsWith("## "))
                            title = NormalizeChapterTitle(trimmed.Substring(3).Trim());
                    }

                    AccumulateWordCounts(line, ref chineseCount, ref englishWords);
                    lineIndex++;
                }

                if (string.IsNullOrEmpty(title))
                {
                    if (!string.IsNullOrEmpty(firstNonEmptyLine))
                    {
                        title = firstNonEmptyLine.Length > 50
                            ? firstNonEmptyLine.Substring(0, 50) + "..."
                            : firstNonEmptyLine;
                    }
                    else
                    {
                        title = "未命名章节";
                    }
                }

                return (title, chineseCount + englishWords);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GeneratedContentService] 读取章节元数据失败 [{Path.GetFileName(filePath)}]: {ex.Message}");
                return ("未命名章节", 0);
            }
        }

        private static void AccumulateWordCounts(string line, ref int chineseCount, ref int englishWords)
        {
            var inWord = false;

            foreach (var c in line)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    chineseCount++;
                }

                var isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (isLetter)
                {
                    if (!inWord)
                    {
                        englishWords++;
                        inWord = true;
                    }
                }
                else
                {
                    inWord = false;
                }
            }
        }

        #region 分类（卷）管理

        public async Task<bool> VolumeExistsAsync(int volumeNumber)
        {
            var volumeService = ServiceLocator.Get<VolumeDesignService>();
            await volumeService.InitializeAsync();
            var scopeId = ServiceLocator.Get<IWorkScopeService>().CurrentSourceBookId;
            return volumeService.GetAllVolumeDesigns().Any(v =>
                v.VolumeNumber == volumeNumber
                && (string.IsNullOrEmpty(scopeId) || string.Equals(v.SourceBookId, scopeId, StringComparison.Ordinal)));
        }

        public async Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId)
        {
            if (string.IsNullOrWhiteSpace(sourceChapterId))
                throw new ArgumentException("章节ID不能为空", nameof(sourceChapterId));

            var parsed = ChapterParserHelper.ParseChapterId(sourceChapterId);
            if (parsed == null)
                throw new ArgumentException($"章节ID格式无效: {sourceChapterId}", nameof(sourceChapterId));

            var (volumeNumber, chapterNumber) = parsed.Value;

            if (!await VolumeExistsAsync(volumeNumber))
                throw new InvalidOperationException($"卷 {volumeNumber} 不存在");

            if (!ChapterExists(sourceChapterId))
                throw new InvalidOperationException($"来源章节 {sourceChapterId} 不存在");

            var sameVolumeNextId = ChapterParserHelper.BuildChapterId(volumeNumber, chapterNumber + 1);
            var targetChapterId = sameVolumeNextId;

            var volumeService = ServiceLocator.Get<VolumeDesignService>();
            await volumeService.InitializeAsync();
            var designs = volumeService.GetAllVolumeDesigns();

            var scopeId = ServiceLocator.Get<IWorkScopeService>().CurrentSourceBookId;
            if (!string.IsNullOrEmpty(scopeId))
                designs = designs.Where(v => string.Equals(v.SourceBookId, scopeId, StringComparison.Ordinal)).ToList();

            var currentDesign = designs.FirstOrDefault(v => v.VolumeNumber == volumeNumber);
            if (currentDesign != null)
            {
                var effectiveEndChapter = currentDesign.EndChapter;

                if (effectiveEndChapter <= 0)
                    effectiveEndChapter = await ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber);

                if (effectiveEndChapter > 0 && chapterNumber >= effectiveEndChapter)
                {
                    var nextDesign = designs
                        .Where(v => v.VolumeNumber > volumeNumber)
                        .OrderBy(v => v.VolumeNumber)
                        .FirstOrDefault();

                    if (nextDesign != null)
                    {
                        var nextStart = nextDesign.StartChapter > 0 ? nextDesign.StartChapter : 1;
                        targetChapterId = ChapterParserHelper.BuildChapterId(nextDesign.VolumeNumber, nextStart);
                        TM.App.Log($"[GeneratedContentService] 跨卷续写: {sourceChapterId} → {targetChapterId}（第{volumeNumber}卷末→第{nextDesign.VolumeNumber}卷首）");
                    }
                }
            }

            if (ChapterExists(targetChapterId))
                throw new InvalidOperationException($"目标章节 {targetChapterId} 已存在，请使用 @重写:{targetChapterId} 指令");

            TM.App.Log($"[GeneratedContentService] 从 {sourceChapterId} 生成下一章ID: {targetChapterId}");
            return targetChapterId;
        }

        #endregion
    }
}
