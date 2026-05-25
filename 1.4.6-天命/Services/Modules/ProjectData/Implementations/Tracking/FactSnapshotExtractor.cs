using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Models.Guides;
using GuideCharacterState = TM.Services.Modules.ProjectData.Models.Guides.CharacterState;
using GuideRelationshipState = TM.Services.Modules.ProjectData.Models.Guides.RelationshipState;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class FactSnapshotExtractor
    {
        private readonly GuideManager _guideManager;

        private GuideContextService? _guideContextService;
        private GuideContextService GuideContextService => _guideContextService ??= ServiceLocator.Get<GuideContextService>();

        #region 构造函数

        public FactSnapshotExtractor(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        #endregion

        #region 常量

        private const string CharacterStateGuideFileName = "character_state_guide.json";
        private const string ConflictProgressGuideFileName = "conflict_progress_guide.json";
        private const string ForeshadowingStatusGuideFileName = "foreshadowing_status_guide.json";

        private static int ActiveEntityWindowChapters => LayeredContextConfig.ActiveEntityWindowChapters;
        private static int ActiveEntityWindowMaxCount  => LayeredContextConfig.ActiveEntityWindowMaxCount;

        #endregion

        #region 公开方法

        public async Task<FactSnapshot> ExtractSnapshotAsync(
            string chapterId,
            List<string> characterIds,
            List<string> locationIds,
            List<string> conflictIds,
            List<string> foreshadowingSetupIds,
            List<string> foreshadowingPayoffIds,
            List<string> worldRuleIds,
            List<string>? factionIds = null)
        {
            var snapshot = new FactSnapshot();
            var prevChapterId = GetPreviousChapterId(chapterId);
            var characterIdSet = characterIds != null && characterIds.Count > 0
                ? new HashSet<string>(characterIds, StringComparer.Ordinal)
                : null;

            snapshot.CharacterStates = await ExtractCharacterStatesAsync(characterIds, prevChapterId);

            snapshot.ConflictProgress = await ExtractConflictProgressAsync(conflictIds);

            snapshot.ForeshadowingStatus = await ExtractForeshadowingStatusAsync(
                foreshadowingSetupIds, 
                foreshadowingPayoffIds);

            var otherEntityIds = new HashSet<string>();
            otherEntityIds.UnionWith(conflictIds);
            otherEntityIds.UnionWith(foreshadowingSetupIds);
            otherEntityIds.UnionWith(foreshadowingPayoffIds);

            snapshot.PlotPoints = await ExtractPlotPointsAsync(chapterId, characterIds, otherEntityIds.ToList());

            snapshot.CharacterDescriptions = await ExtractCharacterDescriptionsAsync(characterIds);

            snapshot.LocationDescriptions = await ExtractLocationDescriptionsAsync(locationIds);

            snapshot.WorldRuleConstraints = await ExtractWorldRuleConstraintsAsync(worldRuleIds);

            snapshot.LocationStates = await ExtractLocationStatesAsync(locationIds, prevChapterId: prevChapterId);

            snapshot.FactionStates = await ExtractFactionStatesAsync(applyLimit: true, priorityIds: factionIds, prevChapterId: prevChapterId);

            snapshot.Timeline = await ExtractTimelineAsync();

            snapshot.CharacterLocations = await ExtractCharacterLocationsAsync(prevChapterId, forceIncludeCharacterIds: characterIds);

            snapshot.ItemStates = await ExtractItemStatesAsync(characterIds);

            if (snapshot.CharacterStates != null && snapshot.CharacterDescriptions != null)
            {
                var activeInjectedIds = snapshot.CharacterStates
                    .Select(s => s.Id)
                    .Where(id => (characterIdSet == null || !characterIdSet.Contains(id)) && !snapshot.CharacterDescriptions.ContainsKey(id))
                    .ToList();

                if (activeInjectedIds.Count > 0)
                {
                    var extraDescs = await ExtractCharacterDescriptionsAsync(activeInjectedIds);
                    foreach (var (id, desc) in extraDescs)
                        snapshot.CharacterDescriptions[id] = desc;

                    TM.App.Log($"[FactSnapshotExtractor] 补充注入活跃角色描述: {activeInjectedIds.Count}条");
                }
            }

            return snapshot;
        }

        public async Task<Dictionary<string, CharacterCoreDescription>> ExtractCharacterDescriptionsAsync(List<string>? characterIds)
        {
            var result = new Dictionary<string, CharacterCoreDescription>();
            if (characterIds == null || characterIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                var characters = await guideService.ExtractCharactersAsync(characterIds);

                foreach (var c in characters)
                {
                    var appearance = c.Appearance ?? string.Empty;
                    result[c.Id] = new CharacterCoreDescription
                    {
                        Id = c.Id,
                        Name = c.Name,
                        HairColor = ExtractHairColor(appearance),
                        EyeColor = string.Empty,
                        Appearance = appearance,
                        PersonalityTags = ParseTags(c.FlawBelief + "," + c.Identity + "," + c.Want)
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色描述失败: {ex.Message}");
            }

            return result;
        }

        public async Task<Dictionary<string, LocationCoreDescription>> ExtractLocationDescriptionsAsync(List<string> locationIds)
        {
            var result = new Dictionary<string, LocationCoreDescription>();
            if (locationIds == null || locationIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                var locations = await guideService.ExtractLocationsAsync(locationIds);

                foreach (var loc in locations)
                {
                    result[loc.Id] = new LocationCoreDescription
                    {
                        Id = loc.Id,
                        Name = loc.Name,
                        Description = loc.Description ?? string.Empty,
                        Features = ParseTags(loc.Description)
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取地点描述失败: {ex.Message}");
            }

            return result;
        }

        public async Task<List<WorldRuleConstraint>> ExtractWorldRuleConstraintsAsync(List<string> worldRuleIds)
        {
            var result = new List<WorldRuleConstraint>();

            if (worldRuleIds == null || worldRuleIds.Count == 0)
                return result;

            try
            {
                var guideService = GuideContextService;
                await guideService.InitializeCacheAsync();
                var worldRules = await guideService.ExtractWorldRulesAsync(worldRuleIds);

                foreach (var rule in worldRules)
                {
                    if (!string.IsNullOrEmpty(rule.HardRules))
                    {
                        result.Add(new WorldRuleConstraint
                        {
                            RuleId = rule.Id,
                            RuleName = rule.Name,
                            Constraint = rule.HardRules,
                            IsHardConstraint = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取世界观约束失败: {ex.Message}");
            }

            return result;
        }

        private static List<string> ParseTags(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            return text.Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && t.Length <= 20)
                .ToList();
        }

        private static readonly string[] HairColorKeywords =
            { "黑发", "金发", "白发", "红发", "银发", "棕发", "蓝发", "紫发", "绿发" };

        private static string ExtractHairColor(string appearance)
        {
            if (string.IsNullOrWhiteSpace(appearance)) return string.Empty;
            foreach (var keyword in HairColorKeywords)
            {
                if (appearance.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return keyword;
            }
            return string.Empty;
        }

        #endregion

        #region 私有方法 - 角色状态抽取

        private async Task<List<CharacterStateSnapshot>> ExtractCharacterStatesAsync(
            List<string>? characterIds,
            string prevChapterId)
        {
            var result = new List<CharacterStateSnapshot>();

            try
            {
                var guide = await AggregateCharacterStateGuideAsync();

                if (characterIds != null && characterIds.Count > 0)
                {
                    foreach (var characterId in characterIds)
                    {
                        if (!guide.Characters.TryGetValue(characterId, out var characterEntry))
                            continue;

                        if (characterEntry.StateHistory == null || characterEntry.StateHistory.Count == 0)
                            continue;

                        var state = BinarySearchState(characterEntry.StateHistory, prevChapterId);
                        if (state == null)
                            state = characterEntry.StateHistory.FirstOrDefault();

                        if (state != null)
                        {
                            result.Add(new CharacterStateSnapshot
                            {
                                Id = characterId,
                                Name = characterEntry.Name,
                                Stage = state.Level,
                                Abilities = string.Join("、", state.Abilities ?? new List<string>()),
                                Relationships = FormatRelationships(state.Relationships),
                                ChapterId = state.Chapter
                            });
                        }
                    }
                }

                if (characterIds != null && characterIds.Count > 0 && !string.IsNullOrEmpty(prevChapterId))
                {
                    var existingResultIds = new HashSet<string>(result.Select(r => r.Id));
                    var parsedPrev = ChapterParserHelper.ParseChapterId(prevChapterId);
                    if (parsedPrev.HasValue && parsedPrev.Value.volumeNumber > 1)
                    {
                        var archives = await ServiceLocator.Get<VolumeFactArchiveStore>()
                            .GetPreviousArchivesAsync(parsedPrev.Value.volumeNumber);
                        var archivesDesc = archives.OrderByDescending(a => a.VolumeNumber).ToList();
                        if (archivesDesc.Count == 0)
                            TM.App.Log($"[FactSnapshotExtractor] {prevChapterId} vol>1但无前卷存档，跨卷角色基线注入将静默跳过（请确认卷设计已配置EndChapter并触发卷末存档）");
                        var needsBaseline = characterIds.Where(id => !existingResultIds.Contains(id)).ToList();
                        foreach (var charId in needsBaseline)
                        {
                            foreach (var archive in archivesDesc)
                            {
                                var baseline = archive.CharacterStates.FirstOrDefault(s => s.Id == charId);
                                if (baseline == null) continue;
                                result.Add(new CharacterStateSnapshot
                                {
                                    Id = charId,
                                    Name = baseline.Name,
                                    Stage = baseline.Stage,
                                    Abilities = baseline.Abilities,
                                    Relationships = baseline.Relationships,
                                    ChapterId = $"vol{archive.VolumeNumber}_archive"
                                });
                                TM.App.Log($"[FactSnapshotExtractor] {charId} 使用前卷存档基线(第{archive.VolumeNumber}卷)");
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(prevChapterId))
                {
                    var existingIds = new HashSet<string>(result.Select(r => r.Id));
                    var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var activeSnapshots = new List<CharacterStateSnapshot>();

                    foreach (var (id, entry) in guide.Characters)
                    {
                        if (existingIds.Contains(id)) continue;
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;

                        var lastState = BinarySearchState(entry.StateHistory, prevChapterId);
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;

                        if (!IsActiveInRecentChapters(lastState.Chapter, prevChapterId, ActiveEntityWindowChapters, GetChaptersPerVol()))
                            continue;

                        activeSnapshots.Add(new CharacterStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Stage = lastState.Level,
                            Abilities = string.Join("、", lastState.Abilities ?? new List<string>()),
                            Relationships = FormatRelationships(lastState.Relationships),
                            ChapterId = lastState.Chapter
                        });
                    }

                    var injected = activeSnapshots
                        .OrderByDescending(s => s.ChapterId, comparer)
                        .Take(ActiveEntityWindowMaxCount)
                        .ToList();

                    result.AddRange(injected);

                    if (injected.Count > 0)
                        TM.App.Log($"[FactSnapshotExtractor] 注入近期活跃角色: {injected.Count}条");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色状态失败: {ex.Message}");
            }

            return result;
        }

        private static int GetChaptersPerVol()
        {
            try
            {
                var volService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                var designs = volService.GetAllVolumeDesigns();
                if (designs != null && designs.Count > 0)
                {
                    var withEnd = designs.Where(d => d.EndChapter > 0 && d.VolumeNumber > 0 && d.StartChapter > 0).ToList();
                    if (withEnd.Count > 0)
                        return (int)System.Math.Round(withEnd.Average(d => (double)(d.EndChapter - d.StartChapter + 1)));
                }
            }
            catch { }
            return 20;
        }

        private static bool IsActiveInRecentChapters(string lastChapterId, string prevChapterId, int windowSize, int chaptersPerVol = 20)
        {
            if (string.IsNullOrEmpty(lastChapterId) || string.IsNullOrEmpty(prevChapterId)) return false;

            if (ChapterParserHelper.CompareChapterId(lastChapterId, prevChapterId) > 0)
                return false;

            var current = ChapterParserHelper.ParseChapterId(prevChapterId);
            var last    = ChapterParserHelper.ParseChapterId(lastChapterId);
            if (current == null || last == null) return false;

            int distance = (current.Value.volumeNumber - last.Value.volumeNumber) * chaptersPerVol
                         + (current.Value.chapterNumber - last.Value.chapterNumber);
            return distance >= 0 && distance <= windowSize;
        }

        private GuideCharacterState? BinarySearchState(List<GuideCharacterState> history, string targetChapterId)
        {
            if (history == null || history.Count == 0)
                return null;

            int left = 0, right = history.Count - 1;
            int resultIndex = -1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                int cmp = ChapterParserHelper.CompareChapterId(history[mid].Chapter, targetChapterId);

                if (cmp <= 0)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return resultIndex >= 0 ? history[resultIndex] : null;
        }

        private string FormatRelationships(Dictionary<string, GuideRelationshipState>? relationships)
        {
            if (relationships == null || relationships.Count == 0)
                return string.Empty;

            var parts = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .Select(r => string.IsNullOrWhiteSpace(r.Value.Relation)
                    ? $"{r.Key}(信任{r.Value.Trust:+#;-#;0})"
                    : $"{r.Key}({r.Value.Relation},{r.Value.Trust:+#;-#;0})");

            return string.Join("、", parts);
        }

        #endregion

        #region 私有方法 - 冲突进度抽取

        private async Task<List<ConflictProgressSnapshot>> ExtractConflictProgressAsync(
            List<string> conflictIds)
        {
            var result = new List<ConflictProgressSnapshot>();

            if (conflictIds == null || conflictIds.Count == 0)
                return result;

            try
            {
                var guide = await AggregateConflictProgressGuideAsync();

                foreach (var conflictId in conflictIds)
                {
                    if (!guide.Conflicts.TryGetValue(conflictId, out var conflictEntry))
                        continue;

                    var recentProgress = (conflictEntry.ProgressPoints ?? new List<ConflictProgressPoint>())
                        .Where(p => !string.IsNullOrWhiteSpace(p.Event))
                        .TakeLast(10)
                        .Select(p => $"{p.Chapter}: {p.Event}")
                        .ToList();

                    result.Add(new ConflictProgressSnapshot
                    {
                        Id = conflictId,
                        Name = conflictEntry.Name,
                        Status = conflictEntry.Status,
                        RecentProgress = recentProgress
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取冲突进度失败: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region 私有方法 - 伏笔状态抽取

        private async Task<List<ForeshadowingStatusSnapshot>> ExtractForeshadowingStatusAsync(
            List<string> setupIds,
            List<string> payoffIds)
        {
            var result = new List<ForeshadowingStatusSnapshot>();

            var allIds = new HashSet<string>();
            if (setupIds != null) allIds.UnionWith(setupIds);
            if (payoffIds != null) allIds.UnionWith(payoffIds);

            if (allIds.Count == 0)
                return result;

            try
            {
                var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(ForeshadowingStatusGuideFileName);

                foreach (var foreshadowId in allIds)
                {
                    if (!guide.Foreshadowings.TryGetValue(foreshadowId, out var entry))
                        continue;

                    result.Add(new ForeshadowingStatusSnapshot
                    {
                        Id = foreshadowId,
                        Name = entry.Name,
                        IsSetup = entry.IsSetup,
                        IsResolved = entry.IsResolved,
                        IsOverdue = entry.IsOverdue,
                        SetupChapterId = entry.ActualSetupChapter,
                        PayoffChapterId = entry.ActualPayoffChapter
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取伏笔状态失败: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region 私有方法 - 关键情节抽取

        private async Task<List<PlotPointSnapshot>> ExtractPlotPointsAsync(
            string currentChapterId,
            List<string>? characterIds,
            List<string> otherEntityIds)
        {
            var result = new List<PlotPointSnapshot>();

            if ((characterIds == null || characterIds.Count == 0) &&
                (otherEntityIds == null || otherEntityIds.Count == 0))
                return result;

            try
            {
                var charSet = new HashSet<string>(characterIds ?? new List<string>());
                var otherSet = new HashSet<string>(otherEntityIds ?? new List<string>());

                var candidates = await ServiceLocator.Get<PlotPointsIndexService>().SearchRecentAsync(
                    currentChapterId, charSet, otherSet, lookbackVolumes: 5);

                if (candidates.Count == 0)
                    return result;

                var chapterComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                var relatedPlotPoints = candidates
                    .OrderByDescending(p => ImportanceScore(p))
                    .ThenByDescending(p => p.Chapter, chapterComparer)
                    .Take(15)
                    .ToList();

                foreach (var plotPoint in relatedPlotPoints)
                {
                    result.Add(new PlotPointSnapshot
                    {
                        Id = plotPoint.Id,
                        Summary = plotPoint.Context,
                        ChapterId = plotPoint.Chapter,
                        RelatedEntityIds = plotPoint.InvolvedCharacters ?? new List<string>(),
                        Storyline = plotPoint.Storyline
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取关键情节失败: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region 私有方法 - 优先级计算

        private static int ImportanceScore(PlotPointEntry p)
        {
            var score = p.Importance switch
            {
                "critical" => 3,
                "important" => 2,
                _ => 1
            };
            if (string.Equals(p.Storyline, "main", StringComparison.OrdinalIgnoreCase))
                score += 2;
            return score;
        }

        #endregion

        #region 私有方法 - 辅助

        private string GetPreviousChapterId(string chapterId)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (parsed == null)
                return string.Empty;

            var (vol, ch) = parsed.Value;

            if (ch > 1)
            {
                return ChapterParserHelper.BuildChapterId(vol, ch - 1);
            }
            else if (vol > 1)
            {
                var lastChapterOfPrevVolume = GetLastChapterOfVolume(vol - 1);
                if (lastChapterOfPrevVolume > 0)
                {
                    return ChapterParserHelper.BuildChapterId(vol - 1, lastChapterOfPrevVolume);
                }
                TM.App.Log($"[FactSnapshotExtractor] 无法确定卷{vol - 1}的最后一章，跳过跨卷状态抽取");
                return string.Empty;
            }

            return string.Empty;
        }

        private int GetLastChapterOfVolume(int volumeNumber)
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!System.IO.Directory.Exists(chaptersPath))
                    return 0;

                var volumePrefix = $"vol{volumeNumber}_ch";
                var chapterFiles = System.IO.Directory.GetFiles(chaptersPath, $"vol{volumeNumber}_ch*.md");

                if (chapterFiles.Length == 0)
                    return 0;

                var maxChapter = chapterFiles
                    .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
                    .Select(name => ChapterParserHelper.ParseChapterId(name))
                    .Where(p => p != null)
                    .Select(p => p!.Value.chapterNumber)
                    .DefaultIfEmpty(0)
                    .Max();

                return maxChapter;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 查询卷{volumeNumber}最后一章失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<List<LocationStateSnapshot>> ExtractLocationStatesAsync(
            List<string>? locationIds, bool allVolumes = false, string? prevChapterId = null)
        {
            var result = new List<LocationStateSnapshot>();
            try
            {
                var guide = await AggregateLocationStateGuideAsync(allVolumes);

                if (locationIds == null || locationIds.Count > 0)
                {
                    var filterIds = (locationIds != null && locationIds.Count > 0)
                        ? new HashSet<string>(locationIds, StringComparer.OrdinalIgnoreCase)
                        : null;
                    foreach (var (id, entry) in guide.Locations)
                    {
                        if (filterIds != null && !filterIds.Contains(id)) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        result.Add(new LocationStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Status = entry.CurrentStatus,
                            ChapterId = lastState?.Chapter ?? string.Empty
                        });
                    }
                }

                if (!string.IsNullOrEmpty(prevChapterId) && !allVolumes)
                {
                    var existingIds = new HashSet<string>(result.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
                    var chaptersPerVol = GetChaptersPerVol();
                    var activeSnapshots = new List<LocationStateSnapshot>();

                    foreach (var (id, entry) in guide.Locations)
                    {
                        if (existingIds.Contains(id)) continue;
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;
                        if (!IsActiveInRecentChapters(lastState.Chapter, prevChapterId, ActiveEntityWindowChapters, chaptersPerVol))
                            continue;
                        activeSnapshots.Add(new LocationStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Status = entry.CurrentStatus,
                            ChapterId = lastState.Chapter
                        });
                    }

                    var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var injected = activeSnapshots
                        .OrderByDescending(s => s.ChapterId, comparer)
                        .Take(ActiveEntityWindowMaxCount)
                        .ToList();
                    result.AddRange(injected);
                    if (injected.Count > 0)
                        TM.App.Log($"[FactSnapshotExtractor] 注入近期活跃地点: {injected.Count}条");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取地点状态失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<FactionStateSnapshot>> ExtractFactionStatesAsync(
            bool applyLimit = false, bool allVolumes = false, List<string>? priorityIds = null, string? prevChapterId = null)
        {
            var result = new List<FactionStateSnapshot>();
            try
            {
                var guide = await AggregateFactionStateGuideAsync(allVolumes);
                foreach (var (id, entry) in guide.Factions)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    result.Add(new FactionStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Status = entry.CurrentStatus,
                        ChapterId = lastState?.Chapter ?? string.Empty
                    });
                }

                if (!string.IsNullOrEmpty(prevChapterId) && !allVolumes)
                {
                    var chaptersPerVol = GetChaptersPerVol();
                    var activeIds = new List<string>();
                    foreach (var (id, entry) in guide.Factions)
                    {
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;
                        if (IsActiveInRecentChapters(lastState.Chapter, prevChapterId, ActiveEntityWindowChapters, chaptersPerVol))
                            activeIds.Add(id);
                    }
                    if (activeIds.Count > 0)
                    {
                        priorityIds = priorityIds != null
                            ? priorityIds.Union(activeIds).Distinct().ToList()
                            : activeIds;
                        TM.App.Log($"[FactSnapshotExtractor] 近期活跃势力补入优先池: {activeIds.Count}条");
                    }
                }

                if (applyLimit)
                {
                    var max = LayeredContextConfig.SnapshotMaxFactionInject;
                    if (result.Count > max)
                    {
                        var factionComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                        if (priorityIds != null && priorityIds.Count > 0)
                        {
                            var prioritySet = new HashSet<string>(priorityIds, StringComparer.OrdinalIgnoreCase);
                            var priority = result.Where(f => prioritySet.Contains(f.Id)).ToList();
                            var others = result
                                .Where(f => !prioritySet.Contains(f.Id))
                                .OrderByDescending(f => f.ChapterId, factionComparer)
                                .Take(Math.Max(0, max - priority.Count))
                                .ToList();
                            result = priority.Concat(others).ToList();
                        }
                        else
                        {
                            result = result
                                .OrderByDescending(f => f.ChapterId, factionComparer)
                                .Take(max)
                                .ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取势力状态失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<TimelineSnapshot>> ExtractTimelineAsync(bool allVolumes = false)
        {
            var result = new List<TimelineSnapshot>();
            try
            {
                var guide = await AggregateTimelineGuideAsync(allVolumes);
                var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                var recentCount = LayeredContextConfig.SnapshotMaxTimelineInject;
                var recent = guide.ChapterTimeline
                    .OrderByDescending(t => t.ChapterId, comparer)
                    .Take(recentCount)
                    .OrderBy(t => t.ChapterId, comparer)
                    .ToList();

                foreach (var entry in recent)
                {
                    result.Add(new TimelineSnapshot
                    {
                        ChapterId = entry.ChapterId,
                        TimePeriod = entry.TimePeriod,
                        ElapsedTime = entry.ElapsedTime,
                        KeyTimeEvent = entry.KeyTimeEvent
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取时间线失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<CharacterLocationSnapshot>> ExtractCharacterLocationsAsync(string prevChapterId, bool skipWindowFilter = false, List<string>? forceIncludeCharacterIds = null)
        {
            var result = new List<CharacterLocationSnapshot>();
            try
            {
                var guide = await AggregateTimelineGuideAsync();
                var forceSet = forceIncludeCharacterIds != null && forceIncludeCharacterIds.Count > 0
                    ? new System.Collections.Generic.HashSet<string>(forceIncludeCharacterIds, StringComparer.Ordinal)
                    : null;
                foreach (var (id, entry) in guide.CharacterLocations)
                {
                    if (!skipWindowFilter
                        && (forceSet == null || !forceSet.Contains(id))
                        && !IsActiveInRecentChapters(entry.LastUpdatedChapter, prevChapterId, ActiveEntityWindowChapters, GetChaptersPerVol()))
                        continue;

                    result.Add(new CharacterLocationSnapshot
                    {
                        CharacterId = id,
                        CharacterName = entry.CharacterName,
                        CurrentLocation = entry.CurrentLocation,
                        ChapterId = entry.LastUpdatedChapter
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色位置失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<ItemStateSnapshot>> ExtractItemStatesAsync(List<string>? characterIds = null, bool applyLimit = true, bool allVolumes = false)
        {
            var result = new List<ItemStateSnapshot>();
            try
            {
                var guide = await AggregateItemStateGuideAsync(allVolumes);
                var all = new List<ItemStateSnapshot>(guide.Items.Count);
                foreach (var (id, entry) in guide.Items)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    all.Add(new ItemStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        CurrentHolder = entry.CurrentHolder,
                        Status = entry.CurrentStatus,
                        ChapterId = lastState?.Chapter ?? string.Empty
                    });
                }

                if (!applyLimit)
                {
                    return all;
                }

                var maxInject = LayeredContextConfig.SnapshotMaxItemInject;
                if (characterIds != null && characterIds.Count > 0)
                {
                    var itemComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var charSet = new System.Collections.Generic.HashSet<string>(characterIds, StringComparer.Ordinal);
                    var related = all.Where(i => charSet.Contains(i.CurrentHolder)).ToList();
                    var others  = all
                        .Where(i => !charSet.Contains(i.CurrentHolder))
                        .OrderByDescending(i => i.ChapterId, itemComparer)
                        .ToList();
                    result.AddRange(related);
                    var remaining = maxInject - related.Count;
                    if (remaining > 0 && others.Count > 0)
                        result.AddRange(others.Take(remaining));
                    TM.App.Log($"[FactSnapshotExtractor] 物品注入: 关联{related.Count}条 + 补充{result.Count - related.Count}条");
                }
                else
                {
                    var itemComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    result = all.Count > maxInject
                        ? all.OrderByDescending(i => i.ChapterId, itemComparer).Take(maxInject).ToList()
                        : all;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取物品状态失败: {ex.Message}");
            }
            return result;
        }

        #endregion

        #region 公开方法 - 卷末全量快照（不依赖打包上下文）

        public async Task<FactSnapshot> ExtractVolumeEndSnapshotAsync(string chapterId)
        {
            var snapshot = new FactSnapshot();
            try
            {
                var charGuide = await AggregateCharacterStateGuideAsync(allVolumes: true);
                foreach (var (id, entry) in charGuide.Characters)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    if (lastState == null) continue;
                    snapshot.CharacterStates.Add(new CharacterStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Stage = lastState.Level,
                        Abilities = string.Join("、", lastState.Abilities ?? new List<string>()),
                        Relationships = FormatRelationships(lastState.Relationships),
                        ChapterId = lastState.Chapter
                    });
                }

                var conflictGuide = await AggregateConflictProgressGuideAsync(allVolumes: true);
                foreach (var (id, entry) in conflictGuide.Conflicts)
                {
                    snapshot.ConflictProgress.Add(new ConflictProgressSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Status = entry.Status,
                        RecentProgress = entry.ProgressPoints
                            .TakeLast(3)
                            .Select(p => p.Event)
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .ToList()
                    });
                }

                var foreshadowGuide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(ForeshadowingStatusGuideFileName);
                foreach (var (id, entry) in foreshadowGuide.Foreshadowings)
                {
                    snapshot.ForeshadowingStatus.Add(new ForeshadowingStatusSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        IsSetup = entry.IsSetup,
                        IsResolved = entry.IsResolved,
                        IsOverdue = entry.IsOverdue,
                        SetupChapterId = entry.ActualSetupChapter,
                        PayoffChapterId = entry.ActualPayoffChapter
                    });
                }

                snapshot.LocationStates = await ExtractLocationStatesAsync(null, allVolumes: true);
                snapshot.FactionStates = await ExtractFactionStatesAsync(applyLimit: false, allVolumes: true);
                snapshot.ItemStates = await ExtractItemStatesAsync(applyLimit: false, allVolumes: true);

                snapshot.Timeline = await ExtractTimelineAsync(allVolumes: true);

                snapshot.CharacterLocations = await ExtractCharacterLocationsAsync(chapterId, skipWindowFilter: true);

                TM.App.Log($"[FactSnapshotExtractor] 卷末全量快照: 角色{snapshot.CharacterStates.Count}+冲突{snapshot.ConflictProgress.Count}+伏笔{snapshot.ForeshadowingStatus.Count}+地点{snapshot.LocationStates.Count}+势力{snapshot.FactionStates.Count}+物品{snapshot.ItemStates.Count}+时间线{snapshot.Timeline.Count}+角色位置{snapshot.CharacterLocations.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 卷末快照抽取失败: {ex.Message}");
            }
            return snapshot;
        }

        #endregion

        #region 分卷聚合辅助

        private async Task<CharacterStateGuide> AggregateCharacterStateGuideAsync(bool allVolumes = false)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(CharacterStateGuideFileName);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new CharacterStateGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName(CharacterStateGuideFileName, vol));
                foreach (var (id, entry) in g.Characters)
                {
                    if (!merged.Characters.ContainsKey(id))
                        merged.Characters[id] = new CharacterStateEntry { Name = entry.Name };
                    merged.Characters[id].StateHistory.AddRange(entry.StateHistory);
                    if (entry.DriftWarnings?.Count > 0)
                        (merged.Characters[id].DriftWarnings ??= new()).AddRange(entry.DriftWarnings);
                }
            }
            foreach (var e in merged.Characters.Values)
                e.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
            return merged;
        }

        private async Task<ConflictProgressGuide> AggregateConflictProgressGuideAsync(bool allVolumes = false)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(ConflictProgressGuideFileName);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new ConflictProgressGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName(ConflictProgressGuideFileName, vol));
                foreach (var (id, entry) in g.Conflicts)
                {
                    if (!merged.Conflicts.ContainsKey(id))
                        merged.Conflicts[id] = new ConflictProgressEntry { Name = entry.Name };
                    merged.Conflicts[id].Status = entry.Status;
                    merged.Conflicts[id].ProgressPoints.AddRange(entry.ProgressPoints);
                }
            }
            return merged;
        }

        private async Task<LocationStateGuide> AggregateLocationStateGuideAsync(bool allVolumes = false)
        {
            const string f = "location_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new LocationStateGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName(f, vol));
                foreach (var (id, entry) in g.Locations) merged.Locations[id] = entry;
            }
            return merged;
        }

        private async Task<FactionStateGuide> AggregateFactionStateGuideAsync(bool allVolumes = false)
        {
            const string f = "faction_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new FactionStateGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName(f, vol));
                foreach (var (id, entry) in g.Factions) merged.Factions[id] = entry;
            }
            return merged;
        }

        private async Task<TimelineGuide> AggregateTimelineGuideAsync(bool allVolumes = false)
        {
            const string f = "timeline_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new TimelineGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<TimelineGuide>(GuideManager.GetVolumeFileName(f, vol));
                merged.ChapterTimeline.AddRange(g.ChapterTimeline);
                foreach (var (id, entry) in g.CharacterLocations) merged.CharacterLocations[id] = entry;
            }
            return merged;
        }

        private async Task<ItemStateGuide> AggregateItemStateGuideAsync(bool allVolumes = false)
        {
            const string f = "item_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = allVolumes ? vols : vols.TakeLast(5).ToList();
            var merged = new ItemStateGuide();
            foreach (var vol in recent)
            {
                var g = await _guideManager.GetGuideAsync<ItemStateGuide>(GuideManager.GetVolumeFileName(f, vol));
                foreach (var (id, entry) in g.Items) merged.Items[id] = entry;
            }
            return merged;
        }

        #endregion
    }
}
