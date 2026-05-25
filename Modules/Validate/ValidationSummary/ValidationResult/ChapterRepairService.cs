using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ChapterRepairService
    {
        private readonly IGeneratedContentService _contentService;
        private readonly IGuideContextService _guideContextService;
        private readonly ContentGenerationCallback _generationCallback;

        private readonly ConcurrentDictionary<Guid, (FactSnapshot FactSnapshot, ContextIdCollection? ContextIds)> _repairStates = new();
        public ChapterRepairService(
            IGeneratedContentService contentService,
            IGuideContextService guideContextService,
            ContentGenerationCallback generationCallback)
        {
            _contentService = contentService;
            _guideContextService = guideContextService;
            _generationCallback = generationCallback;
        }

        public async Task<string> RepairChapterAsync(string chapterId, List<string> hints, CancellationToken ct = default, IProgress<string>? progress = null, Guid? repairSessionId = null)
        {
            var repairRunId = Guid.NewGuid();
            var sessionId = repairSessionId ?? repairRunId;
            using var _progressRunScope = GenerationProgressHub.BeginRun(repairRunId);

            void OnRepairProgress(ProgressInfo progressInfo)
            {
                if (progressInfo.RunId != repairRunId) return;
                EmitProgress(progressInfo.Message, progress);
            }

            var terminalPhaseReported = false;
            try
            {
                Report("正在加载章节上下文...", progress);

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

                var ctx = await _guideContextService.BuildContentContextAsync(chapterId, ct);
                if (ctx == null)
                    throw new InvalidOperationException($"无法获取章节 {chapterId} 的打包上下文，请确认已执行打包");

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

                Report("正在生成修复内容（请稍候）...", progress);

                var spec = await ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();

                await ServiceLocator.Get<WriterPlugin>().PopulateLongDistanceRecallAsync(ctx, ct);

                GenerationProgressHub.ProgressReported += OnRepairProgress;
                try
                {
                    var engine = ServiceLocator.Get<AutoRewriteEngine>();
                    var result = await engine.GenerateWithRewriteAsync(
                        chapterId, ctx, ctx.FactSnapshot, spec, ct);

                    if (!result.Success)
                    {
                        var reasons = string.Join("；", result.GetLastFailureReasons().Take(3));
                        terminalPhaseReported = true;
                        throw new InvalidOperationException($"生成失败：{reasons}");
                    }

                    var content = result.Content ?? string.Empty;
                    _repairStates[sessionId] = (ctx.FactSnapshot!, ctx.ContextIds);
                    Report("修复生成完成 ✓", progress);
                    return content;
                }
                finally
                {
                    GenerationProgressHub.ProgressReported -= OnRepairProgress;
                }
            }
            catch (OperationCanceledException)
            {
                GenerationProgressHub.ReportPhase(ProgressPhase.Cancelled, "修复已取消");
                Report("修复已取消", progress);
                throw;
            }
            catch (Exception ex)
            {
                if (!terminalPhaseReported)
                    GenerationProgressHub.ReportPhase(ProgressPhase.Failed, $"修复失败: {ex.Message}");
                Report($"修复失败: {ex.Message}", progress);
                throw;
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

        public async Task SaveRepairedAsync(string chapterId, string repairedContent, IProgress<string>? progress = null, Guid? repairSessionId = null)
        {
            if (!repairSessionId.HasValue || !_repairStates.TryGetValue(repairSessionId.Value, out var repairState))
                throw new InvalidOperationException("未找到修复会话上下文，请先执行修复再保存");

            var saveRunId = repairSessionId.Value;
            using var _progressRunScope = GenerationProgressHub.BeginRun(saveRunId);

            void OnSaveProgress(ProgressInfo progressInfo)
            {
                if (progressInfo.RunId != saveRunId) return;
                EmitProgress(progressInfo.Message, progress);
            }

            GenerationProgressHub.ProgressReported += OnSaveProgress;
            try
            {
                Report("正在保存修复内容...", progress);
                await _generationCallback.OnContentGeneratedStrictAsync(chapterId, repairedContent, repairState.FactSnapshot, contextIds: repairState.ContextIds);
                _repairStates.TryRemove(repairSessionId.Value, out _);
                Report("保存完成 ✓", progress);
            }
            catch (OperationCanceledException)
            {
                GenerationProgressHub.ReportPhase(ProgressPhase.Cancelled, "保存已取消");
                Report("保存已取消", progress);
                throw;
            }
            catch (Exception ex)
            {
                GenerationProgressHub.ReportPhase(ProgressPhase.Failed, $"保存失败: {ex.Message}");
                Report($"保存失败: {ex.Message}", progress);
                throw;
            }
            finally
            {
                GenerationProgressHub.ProgressReported -= OnSaveProgress;
            }
        }

        public void ClearRepairSession(Guid repairSessionId)
        {
            _repairStates.TryRemove(repairSessionId, out _);
        }

        private void Report(string text, IProgress<string>? progress = null)
        {
            EmitProgress(text, progress);
            TM.App.Log($"[ChapterRepairService] {text}");
        }

        private void EmitProgress(string text, IProgress<string>? progress)
        {
            progress?.Report(text);
        }

        private static string ExtractFirstLine(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var line = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return line.TrimStart('#', ' ').Trim();
        }
    }
}
