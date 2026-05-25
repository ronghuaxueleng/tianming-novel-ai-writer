using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ConsistencyReconciler
    {
        private readonly GuideManager _guideManager;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;
        private readonly VectorSearchService _vectorSearchService;

        public ConsistencyReconciler(
            GuideManager guideManager,
            ChapterSummaryStore summaryStore,
            ChapterMilestoneStore milestoneStore,
            VectorSearchService vectorSearchService)
        {
            _guideManager = guideManager;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;
            _vectorSearchService = vectorSearchService;
        }

        public class ReconcileResult
        {
            public int StagingCleaned { get; set; }
            public int BakCleaned { get; set; }
            public int SummariesRepaired { get; set; }
            public int VectorReindexed { get; set; }
            public List<string> CorruptedGuides { get; set; } = new();
            public List<string> TrackingGaps { get; set; } = new();
            public int TrackingGapSummariesRepaired { get; set; }
            public int KeywordIndexRepaired { get; set; }
            public int FactArchivesRepaired { get; set; }
            public int TrackingOrphansCleared { get; set; }
            public List<string> Errors { get; set; } = new();

            public bool HasRepairs =>
                StagingCleaned > 0 || BakCleaned > 0 ||
                SummariesRepaired > 0 || VectorReindexed > 0 ||
                CorruptedGuides.Count > 0 || TrackingGapSummariesRepaired > 0 ||
                KeywordIndexRepaired > 0 || FactArchivesRepaired > 0 || TrackingOrphansCleared > 0;
        }

        public async Task<ReconcileResult> ReconcileAsync()
        {
            var result = new ReconcileResult();
            TM.App.Log("[Reconciler] 开始一致性对账...");

            try
            {
                _guideManager.RecoverPendingFlush();
                TM.App.Log("[Reconciler] GuideManager pending flush 已检查/恢复");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] RecoverPendingFlush 失败（非致命）: {ex.Message}");
            }

            try
            {
                CleanStagingAndBackups(result);

                await ValidateGuidesIntegrityAsync(result);

                await ReconcileSummariesAsync(result);

                await ReconcileVectorIndexAsync(result);

                await DetectTrackingGapsAsync(result);

                await ReconcileKeywordIndexAsync(result);

                await ReconcileFactArchivesAsync(result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"对账过程异常: {ex.Message}");
                TM.App.Log($"[Reconciler] 对账异常: {ex.Message}");
            }

            if (result.HasRepairs || result.TrackingGaps.Count > 0)
            {
                TM.App.Log($"[Reconciler] done: s={result.StagingCleaned}, b={result.BakCleaned}, " +
                           $"sm={result.SummariesRepaired}, vi={result.VectorReindexed}, " +
                           $"cg={result.CorruptedGuides.Count}, tg={result.TrackingGaps.Count}, " +
                           $"kw={result.KeywordIndexRepaired}, fa={result.FactArchivesRepaired}");
            }
            else
            {
                TM.App.Log("[Reconciler] 对账完成: 所有数据一致，无需修复");
            }

            return result;
        }

        private void CleanStagingAndBackups(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var stagingPath = Path.Combine(chaptersPath, ".staging");
            if (Directory.Exists(stagingPath))
            {
                var stagingFiles = Directory.GetFiles(stagingPath, "*.md");
                foreach (var file in stagingFiles)
                {
                    try
                    {
                        var chapterId = Path.GetFileNameWithoutExtension(file);
                        var finalFile = Path.Combine(chaptersPath, $"{chapterId}.md");

                        if (!File.Exists(finalFile) ||
                            File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(finalFile))
                        {
                            File.Move(file, finalFile, overwrite: true);
                            TM.App.Log($"[Reconciler] recovered: {chapterId}");
                        }
                        else
                        {
                            File.Delete(file);
                        }
                        result.StagingCleaned++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"清理staging失败 {file}: {ex.Message}");
                    }
                }

                try
                {
                    if (Directory.Exists(stagingPath) && !Directory.EnumerateFileSystemEntries(stagingPath).Any())
                        Directory.Delete(stagingPath);
                }
                catch { }
            }

            var bakFiles = Directory.GetFiles(chaptersPath, "*.bak");
            foreach (var file in bakFiles)
            {
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(file);
                    var finalFile = Path.Combine(chaptersPath, baseName);

                    if (!File.Exists(finalFile))
                    {
                        File.Move(file, finalFile);
                        TM.App.Log($"[Reconciler] 从bak恢复: {baseName}");
                    }
                    else
                    {
                        File.Delete(file);
                    }
                    result.BakCleaned++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"清理bak失败 {file}: {ex.Message}");
                }
            }

            var configPath = StoragePathHelper.GetProjectConfigPath();
            if (Directory.Exists(configPath))
            {
                foreach (var tmpFile in Directory.GetFiles(configPath, "*.tmp", SearchOption.AllDirectories))
                {
                    try { File.Delete(tmpFile); result.StagingCleaned++; } catch { }
                }
            }

            var manifestTmp = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json.tmp");
            if (File.Exists(manifestTmp))
            {
                try { File.Delete(manifestTmp); result.StagingCleaned++; } catch { }
            }

            try
            {
                var modulesRoot = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules");
                if (Directory.Exists(modulesRoot))
                {
                    foreach (var tmpFile in Directory.GetFiles(modulesRoot, "*.tmp", SearchOption.AllDirectories))
                    {
                        try { File.Delete(tmpFile); result.StagingCleaned++; } catch { }
                    }
                }
            }
            catch { }

            var guidesPath = Path.Combine(configPath, "guides");
            if (Directory.Exists(guidesPath))
            {
                foreach (var bakFile in Directory.GetFiles(guidesPath, "*.bak"))
                {
                    try
                    {
                        var originalName = bakFile[..^4];
                        if (!File.Exists(originalName))
                        {
                            File.Move(bakFile, originalName);
                            TM.App.Log($"[Reconciler] 从bak恢复guide: {Path.GetFileName(originalName)}");
                        }
                        else
                        {
                            File.Delete(bakFile);
                        }
                    }
                    catch { }
                }
            }

            var projectPath = StoragePathHelper.GetCurrentProjectPath();
            if (Directory.Exists(projectPath))
            {
                foreach (var backupDir in Directory.GetDirectories(projectPath, "_backup_*"))
                {
                    try
                    {
                        Directory.Delete(backupDir, true);
                        result.StagingCleaned++;
                        TM.App.Log($"[Reconciler] 清理孤立备份目录: {Path.GetFileName(backupDir)}");
                    }
                    catch { }
                }
            }
        }

        private async Task ValidateGuidesIntegrityAsync(ReconcileResult result)
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            if (!Directory.Exists(guidesPath)) return;

            var guideFiles = Directory.GetFiles(guidesPath, "*.json", SearchOption.AllDirectories);
            foreach (var file in guideFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    using var doc = JsonDocument.Parse(json);
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(file);
                    result.CorruptedGuides.Add(fileName);
                    TM.App.Log($"[Reconciler] 发现损坏的guide: {fileName}: {ex.Message}");

                    var bakFile = file + ".bak";
                    if (File.Exists(bakFile))
                    {
                        try
                        {
                            var bakJson = await File.ReadAllTextAsync(bakFile);
                            using var bakDoc = JsonDocument.Parse(bakJson);
                            File.Copy(bakFile, file, overwrite: true);
                            result.CorruptedGuides.Remove(fileName);
                            TM.App.Log($"[Reconciler] 已从 bak恢复损坏的guide: {fileName}");
                        }
                        catch
                        {
                            TM.App.Log($"[Reconciler] bak文件也已损坏，无法恢复: {fileName}");
                        }
                    }

                    if (result.CorruptedGuides.Contains(fileName) &&
                        fileName.StartsWith("content_guide", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = $"content_guide 分片 [{fileName}] 已损坏且无法自动恢复，请重新执行【全量打包】以重建指导文件。";
                        if (!result.Errors.Contains(msg))
                            result.Errors.Add(msg);
                        TM.App.Log($"[Reconciler] ⚠️ {msg}");
                    }
                }
            }

            var milestoneFiles = Directory.GetFiles(guidesPath, "*.txt", SearchOption.AllDirectories);
            foreach (var file in milestoneFiles)
            {
                try
                {
                    await File.ReadAllTextAsync(file);
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(file);
                    result.CorruptedGuides.Add(fileName);
                    TM.App.Log($"[Reconciler] 发现损坏的里程碑文件: {fileName}: {ex.Message}");
                }
            }
        }

        private async Task ReconcileSummariesAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length == 0) return;

            Dictionary<string, string> existingSummaries;
            try
            {
                existingSummaries = await _summaryStore.GetAllSummariesAsync();
            }
            catch
            {
                existingSummaries = new Dictionary<string, string>();
            }

            var mdChapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in mdFiles) mdChapterIds.Add(Path.GetFileNameWithoutExtension(f));

            var orphanAffectedVolumes = new HashSet<int>();
            foreach (var (orphanId, _) in existingSummaries)
            {
                if (!mdChapterIds.Contains(orphanId))
                {
                    try
                    {
                        await _summaryStore.RemoveSummaryAsync(orphanId);
                        TM.App.Log($"[Reconciler] 删除孤立摘要: {orphanId}（MD不存在）");
                        result.SummariesRepaired++;
                        var p = ChapterParserHelper.ParseChapterId(orphanId);
                        if (p.HasValue) orphanAffectedVolumes.Add(p.Value.volumeNumber);
                    }
                    catch { }
                }
            }

            foreach (var vol in orphanAffectedVolumes)
            {
                try
                {
                    var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(vol, volSummaries);
                    TM.App.Log($"[Reconciler] 已重建第{vol}卷里程碑（因清理{orphanAffectedVolumes.Count}条孤立摘要）");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"孤立清理后重建里程碑失败 vol{vol}: {ex.Message}");
                }
            }

            var repairedVolumes = new HashSet<int>();
            foreach (var mdFile in mdFiles)
            {
                var chapterId = Path.GetFileNameWithoutExtension(mdFile);
                if (existingSummaries.ContainsKey(chapterId))
                    continue;

                try
                {
                    var content = await ReadHeadAsync(mdFile, 2000);
                    var summary = ExtractSummaryFromHead(content);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        await _summaryStore.SetSummaryAsync(chapterId, summary);
                        result.SummariesRepaired++;
                        TM.App.Log($"[Reconciler] 补建摘要: {chapterId}");
                        var vol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
                        repairedVolumes.Add(vol);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"补建摘要失败 {chapterId}: {ex.Message}");
                }
            }

            foreach (var vol in repairedVolumes)
            {
                try
                {
                    var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(vol, volSummaries);
                    TM.App.Log($"[Reconciler] 已重建第{vol}卷里程碑（因补建{volSummaries.Count}条摘要）");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"重建里程碑失败 vol{vol}: {ex.Message}");
                }
            }
        }

        private async Task ReconcileVectorIndexAsync(ReconcileResult result)
        {
            try
            {
                var flagPath = GetVectorDegradedFlagPath();
                var wasDegraded = File.Exists(flagPath);
                if (wasDegraded)
                    TM.App.Log("[Reconciler] 检测到向量索引降级标志，将尝试重建...");

                await _vectorSearchService.InitializeAsync();

                if (wasDegraded && File.Exists(flagPath))
                {
                    if (_vectorSearchService.CurrentMode != SearchMode.Keyword)
                    {
                        try { File.Delete(flagPath); } catch { }
                        TM.App.Log("[Reconciler] 向量索引重建完成，已清除降级标志");
                        result.VectorReindexed++;
                    }
                    else
                    {
                        TM.App.Log("[Reconciler] 向量服务处于Keyword模式，保留降级标志以便后续自愈");
                    }
                }
                else
                {
                    TM.App.Log("[Reconciler] 向量索引增量对账完成");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"向量索引对账失败: {ex.Message}");
                TM.App.Log($"[Reconciler] 向量索引对账失败: {ex.Message}");
            }
        }

        private static string GetVectorDegradedFlagPath()
        {
            return Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "vector_degraded.flag");
        }

        private async Task DetectTrackingGapsAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length <= 1) return;

            try
            {
                var _rcVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                var stateGuide = new CharacterStateGuide();
                foreach (var _rcV in _rcVols.TakeLast(5))
                {
                    var _rcG = await _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", _rcV));
                    foreach (var (_rcId, _rcE) in _rcG.Characters)
                    {
                        if (!stateGuide.Characters.ContainsKey(_rcId))
                            stateGuide.Characters[_rcId] = new CharacterStateEntry { Name = _rcE.Name };
                        stateGuide.Characters[_rcId].StateHistory.AddRange(_rcE.StateHistory);
                    }
                }

                var trackedChapters = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in stateGuide.Characters.Values)
                {
                    foreach (var state in entry.StateHistory)
                    {
                        if (!string.IsNullOrWhiteSpace(state.Chapter))
                            trackedChapters.Add(state.Chapter);
                    }
                }

                if (trackedChapters.Count == 0) return;

                foreach (var mdFile in mdFiles)
                {
                    var chapterId = Path.GetFileNameWithoutExtension(mdFile);
                    if (!trackedChapters.Contains(chapterId))
                    {
                        result.TrackingGaps.Add(chapterId);
                    }
                }

                var mdChapterIds = new HashSet<string>(
                    mdFiles.Select(f => Path.GetFileNameWithoutExtension(f)),
                    StringComparer.OrdinalIgnoreCase);

                var orphanTracked = trackedChapters
                    .Where(id => !mdChapterIds.Contains(id))
                    .ToList();

                if (orphanTracked.Count > 0)
                {
                    TM.App.Log($"[Reconciler] 发现{orphanTracked.Count}个追踪 guide 孤立章节，开始清理...");
                    var callback = ServiceLocator.Get<ContentGenerationCallback>();
                    foreach (var orphanId in orphanTracked)
                    {
                        try
                        {
                            await callback.OnChapterDeletedAsync(orphanId);
                            result.TrackingOrphansCleared++;
                            TM.App.Log($"[Reconciler] 已清理孤立追踪数据: {orphanId}");
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[Reconciler] 清理孤立追踪数据失败: {orphanId}: {ex.Message}");
                        }
                    }
                }

                if (result.TrackingGaps.Count > 0)
                {
                    TM.App.Log($"[Reconciler] gaps: {result.TrackingGaps.Count}: " +
                               string.Join(", ", result.TrackingGaps.Take(10)));

                    foreach (var gapId in result.TrackingGaps)
                    {
                        try
                        {
                            var existing = await _summaryStore.GetSummaryAsync(gapId);
                            if (!string.IsNullOrWhiteSpace(existing)) continue;

                            var mdPath = Path.Combine(chaptersPath, $"{gapId}.md");
                            if (!File.Exists(mdPath)) continue;

                            var head = await ReadHeadAsync(mdPath, 2000);
                            var summary = ExtractSummaryFromHead(head);
                            if (string.IsNullOrWhiteSpace(summary)) continue;

                            await _summaryStore.SetSummaryAsync(gapId, summary);
                            result.TrackingGapSummariesRepaired++;
                        }
                        catch { }
                    }

                    if (result.TrackingGapSummariesRepaired > 0)
                        TM.App.Log($"[Reconciler] 已为{result.TrackingGapSummariesRepaired}个缺章补齐摘要");

                    try
                    {
                        var chapList = string.Join("、", result.TrackingGaps.Take(5));
                        var suffix = result.TrackingGaps.Count > 5 ? $" 等{result.TrackingGaps.Count}章" : string.Empty;
                        GlobalToast.Warning("追踪数据缺口",
                            $"{chapList}{suffix} 的角色/冲突等追踪数据缺失，建议重新导入该章内容修复。");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"追踪缺章检测失败: {ex.Message}");
            }
        }

        private async Task ReconcileKeywordIndexAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length == 0) return;

            try
            {
                var kwIndexService = ServiceLocator.Get<KeywordChapterIndexService>();

                var indexedIds = await kwIndexService.GetIndexedChapterIdsAsync();
                var missingIds = mdFiles
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(id => !indexedIds.Contains(id))
                    .ToList();

                if (missingIds.Count == 0) return;
                TM.App.Log($"[Reconciler] 关键词索引缺失 {missingIds.Count} 章，开始 best-effort 补建...");

                var knownEntityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var volNums = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                    foreach (var vol in volNums.TakeLast(5))
                    {
                        var guide = await _guideManager.GetGuideAsync<CharacterStateGuide>(
                            GuideManager.GetVolumeFileName("character_state_guide.json", vol));
                        foreach (var entry in guide.Characters.Values)
                            if (!string.IsNullOrWhiteSpace(entry.Name))
                                knownEntityNames.Add(entry.Name);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[Reconciler] 关键词对账：读取角色Guide失败（仍继续）: {ex.Message}");
                }

                try
                {
                    var foreshadowGuide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                    foreach (var f in foreshadowGuide.Foreshadowings.Values)
                        if (!string.IsNullOrWhiteSpace(f.Name)) knownEntityNames.Add(f.Name);
                }
                catch { }

                try
                {
                    var locVolNums = _guideManager.GetExistingVolumeNumbers("location_state_guide.json");
                    foreach (var vol in locVolNums.TakeLast(5))
                    {
                        var locGuide = await _guideManager.GetGuideAsync<LocationStateGuide>(
                            GuideManager.GetVolumeFileName("location_state_guide.json", vol));
                        foreach (var e in locGuide.Locations.Values)
                            if (!string.IsNullOrWhiteSpace(e.Name)) knownEntityNames.Add(e.Name);
                    }
                }
                catch { }

                try
                {
                    var facVolNums = _guideManager.GetExistingVolumeNumbers("faction_state_guide.json");
                    foreach (var vol in facVolNums.TakeLast(5))
                    {
                        var facGuide = await _guideManager.GetGuideAsync<FactionStateGuide>(
                            GuideManager.GetVolumeFileName("faction_state_guide.json", vol));
                        foreach (var e in facGuide.Factions.Values)
                            if (!string.IsNullOrWhiteSpace(e.Name)) knownEntityNames.Add(e.Name);
                    }
                }
                catch { }

                foreach (var chapterId in missingIds)
                {
                    try
                    {
                        var summary = await _summaryStore.GetSummaryAsync(chapterId);
                        if (string.IsNullOrWhiteSpace(summary)) continue;

                        var matched = knownEntityNames
                            .Where(name => summary.Contains(name, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (matched.Count == 0) continue;

                        await kwIndexService.IndexChapterFromKeywordsAsync(chapterId, matched);
                        result.KeywordIndexRepaired++;
                    }
                    catch { }
                }

                if (result.KeywordIndexRepaired > 0)
                    TM.App.Log($"[Reconciler] 关键词索引 best-effort 补建完成: {result.KeywordIndexRepaired} 章");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"关键词索引对账失败: {ex.Message}");
                TM.App.Log($"[Reconciler] 关键词索引对账失败: {ex.Message}");
            }
        }

        private async Task ReconcileFactArchivesAsync(ReconcileResult result)
        {
            try
            {
                var archivesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
                var allSummaries = await _summaryStore.GetAllSummariesAsync();
                if (allSummaries.Count == 0) return;

                var presentVolumes = new HashSet<int>();
                foreach (var chapterId in allSummaries.Keys)
                {
                    var p = ChapterParserHelper.ParseChapterId(chapterId);
                    if (p.HasValue) presentVolumes.Add(p.Value.volumeNumber);
                }

                var volumeDesignService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                if (!volumeDesignService.IsInitialized)
                    await volumeDesignService.InitializeAsync();
                var allVolumeDesigns = volumeDesignService.GetAllVolumeDesigns();

                foreach (var vol in presentVolumes.OrderBy(v => v))
                {
                    var isMaxVol = !presentVolumes.Contains(vol + 1);
                    bool isCompleted;
                    var volDesign = allVolumeDesigns.FirstOrDefault(d => d.VolumeNumber == vol);
                    if (volDesign != null && volDesign.EndChapter > 0)
                    {
                        var endChapterId = $"vol{vol}_ch{volDesign.EndChapter}";
                        isCompleted = allSummaries.ContainsKey(endChapterId);
                    }
                    else
                    {
                        var volChapterCount = allSummaries.Keys
                            .Count(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == vol);
                        isCompleted = !isMaxVol || volChapterCount >= 7;
                    }
                    if (!isCompleted) continue;

                    var archivePath = Path.Combine(archivesDir, $"vol{vol}.json");
                    if (File.Exists(archivePath)) continue;

                    try
                    {
                        var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol);
                        var lastChapterId = volSummaries.Keys
                            .Where(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == vol)
                            .OrderBy(id => ChapterParserHelper.ParseChapterId(id)?.chapterNumber ?? 0)
                            .LastOrDefault() ?? $"vol{vol}_ch0";

                        var snapshot = await ServiceLocator.Get<FactSnapshotExtractor>().ExtractVolumeEndSnapshotAsync(lastChapterId);
                        Directory.CreateDirectory(archivesDir);
                        await ServiceLocator.Get<VolumeFactArchiveStore>().ArchiveVolumeAsync(vol, snapshot, lastChapterId);

                        result.FactArchivesRepaired++;
                        TM.App.Log($"[Reconciler] 已回补第{vol}卷 fact_archive（full snapshot）");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[Reconciler] 第{vol}卷 fact_archive 回补失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"fact_archive对账失败: {ex.Message}");
            }
        }

        public async Task AutoArchiveVolumeIfNeededAsync(int volumeNumber)
        {
            try
            {
                var volSummaries = await _summaryStore.GetVolumeSummariesAsync(volumeNumber);
                var lastChapterId = volSummaries.Keys
                    .Where(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == volumeNumber)
                    .OrderBy(id => ChapterParserHelper.ParseChapterId(id)?.chapterNumber ?? 0)
                    .LastOrDefault();

                if (string.IsNullOrEmpty(lastChapterId))
                {
                    try
                    {
                        var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                        if (Directory.Exists(chaptersPath))
                        {
                            var pattern = $"vol{volumeNumber}_ch";
                            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly)
                                .Select(Path.GetFileNameWithoutExtension)
                                .Where(n => !string.IsNullOrWhiteSpace(n) && n.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                .Select(n => n!)
                                .ToList();

                            var best = mdFiles
                                .Select(id => new { Id = id, Parsed = ChapterParserHelper.ParseChapterId(id) })
                                .Where(x => x.Parsed.HasValue && x.Parsed.Value.volumeNumber == volumeNumber)
                                .OrderBy(x => x.Parsed!.Value.chapterNumber)
                                .LastOrDefault();

                            if (best != null)
                            {
                                lastChapterId = best.Id;
                                TM.App.Log($"[Reconciler] 第{volumeNumber}卷摘要缺失，已从章节文件推断卷末章: {lastChapterId}");
                            }
                        }
                    }
                    catch (Exception inferEx)
                    {
                        TM.App.Log($"[Reconciler] 第{volumeNumber}卷推断卷末章失败: {inferEx.Message}");
                    }
                }

                if (string.IsNullOrEmpty(lastChapterId))
                {
                    TM.App.Log($"[Reconciler] 第{volumeNumber}卷无已生成章节，跳过自动存档");
                    return;
                }

                try
                {
                    var volumeDesignService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                    if (!volumeDesignService.IsInitialized)
                        await volumeDesignService.InitializeAsync();
                    var volDesign = volumeDesignService.GetAllVolumeDesigns().FirstOrDefault(d => d.VolumeNumber == volumeNumber);
                    if (volDesign != null && volDesign.EndChapter > 0)
                    {
                        var endChapterId = $"vol{volumeNumber}_ch{volDesign.EndChapter}";
                        if (!string.Equals(lastChapterId, endChapterId, StringComparison.OrdinalIgnoreCase))
                        {
                            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                            var endFile = Path.Combine(chaptersPath, $"{endChapterId}.md");
                            if (volSummaries.ContainsKey(endChapterId) || File.Exists(endFile))
                            {
                                lastChapterId = endChapterId;
                            }
                            else
                            {
                                TM.App.Log($"[Reconciler] 第{volumeNumber}卷未到EndChapter={volDesign.EndChapter}，跳过自动存档（last={lastChapterId}）");
                                return;
                            }
                        }
                    }
                }
                catch (Exception p6Ex)
                {
                    TM.App.Log($"[Reconciler] P6 EndChapter校验失败（非致命，继续旧逻辑）: {p6Ex.Message}");
                }

                var snapshot = await ServiceLocator.Get<FactSnapshotExtractor>().ExtractVolumeEndSnapshotAsync(lastChapterId);
                var archivesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
                Directory.CreateDirectory(archivesDir);
                await ServiceLocator.Get<VolumeFactArchiveStore>().ArchiveVolumeAsync(volumeNumber, snapshot, lastChapterId);

                TM.App.Log($"[Reconciler] 第{volumeNumber}卷自动存档完成，最后章节: {lastChapterId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] 第{volumeNumber}卷自动存档失败（非致命）: {ex.Message}");
            }
        }

        private static async Task<string> ReadHeadAsync(string filePath, int maxChars)
        {
            var bufferSize = maxChars * 3;
            var buffer = new byte[bufferSize];
            int bytesRead;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            }

            if (bytesRead == 0) return string.Empty;
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return text.Length > maxChars ? text[..maxChars] : text;
        }

        private static string ExtractSummaryFromHead(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var cleaned = content.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (cleaned.Length <= 500) return cleaned;

            var cutRegion = cleaned[..500];
            var lastSentenceEnd = cutRegion.LastIndexOfAny(new[] { '。', '！', '？', '…', '"' });
            if (lastSentenceEnd > 200)
            {
                return cutRegion[..(lastSentenceEnd + 1)] + "……";
            }

            return cutRegion + "……";
        }
    }
}
