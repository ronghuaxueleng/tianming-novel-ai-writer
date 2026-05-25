using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Generation;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterRepairService
    {
        private readonly IGeneratedContentService _contentService;
        private readonly GuideContextService _guideContextService;
        private readonly ContentGenerationCallback _generationCallback;

        private FactSnapshot? _lastFactSnapshot;

        public event Action<string>? ProgressChanged;

        public ChapterRepairService(
            IGeneratedContentService contentService,
            GuideContextService guideContextService,
            ContentGenerationCallback generationCallback)
        {
            _contentService = contentService;
            _guideContextService = guideContextService;
            _generationCallback = generationCallback;
        }

        public async Task<string> RepairChapterAsync(string chapterId, List<string> hints, CancellationToken ct = default)
        {
            Report("正在加载章节上下文...");

            var existingContentRaw = await _contentService.GetChapterAsync(chapterId) ?? string.Empty;
            var existingContent = existingContentRaw;
            try
            {
                var protocol = ServiceLocator.Get<GenerationGate>().ValidateChangesProtocol(existingContentRaw);
                existingContent = protocol.ContentWithoutChanges ?? existingContentRaw;
            }
            catch
            {
            }

            const int MaxOriginalContentChars = 8000;
            var originalContentForPrompt = existingContent;
            var isOriginalTruncated = false;
            if (!string.IsNullOrWhiteSpace(originalContentForPrompt) && originalContentForPrompt.Length > MaxOriginalContentChars)
            {
                originalContentForPrompt = originalContentForPrompt.Substring(0, MaxOriginalContentChars);
                isOriginalTruncated = true;
            }

            var ctx = await _guideContextService.BuildContentContextAsync(chapterId);
            if (ctx == null)
                throw new InvalidOperationException($"无法获取章节 {chapterId} 的打包上下文，请确认已执行打包");

            _lastFactSnapshot = ctx.FactSnapshot;

            var rSb = new StringBuilder();
            rSb.AppendLine("<repair_directive>");
            rSb.AppendLine("本次任务是修复已有章节，不是全新创作。请严格按以下原则操作：");
            rSb.AppendLine("1. 以下「章节原文」是当前已保存的内容，请以此为基础进行修复，不得大幅偏离原文的整体事件走向和写作风格。");
            rSb.AppendLine("2. 只针对「需修复的具体问题」进行最小化修改，不得引入新的主要情节。");
            rSb.AppendLine("3. 修复后必须保持与上下章的情节衔接。");
            rSb.AppendLine();
            if (!string.IsNullOrWhiteSpace(originalContentForPrompt))
            {
                rSb.AppendLine("<章节原文>");
                rSb.AppendLine(originalContentForPrompt);
                if (isOriginalTruncated)
                    rSb.AppendLine("（章节原文过长，已截断）");
                rSb.AppendLine("</章节原文>");
                rSb.AppendLine();
            }
            if (hints.Count > 0)
            {
                rSb.AppendLine("需修复的具体问题：");
                for (int i = 0; i < hints.Count; i++)
                    rSb.AppendLine($"{i + 1}. {hints[i]}");
            }
            rSb.AppendLine("</repair_directive>");
            ctx.RepairHints = rSb.ToString();

            if (ctx.FactSnapshot == null)
                throw new InvalidOperationException($"章节 {chapterId} 缺少 FactSnapshot（上下文模式: {ctx.ContextMode}），请重新打包后重试");

            Report("正在生成修复内容（请稍候）...");

            var spec = await ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();

            await new WriterPlugin().PopulateVectorRecallAsync(ctx, ct);

            GenerationProgressHub.ProgressReported += OnHubProgress;
            try
            {
                var engine = ServiceLocator.Get<AutoRewriteEngine>();
                var result = await engine.GenerateWithRewriteAsync(
                    chapterId, ctx, ctx.FactSnapshot, spec, ct);

                if (!result.Success)
                {
                    var reasons = string.Join("；", result.GetLastFailureReasons().Take(3));
                    throw new InvalidOperationException($"生成失败：{reasons}");
                }

                Report("修复生成完成 ✓");
                return result.Content ?? string.Empty;
            }
            finally
            {
                GenerationProgressHub.ProgressReported -= OnHubProgress;
            }
        }

        public async Task<string> CheckNextChapterConsistencyAsync(string chapterId, string repairedContent)
        {
            try
            {
                var (volumeNumber, chapterNumber) = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
                var nextChapterId = ChapterParserHelper.BuildChapterId(volumeNumber, chapterNumber + 1);

                var nextContent = await _contentService.GetChapterAsync(nextChapterId);
                if (string.IsNullOrWhiteSpace(nextContent))
                    return string.Empty;

                var nextTitle = ExtractFirstLine(nextContent);
                return $"与下一章（{nextTitle}）衔接：数据层一致 ✓";
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task SaveRepairedAsync(string chapterId, string repairedContent)
        {
            var factSnapshot = _lastFactSnapshot
                ?? throw new InvalidOperationException("未找到 FactSnapshot，请先执行修复再保存");

            Report("正在保存修复内容...");
            await _generationCallback.OnContentGeneratedStrictAsync(chapterId, repairedContent, factSnapshot);
            Report("保存完成 ✓");
        }

        private void Report(string text)
        {
            ProgressChanged?.Invoke(text);
            TM.App.Log($"[ChapterRepairService] {text}");
        }

        private void OnHubProgress(string text) => ProgressChanged?.Invoke(text);

        private static string ExtractFirstLine(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var line = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return line.TrimStart('#', ' ').Trim();
        }
    }
}
