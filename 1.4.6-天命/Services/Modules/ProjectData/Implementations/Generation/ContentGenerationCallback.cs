using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Helpers;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class ContentGenerationCallback
    {
        private readonly GenerationGate _generationGate;
        private readonly GuideManager _guideManager;
        private readonly LedgerTrimService _ledgerTrim;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;
        private readonly VectorSearchService _vectorSearchService;
        private readonly CharacterStateService _characterStateService;
        private readonly ConflictProgressService _conflictProgressService;
        private readonly PlotPointsIndexService _plotPointsIndexService;
        private readonly ForeshadowingStatusService _foreshadowingStatusService;
        private readonly LocationStateService _locationStateService;
        private readonly FactionStateService _factionStateService;
        private readonly TimelineService _timelineService;
        private readonly ItemStateService _itemStateService;

        private sealed record NameMapCacheEntry(Dictionary<string, string> Map, DateTime Expires);
        private volatile NameMapCacheEntry? _nameMapCache;

        public void ClearNameMapCache() => _nameMapCache = null;

        public ContentGenerationCallback(
            GenerationGate generationGate,
            GuideManager guideManager,
            LedgerTrimService ledgerTrimService,
            ChapterSummaryStore summaryStore,
            ChapterMilestoneStore milestoneStore,
            VectorSearchService vectorSearchService,
            CharacterStateService characterStateService,
            ConflictProgressService conflictProgressService,
            PlotPointsIndexService plotPointsIndexService,
            ForeshadowingStatusService foreshadowingStatusService,
            LocationStateService locationStateService,
            FactionStateService factionStateService,
            TimelineService timelineService,
            ItemStateService itemStateService)
        {
            _generationGate = generationGate;
            _guideManager = guideManager;
            _ledgerTrim = ledgerTrimService;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;
            _vectorSearchService = vectorSearchService;
            _characterStateService = characterStateService;
            _conflictProgressService = conflictProgressService;
            _plotPointsIndexService = plotPointsIndexService;
            _foreshadowingStatusService = foreshadowingStatusService;
            _locationStateService = locationStateService;
            _factionStateService = factionStateService;
            _timelineService = timelineService;
            _itemStateService = itemStateService;

            StoragePathHelper.CurrentProjectChanged += (_, _) => _nameMapCache = null;
        }
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly System.Text.RegularExpressions.Regex RegexTagBlock = new(
            @"<\s*(think|thinking|analysis)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex RegexFencedBlock = new(
            @"```(?:thinking|analysis|reasoning)[\s\S]*?```",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex RegexOrphanTag = new(
            @"(?m)^\s*</?\s*(think|thinking|analysis)\b[^>]*>\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        public async Task OnContentGeneratedStrictAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            GateResult? gateResult = null,
            DesignElementNames? designElements = null)
        {
            await OnContentGeneratedInternalAsync(
                chapterId,
                rawContent,
                factSnapshot,
                gateResult,
                designElements);
        }

        public async Task OnExternalContentSavedAsync(string chapterId, string content)
        {
            TM.App.Log($"[ContentCallback] 外部内容保存: {chapterId}");

            ChapterChanges? externalChanges = null;
            var protocol = _generationGate.ValidateChangesProtocol(content);
            if (!protocol.Success)
            {
                var hasProtocolError = protocol.Errors
                    .Any(e => !e.Contains("未识别到CHANGES区域", StringComparison.Ordinal));
                if (hasProtocolError)
                {
                    var reason = string.Join("; ", protocol.Errors.Take(3));
                    throw new InvalidOperationException($"外部内容CHANGES协议无效: {reason}");
                }
            }
            if (protocol.Success && protocol.Changes != null)
            {
                var structResult = _generationGate.ValidateStructuralOnly(protocol.Changes);
                if (!structResult.Success)
                {
                    var issues = string.Join("; ", structResult.GetIssueDescriptions().Take(5));
                    throw new InvalidOperationException($"外部内容CHANGES结构校验失败: {issues}");
                }

                externalChanges = protocol.Changes;
                content = protocol.ContentWithoutChanges ?? content;
                TM.App.Log($"[ContentCallback] {chapterId} 检测到CHANGES块，将同步追踪Guide");
            }

            var persistedContent = await NormalizePersistedContentAsync(chapterId, content);

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var stagingPath = Path.Combine(chaptersPath, ".staging");
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
            var stagingFile = Path.Combine(stagingPath, $"{chapterId}.md");
            var backupFile = chapterFile + ".bak";
            var hadExistingFile = File.Exists(chapterFile);
            var contentChanged = true;
            if (hadExistingFile)
            {
                try
                {
                    var oldPersisted = await File.ReadAllTextAsync(chapterFile);
                    contentChanged = !string.Equals(oldPersisted, persistedContent, StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 比较旧正文失败（按已变更处理）: {ex.Message}");
                    contentChanged = true;
                }
            }

            string? summary = null;
            var guideFlushed = false;
            var trackingClearedForNoChanges = false;
            try
            {
                if (!Directory.Exists(stagingPath))
                    Directory.CreateDirectory(stagingPath);

                await File.WriteAllTextAsync(stagingFile, persistedContent);

                if (hadExistingFile)
                    File.Copy(chapterFile, backupFile, overwrite: true);

                File.Move(stagingFile, chapterFile, overwrite: true);

                var nameMap = externalChanges != null ? await BuildEntityNameMapAsync() : null;
                summary = externalChanges != null
                    ? BuildStructuredSummary(persistedContent, externalChanges, nameMap)
                    : ExtractSummary(persistedContent);

                if (externalChanges != null)
                {
                    if (hadExistingFile)
                    {
                        await RemoveTrackingDataForChapterAsync(chapterId);
                        TM.App.Log($"[ContentCallback] {chapterId} 外部重写：已清除旧追踪数据");
                    }

                    await UpdateTrackingGuidesAsync(chapterId, externalChanges);
                    TM.App.Log($"[ContentCallback] {chapterId} 外部CHANGES追踪已更新");
                }
                else if (hadExistingFile && contentChanged)
                {
                    await RemoveTrackingDataForChapterAsync(chapterId);
                    trackingClearedForNoChanges = true;
                    TM.App.Log($"[ContentCallback] {chapterId} 未提供CHANGES，已清除旧追踪数据，避免状态陈旧");
                }

                await _guideManager.FlushAllAsync();
                guideFlushed = true;

                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                await UpdateChapterSummaryAsync(chapterId, summary);

                await _ledgerTrim.TrimAllAsync();

                if (File.Exists(backupFile))
                    File.Delete(backupFile);

                TM.App.Log($"[ContentCallback] {chapterId} ext ok");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} ext err: {ex.Message}");

                if (!guideFlushed)
                {
                    _guideManager.DiscardDirtyAndEvict();
                }

                try
                {
                    if (File.Exists(stagingFile))
                        File.Delete(stagingFile);

                    if (!guideFlushed)
                    {
                        if (hadExistingFile && File.Exists(backupFile))
                        {
                            File.Copy(backupFile, chapterFile, overwrite: true);
                            File.Delete(backupFile);
                        }
                        else if (!hadExistingFile && File.Exists(chapterFile))
                        {
                            File.Delete(chapterFile);
                        }
                    }
                    else
                    {
                        if (File.Exists(backupFile))
                            File.Delete(backupFile);
                        TM.App.Log($"[ContentCallback] {chapterId} partial ok");
                    }
                }
                catch (Exception rollbackEx)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 回滚失败: {rollbackEx.Message}");
                }

                if (!guideFlushed)
                    throw;
            }

            try
            {
                await _vectorSearchService.IndexChapterAsync(chapterId, chapterFile);
                if (_vectorSearchService.CurrentMode == TM.Services.Framework.AI.SemanticKernel.SearchMode.Keyword)
                    WriteVectorDegradedFlag();
                else
                    ClearVectorDegradedFlag();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 向量索引更新失败（不影响正文）: {ex.Message}");
                WriteVectorDegradedFlag();
            }

            if (externalChanges != null)
            {
                try
                {
                    await ServiceLocator.Get<KeywordChapterIndexService>().IndexChapterAsync(chapterId, externalChanges);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 关键词索引更新失败（不影响正文）: {ex.Message}");
                }
            }
            else if (trackingClearedForNoChanges)
            {
                GlobalToast.Warning("追踪待补全", $"章节 {chapterId} 未提供CHANGES，旧追踪数据已清除。建议使用带CHANGES导入或重新生成。", 5000);
            }

            await TryUpdateVolumeMilestoneAsync(chapterId, summary, isRewrite: hadExistingFile);
        }

        private async Task OnContentGeneratedInternalAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            GateResult? gateResult,
            DesignElementNames? designElements = null)
        {
            ChapterChanges? changes;
            string content;

            const bool strictGate = true;

            if (gateResult != null && gateResult.Success)
            {
                changes = gateResult.ParsedChanges;
                var _cleanedProtocol = _generationGate.ValidateChangesProtocol(rawContent);
                content = _cleanedProtocol.ContentWithoutChanges ?? rawContent;
                TM.App.Log($"[ContentCallback] {chapterId} 复用校验结果（正文取自已清理rawContent）");
            }
            else
            {
                var newGateResult = await _generationGate.ValidateAsync(
                    chapterId,
                    rawContent,
                    factSnapshot,
                    designElements);

                if (!newGateResult.Success)
                {
                    var err = $"[ContentCallback] {chapterId} 落盘前校验失败: {string.Join("; ", newGateResult.GetTopFailures(5))}";
                    TM.App.Log(err);
                    throw new InvalidOperationException(err);
                }

                changes = newGateResult.ParsedChanges;
                content = newGateResult.ContentWithoutChanges ?? string.Empty;
                TM.App.Log($"[ContentCallback] {chapterId} 校验通过（复用解析结果）");
            }

            content = await NormalizePersistedContentAsync(chapterId, content);

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var stagingPath = Path.Combine(chaptersPath, ".staging");
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
            var stagingFile = Path.Combine(stagingPath, $"{chapterId}.md");
            var backupFile = chapterFile + ".bak";
            var hadExistingFile = File.Exists(chapterFile);

            string? summary = null;
            var guideFlushed = false;
            try
            {
                if (!Directory.Exists(stagingPath))
                {
                    Directory.CreateDirectory(stagingPath);
                }

                await File.WriteAllTextAsync(stagingFile, content);
                TM.App.Log($"[ContentCallback] S1: {chapterId}");

                if (hadExistingFile)
                {
                    File.Copy(chapterFile, backupFile, overwrite: true);
                }

                File.Move(stagingFile, chapterFile, overwrite: true);
                TM.App.Log($"[ContentCallback] S2: {chapterId}");

                var nameMap = changes != null ? await BuildEntityNameMapAsync() : null;
                summary = changes != null
                    ? BuildStructuredSummary(content, changes, nameMap)
                    : ExtractSummary(content);

                if (strictGate && changes != null && !string.IsNullOrWhiteSpace(summary))
                {
                    try
                    {
                        var _driftVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                        var _driftAllChars = new Dictionary<string, CharacterStateEntry>();
                        foreach (var _dv in _driftVols.TakeLast(5))
                        {
                            var _dg = await _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", _dv));
                            foreach (var (_did, _de) in _dg.Characters)
                                if (!_driftAllChars.ContainsKey(_did))
                                    _driftAllChars[_did] = _de;
                        }
                        var _driftVolFile = GuideManager.GetVolumeFileName("character_state_guide.json",
                            ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);
                        var _driftCurGuide = await _guideManager.GetGuideAsync<CharacterStateGuide>(_driftVolFile);
                        var declaredIds = new HashSet<string>(
                            changes.CharacterStateChanges?.Select(c => c.CharacterId) ?? Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase);
                        var changesPatched = false;
                        bool driftFound = false;
                        foreach (var (id, entry) in _driftAllChars)
                        {
                            if (declaredIds.Contains(id)) continue;
                            if (!string.IsNullOrWhiteSpace(entry.Name) && entry.Name.Length >= 2 && summary.Contains(entry.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                var warnMsg = $"{chapterId}: 出现于摘要但CHANGES未申报，状态可能不一致";
                                TM.App.Log($"[ContentCallback] 漂移检测 {chapterId}: 角色\"{entry.Name}\"出现于正文但CHANGES无申报，自动补录出现记录");
                                if (!_driftCurGuide.Characters.ContainsKey(id))
                                    _driftCurGuide.Characters[id] = new CharacterStateEntry { Name = entry.Name };
                                var _curEntry = _driftCurGuide.Characters[id];
                                var maxWarn = LayeredContextConfig.DriftWarningsMaxPerEntity;
                                if (_curEntry.DriftWarnings.Count >= maxWarn)
                                    _curEntry.DriftWarnings.RemoveRange(0, _curEntry.DriftWarnings.Count - maxWarn + 1);
                                _curEntry.DriftWarnings.Add(warnMsg);
                                driftFound = true;

                                var autoChange = new TM.Services.Modules.ProjectData.Models.Tracking.CharacterStateChange
                                {
                                    CharacterId = id,
                                    KeyEvent = "[自动补录] 出现于本章正文，CHANGES未申报",
                                    Importance = "important"
                                };

                                changes.CharacterStateChanges ??= new();
                                if (!changes.CharacterStateChanges.Any(c => string.Equals(c.CharacterId, id, StringComparison.OrdinalIgnoreCase)))
                                {
                                    changes.CharacterStateChanges.Add(autoChange);
                                    declaredIds.Add(id);
                                    changesPatched = true;
                                    TM.App.Log($"[ContentCallback] 已自动补录角色 \"{entry.Name}\" 出现记录到CHANGES: {chapterId}");
                                }
                            }
                        }
                        if (changesPatched)
                            summary = BuildStructuredSummary(content, changes, nameMap);
                        if (driftFound)
                            _guideManager.MarkDirty(_driftVolFile);
                    }
                    catch (Exception driftEx)
                    {
                        TM.App.Log($"[ContentCallback] 漂移检测失败: {driftEx.Message}");
                    }
                }

                if (hadExistingFile && changes != null)
                {
                    await RemoveTrackingDataForChapterAsync(chapterId);
                }

                if (changes != null)
                {
                    await UpdateTrackingGuidesAsync(chapterId, changes);
                }
                else
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 无CHANGES，跳过追踪更新");
                }

                await _guideManager.FlushAllAsync();
                guideFlushed = true;

                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                await UpdateChapterSummaryAsync(chapterId, summary);

                await _ledgerTrim.TrimAllAsync();

                if (changes != null)
                {
                    try
                    {
                        await ServiceLocator.Get<KeywordChapterIndexService>()
                            .IndexChapterAsync(chapterId, changes);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ContentCallback] 关键词索引更新失败（非致命）: {ex.Message}");
                    }
                }

                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }

                TM.App.Log($"[ContentCallback] {chapterId} ok");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} err: {ex.Message}");

                if (!guideFlushed)
                {
                    _guideManager.DiscardDirtyAndEvict();
                }

                try
                {
                    if (File.Exists(stagingFile))
                    {
                        File.Delete(stagingFile);
                    }

                    if (!guideFlushed)
                    {
                        if (hadExistingFile && File.Exists(backupFile))
                        {
                            File.Copy(backupFile, chapterFile, overwrite: true);
                            File.Delete(backupFile);
                            TM.App.Log($"[ContentCallback] {chapterId} 已恢复备份");
                        }
                        else if (!hadExistingFile && File.Exists(chapterFile))
                        {
                            File.Delete(chapterFile);
                            TM.App.Log($"[ContentCallback] {chapterId} 已删除新建文件");
                        }
                    }
                    else
                    {
                        if (File.Exists(backupFile))
                            File.Delete(backupFile);
                        TM.App.Log($"[ContentCallback] {chapterId} partial ok (idx)");
                    }
                }
                catch (Exception rollbackEx)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 回滚失败: {rollbackEx.Message}");
                }

                if (!guideFlushed)
                    throw;
            }

            try
            {
                await _vectorSearchService.IndexChapterAsync(chapterId, chapterFile);
                if (_vectorSearchService.CurrentMode == TM.Services.Framework.AI.SemanticKernel.SearchMode.Keyword)
                    WriteVectorDegradedFlag();
                else
                    ClearVectorDegradedFlag();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 向量索引更新失败（不影响正文）: {ex.Message}");
                WriteVectorDegradedFlag();
            }

            await TryUpdateVolumeMilestoneAsync(chapterId, summary, isRewrite: hadExistingFile);
        }

        private async Task TryUpdateVolumeMilestoneAsync(string chapterId, string? summary = null, bool archiveOnEndChapter = true, bool isRewrite = false)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue) return;

                var volumeNumber = parsed.Value.volumeNumber;
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    if (isRewrite)
                    {
                        var currentSummaries = await _summaryStore.GetVolumeSummariesAsync(volumeNumber);
                        await _milestoneStore.RebuildVolumeMilestoneAsync(volumeNumber, currentSummaries);
                        TM.App.Log($"[ContentCallback] {chapterId} 重写场景，已全量重建第{volumeNumber}卷里程碑");
                    }
                    else
                    {
                        await _milestoneStore.AppendChapterMilestoneAsync(volumeNumber, chapterId, summary);
                    }
                }

                if (archiveOnEndChapter)
                    await TryArchiveVolumeFactAsync(chapterId, volumeNumber);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 里程碑更新失败（不影响正文）: {ex.Message}");
            }
        }

        private async Task TryArchiveVolumeFactAsync(string chapterId, int volumeNumber)
        {
            try
            {
                var volumeService = ServiceLocator.Get<VolumeDesignService>();
                if (!volumeService.IsInitialized)
                    await volumeService.InitializeAsync();
                var designs = volumeService.GetAllVolumeDesigns();
                var volumeDesign = designs.FirstOrDefault(v => v.VolumeNumber == volumeNumber);

                var effectiveEndChapter = volumeDesign?.EndChapter ?? 0;

                if (effectiveEndChapter <= 0)
                {
                    effectiveEndChapter = await ResolveVolumeEndChapterAsync(volumeNumber);
                    if (effectiveEndChapter > 0)
                        TM.App.Log($"[ContentCallback] 第{volumeNumber}卷EndChapter未配置，自动推断为: {effectiveEndChapter}");
                }

                if (effectiveEndChapter <= 0)
                {
                    TM.App.Log($"[ContentCallback] 第{volumeNumber}卷无法确定结束章节号（ContentGuide可能为空），跳过卷末存档");
                    return;
                }

                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue || parsed.Value.chapterNumber != effectiveEndChapter) return;

                var snapshot = await ServiceLocator.Get<FactSnapshotExtractor>().ExtractVolumeEndSnapshotAsync(chapterId);
                await ServiceLocator.Get<VolumeFactArchiveStore>().ArchiveVolumeAsync(volumeNumber, snapshot, chapterId);
                TM.App.Log($"[ContentCallback] 第{volumeNumber}卷事实存档完成: {chapterId}（effectiveEndChapter={effectiveEndChapter}）");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 卷事实存档失败（不影响正文）: {ex.Message}");
            }
        }

        private static Task<int> ResolveVolumeEndChapterAsync(int volumeNumber)
            => ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber);

        private async Task<string> NormalizePersistedContentAsync(string chapterId, string content)
        {
            var normalizedBody = StripModelArtifacts(content);
            normalizedBody = StripLeadingHeadings(normalizedBody);

            var packagedTitle = await GetPackagedChapterTitleStrictAsync(chapterId);

            var canonicalTitle = BuildCanonicalTitle(chapterId, packagedTitle);
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                return $"# {canonicalTitle}";
            }

            return $"# {canonicalTitle}\n\n{normalizedBody}";
        }

        private static async Task<string> GetPackagedChapterTitleStrictAsync(string chapterId)
        {
            try
            {
                var guideService = ServiceLocator.Get<GuideContextService>();
                var guide = await guideService.GetContentGuideAsync();
                if (guide?.Chapters != null && guide.Chapters.TryGetValue(chapterId, out var entry) && !string.IsNullOrWhiteSpace(entry?.Title))
                    return ChapterParserHelper.NormalizeChapterTitle(entry.Title.Trim());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 获取打包标题异常（将使用章节号）: {ex.Message}");
            }

            TM.App.Log($"[ContentCallback] {chapterId} 未找到打包标题，使用章节号落盘");
            return string.Empty;
        }

        private static string BuildCanonicalTitle(string chapterId, string title)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            var chapterNum = parsed?.chapterNumber ?? 0;
            if (chapterNum > 0)
            {
                return string.IsNullOrWhiteSpace(title) ? $"第{chapterNum}章" : $"第{chapterNum}章 {title}";
            }

            return string.IsNullOrWhiteSpace(title) ? chapterId : title;
        }

        private static string StripLeadingHeadings(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var text = content.TrimStart();
            while (!string.IsNullOrEmpty(text))
            {
                var firstLineEnd = text.IndexOf('\n');
                var firstLine = (firstLineEnd >= 0 ? text.Substring(0, firstLineEnd) : text).Trim();
                if (!firstLine.StartsWith("#", StringComparison.Ordinal))
                {
                    break;
                }

                text = firstLineEnd >= 0 ? text.Substring(firstLineEnd + 1).TrimStart() : string.Empty;
            }

            return text.Trim();
        }

        private static string StripModelArtifacts(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            content = RegexTagBlock.Replace(content, string.Empty);
            content = RegexFencedBlock.Replace(content, string.Empty);
            content = RegexOrphanTag.Replace(content, string.Empty);

            return content.Trim();
        }

        public async Task OnChapterDeletedAsync(string chapterId)
        {
            TM.App.Log($"[ContentCallback] 开始级联清理: {chapterId}");

            try
            {
                await _summaryStore.RemoveSummaryAsync(chapterId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 清理摘要失败: {ex.Message}");
            }

            try { await _characterStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理角色状态失败: {ex.Message}"); }

            try { await _conflictProgressService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理冲突进度失败: {ex.Message}"); }

            try { await _plotPointsIndexService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理情节索引失败: {ex.Message}"); }

            try { await _foreshadowingStatusService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理伏笔状态失败: {ex.Message}"); }

            try { await _locationStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理地点状态失败: {ex.Message}"); }

            try { await _factionStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理势力状态失败: {ex.Message}"); }

            try { await _timelineService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理时间线失败: {ex.Message}"); }

            try { await _itemStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理物品状态失败: {ex.Message}"); }

            try { ServiceLocator.Get<RelationStrengthService>().InvalidateCache(); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 关联强度缓存失效失败（非致命）: {ex.Message}"); }

            try
            {
                await _guideManager.FlushAllAsync();
                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] guides刷盘失败: {ex.Message}");
            }

            try
            {
                await _vectorSearchService.RemoveChapterAsync(chapterId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 清理向量索引失败（非致命）: {ex.Message}");
            }

            try
            {
                await ServiceLocator.Get<KeywordChapterIndexService>().RemoveChapterAsync(chapterId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 清理关键词索引失败（非致命）: {ex.Message}");
            }

            try
            {
                var _parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_parsed.HasValue)
                {
                    var _vol = _parsed.Value.volumeNumber;
                    var _currentSummaries = await _summaryStore.GetVolumeSummariesAsync(_vol);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(_vol, _currentSummaries);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 里程碑重建失败（非致命）: {ex.Message}");
            }

            try
            {
                var _archiveStore = ServiceLocator.Get<VolumeFactArchiveStore>();
                var _archiveParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_archiveParsed.HasValue)
                    await _archiveStore.DeleteArchiveIfLastChapterAsync(_archiveParsed.Value.volumeNumber, chapterId);
                else
                    _archiveStore.InvalidateCache();
            }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 存档联动清理失败（非致命）: {ex.Message}"); }

            TM.App.Log($"[ContentCallback] 级联清理完成: {chapterId}");
        }

        private async Task RemoveTrackingDataForChapterAsync(string chapterId)
        {
            try { await _characterStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除角色状态失败（重写前）: {ex.Message}"); }

            try { await _conflictProgressService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除冲突进度失败（重写前）: {ex.Message}"); }

            try { await _plotPointsIndexService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除情节索引失败（重写前）: {ex.Message}"); }

            try { await _foreshadowingStatusService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除伏笔状态失败（重写前）: {ex.Message}"); }

            try { await _locationStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除地点状态失败（重写前）: {ex.Message}"); }

            try { await _factionStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除势力状态失败（重写前）: {ex.Message}"); }

            try { await _timelineService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除时间线失败（重写前）: {ex.Message}"); }

            try { await _itemStateService.RemoveChapterDataAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除物品状态失败（重写前）: {ex.Message}"); }

            try { ServiceLocator.Get<RelationStrengthService>().InvalidateCache(); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 关联强度缓存失效失败（重写前）: {ex.Message}"); }

            try { await ServiceLocator.Get<KeywordChapterIndexService>().RemoveChapterAsync(chapterId); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除关键词索引失败（重写前）: {ex.Message}"); }

            TM.App.Log($"[ContentCallback] 已清除 {chapterId} 旧追踪数据（重写前）");
        }

        private async Task UpdateTrackingGuidesAsync(string chapterId, ChapterChanges changes)
        {
            var charCount = changes.CharacterStateChanges?.Count ?? 0;
            foreach (var change in changes.CharacterStateChanges ?? new())
            {
                await _characterStateService.UpdateCharacterStateAsync(chapterId, change);
            }
            if (charCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新角色状态: {charCount}条");

            var conflictCount = changes.ConflictProgress?.Count ?? 0;
            foreach (var change in changes.ConflictProgress ?? new())
            {
                await _conflictProgressService.UpdateConflictProgressAsync(chapterId, change);
            }
            if (conflictCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新冲突进度: {conflictCount}条");

            var plotCount = changes.NewPlotPoints?.Count ?? 0;
            foreach (var change in changes.NewPlotPoints ?? new())
            {
                await _plotPointsIndexService.AddPlotPointAsync(chapterId, change);
            }
            if (plotCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 添加关键情节: {plotCount}条");

            var foreshadowCount = changes.ForeshadowingActions?.Count ?? 0;
            foreach (var action in changes.ForeshadowingActions ?? new())
            {
                if (action.Action == "setup")
                    await _foreshadowingStatusService.MarkAsSetupAsync(action.ForeshadowId, chapterId);
                else if (action.Action == "payoff")
                    await _foreshadowingStatusService.MarkAsResolvedAsync(action.ForeshadowId, chapterId);
            }
            if (foreshadowCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新伏笔状态: {foreshadowCount}条");

            var locationCount = changes.LocationStateChanges?.Count ?? 0;
            foreach (var change in changes.LocationStateChanges ?? new())
            {
                await _locationStateService.UpdateLocationStateAsync(chapterId, change);
            }
            if (locationCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新地点状态: {locationCount}条");

            var factionCount = changes.FactionStateChanges?.Count ?? 0;
            foreach (var change in changes.FactionStateChanges ?? new())
            {
                await _factionStateService.UpdateFactionStateAsync(chapterId, change);
            }
            if (factionCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新势力状态: {factionCount}条");

            if (changes.TimeProgression != null)
            {
                await _timelineService.UpdateTimeProgressionAsync(chapterId, changes.TimeProgression);
                TM.App.Log($"[ContentCallback] {chapterId} 更新时间推进");
            }

            var movementCount = changes.CharacterMovements?.Count ?? 0;
            if (movementCount > 0)
            {
                await _timelineService.UpdateCharacterMovementsAsync(chapterId, changes.CharacterMovements!);
                TM.App.Log($"[ContentCallback] {chapterId} 更新角色位置: {movementCount}条");
            }

            var itemCount = changes.ItemTransfers?.Count ?? 0;
            foreach (var change in changes.ItemTransfers ?? new())
            {
                await _itemStateService.UpdateItemStateAsync(chapterId, change);
            }
            if (itemCount > 0)
                TM.App.Log($"[ContentCallback] {chapterId} 更新物品流转: {itemCount}条");

            await _foreshadowingStatusService.RefreshOverdueStatusAsync(chapterId);

            TM.App.Log($"[ContentCallback] {chapterId} done");
        }

        private async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, string>> BuildEntityNameMapAsync()
        {
            var cached = _nameMapCache;
            if (cached != null && DateTime.UtcNow < cached.Expires)
                return cached.Map;

            var map = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                var _nmGm = _guideManager;
                foreach (var _v in _nmGm.GetExistingVolumeNumbers("character_state_guide.json").TakeLast(5))
                { var _g = await _nmGm.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", _v)); foreach (var (id, e) in _g.Characters) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name; }
                foreach (var _v in _nmGm.GetExistingVolumeNumbers("conflict_progress_guide.json").TakeLast(5))
                { var _g = await _nmGm.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName("conflict_progress_guide.json", _v)); foreach (var (id, e) in _g.Conflicts) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name; }
                var _fowGuide = await _nmGm.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                foreach (var (id, e) in _fowGuide.Foreshadowings) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _v in _nmGm.GetExistingVolumeNumbers("location_state_guide.json").TakeLast(5))
                { var _g = await _nmGm.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName("location_state_guide.json", _v)); foreach (var (id, e) in _g.Locations) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name; }
                foreach (var _v in _nmGm.GetExistingVolumeNumbers("faction_state_guide.json").TakeLast(5))
                { var _g = await _nmGm.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName("faction_state_guide.json", _v)); foreach (var (id, e) in _g.Factions) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name; }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] BuildEntityNameMapAsync失败，将回退到ID: {ex.Message}");
            }
            _nameMapCache = new NameMapCacheEntry(map, DateTime.UtcNow.AddSeconds(60));
            return map;
        }

        private string BuildStructuredSummary(string content, ChapterChanges changes,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? nameMap = null)
        {
            string R(string id) => (!string.IsNullOrWhiteSpace(id) && nameMap != null && nameMap.TryGetValue(id, out var n)) ? n : id;

            var sb = new System.Text.StringBuilder();

            sb.AppendLine(ExtractSummary(content, 200));

            foreach (var c in changes.CharacterStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.KeyEvent))
                    sb.AppendLine($"[角色]{R(c.CharacterId)}: {c.KeyEvent}");
            }
            foreach (var c in changes.ConflictProgress ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.Event))
                    sb.AppendLine($"[冲突]{R(c.ConflictId)}: {c.Event}→{c.NewStatus}");
            }
            foreach (var p in changes.NewPlotPoints ?? new())
            {
                if (!string.IsNullOrWhiteSpace(p.Context))
                    sb.AppendLine($"[情节]{p.Context}");
            }
            foreach (var f in changes.ForeshadowingActions ?? new())
            {
                if (!string.IsNullOrWhiteSpace(f.ForeshadowId))
                    sb.AppendLine($"[伏笔]{R(f.ForeshadowId)}: {f.Action}");
            }
            foreach (var l in changes.LocationStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(l.Event))
                    sb.AppendLine($"[地点]{R(l.LocationId)}: {l.Event}→{l.NewStatus}");
            }
            foreach (var fa in changes.FactionStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(fa.Event))
                    sb.AppendLine($"[势力]{R(fa.FactionId)}: {fa.Event}→{fa.NewStatus}");
            }
            if (changes.TimeProgression != null && !string.IsNullOrWhiteSpace(changes.TimeProgression.TimePeriod))
            {
                sb.AppendLine($"[时间]{changes.TimeProgression.TimePeriod} 经过{changes.TimeProgression.ElapsedTime}");
            }
            foreach (var m in changes.CharacterMovements ?? new())
            {
                if (!string.IsNullOrWhiteSpace(m.ToLocation))
                    sb.AppendLine($"[移动]{R(m.CharacterId)}: {R(m.FromLocation)}→{R(m.ToLocation)}");
            }
            foreach (var item in changes.ItemTransfers ?? new())
            {
                if (!string.IsNullOrWhiteSpace(item.Event))
                    sb.AppendLine($"[物品]{item.ItemName}: {item.Event} ({R(item.FromHolder)}→{R(item.ToHolder)})");
            }

            return sb.ToString().Trim();
        }

        private string ExtractSummary(string content, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var cleaned = content.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (cleaned.Length <= maxLength) return cleaned;

            var cutRegion = cleaned.Substring(0, maxLength);
            var lastSentenceEnd = cutRegion.LastIndexOfAny(new[] { '。', '！', '？', '…', '"' });
            if (lastSentenceEnd > maxLength / 3)
            {
                return cutRegion.Substring(0, lastSentenceEnd + 1) + "……";
            }

            return cutRegion + "……";
        }

        private async Task UpdateChapterSummaryAsync(string chapterId, string summary)
        {
            const int maxRetries = 2;
            for (int _attempt = 1; _attempt <= maxRetries; _attempt++)
            {
                try
                {
                    await _summaryStore.SetSummaryAsync(chapterId, summary);
                    TM.App.Log($"[ContentCallback] 已更新章节摘要: {chapterId}");
                    return;
                }
                catch (Exception ex)
                {
                    if (_attempt < maxRetries)
                    {
                        TM.App.Log($"[ContentCallback] 摘要写入第{_attempt}次失败，重试中: {ex.Message}");
                        await Task.Delay(100);
                    }
                    else
                    {
                        TM.App.Log($"[ContentCallback] ⚠️ 摘要写入失败，将由启动对账修复: {chapterId}: {ex.Message}");
                    }
                }
            }
        }

        private static void WriteVectorDegradedFlag()
        {
            try
            {
                var flagPath = Path.Combine(
                    StoragePathHelper.GetCurrentProjectPath(),
                    "vector_degraded.flag");
                File.WriteAllText(flagPath, DateTime.UtcNow.ToString("o"));
            }
            catch { }
        }

        private static void ClearVectorDegradedFlag()
        {
            try
            {
                var flagPath = Path.Combine(
                    StoragePathHelper.GetCurrentProjectPath(),
                    "vector_degraded.flag");
                if (File.Exists(flagPath))
                    File.Delete(flagPath);
            }
            catch { }
        }
    }
}
