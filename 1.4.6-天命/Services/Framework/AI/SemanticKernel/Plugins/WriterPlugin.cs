using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Generate.Content.Services;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using System.Reflection;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true)]
    public class WriterPlugin
    {
        private PanelCommunicationService Comm => ServiceLocator.Get<PanelCommunicationService>();
        private VolumeDesignService VolumeDesignService => ServiceLocator.Get<VolumeDesignService>();

        public class SavedChapterResult
        {
            public string ChapterId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string SavedContent { get; set; } = string.Empty;
            public string DisplayContent { get; set; } = string.Empty;
            public string? ChangesJson { get; set; }
            public double? ChangesDurationSeconds { get; set; }
        }

        private async Task<string> GenerateDefaultNextChapterIdAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            var chapters = await contentService.GetGeneratedChaptersAsync();

            var baseChapterNumber = 0;
            if (CurrentChapterTracker.HasCurrentChapter)
            {
                var parsed = ChapterParserHelper.ParseChapterId(CurrentChapterTracker.CurrentChapterId);
                if (parsed.HasValue)
                {
                    baseChapterNumber = parsed.Value.chapterNumber;
                }
            }

            if (baseChapterNumber <= 0 && chapters.Count > 0)
            {
                baseChapterNumber = chapters.Max(c => c.ChapterNumber);
            }

            var targetChapterNumber = baseChapterNumber > 0 ? baseChapterNumber + 1 : 1;
            var volumeNumber = await ResolveVolumeNumberForChapterAsync(ct, targetChapterNumber);
            return ChapterParserHelper.BuildChapterId(volumeNumber, targetChapterNumber);
        }

        private async Task<int> ResolveVolumeNumberForChapterAsync(CancellationToken ct, int chapterNumber)
        {
            if (chapterNumber <= 0)
            {
                throw new InvalidOperationException("章节号无效");
            }

            ct.ThrowIfCancellationRequested();
            await VolumeDesignService.InitializeAsync();
            var designs = VolumeDesignService.GetAllVolumeDesigns();

            var _scopeId = ServiceLocator.Get<IWorkScopeService>().CurrentSourceBookId;
            if (!string.IsNullOrEmpty(_scopeId))
                designs = designs.Where(v => string.Equals(v.SourceBookId, _scopeId, StringComparison.Ordinal)).ToList();

            var matches = designs
                .Where(v => v.VolumeNumber > 0)
                .Where(v => v.StartChapter > 0)
                .Where(v => v.EndChapter <= 0
                    ? chapterNumber >= v.StartChapter
                    : chapterNumber >= v.StartChapter && chapterNumber <= v.EndChapter)
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0].VolumeNumber;
            }

            if (matches.Count == 0)
            {
                var volumeNumberFromGuide = await TryResolveVolumeNumberFromContentGuideAsync(ct, designs, chapterNumber);
                if (volumeNumberFromGuide.HasValue)
                {
                    return volumeNumberFromGuide.Value;
                }

                var availableVolumes = designs
                    .Where(v => v.VolumeNumber > 0)
                    .Select(v => v.VolumeNumber)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                if (availableVolumes.Count == 1)
                {
                    var soleVolume = designs.First(v => v.VolumeNumber == availableVolumes[0]);
                    if (soleVolume.StartChapter > 0 && soleVolume.EndChapter > 0)
                    {
                        TM.App.Log($"[WriterPlugin] 警告: 第{chapterNumber}章超出第{soleVolume.VolumeNumber}卷的设计范围({soleVolume.StartChapter}-{soleVolume.EndChapter})，该章节可能缺少规划/蓝图数据");
                    }
                    return availableVolumes[0];
                }

                throw new InvalidOperationException($"未找到包含第{chapterNumber}章的分卷范围，请在分卷设计中配置章节范围或明确卷号。");
            }

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            var currentVolPriority = 0;
            if (CurrentChapterTracker.HasCurrentChapter)
            {
                var currentParsed = ChapterParserHelper.ParseChapterId(CurrentChapterTracker.CurrentChapterId);
                if (currentParsed.HasValue)
                    currentVolPriority = currentParsed.Value.volumeNumber;
            }

            var orderedMatches = matches
                .OrderByDescending(m => m.VolumeNumber == currentVolPriority ? 1 : 0)
                .ThenBy(m => m.VolumeNumber)
                .ToList();

            foreach (var match in orderedMatches)
            {
                var candidateId = ChapterParserHelper.BuildChapterId(match.VolumeNumber, chapterNumber);
                if (!contentService.ChapterExists(candidateId))
                {
                    TM.App.Log($"[WriterPlugin] F3b消歧: 第{chapterNumber}章 → 第{match.VolumeNumber}卷（{candidateId}未落盘）");
                    return match.VolumeNumber;
                }
            }

            var existingIds = orderedMatches
                .Select(m => ChapterParserHelper.BuildChapterId(m.VolumeNumber, chapterNumber))
                .ToList();
            var idList = string.Join("、", existingIds);
            throw new InvalidOperationException(
                $"第{chapterNumber}章在以下卷中均已生成：{idList}。\n" +
                $"如需新建，请指定卷号（如\"生成第X卷第{chapterNumber}章\"）；\n" +
                $"如需重写，请使用 @重写:{existingIds.First()} 指令。");
        }

        private async Task<int?> TryResolveVolumeNumberFromContentGuideAsync(
            CancellationToken ct,
            IList<VolumeDesignData> volumeDesigns,
            int chapterNumber)
        {
            ct.ThrowIfCancellationRequested();

            var guideService = ServiceLocator.Get<GuideContextService>();
            var guide = await guideService.GetContentGuideAsync();
            if (guide?.Chapters == null || guide.Chapters.Count == 0)
            {
                return null;
            }

            var volumeNumbers = volumeDesigns
                .Where(v => v.VolumeNumber > 0)
                .Select(v => v.VolumeNumber)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            var matched = new List<int>();
            foreach (var vol in volumeNumbers)
            {
                var chapterId = ChapterParserHelper.BuildChapterId(vol, chapterNumber);
                if (guide.Chapters.ContainsKey(chapterId))
                {
                    matched.Add(vol);
                }
            }

            if (matched.Count == 1)
            {
                return matched[0];
            }

            return null;
        }

        private async Task ValidateChapterIdVolumeAsync(CancellationToken ct, string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
            {
                throw new InvalidOperationException("章节ID不能为空");
            }

            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (!parsed.HasValue)
            {
                throw new InvalidOperationException($"章节ID格式无效: {chapterId}");
            }

            ct.ThrowIfCancellationRequested();
            await VolumeDesignService.InitializeAsync();
            var _validateScopeId = ServiceLocator.Get<IWorkScopeService>().CurrentSourceBookId;
            var volumeExists = VolumeDesignService.GetAllVolumeDesigns()
                .Any(v => v.VolumeNumber == parsed.Value.volumeNumber
                       && (string.IsNullOrEmpty(_validateScopeId) || string.Equals(v.SourceBookId, _validateScopeId, StringComparison.Ordinal)));

            if (!volumeExists)
            {
                throw new InvalidOperationException($"卷 {parsed.Value.volumeNumber} 未在分卷设计中定义，无法生成。请先创建分卷或修正章节ID。");
            }
        }

        public async Task<SavedChapterResult> SaveExternalChapterAsync(
            CancellationToken ct,
            string title,
            string content,
            string chapterId = "")
        {
            ct.ThrowIfCancellationRequested();

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            if (string.IsNullOrWhiteSpace(chapterId))
            {
                chapterId = await GenerateDefaultNextChapterIdAsync(ct);
            }

            await ValidateChapterIdVolumeAsync(ct, chapterId);

            var _extParsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (_extParsed.HasValue && _extParsed.Value.chapterNumber == 1 && _extParsed.Value.volumeNumber > 1)
            {
                var _extPrevVol = _extParsed.Value.volumeNumber - 1;
                try
                {
                    var _extArchiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                    var _extPrevArchives = await _extArchiveStore.GetPreviousArchivesAsync(_extParsed.Value.volumeNumber);
                    if (!_extPrevArchives.Any(a => a.VolumeNumber == _extPrevVol))
                    {
                        TM.App.Log($"[WriterPlugin] 外部保存检测到新卷第1章，自动存档第{_extPrevVol}卷...");
                        var _extReconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                        await _extReconciler.AutoArchiveVolumeIfNeededAsync(_extPrevVol);
                        TM.App.Log($"[WriterPlugin] 第{_extPrevVol}卷自动存档完成");
                    }
                }
                catch (Exception _extArchiveEx)
                {
                    TM.App.Log($"[WriterPlugin] 外部保存第{_extPrevVol}卷自动存档失败（不阻断保存）: {_extArchiveEx.Message}");
                }
            }

            var savedContent = StripLeadingTitle(content ?? string.Empty);

            ct.ThrowIfCancellationRequested();
            var callback = ServiceLocator.Get<ContentGenerationCallback>();
            await callback.OnExternalContentSavedAsync(chapterId, savedContent);

            var persisted = await contentService.GetChapterAsync(chapterId) ?? savedContent;

            return new SavedChapterResult
            {
                ChapterId = chapterId,
                Title = title,
                SavedContent = persisted,
                DisplayContent = persisted
            };
        }

        private static SavedChapterResult BuildSavedChapterResult(
            string chapterId,
            string title,
            string savedContent,
            ProtocolValidationResult? protocol,
            double changesSeconds)
        {
            var result = new SavedChapterResult
            {
                ChapterId = chapterId,
                Title = title,
                SavedContent = savedContent,
                DisplayContent = protocol?.ContentWithoutChanges ?? savedContent,
                ChangesDurationSeconds = changesSeconds
            };

            if (protocol?.Changes != null)
            {
                result.ChangesJson = System.Text.Json.JsonSerializer.Serialize(protocol.Changes, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }

            return result;
        }

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
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[WriterPlugin] {key}: {ex.Message}");
        }

        [KernelFunction("GenerateChapter")]
        [Description("根据章节ID对应的打包数据和项目设定生成完整章节内容，并保存到当前项目的章节文件中。")]
        public async Task<string> GenerateChapterAsync(
            CancellationToken ct,
            [Description("章节ID，如 vol1_ch1。留空时根据当前启用的卷自动生成。")] string chapterId = "",
            [Description("写作风格要求，如'轻松幽默'，可选")] string style = "",
            [Description("目标字数，例如 3500，0 表示不强制")] int wordCount = 0)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapter: {chapterId}");

            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    const string msg = "[生成失败] 未指定章节ID。请使用 @chapter:volN_chM / @重写:volN_chM / @续写:volN_chM，或输入“第N卷第M章/第M章”（需分卷设计配置章节范围）。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                await ValidateChapterIdVolumeAsync(ct, chapterId);

                var _kfParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_kfParsed.HasValue && _kfParsed.Value.chapterNumber == 1 && _kfParsed.Value.volumeNumber > 1)
                {
                    var _kfPrevVol = _kfParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _kfArchiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                        var _kfPrevArchives = await _kfArchiveStore.GetPreviousArchivesAsync(_kfParsed.Value.volumeNumber);
                        if (!_kfPrevArchives.Any(a => a.VolumeNumber == _kfPrevVol))
                        {
                            TM.App.Log($"[WriterPlugin] 检测到新卷第1章，自动存档第{_kfPrevVol}卷...");
                            var _kfReconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                            await _kfReconciler.AutoArchiveVolumeIfNeededAsync(_kfPrevVol);
                            TM.App.Log($"[WriterPlugin] 第{_kfPrevVol}卷自动存档完成");
                        }
                    }
                    catch (Exception _kfArchiveEx)
                    {
                        TM.App.Log($"[WriterPlugin] 第{_kfPrevVol}卷自动存档失败（不阻断生成）: {_kfArchiveEx.Message}");
                    }
                }

                var contentServiceF2 = ServiceLocator.Get<GeneratedContentService>();
                if (contentServiceF2.ChapterExists(chapterId))
                {
                    var dupMsg = $"章节 {chapterId} 已存在。如需重新生成请使用 @重写:{chapterId} 指令。";
                    TM.App.Log($"[WriterPlugin] 重复生成拦截: {dupMsg}");
                    return dupMsg;
                }

                TM.App.Log($"[WriterPlugin] 开始生成章节: {chapterId}");
                GenerationProgressHub.Report($"正在准备生成 {chapterId}：加载打包上下文...");

                var guideService = ServiceLocator.Get<GuideContextService>();
                var ctx = await guideService.BuildContentContextAsync(chapterId);
                if (ctx == null)
                {
                    var msg = $"[生成失败] 无法获取章节 {chapterId} 的打包上下文，请确认已执行打包。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot == null)
                {
                    var msg = $"[生成失败] 章节 {chapterId} 为正式版上下文（打包+MD），但 FactSnapshot 缺失。请重新打包或修复账本后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                GenerationProgressHub.Report("正在并行加载前置数据...");
                using var vectorCts = new CancellationTokenSource();
                var specTask = ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();
                var vectorTask = PopulateVectorRecallAsync(ctx, vectorCts.Token);

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot != null)
                {
                    var currentScope = await ServiceLocator.Get<IWorkScopeService>().GetCurrentScopeAsync();
                    var publishService = ServiceLocator.Get<IPublishService>();
                    var manifest = publishService.GetManifest();
                    if (string.IsNullOrEmpty(currentScope))
                    {
                        var scopeMsg = "[生成失败] Scope为空，无法进行正式版生成。请先在智能拆书模块创建并保存拆书条目。";
                        TM.App.Log($"[WriterPlugin] {scopeMsg}");
                        vectorCts.Cancel();
                        return scopeMsg;
                    }
                    if (manifest != null && !string.IsNullOrEmpty(manifest.SourceBookId) && 
                        !string.Equals(currentScope, manifest.SourceBookId, StringComparison.Ordinal))
                    {
                        var scopeMsg = $"[生成失败] 当前Scope({currentScope})与打包数据的SourceBookId({manifest.SourceBookId})不一致。请重新打包后重试。";
                        TM.App.Log($"[WriterPlugin] {scopeMsg}");
                        vectorCts.Cancel();
                        return scopeMsg;
                    }

                    var changeDetection = ServiceLocator.Get<IChangeDetectionService>();
                    GenerationProgressHub.Report("正在校验打包一致性：扫描模块变更...");
                    await changeDetection.RefreshAllAsync();
                    var configService = ServiceLocator.Get<ContentConfigService>();

                    var enabledChangedModules = changeDetection.GetChangedModules()
                        .Where(m => configService.IsModuleEnabled(m))
                        .ToList();

                    if (enabledChangedModules.Count > 0)
                    {
                        var msg = "[生成失败] 检测到已启用模块存在未打包变更，正式版生成条件未满足。请先重新打包后重试。\n" +
                                  string.Join("\n", enabledChangedModules.Select(m => $"- {m}"));
                        TM.App.Log($"[WriterPlugin] {msg}");
                        vectorCts.Cancel();
                        return msg;
                    }

                    var contextIdsValidation = await guideService.ValidateContextIdsAsync(ctx.ContextIds);
                    if (!contextIdsValidation.IsValid)
                    {
                        var msg = $"[生成失败] ContextIds 解析失败，索引与本体不一致。\n{contextIdsValidation.GetErrorSummary()}";
                        TM.App.Log($"[WriterPlugin] {msg}");
                        vectorCts.Cancel();
                        return msg;
                    }
                }

                var projectSpec = await specTask;
                CreativeSpec? overrideSpec = null;
                if (!string.IsNullOrWhiteSpace(style) || wordCount > 0)
                {
                    overrideSpec = new CreativeSpec();
                    if (!string.IsNullOrWhiteSpace(style))
                    {
                        overrideSpec.WritingStyle = style;
                    }

                    if (wordCount > 0)
                    {
                        overrideSpec.TargetWordCount = wordCount;
                    }
                }

                var effectiveSpec = CreativeSpec.Merge(projectSpec, overrideSpec);
                await vectorTask;

                string rawContent;

                if (ctx.FactSnapshot == null && ctx.ContextMode != ContentContextMode.Full)
                {
                    var msg = $"[生成失败] {chapterId} 缺少 FactSnapshot，强一致模式下禁止轻量生成/跳过一致性校验。请先完成打包后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    return msg;
                }

                TM.App.Log($"[WriterPlugin] gen: {chapterId}");
                GenerationProgressHub.Report("准备完成，开始正式生成章节...");
                var genResult = await ServiceLocator.Get<AutoRewriteEngine>().GenerateWithRewriteAsync(
                    chapterId,
                    ctx,
                    ctx.FactSnapshot!,
                    effectiveSpec);

                if (!genResult.Success)
                {
                    if (genResult.RequiresManualIntervention)
                    {
                        TM.App.Log($"[WriterPlugin] 需要人工介入: {chapterId}");
                        return $"[生成失败] {genResult.InterventionHint}\n\n最后失败原因：\n{string.Join("\n", genResult.GetLastFailureReasons().Select(f => $"- {f}"))}";
                    }

                    var error = string.IsNullOrWhiteSpace(genResult.ErrorMessage)
                        ? "[生成失败] AI 未返回任何内容"
                        : $"[生成失败] {genResult.ErrorMessage}";
                    TM.App.Log($"[WriterPlugin] {error}");
                    return error;
                }

                rawContent = genResult.Content!;
                TM.App.Log($"[WriterPlugin] gen ok: {chapterId}, attempts: {genResult.TotalAttempts}");

                var cleaned = StripLeadingTitle(
                    StripPromptEchoKeepChanges(CleanContentKeepChanges(rawContent), ctx.Title));

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                var effectiveSnapshot2 = ctx.FactSnapshot ?? await TryBuildLazySnapshotAsync(chapterId, ctx.ContextIds);
                if (effectiveSnapshot2 != null)
                {
                    await callback.OnContentGeneratedStrictAsync(
                        chapterId,
                        cleaned,
                        effectiveSnapshot2,
                        genResult.GateResult,
                        genResult.DesignElements);
                }
                else
                {
                    var msg = $"[生成失败] {chapterId} FactSnapshot不可用，强一致模式下禁止降级为非严格保存。请检查打包上下文是否完整后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    return msg;
                }

                var actualWordCount = CountWords(cleaned);
                var title = string.IsNullOrWhiteSpace(ctx.Title) ? chapterId : ctx.Title;

                TM.App.Log($"[WriterPlugin] 章节生成并保存成功: {chapterId}, 标题: {title}, 字数: {actualWordCount}");
                GlobalToast.Success("章节已保存", $"「{title}」约 {actualWordCount} 字");

                var persisted = await ServiceLocator.Get<GeneratedContentService>().GetChapterAsync(chapterId);
                var displayContent = persisted ?? cleaned;

                CurrentChapterTracker.SetCurrentChapter(chapterId, title);
                await TryAutoSwitchVolumeAfterGenerationAsync(chapterId);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Comm.PublishRefreshChapterList();
                    Comm.PublishChapterSelected(chapterId, BuildCanonicalTabTitle(chapterId, title), displayContent);
                    StandardDialog.FlashTaskbarIfBackground(System.Windows.Application.Current.MainWindow);
                });

                return $"已生成章节「{title}」，约 {actualWordCount} 字。\n内容已保存到项目章节文件（{chapterId}.md），对话中不展示完整正文。";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 生成已取消");
                return "[已取消] 生成被用户取消";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                return $"[异常] {ex.Message}";
            }
        }

        public async Task<string> GenerateChapterAsync(CancellationToken ct, string chapterId = "")
        {
            TM.App.Log($"[WriterPlugin] GenerateChapter (可取消): {chapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    throw new InvalidOperationException("未指定章节ID。请使用 @chapter:volN_chM / @重写:volN_chM / @续写:volN_chM，或输入“第N卷第M章/第M章”（需分卷设计配置章节范围）。");
                }

                await ValidateChapterIdVolumeAsync(ct, chapterId);

                var _autoArchiveParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_autoArchiveParsed.HasValue && _autoArchiveParsed.Value.chapterNumber == 1 && _autoArchiveParsed.Value.volumeNumber > 1)
                {
                    var _prevVol = _autoArchiveParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _archiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                        var _prevArchives = await _archiveStore.GetPreviousArchivesAsync(_autoArchiveParsed.Value.volumeNumber);
                        if (!_prevArchives.Any(a => a.VolumeNumber == _prevVol))
                        {
                            TM.App.Log($"[WriterPlugin] 检测到新卷第1章，自动存档第{_prevVol}卷...");
                            var _reconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                            await _reconciler.AutoArchiveVolumeIfNeededAsync(_prevVol);
                            TM.App.Log($"[WriterPlugin] 第{_prevVol}卷自动存档完成");
                        }
                    }
                    catch (Exception _archiveEx)
                    {
                        TM.App.Log($"[WriterPlugin] 第{_prevVol}卷自动存档失败（不阻断生成）: {_archiveEx.Message}");
                    }
                }

                var contentServiceF2 = ServiceLocator.Get<GeneratedContentService>();
                if (contentServiceF2.ChapterExists(chapterId))
                {
                    var dupMsg = $"章节 {chapterId} 已存在。如需重新生成请使用 @重写:{chapterId} 指令。";
                    TM.App.Log($"[WriterPlugin] 重复生成拦截: {dupMsg}");
                    throw new InvalidOperationException(dupMsg);
                }

                ct.ThrowIfCancellationRequested();

                GenerationProgressHub.Report($"正在准备生成 {chapterId}：加载打包上下文...");
                var guideService = ServiceLocator.Get<GuideContextService>();
                var ctx = await guideService.BuildContentContextAsync(chapterId);
                if (ctx == null)
                {
                    var errorMsg = $"无法获取章节 {chapterId} 的打包上下文，请确认已执行打包";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot == null)
                {
                    var errorMsg = $"章节 {chapterId} 为正式版上下文（打包+MD），但 FactSnapshot 缺失。请重新打包或修复账本后重试。";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                GenerationProgressHub.Report("正在并行加载前置数据...");
                using var vectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var specTask = ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();
                var vectorTask = PopulateVectorRecallAsync(ctx, vectorCts.Token);

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot != null)
                {
                    await EnsureScopeConsistencyAsync();

                    ct.ThrowIfCancellationRequested();
                    var changeDetection = ServiceLocator.Get<IChangeDetectionService>();
                    GenerationProgressHub.Report("正在校验打包一致性：扫描模块变更...");
                    await changeDetection.RefreshAllAsync();
                    var configService = ServiceLocator.Get<ContentConfigService>();

                    var enabledChangedModules = changeDetection.GetChangedModules()
                        .Where(m => configService.IsModuleEnabled(m))
                        .ToList();

                    if (enabledChangedModules.Count > 0)
                    {
                        var errorMsg = "检测到已启用模块存在未打包变更，正式版生成条件未满足。请先重新打包后重试。\n" +
                                       string.Join("\n", enabledChangedModules.Select(m => $"- {m}"));
                        TM.App.Log($"[WriterPlugin] {errorMsg}");
                        vectorCts.Cancel();
                        throw new InvalidOperationException(errorMsg);
                    }

                    var contextIdsValidation = await guideService.ValidateContextIdsAsync(ctx.ContextIds);
                    if (!contextIdsValidation.IsValid)
                    {
                        var errorMsg = $"ContextIds 解析失败，索引与本体不一致。\n{contextIdsValidation.GetErrorSummary()}";
                        TM.App.Log($"[WriterPlugin] {errorMsg}");
                        vectorCts.Cancel();
                        throw new InvalidOperationException(errorMsg);
                    }
                }

                ct.ThrowIfCancellationRequested();

                var projectSpec = await specTask;
                var effectiveSpec = CreativeSpec.Merge(projectSpec, null);
                await vectorTask;

                ct.ThrowIfCancellationRequested();

                string rawContent;

                if (ctx.FactSnapshot == null && ctx.ContextMode != ContentContextMode.Full)
                {
                    var msg = $"章节 {chapterId} 缺少 FactSnapshot，强一致模式下禁止轻量生成/跳过一致性校验。请先完成打包后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    throw new InvalidOperationException(msg);
                }

                TM.App.Log($"[WriterPlugin] gen: {chapterId}");
                GenerationProgressHub.Report("准备完成，开始正式生成章节...");
                var genResult = await ServiceLocator.Get<AutoRewriteEngine>().GenerateWithRewriteAsync(
                    chapterId,
                    ctx,
                    ctx.FactSnapshot!,
                    effectiveSpec,
                    ct);

                if (!genResult.Success)
                {
                    if (genResult.RequiresManualIntervention)
                    {
                        TM.App.Log($"[WriterPlugin] 需要人工介入: {chapterId}");
                        throw new InvalidOperationException($"{genResult.InterventionHint}\n最后失败原因：{string.Join("; ", genResult.GetLastFailureReasons())}");
                    }

                    var errorMsg = string.IsNullOrWhiteSpace(genResult.ErrorMessage)
                        ? "AI 未返回任何内容"
                        : genResult.ErrorMessage;
                    TM.App.Log($"[WriterPlugin] AI生成失败: {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                rawContent = genResult.Content!;
                TM.App.Log($"[WriterPlugin] gen ok: {chapterId}, attempts: {genResult.TotalAttempts}");

                ct.ThrowIfCancellationRequested();

                var cleaned = StripLeadingTitle(
                    StripPromptEchoKeepChanges(CleanContentKeepChanges(rawContent), ctx.Title));

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                var effectiveSnapshot3 = ctx.FactSnapshot ?? await TryBuildLazySnapshotAsync(chapterId, ctx.ContextIds);
                if (effectiveSnapshot3 != null)
                {
                    await callback.OnContentGeneratedStrictAsync(
                        chapterId,
                        cleaned,
                        effectiveSnapshot3,
                        genResult.GateResult,
                        genResult.DesignElements);
                }
                else
                {
                    var msg = $"{chapterId} FactSnapshot不可用，强一致模式下禁止降级为非严格保存。请检查打包上下文是否完整后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    throw new InvalidOperationException(msg);
                }

                var actualWordCount = CountWords(cleaned);
                var title = string.IsNullOrWhiteSpace(ctx.Title) ? chapterId : ctx.Title;

                TM.App.Log($"[WriterPlugin] 章节生成成功: {chapterId}, 字数: {actualWordCount}");
                GlobalToast.Success("章节已保存", $"「{title}」约 {actualWordCount} 字");

                var persisted = await ServiceLocator.Get<GeneratedContentService>().GetChapterAsync(chapterId);
                var displayContent = persisted ?? cleaned;

                CurrentChapterTracker.SetCurrentChapter(chapterId, title);
                await TryAutoSwitchVolumeAfterGenerationAsync(chapterId);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Comm.PublishRefreshChapterList();
                    Comm.PublishChapterSelected(chapterId, BuildCanonicalTabTitle(chapterId, title), displayContent);
                    StandardDialog.FlashTaskbarIfBackground(System.Windows.Application.Current.MainWindow);
                });

                return $"已生成章节「{title}」，约 {actualWordCount} 字。";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 生成已取消");
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GenerateChapterByNumberAsync(CancellationToken ct, int chapterNumber)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapterByNumber: 第{chapterNumber}章");

            try
            {
                ct.ThrowIfCancellationRequested();

                var volumeNumber = await ResolveVolumeNumberForChapterAsync(ct, chapterNumber);
                var chapterId = ChapterParserHelper.BuildChapterId(volumeNumber, chapterNumber);

                GlobalToast.Info("卷归属", $"第{chapterNumber}章 → 第{volumeNumber}卷");

                return await GenerateChapterAsync(ct, chapterId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GenerateChapterFromSourceAsync(CancellationToken ct, string sourceChapterId)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapterFromSource: {sourceChapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                var contentService = ServiceLocator.Get<GeneratedContentService>();

                if (!contentService.ChapterExists(sourceChapterId))
                {
                    var errorMsg = $"来源章节 {sourceChapterId} 不存在";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                ct.ThrowIfCancellationRequested();

                var targetChapterId = await contentService.GenerateNextChapterIdFromSourceAsync(sourceChapterId);
                TM.App.Log($"[WriterPlugin] 续写目标章节: {sourceChapterId} → {targetChapterId}");

                ct.ThrowIfCancellationRequested();

                return await GenerateChapterAsync(ct, targetChapterId);
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 续写已取消");
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 续写异常: {ex.Message}");
                throw;
            }
        }

        public async Task<string> RewriteChapterAsync(CancellationToken ct, string targetChapterId)
        {
            TM.App.Log($"[WriterPlugin] RewriteChapter: {targetChapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                var contentService = ServiceLocator.Get<GeneratedContentService>();

                if (!contentService.ChapterExists(targetChapterId))
                {
                    var errorMsg = $"目标章节 {targetChapterId} 不存在，请先生成该章节或检查章节ID";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                ct.ThrowIfCancellationRequested();

                var oldContent = await contentService.GetChapterAsync(targetChapterId);
                if (!string.IsNullOrEmpty(oldContent))
                    ChapterDiffContext.SetOld(targetChapterId, oldContent);

                var _rwParsed = ChapterParserHelper.ParseChapterId(targetChapterId);
                if (_rwParsed.HasValue && _rwParsed.Value.chapterNumber == 1 && _rwParsed.Value.volumeNumber > 1)
                {
                    var _rwPrevVol = _rwParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _rwArchiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                        var _rwPrevArchives = await _rwArchiveStore.GetPreviousArchivesAsync(_rwParsed.Value.volumeNumber);
                        if (!_rwPrevArchives.Any(a => a.VolumeNumber == _rwPrevVol))
                        {
                            TM.App.Log($"[WriterPlugin] 重写检测到新卷第1章，补触存档第{_rwPrevVol}卷...");
                            var _rwReconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                            await _rwReconciler.AutoArchiveVolumeIfNeededAsync(_rwPrevVol);
                        }
                    }
                    catch (Exception _rwArchiveEx)
                    {
                        TM.App.Log($"[WriterPlugin] 重写时第{_rwPrevVol}卷存档检查失败（不阻断重写）: {_rwArchiveEx.Message}");
                    }
                }

                var guideService = ServiceLocator.Get<GuideContextService>();
                var ctx = await guideService.BuildContentContextAsync(targetChapterId);

                ct.ThrowIfCancellationRequested();

                var result = await GenerateChapterWithContextAsync(ct, targetChapterId, ctx);

                var newContent = await contentService.GetChapterAsync(targetChapterId);
                if (!string.IsNullOrEmpty(newContent))
                    ChapterDiffContext.SetNew(targetChapterId, newContent);

                return result;
            }
            catch (OperationCanceledException)
            {
                ChapterDiffContext.Clear();
                TM.App.Log($"[WriterPlugin] 重写已取消");
                throw;
            }
            catch (Exception ex)
            {
                ChapterDiffContext.Clear();
                TM.App.Log($"[WriterPlugin] 重写异常: {ex.Message}");
                throw;
            }
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private async Task<string> GenerateChapterWithContextAsync(
            CancellationToken ct, 
            string chapterId, 
            ContentTaskContext? ctx)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapterWithContext: {chapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                var projectSpec = await ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();
                var effectiveSpec = CreativeSpec.Merge(projectSpec, null);

                ct.ThrowIfCancellationRequested();

                string rawContent;

                if (ctx != null && ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot == null)
                {
                    throw new InvalidOperationException($"章节 {chapterId} 上下文不完整，请重新打包后重试。");
                }
                if (ctx == null || (ctx.FactSnapshot == null && ctx.ContextMode != ContentContextMode.Full))
                {
                    var msg = $"章节 {chapterId} 缺少 FactSnapshot，强一致模式下禁止轻量生成/跳过一致性校验。请先完成打包后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    throw new InvalidOperationException(msg);
                }

                await EnsureScopeConsistencyAsync();

                TM.App.Log($"[WriterPlugin] gen: {chapterId}");
                var genResult = await ServiceLocator.Get<AutoRewriteEngine>().GenerateWithRewriteAsync(
                    chapterId,
                    ctx!,
                    ctx!.FactSnapshot!,
                    effectiveSpec,
                    ct);

                if (!genResult.Success)
                {
                    if (genResult.RequiresManualIntervention)
                    {
                        TM.App.Log($"[WriterPlugin] 需要人工介入: {chapterId}");
                        throw new InvalidOperationException($"{genResult.InterventionHint}\n最后失败原因：{string.Join("; ", genResult.GetLastFailureReasons())}");
                    }

                    var errorMsg = string.IsNullOrWhiteSpace(genResult.ErrorMessage)
                        ? "AI 未返回任何内容"
                        : genResult.ErrorMessage;
                    TM.App.Log($"[WriterPlugin] AI生成失败: {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                rawContent = genResult.Content!;
                TM.App.Log($"[WriterPlugin] gen ok: {chapterId}, attempts: {genResult.TotalAttempts}");

                ct.ThrowIfCancellationRequested();

                var cleaned = StripLeadingTitle(
                    StripPromptEchoKeepChanges(CleanContentKeepChanges(rawContent), ctx.Title));

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                var effectiveSnapshot3 = ctx.FactSnapshot ?? await TryBuildLazySnapshotAsync(chapterId, ctx.ContextIds);
                if (effectiveSnapshot3 != null)
                {
                    await callback.OnContentGeneratedStrictAsync(
                        chapterId,
                        cleaned,
                        effectiveSnapshot3,
                        genResult.GateResult,
                        genResult.DesignElements);
                }
                else
                {
                    var msg = $"{chapterId} FactSnapshot不可用，强一致模式下禁止降级为非严格保存。请检查打包上下文是否完整后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    throw new InvalidOperationException(msg);
                }

                var actualWordCount = CountWords(cleaned);
                var title = string.IsNullOrWhiteSpace(ctx.Title) ? chapterId : ctx.Title;

                TM.App.Log($"[WriterPlugin] 章节生成成功: {chapterId}, 字数: {actualWordCount}");
                GlobalToast.Success("章节已保存", $"「{title}」约 {actualWordCount} 字");

                var persisted = await ServiceLocator.Get<GeneratedContentService>().GetChapterAsync(chapterId);
                var displayContent = persisted ?? cleaned;
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    CurrentChapterTracker.SetCurrentChapter(chapterId, title);
                    Comm.PublishRefreshChapterList();
                    Comm.PublishChapterSelected(chapterId, BuildCanonicalTabTitle(chapterId, title), displayContent);
                    StandardDialog.FlashTaskbarIfBackground(System.Windows.Application.Current.MainWindow);
                });

                return $"已生成章节「{title}」，约 {actualWordCount} 字。";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 生成已取消");
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                throw;
            }
        }

        private static string CleanGeneratedContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = content.Trim();

            content = StripModelArtifacts(content);

            if (content.StartsWith("```"))
            {
                var endIndex = content.IndexOf('\n');
                if (endIndex > 0)
                    content = content.Substring(endIndex + 1);
            }
            if (content.EndsWith("```"))
                content = content.Substring(0, content.Length - 3);

            if (content.StartsWith("---"))
            {
                var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
                if (endIndex > 0)
                {
                    var nextLineIndex = content.IndexOf('\n', endIndex + 3);
                    if (nextLineIndex > 0)
                        content = content.Substring(nextLineIndex + 1);
                    else
                        content = content.Substring(endIndex + 3);
                }
            }

            var lines = content.Split('\n');
            var cleanedLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                var cleanLine = line;

                if (!cleanLine.TrimStart().StartsWith("#"))
                {
                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"\*\*([^*]+)\*\*", "$1");
                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"__([^_]+)__", "$1");

                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"(?<![*])\*([^*]+)\*(?![*])", "$1");

                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"`([^`]+)`", "$1");
                }

                cleanedLines.Add(cleanLine);
            }

            content = string.Join("\n", cleanedLines);

            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"^##\s*(正文|内容|章节内容)\s*\n",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline);

            content = System.Text.RegularExpressions.Regex.Replace(content, @"\n{3,}", "\n\n");

            return content.Trim();
        }

        private static string CleanContentKeepChanges(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
                return string.Empty;

            var separator = GenerationGate.ChangesSeparator;
            var idx = rawContent.IndexOf(separator, StringComparison.Ordinal);
            if (idx < 0)
            {
                return CleanGeneratedContent(rawContent);
            }

            var contentPart = rawContent.Substring(0, idx).Trim();
            var changesPart = rawContent.Substring(idx + separator.Length).Trim();

            var cleanedContent = CleanGeneratedContent(contentPart);

            if (string.IsNullOrEmpty(changesPart))
                return cleanedContent;

            return $"{cleanedContent}\n\n{separator}\n{changesPart}";
        }

        private static string StripPromptEchoKeepChanges(string rawContentWithChanges, string? expectedTitle)
        {
            if (string.IsNullOrWhiteSpace(rawContentWithChanges))
                return string.Empty;

            var separator = GenerationGate.ChangesSeparator;
            var idx = rawContentWithChanges.IndexOf(separator, StringComparison.Ordinal);
            if (idx < 0)
            {
                return StripPromptEchoFromBody(rawContentWithChanges, expectedTitle);
            }

            var body = rawContentWithChanges.Substring(0, idx).Trim();
            var changesPart = rawContentWithChanges.Substring(idx + separator.Length).Trim();

            var cleanedBody = StripPromptEchoFromBody(body, expectedTitle);
            if (string.IsNullOrWhiteSpace(changesPart))
                return cleanedBody;

            return $"{cleanedBody}\n\n{separator}\n{changesPart}";
        }

        private static string StripPromptEchoFromBody(string body, string? expectedTitle)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            var text = body.TrimStart();

            var m = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?m)^\s*#\s*第\s*[0-9一二三四五六七八九十百千]+\s*章.*$",
                System.Text.RegularExpressions.RegexOptions.None);
            if (m.Success)
            {
                return text.Substring(m.Index).TrimStart();
            }

            if (!string.IsNullOrWhiteSpace(expectedTitle))
            {
                var titleLine = "# " + expectedTitle.Trim();
                var idx = text.IndexOf(titleLine, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    return text.Substring(idx).TrimStart();
                }
            }

            var m2 = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?m)^\s*#\s+(?!章节生成任务\b).+$");
            if (m2.Success)
            {
                return text.Substring(m2.Index).TrimStart();
            }

            return text;
        }

        private static string StripLeadingTitle(string contentWithChanges)
        {
            if (string.IsNullOrWhiteSpace(contentWithChanges))
                return contentWithChanges;

            var separator = GenerationGate.ChangesSeparator;
            var sepIdx = contentWithChanges.IndexOf(separator, StringComparison.Ordinal);

            string body, changesSuffix;
            if (sepIdx >= 0)
            {
                body = contentWithChanges.Substring(0, sepIdx).TrimEnd();
                changesSuffix = "\n\n" + contentWithChanges.Substring(sepIdx);
            }
            else
            {
                body = contentWithChanges;
                changesSuffix = string.Empty;
            }

            var trimmedBody = body.TrimStart();
            if (trimmedBody.Length > 0)
            {
                var firstLineEnd = trimmedBody.IndexOf('\n');
                var firstLine = (firstLineEnd >= 0 ? trimmedBody.Substring(0, firstLineEnd) : trimmedBody).Trim();

                if (firstLine.StartsWith("#"))
                {
                    trimmedBody = firstLineEnd >= 0
                        ? trimmedBody.Substring(firstLineEnd + 1).TrimStart()
                        : string.Empty;
                }
            }

            return $"{trimmedBody}{changesSuffix}";
        }

        private static string BuildCanonicalTabTitle(string chapterId, string? title)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            var chapterNum = parsed?.chapterNumber ?? 0;

            if (chapterNum > 0 && !string.IsNullOrWhiteSpace(title))
                return $"第{chapterNum}章 {title}";
            if (chapterNum > 0)
                return $"第{chapterNum}章";
            if (!string.IsNullOrWhiteSpace(title))
                return title;
            return chapterId;
        }

        private static string StripModelArtifacts(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"<\s*(think|thinking|analysis)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"```(?:thinking|analysis|reasoning)[\s\S]*?```",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"(?m)^\s*</?\s*(think|thinking|analysis)\b[^>]*>\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var m = System.Text.RegularExpressions.Regex.Match(
                content,
                @"(?m)^\s*#\s*第\s*[0-9一二三四五六七八九十百千]+\s*章.*$");
            if (m.Success)
            {
                content = content.Substring(m.Index).TrimStart();
            }

            return content.Trim();
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int count = 0;
            bool inWord = false;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    inWord = false;
                }
                else if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    count++;
                    inWord = false;
                }
                else if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }

            return count;
        }

        #region 长距离记忆召回

        public async Task PopulateVectorRecallAsync(ContentTaskContext ctx, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var vectorSearch = ServiceLocator.Get<VectorSearchService>();
                if (!vectorSearch.IsAvailable)
                {
                    await vectorSearch.InitializeAsync();
                    if (!vectorSearch.IsAvailable)
                    {
                        ct.ThrowIfCancellationRequested();
                        var keywordIndex = ServiceLocator.Get<KeywordChapterIndexService>();
                        var queryText = BuildVectorSearchQuery(ctx);
                        if (string.IsNullOrWhiteSpace(queryText)) return;

                        var queryKeywords = queryText
                            .Split(new[] { ' ', '\n', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
                            .Distinct()
                            .ToList();
                        var recallIds = await keywordIndex.SearchAsync(queryKeywords, topK: 5);

                        var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                        foreach (var chapId in recallIds.Where(id =>
                            !string.Equals(id, ctx.ChapterId, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(id, ctx.PreviousChapterId, StringComparison.OrdinalIgnoreCase)))
                        {
                            ct.ThrowIfCancellationRequested();
                            var mdPath = System.IO.Path.Combine(chaptersPath, $"{chapId}.md");
                            if (!System.IO.File.Exists(mdPath)) continue;
                            try
                            {
                                var head = await ReadFileHeadAsync(mdPath, 300);
                                if (!string.IsNullOrWhiteSpace(head))
                                {
                                    ctx.VectorRecallFragments ??= new();
                                    ctx.VectorRecallFragments.Add(new VectorRecallFragment
                                    {
                                        ChapterId = chapId,
                                        Content = head,
                                        Score = 0.0
                                    });
                                }
                            }
                            catch { }
                        }
                        TM.App.Log($"[WriterPlugin] 向量不可用，关键词兜底召回 {ctx.VectorRecallFragments?.Count ?? 0} 章");
                        return;
                    }
                }

                ct.ThrowIfCancellationRequested();
                var query = BuildVectorSearchQuery(ctx);
                if (string.IsNullOrWhiteSpace(query))
                    return;

                var results = await vectorSearch.SearchAsync(query, topK: 5);
                if (results.Count == 0)
                    return;

                var currentChapterId = ctx.ChapterId;
                var previousChapterId = ctx.PreviousChapterId;
                var filtered = results
                    .Where(r => !string.Equals(r.ChapterId, currentChapterId, StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(r.ChapterId, previousChapterId, StringComparison.OrdinalIgnoreCase))
                    .Where(r => r.Score >= 0.3)
                    .Take(3)
                    .ToList();

                if (filtered.Count == 0)
                    return;

                ctx.VectorRecallFragments = filtered
                    .Select(r => new VectorRecallFragment
                    {
                        ChapterId = r.ChapterId,
                        Content = r.Content,
                        Score = r.Score
                    })
                    .ToList();

                TM.App.Log($"[WriterPlugin] 向量召回 {ctx.VectorRecallFragments.Count} 条远距离片段（查询: {TruncateForLog(query, 60)}）");
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[WriterPlugin] 向量召回已取消");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 向量召回失败（非致命）: {ex.Message}");
            }
        }

        public static string BuildVectorSearchQuery(ContentTaskContext ctx)
        {
            var parts = new List<string>();

            if (ctx.FactSnapshot?.ForeshadowingStatus != null)
            {
                foreach (var f in ctx.FactSnapshot.ForeshadowingStatus.Where(f => f.IsSetup && !f.IsResolved))
                {
                    if (!string.IsNullOrWhiteSpace(f.Name))
                        parts.Add(f.Name);
                }
            }

            if (ctx.ChapterPlan != null)
            {
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTheme))
                    parts.Add(ctx.ChapterPlan.ChapterTheme);
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainGoal))
                    parts.Add(ctx.ChapterPlan.MainGoal);
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Foreshadowing))
                    parts.Add(ctx.ChapterPlan.Foreshadowing);
            }

            if (ctx.Characters != null)
            {
                foreach (var c in ctx.Characters.Take(5))
                {
                    if (!string.IsNullOrWhiteSpace(c.Name))
                        parts.Add(c.Name);
                }
            }

            if (ctx.PlotRules != null)
            {
                foreach (var p in ctx.PlotRules.Take(3))
                {
                    if (!string.IsNullOrWhiteSpace(p.Name))
                        parts.Add(p.Name);
                }
            }

            return string.Join(" ", parts.Distinct().Take(15));
        }

        private static string TruncateForLog(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text ?? string.Empty;
            return text.Substring(0, maxLen) + "...";
        }

        private static async Task<string> ReadFileHeadAsync(string filePath, int maxChars)
        {
            var text = await System.IO.File.ReadAllTextAsync(filePath);
            if (text.Length <= maxChars) return text;
            var cut = text.Substring(0, maxChars);
            var lastBreak = cut.LastIndexOfAny(new[] { '\n', '。', '！', '？' });
            return lastBreak > maxChars / 2 ? cut.Substring(0, lastBreak + 1) : cut;
        }

        private static async Task<FactSnapshot?> TryBuildLazySnapshotAsync(
            string chapterId,
            ContextIdCollection? contextIds)
        {
            if (contextIds == null) return null;
            try
            {
                var guideService = ServiceLocator.Get<GuideContextService>();
                var snapshot = await guideService.ExtractFactSnapshotForChapterAsync(chapterId, contextIds);
                if (snapshot != null)
                    TM.App.Log($"[WriterPlugin] {chapterId} FactSnapshot延迟构建成功，升级为严格路径");
                return snapshot;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] {chapterId} FactSnapshot延迟构建失败: {ex.Message}");
                return null;
            }
        }

        private static async Task EnsureScopeConsistencyAsync()
        {
            var currentScope = await ServiceLocator.Get<IWorkScopeService>().GetCurrentScopeAsync();
            var manifest = ServiceLocator.Get<IPublishService>().GetManifest();

            if (string.IsNullOrEmpty(currentScope))
                throw new InvalidOperationException("[生成失败] Scope为空，无法进行正式版生成。请先在智能拆书模块创建并保存拆书条目。");

            if (manifest != null && !string.IsNullOrEmpty(manifest.SourceBookId) &&
                !string.Equals(currentScope, manifest.SourceBookId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"[生成失败] 当前Scope({currentScope})与打包数据的SourceBookId({manifest.SourceBookId})不一致。请重新打包后重试。");
        }

        #endregion

        #region F5: 卷末自动切换

        private async Task TryAutoSwitchVolumeAfterGenerationAsync(string chapterId)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue) return;

                var vol = parsed.Value.volumeNumber;
                var ch = parsed.Value.chapterNumber;

                await VolumeDesignService.InitializeAsync();
                var designs = VolumeDesignService.GetAllVolumeDesigns();

                var _switchScopeId = ServiceLocator.Get<IWorkScopeService>().CurrentSourceBookId;
                if (!string.IsNullOrEmpty(_switchScopeId))
                    designs = designs.Where(v => string.Equals(v.SourceBookId, _switchScopeId, StringComparison.Ordinal)).ToList();

                var currentDesign = designs.FirstOrDefault(v => v.VolumeNumber == vol);
                if (currentDesign == null) return;

                var effectiveEndChapter = currentDesign.EndChapter;

                if (effectiveEndChapter <= 0)
                {
                    effectiveEndChapter = await ResolveVolumeEndChapterFromGuideAsync(vol);
                    if (effectiveEndChapter > 0)
                        TM.App.Log($"[WriterPlugin] F5: 第{vol}卷EndChapter未配置，从ContentGuide推断为{effectiveEndChapter}");
                }

                if (effectiveEndChapter <= 0 || ch != effectiveEndChapter)
                    return;

                var nextDesign = designs
                    .Where(v => v.VolumeNumber > vol)
                    .OrderBy(v => v.VolumeNumber)
                    .FirstOrDefault();

                if (nextDesign == null)
                {
                    TM.App.Log($"[WriterPlugin] F5: 第{vol}卷已是最后一卷，不切换");
                    return;
                }

                var nextStart = nextDesign.StartChapter > 0 ? nextDesign.StartChapter : 1;
                var nextChapterId = ChapterParserHelper.BuildChapterId(nextDesign.VolumeNumber, nextStart);

                CurrentChapterTracker.SetCurrentChapter(nextChapterId, $"第{nextDesign.VolumeNumber}卷（待生成）");
                GlobalToast.Info("卷切换", $"第{vol}卷已完成，已自动切换到第{nextDesign.VolumeNumber}卷");
                TM.App.Log($"[WriterPlugin] F5: 卷末切换 {chapterId} → {nextChapterId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] F5: 卷末切换检测失败（非致命）: {ex.Message}");
                GlobalToast.Warning("卷切换检测失败", "请手动确认当前卷是否正确");
            }
        }

        private static Task<int> ResolveVolumeEndChapterFromGuideAsync(int volumeNumber)
            => ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber);

        #endregion
    }
}
