using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class LedgerTrimService
    {
        private readonly GuideManager _guideManager;

        private static int CharacterStateKeepRecent   => LayeredContextConfig.LedgerCharacterStateKeepRecent;
        private static int ConflictProgressKeepRecent  => LayeredContextConfig.LedgerConflictProgressKeepRecent;
        private static int PlotPointsKeepRecent        => LayeredContextConfig.LedgerPlotPointsKeepRecent;
        private static int LocationStateKeepRecent     => LayeredContextConfig.LedgerLocationStateKeepRecent;
        private static int FactionStateKeepRecent      => LayeredContextConfig.LedgerFactionStateKeepRecent;
        private static int TimelineKeepRecent          => LayeredContextConfig.LedgerTimelineKeepRecent;
        private static int MovementKeepRecent          => LayeredContextConfig.LedgerMovementKeepRecent;
        private static int ItemStateKeepRecent         => LayeredContextConfig.LedgerItemStateKeepRecent;
        private static int MaxCriticalPerEntity => LayeredContextConfig.LedgerMaxCriticalPerEntity;

        private static readonly System.Collections.Generic.HashSet<string> PlotPointEscalationKeywords =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                "转折", "揭示", "决战", "牺牲", "死亡", "背叛", "觉醒", "终章",
                "最终", "真相", "关键", "逆转", "崩溃", "覆灭", "重生", "突破",
                "心理", "内心", "心境", "转变", "暗线", "隐线", "伏线", "暗示",
                "预兆", "推进", "心魔", "执念", "动摇", "崩塌", "觉察", "发现"
            };

        private static bool HasEscalationKeyword(PlotPointEntry p)
        {
            if (string.IsNullOrEmpty(p.Context)) return false;
            foreach (var kw in PlotPointEscalationKeywords)
                if (p.Context.Contains(kw, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsCritical(string? importance) =>
            string.Equals(importance, "critical", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsImportant(string? importance) =>
            string.Equals(importance, "important", System.StringComparison.OrdinalIgnoreCase);

        private static int ImportantKeepRecent => LayeredContextConfig.LedgerImportantKeepRecent;

        public LedgerTrimService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        public class TrimResult
        {
            public int CharactersTrimmed { get; set; }
            public int TotalStatesTrimmed { get; set; }
            public int ConflictsTrimmed { get; set; }
            public int TotalProgressTrimmed { get; set; }
            public int PlotPointsTrimmed { get; set; }
            public int LocationsTrimmed { get; set; }
            public int TotalLocationStatesTrimmed { get; set; }
            public int FactionsTrimmed { get; set; }
            public int TotalFactionStatesTrimmed { get; set; }
            public int TimelineEntriesTrimmed { get; set; }
            public int CharactersMovementsTrimmed { get; set; }
            public int TotalMovementsTrimmed { get; set; }
            public int ItemsTrimmed { get; set; }
            public int TotalItemStatesTrimmed { get; set; }
            public int ForeshadowingsResolved { get; set; }
        }

        public async Task<TrimResult> TrimAllAsync()
        {
            var result = new TrimResult();

            await TrimCharacterStateHistoryAsync(result);
            await TrimConflictProgressAsync(result);
            await TrimPlotPointsAsync(result);
            await TrimLocationStateHistoryAsync(result);
            await TrimFactionStateHistoryAsync(result);
            await TrimTimelineAsync(result);
            await TrimCharacterMovementsAsync(result);
            await TrimItemStateHistoryAsync(result);
            await TrimForeshadowingsAsync(result);

            if (result.TotalStatesTrimmed > 0 ||
                result.TotalProgressTrimmed > 0 ||
                result.PlotPointsTrimmed > 0 ||
                result.TotalLocationStatesTrimmed > 0 ||
                result.TotalFactionStatesTrimmed > 0 ||
                result.TimelineEntriesTrimmed > 0 ||
                result.TotalMovementsTrimmed > 0 ||
                result.TotalItemStatesTrimmed > 0 ||
                result.ForeshadowingsResolved > 0)
            {
                await _guideManager.FlushAllAsync();
                TM.App.Log($"[LedgerTrim] 裁剪完成: 角色{result.CharactersTrimmed}个({result.TotalStatesTrimmed}条状态), " +
                           $"冲突{result.ConflictsTrimmed}个({result.TotalProgressTrimmed}条进度), " +
                           $"情节点{result.PlotPointsTrimmed}条, " +
                           $"地点{result.LocationsTrimmed}个({result.TotalLocationStatesTrimmed}条状态), " +
                           $"势力{result.FactionsTrimmed}个({result.TotalFactionStatesTrimmed}条状态), " +
                           $"时间线{result.TimelineEntriesTrimmed}条, " +
                           $"移动{result.CharactersMovementsTrimmed}人({result.TotalMovementsTrimmed}条), " +
                           $"物品{result.ItemsTrimmed}个({result.TotalItemStatesTrimmed}条状态), " +
                           $"伏笔清理{result.ForeshadowingsResolved}条");
            }
            else
            {
                TM.App.Log("[LedgerTrim] skip: within limits");
            }

            return result;
        }

        private async Task TrimCharacterStateHistoryAsync(TrimResult result)
        {
            foreach (var csFile in GetVolFiles("character_state_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<CharacterStateGuide>(csFile);
            var csPrev = result.TotalStatesTrimmed;
            foreach (var (characterId, entry) in guide.Characters)
            {
                var history = entry.StateHistory;
                if (history.Count <= CharacterStateKeepRecent)
                    continue;

                var trimCount = history.Count - CharacterStateKeepRecent;
                var trimZone = history.Take(trimCount).ToList();
                var keepZone = history.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();

                var normalAnchors = BuildNormalAnchors(trueNormalEntries);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<CharacterState>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);

                entry.StateHistory = rebuilt;
                result.CharactersTrimmed++;
                result.TotalStatesTrimmed += System.Math.Max(0, actualTrimmed);
            }

            if (result.TotalStatesTrimmed > csPrev) _guideManager.MarkDirty(csFile);
            }
        }

        private static int NormalSampleInterval => LayeredContextConfig.LedgerNormalSampleInterval;

        private static System.Collections.Generic.List<CharacterState> BuildNormalAnchors(
            System.Collections.Generic.List<CharacterState> normals)
        {
            var anchors = new System.Collections.Generic.List<CharacterState>();
            if (normals.Count == 0) return anchors;

            var first = normals.First();
            first.KeyEvent = $"[起始锚点] {first.KeyEvent}（归档{normals.Count}条）";
            anchors.Add(first);

            for (int i = NormalSampleInterval; i < normals.Count - 1; i += NormalSampleInterval)
            {
                var sample = normals[i];
                sample.KeyEvent = $"[中间快照@{i}] {sample.KeyEvent}";
                anchors.Add(sample);
            }

            if (normals.Count > 1)
            {
                var last = normals.Last();
                last.KeyEvent = $"[截止快照] {last.KeyEvent}";
                anchors.Add(last);
            }

            return anchors;
        }

        private static System.Collections.Generic.List<PlotPointEntry> BuildPlotPointAnchors(
            System.Collections.Generic.List<PlotPointEntry> normals)
        {
            var anchors = new System.Collections.Generic.List<PlotPointEntry>();
            if (normals.Count == 0) return anchors;

            var first = normals.First();
            first.Context = $"[起始锚点] {first.Context}（归档{normals.Count}条）";
            anchors.Add(first);

            for (int i = NormalSampleInterval; i < normals.Count - 1; i += NormalSampleInterval)
            {
                var sample = normals[i];
                sample.Context = $"[中间快照@{i}] {sample.Context}";
                anchors.Add(sample);
            }

            if (normals.Count > 1)
            {
                var last = normals.Last();
                last.Context = $"[截止快照] {last.Context}";
                anchors.Add(last);
            }

            return anchors;
        }

        private async Task TrimConflictProgressAsync(TrimResult result)
        {
            foreach (var cpFile in GetVolFiles("conflict_progress_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<ConflictProgressGuide>(cpFile);
            var cpPrev = result.TotalProgressTrimmed;
            foreach (var (conflictId, entry) in guide.Conflicts)
            {
                var points = entry.ProgressPoints;
                if (points.Count <= ConflictProgressKeepRecent)
                    continue;

                var trimCount = points.Count - ConflictProgressKeepRecent;
                var trimZone = points.Take(trimCount).ToList();
                var keepZone = points.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(p => IsCritical(p.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(p => IsImportant(p.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(p => !IsCritical(p.Importance) && !IsImportant(p.Importance)).ToList();
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - (trueNormalEntries.Count > 1 ? 2 : 1));

                var rebuilt = new List<ConflictProgressPoint>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                if (trueNormalEntries.Count > 0)
                {
                    var firstEntry = trueNormalEntries.First();
                    firstEntry.Description = $"[起始锚点] {firstEntry.Description}（归档{trueNormalEntries.Count}条）";
                    rebuilt.Add(firstEntry);
                    if (trueNormalEntries.Count > 1)
                    {
                        var lastEntry = trueNormalEntries.Last();
                        lastEntry.Description = $"[截止快照] {lastEntry.Description}";
                        rebuilt.Add(lastEntry);
                    }
                }
                rebuilt.AddRange(keepZone);

                entry.ProgressPoints = rebuilt;
                result.ConflictsTrimmed++;
                result.TotalProgressTrimmed += System.Math.Max(0, actualTrimmed);
            }

            if (result.TotalProgressTrimmed > cpPrev) _guideManager.MarkDirty(cpFile);
            }
        }

        private async Task TrimPlotPointsAsync(TrimResult result)
        {
            var store = ServiceLocator.Get<PlotPointsIndexService>();
            var volNumbers = store.GetExistingVolumeNumbers();

            foreach (var vol in volNumbers)
            {
                var points = await store.GetVolumeEntriesAsync(vol);
                if (points.Count <= PlotPointsKeepRecent)
                    continue;

                var trimCount = points.Count - PlotPointsKeepRecent;
                var trimZone = points.Take(trimCount).ToList();
                var keepZone = points.Skip(trimCount).ToList();

                foreach (var p in trimZone)
                    if (!IsCritical(p.Importance) && HasEscalationKeyword(p))
                        p.Importance = "critical";

                var criticalEntries = trimZone.Where(p => IsCritical(p.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(p => IsImportant(p.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(p => !IsCritical(p.Importance) && !IsImportant(p.Importance)).ToList();
                var normalAnchors = BuildPlotPointAnchors(trueNormalEntries);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<PlotPointEntry>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);

                await store.SetVolumeEntriesAsync(vol, rebuilt);
                result.PlotPointsTrimmed += System.Math.Max(0, actualTrimmed);
            }
        }

        private async Task TrimLocationStateHistoryAsync(TrimResult result)
        {
            foreach (var locFile in GetVolFiles("location_state_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<LocationStateGuide>(locFile);
            var locPrev = result.TotalLocationStatesTrimmed;
            foreach (var (_, entry) in guide.Locations)
            {
                var history = entry.StateHistory;
                if (history.Count <= LocationStateKeepRecent)
                    continue;

                var trimCount = history.Count - LocationStateKeepRecent;
                var trimZone = history.Take(trimCount).ToList();
                var keepZone = history.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                var normalAnchors = BuildLocationAnchors(trueNormalEntries);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<LocationStatePoint>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);
                entry.StateHistory = rebuilt;

                result.LocationsTrimmed++;
                result.TotalLocationStatesTrimmed += System.Math.Max(0, actualTrimmed);
            }

            if (result.TotalLocationStatesTrimmed > locPrev) _guideManager.MarkDirty(locFile);
            }
        }

        private async Task TrimFactionStateHistoryAsync(TrimResult result)
        {
            foreach (var facFile in GetVolFiles("faction_state_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<FactionStateGuide>(facFile);
            var facPrev = result.TotalFactionStatesTrimmed;
            foreach (var (_, entry) in guide.Factions)
            {
                var history = entry.StateHistory;
                if (history.Count <= FactionStateKeepRecent)
                    continue;

                var trimCount = history.Count - FactionStateKeepRecent;
                var trimZone = history.Take(trimCount).ToList();
                var keepZone = history.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                var normalAnchors = BuildFactionAnchors(trueNormalEntries);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<FactionStatePoint>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);
                entry.StateHistory = rebuilt;

                result.FactionsTrimmed++;
                result.TotalFactionStatesTrimmed += System.Math.Max(0, actualTrimmed);
            }

            if (result.TotalFactionStatesTrimmed > facPrev) _guideManager.MarkDirty(facFile);
            }
        }

        private async Task TrimTimelineAsync(TrimResult result)
        {
            foreach (var tlFile in GetVolFiles("timeline_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<TimelineGuide>(tlFile);
            var tlPrev = result.TimelineEntriesTrimmed;
            var timeline = guide.ChapterTimeline;
            if (timeline.Count <= TimelineKeepRecent)
                continue;

            var trimCount = timeline.Count - TimelineKeepRecent;
            var trimZone = timeline.Take(trimCount).ToList();
            var keepZone = timeline.Skip(trimCount).ToList();

            var criticalEntries = trimZone.Where(t => IsCritical(t.Importance)).ToList();
            if (criticalEntries.Count > MaxCriticalPerEntity)
                criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

            var importantEntries = trimZone.Where(t => IsImportant(t.Importance)).TakeLast(ImportantKeepRecent).ToList();
            var trueNormalEntries = trimZone.Where(t => !IsCritical(t.Importance) && !IsImportant(t.Importance)).ToList();
            var actualTrimmed = System.Math.Max(0, trimCount - criticalEntries.Count - importantEntries.Count - System.Math.Min(trueNormalEntries.Count, 2));

            var rebuilt = new List<ChapterTimeEntry>();
            rebuilt.AddRange(criticalEntries);
            rebuilt.AddRange(importantEntries);
            if (trueNormalEntries.Count > 0)
            {
                var firstEntry = trueNormalEntries.First();
                firstEntry.KeyTimeEvent = $"[起始时间间] {firstEntry.KeyTimeEvent}（归档{trueNormalEntries.Count}条）";
                rebuilt.Add(firstEntry);
                if (trueNormalEntries.Count > 1)
                {
                    var lastEntry = trueNormalEntries.Last();
                    lastEntry.KeyTimeEvent = $"[时间截止快照] {lastEntry.KeyTimeEvent}";
                    rebuilt.Add(lastEntry);
                }
            }
            rebuilt.AddRange(keepZone);

            guide.ChapterTimeline = rebuilt;
            result.TimelineEntriesTrimmed += System.Math.Max(0, actualTrimmed);

            if (result.TimelineEntriesTrimmed > tlPrev) _guideManager.MarkDirty(tlFile);
            }
        }

        private async Task TrimCharacterMovementsAsync(TrimResult result)
        {
            foreach (var mvFile in GetVolFiles("timeline_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<TimelineGuide>(mvFile);
            var modified = false;

            foreach (var (_, entry) in guide.CharacterLocations)
            {
                var moves = entry.MovementHistory;
                if (moves.Count <= MovementKeepRecent)
                    continue;

                var trimCount = moves.Count - MovementKeepRecent;
                var trimZone = moves.Take(trimCount).ToList();
                var keepZone = moves.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(m => IsCritical(m.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(m => IsImportant(m.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(m => !IsCritical(m.Importance) && !IsImportant(m.Importance)).ToList();

                var rebuilt = new List<MovementRecord>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                if (trueNormalEntries.Count > 0)
                {
                    rebuilt.Add(trueNormalEntries.First());
                    if (trueNormalEntries.Count > 1)
                        rebuilt.Add(trueNormalEntries.Last());
                }
                rebuilt.AddRange(keepZone);

                entry.MovementHistory = rebuilt;
                var actualTrimmed = trimCount - criticalEntries.Count - importantEntries.Count - System.Math.Min(trueNormalEntries.Count, 2);
                if (actualTrimmed > 0)
                {
                    result.CharactersMovementsTrimmed++;
                    result.TotalMovementsTrimmed += actualTrimmed;
                    modified = true;
                }
            }

            if (modified) _guideManager.MarkDirty(mvFile);
            }
        }

        private async Task TrimItemStateHistoryAsync(TrimResult result)
        {
            foreach (var itFile in GetVolFiles("item_state_guide.json"))
            {
            var guide = await _guideManager.GetGuideAsync<ItemStateGuide>(itFile);
            var itPrev = result.TotalItemStatesTrimmed;
            foreach (var (_, entry) in guide.Items)
            {
                var history = entry.StateHistory;
                if (history.Count <= ItemStateKeepRecent)
                    continue;

                var trimCount = history.Count - ItemStateKeepRecent;
                var trimZone = history.Take(trimCount).ToList();
                var keepZone = history.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                if (criticalEntries.Count > MaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(MaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(ImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                var normalAnchors = BuildItemAnchors(trueNormalEntries);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<ItemStatePoint>();
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);
                entry.StateHistory = rebuilt;

                result.ItemsTrimmed++;
                result.TotalItemStatesTrimmed += System.Math.Max(0, actualTrimmed);
            }

            if (result.TotalItemStatesTrimmed > itPrev) _guideManager.MarkDirty(itFile);
            }
        }

        private async Task TrimForeshadowingsAsync(TrimResult result)
        {
            const string fileName = "foreshadowing_status_guide.json";
            var guide = await _guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ForeshadowingStatusGuide>(fileName);
            var modified = false;

            var resolvedIds = new System.Collections.Generic.HashSet<string>(
                guide.Foreshadowings.Where(kv => kv.Value.IsResolved).Select(kv => kv.Key));

            var removedPending = guide.PendingList.RemoveAll(p => resolvedIds.Contains(p.Id));
            var removedOverdue = guide.OverdueList.RemoveAll(o => resolvedIds.Contains(o.Id));

            if (removedPending > 0 || removedOverdue > 0)
            {
                result.ForeshadowingsResolved += removedPending + removedOverdue;
                modified = true;
            }

            if (modified)
                _guideManager.MarkDirty(fileName);
        }

        private List<string> GetVolFiles(string baseFile)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(baseFile);
            return vols.Select(v => GuideManager.GetVolumeFileName(baseFile, v)).ToList();
        }

        private static List<LocationStatePoint> BuildLocationAnchors(List<LocationStatePoint> normals)
        {
            var anchors = new List<LocationStatePoint>();
            if (normals.Count == 0) return anchors;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = NormalSampleInterval; i < normals.Count - 1; i += NormalSampleInterval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }

        private static List<FactionStatePoint> BuildFactionAnchors(List<FactionStatePoint> normals)
        {
            var anchors = new List<FactionStatePoint>();
            if (normals.Count == 0) return anchors;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = NormalSampleInterval; i < normals.Count - 1; i += NormalSampleInterval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }

        private static List<ItemStatePoint> BuildItemAnchors(List<ItemStatePoint> normals)
        {
            var anchors = new List<ItemStatePoint>();
            if (normals.Count == 0) return anchors;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = NormalSampleInterval; i < normals.Count - 1; i += NormalSampleInterval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }
    }
}
