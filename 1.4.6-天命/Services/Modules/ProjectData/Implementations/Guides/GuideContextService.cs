using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Contexts.Generate;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Characters;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public static class LayeredContextConfig
    {
        public static int PreviousSummaryCount { get; set; } = 30;

        public const int MdFallbackMaxDistance = 1;

        public static int MdSummaryExtractLength { get; set; } = 500;

        public static int ActiveEntityWindowChapters { get; set; } = 8;

        public static int ActiveEntityWindowMaxCount { get; set; } = 25;

        public static int SummaryRecentWindowCount { get; set; } = 30;

        public static int MilestoneAnchorInterval { get; set; } = 8;

        public static int VolumeMilestoneMaxChars { get; set; } = 20000;

        public static int VolumeMilestoneTailRecentCount { get; set; } = 15;

        public const int PreviousChapterTailLength = 1000;

        public const int PreviousChapterTailMinLength = 200;

        public static int LedgerCharacterStateKeepRecent { get; set; } = 10000000;
        public static int LedgerConflictProgressKeepRecent { get; set; } = 2000000;
        public static int LedgerPlotPointsKeepRecent { get; set; } = 10000000;
        public static int LedgerLocationStateKeepRecent { get; set; } = 5000000;
        public static int LedgerFactionStateKeepRecent { get; set; } = 5000000;
        public static int LedgerTimelineKeepRecent { get; set; } = 10000000;
        public static int LedgerMovementKeepRecent { get; set; } = 5000000;
        public static int LedgerItemStateKeepRecent { get; set; } = 5000000;
        public static int LedgerMaxCriticalPerEntity { get; set; } = int.MaxValue;
        public static int LedgerImportantKeepRecent { get; set; } = 2000000;
        public static int LedgerNormalSampleInterval { get; set; } = 50;
        public static int DriftWarningsMaxPerEntity { get; set; } = 100000;
        public static int SnapshotMaxFactionInject { get; set; } = 30;
        public static int SnapshotMaxItemInject { get; set; } = 50;
        public static int SnapshotMaxTimelineInject { get; set; } = 5;
        public static int MilestoneMaxPreviousVolumes { get; set; } = 12;
        public static int ArchiveMaxPreviousVolumes { get; set; } = 8;

        public static int ArchiveInjectMaxCharacterStates { get; set; } = 60;

        public static int ArchiveInjectMaxConflictProgress { get; set; } = 25;

        public static int ArchiveInjectMaxTimelineEntries { get; set; } = 10;

        public static int ArchiveInjectMaxCharacterLocations { get; set; } = 50;

        public static int ArchiveInjectMaxFactionStates { get; set; } = 20;

        public static int ArchiveInjectMaxLocationStates { get; set; } = 20;

        public static int ArchiveInjectMaxFieldChars { get; set; } = 300;

        public static int ArchiveInjectMaxItemStates { get; set; } = 50;

        public static int ArchiveInjectMaxForeshadowingStatus { get; set; } = 40;

        private static readonly string SettingsFileName = "layered_context_settings.json";

        public static async Task InitializeFromStorageAsync()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    StoragePathHelper.GetServicesStoragePath("Settings"), SettingsFileName);
                if (!System.IO.File.Exists(path)) return;

                var json = await System.IO.File.ReadAllTextAsync(path);
                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json, opts);
                if (dict == null) return;

                void TrySetInt(string key, Action<int> setter, int min, int max)
                {
                    if (dict.TryGetValue(key, out var el) && el.TryGetInt32(out var v))
                        setter(Math.Clamp(v, min, max));
                }

                TrySetInt(nameof(ActiveEntityWindowChapters),         v => ActiveEntityWindowChapters = v, 1, 1000);
                TrySetInt(nameof(ActiveEntityWindowMaxCount),         v => ActiveEntityWindowMaxCount = v, 0, 10000);
                TrySetInt(nameof(SummaryRecentWindowCount),           v => SummaryRecentWindowCount = v, 0, 2000);
                TrySetInt(nameof(PreviousSummaryCount),               v => PreviousSummaryCount = v, 0, 2000);
                TrySetInt(nameof(MilestoneAnchorInterval),            v => MilestoneAnchorInterval = v, 1, 1000);
                TrySetInt(nameof(VolumeMilestoneMaxChars),            v => VolumeMilestoneMaxChars = v, 0, 2000000);
                TrySetInt(nameof(VolumeMilestoneTailRecentCount),     v => VolumeMilestoneTailRecentCount = v, 0, 2000);
                TrySetInt(nameof(MilestoneMaxPreviousVolumes),        v => MilestoneMaxPreviousVolumes = v, 0, 2000);
                TrySetInt(nameof(ArchiveMaxPreviousVolumes),          v => ArchiveMaxPreviousVolumes = v, 0, 2000);
                TrySetInt(nameof(SnapshotMaxFactionInject),           v => SnapshotMaxFactionInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxItemInject),              v => SnapshotMaxItemInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxTimelineInject),          v => SnapshotMaxTimelineInject = v, 1, 50);
                TrySetInt(nameof(ArchiveInjectMaxCharacterStates),    v => ArchiveInjectMaxCharacterStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxConflictProgress),   v => ArchiveInjectMaxConflictProgress = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxTimelineEntries),    v => ArchiveInjectMaxTimelineEntries = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxCharacterLocations), v => ArchiveInjectMaxCharacterLocations = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxFactionStates),      v => ArchiveInjectMaxFactionStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxLocationStates),     v => ArchiveInjectMaxLocationStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxFieldChars),         v => ArchiveInjectMaxFieldChars = v, 0, 1000000);
                TrySetInt(nameof(ArchiveInjectMaxItemStates),         v => ArchiveInjectMaxItemStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxForeshadowingStatus), v => ArchiveInjectMaxForeshadowingStatus = v, 0, 10000);

                TM.App.Log("[LayeredContextConfig] 已从本地存储加载参数");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LayeredContextConfig] 加载本地参数失败，使用默认值: {ex.Message}");
            }
        }

        public static async Task SaveToStorageAsync()
        {
            try
            {
                var dir = StoragePathHelper.GetServicesStoragePath("Settings");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, SettingsFileName);
                var dict = new Dictionary<string, int>
                {
                    [nameof(ActiveEntityWindowChapters)]    = ActiveEntityWindowChapters,
                    [nameof(ActiveEntityWindowMaxCount)]    = ActiveEntityWindowMaxCount,
                    [nameof(SummaryRecentWindowCount)]      = SummaryRecentWindowCount,
                    [nameof(PreviousSummaryCount)]          = PreviousSummaryCount,
                    [nameof(MilestoneAnchorInterval)]       = MilestoneAnchorInterval,
                    [nameof(VolumeMilestoneMaxChars)]       = VolumeMilestoneMaxChars,
                    [nameof(VolumeMilestoneTailRecentCount)]= VolumeMilestoneTailRecentCount,
                    [nameof(MilestoneMaxPreviousVolumes)]   = MilestoneMaxPreviousVolumes,
                    [nameof(ArchiveMaxPreviousVolumes)]     = ArchiveMaxPreviousVolumes,
                    [nameof(SnapshotMaxFactionInject)]      = SnapshotMaxFactionInject,
                    [nameof(SnapshotMaxItemInject)]         = SnapshotMaxItemInject,
                    [nameof(SnapshotMaxTimelineInject)]     = SnapshotMaxTimelineInject,
                    [nameof(ArchiveInjectMaxCharacterStates)]    = ArchiveInjectMaxCharacterStates,
                    [nameof(ArchiveInjectMaxConflictProgress)]   = ArchiveInjectMaxConflictProgress,
                    [nameof(ArchiveInjectMaxTimelineEntries)]    = ArchiveInjectMaxTimelineEntries,
                    [nameof(ArchiveInjectMaxCharacterLocations)] = ArchiveInjectMaxCharacterLocations,
                    [nameof(ArchiveInjectMaxFactionStates)]      = ArchiveInjectMaxFactionStates,
                    [nameof(ArchiveInjectMaxLocationStates)]     = ArchiveInjectMaxLocationStates,
                    [nameof(ArchiveInjectMaxFieldChars)]         = ArchiveInjectMaxFieldChars,
                    [nameof(ArchiveInjectMaxItemStates)]              = ArchiveInjectMaxItemStates,
                    [nameof(ArchiveInjectMaxForeshadowingStatus)]    = ArchiveInjectMaxForeshadowingStatus,
                };
                var json = System.Text.Json.JsonSerializer.Serialize(dict,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(path, json);
                TM.App.Log("[LayeredContextConfig] 参数已保存到本地存储");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LayeredContextConfig] 保存本地参数失败: {ex.Message}");
            }
        }
    }

    public class GuideContextService
    {
        private readonly FactSnapshotExtractor _factSnapshotExtractor;
        private readonly IWorkScopeService _workScopeService;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;

        private static string[] _cachedChapterIds = Array.Empty<string>();
        private static string _chapterIdsCachedForPath = string.Empty;
        private static DateTime _chapterIdsCachedAt = DateTime.MinValue;
        private static readonly object _chapterIdsCacheLock = new();

        public GuideContextService(FactSnapshotExtractor factSnapshotExtractor, IWorkScopeService workScopeService, ChapterSummaryStore summaryStore, ChapterMilestoneStore milestoneStore)
        {
            _factSnapshotExtractor = factSnapshotExtractor;
            _workScopeService = workScopeService;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;

            _workScopeService.ScopeChanged += (_, _) => ClearCache();
            CacheInvalidated += (_, _) => ClearCache();

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => ClearCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public static event EventHandler? CacheInvalidated;

        public static void RaiseCacheInvalidated()
        {
            CacheInvalidated?.Invoke(null, EventArgs.Empty);
            TM.App.Log("[GuideContextService] 已触发全局缓存失效事件");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string TruncateString(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private static List<VolumeFactArchive> BuildInjectableArchives(
            List<VolumeFactArchive> rawArchives,
            Models.Guides.ContextIdCollection? contextIds)
        {
            if (rawArchives == null || rawArchives.Count == 0)
                return new List<VolumeFactArchive>();

            var focusCharacters = contextIds?.Characters ?? new List<string>();
            var focusConflicts = contextIds?.Conflicts ?? new List<string>();
            var focusFactions = contextIds?.Factions ?? new List<string>();
            var focusLocations = contextIds?.Locations ?? new List<string>();

            var maxChars = LayeredContextConfig.ArchiveInjectMaxFieldChars;
            var maxCharacterStates = LayeredContextConfig.ArchiveInjectMaxCharacterStates;
            var maxConflicts = LayeredContextConfig.ArchiveInjectMaxConflictProgress;
            var maxTimeline = LayeredContextConfig.ArchiveInjectMaxTimelineEntries;
            var maxLocations = LayeredContextConfig.ArchiveInjectMaxCharacterLocations;
            var maxFactionStates = LayeredContextConfig.ArchiveInjectMaxFactionStates;
            var maxLocationStates = LayeredContextConfig.ArchiveInjectMaxLocationStates;

            var focusCharacterSet = new HashSet<string>(focusCharacters, StringComparer.OrdinalIgnoreCase);
            var focusConflictSet = new HashSet<string>(focusConflicts, StringComparer.OrdinalIgnoreCase);
            var focusFactionSet = new HashSet<string>(focusFactions, StringComparer.OrdinalIgnoreCase);
            var focusLocationSet = new HashSet<string>(focusLocations, StringComparer.OrdinalIgnoreCase);

            var result = new List<VolumeFactArchive>(rawArchives.Count);
            foreach (var archive in rawArchives)
            {
                if (archive == null) continue;

                var trimmed = new VolumeFactArchive
                {
                    VolumeNumber = archive.VolumeNumber,
                    LastChapterId = archive.LastChapterId ?? string.Empty,
                    ArchivedAt = archive.ArchivedAt,
                    CharacterStates = new List<CharacterStateSnapshot>(),
                    ConflictProgress = new List<ConflictProgressSnapshot>(),
                    ForeshadowingStatus = new List<ForeshadowingStatusSnapshot>(),
                    LocationStates = new List<LocationStateSnapshot>(),
                    FactionStates = new List<FactionStateSnapshot>(),
                    ItemStates = new List<ItemStateSnapshot>(),
                    Timeline = new List<TimelineSnapshot>(),
                    CharacterLocations = new List<CharacterLocationSnapshot>()
                };

                if (archive.CharacterStates != null && archive.CharacterStates.Count > 0)
                {
                    var selected = focusCharacterSet.Count > 0
                        ? archive.CharacterStates.Where(s => !string.IsNullOrWhiteSpace(s.Id) && focusCharacterSet.Contains(s.Id))
                        : archive.CharacterStates.Where(s => !string.IsNullOrWhiteSpace(s.Id));

                    foreach (var cs in selected)
                    {
                        if (trimmed.CharacterStates.Count >= maxCharacterStates) break;
                        trimmed.CharacterStates.Add(new CharacterStateSnapshot
                        {
                            Id = cs.Id ?? string.Empty,
                            Name = cs.Name ?? string.Empty,
                            Stage = TruncateString(cs.Stage, maxChars),
                            Abilities = TruncateString(cs.Abilities, maxChars),
                            Relationships = TruncateString(cs.Relationships, maxChars),
                            ChapterId = cs.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ConflictProgress != null && archive.ConflictProgress.Count > 0)
                {
                    var selected = focusConflictSet.Count > 0
                        ? archive.ConflictProgress.Where(c => !string.IsNullOrWhiteSpace(c.Id) && focusConflictSet.Contains(c.Id))
                        : archive.ConflictProgress.Where(c => !string.IsNullOrWhiteSpace(c.Status));

                    foreach (var cf in selected)
                    {
                        if (trimmed.ConflictProgress.Count >= maxConflicts) break;
                        var progress = cf.RecentProgress ?? new List<string>();
                        trimmed.ConflictProgress.Add(new ConflictProgressSnapshot
                        {
                            Id = cf.Id ?? string.Empty,
                            Name = cf.Name ?? string.Empty,
                            Status = TruncateString(cf.Status, maxChars),
                            RecentProgress = progress
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .Take(3)
                                .Select(p => TruncateString(p, maxChars))
                                .ToList()
                        });
                    }
                }

                if (archive.Timeline != null && archive.Timeline.Count > 0 && maxTimeline > 0)
                {
                    var take = Math.Min(maxTimeline, archive.Timeline.Count);
                    var skip = Math.Max(0, archive.Timeline.Count - take);
                    foreach (var t in archive.Timeline.Skip(skip))
                    {
                        trimmed.Timeline.Add(new TimelineSnapshot
                        {
                            ChapterId = t.ChapterId ?? string.Empty,
                            TimePeriod = TruncateString(t.TimePeriod, maxChars),
                            ElapsedTime = TruncateString(t.ElapsedTime, maxChars),
                            KeyTimeEvent = TruncateString(t.KeyTimeEvent, maxChars)
                        });
                    }
                }

                if (archive.CharacterLocations != null && archive.CharacterLocations.Count > 0 && maxLocations > 0)
                {
                    var selected = focusCharacterSet.Count > 0
                        ? archive.CharacterLocations.Where(l => !string.IsNullOrWhiteSpace(l.CharacterId) && focusCharacterSet.Contains(l.CharacterId))
                        : archive.CharacterLocations.Where(l => !string.IsNullOrWhiteSpace(l.CharacterId));

                    foreach (var loc in selected)
                    {
                        if (trimmed.CharacterLocations.Count >= maxLocations) break;
                        trimmed.CharacterLocations.Add(new CharacterLocationSnapshot
                        {
                            CharacterId = loc.CharacterId ?? string.Empty,
                            CharacterName = loc.CharacterName ?? string.Empty,
                            CurrentLocation = TruncateString(loc.CurrentLocation, maxChars),
                            ChapterId = loc.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.FactionStates != null && archive.FactionStates.Count > 0 && maxFactionStates > 0)
                {
                    var selected = focusFactionSet.Count > 0
                        ? archive.FactionStates.Where(f => !string.IsNullOrWhiteSpace(f.Id) && focusFactionSet.Contains(f.Id))
                        : archive.FactionStates.Where(f => !string.IsNullOrWhiteSpace(f.Status));

                    foreach (var fac in selected)
                    {
                        if (trimmed.FactionStates.Count >= maxFactionStates) break;
                        trimmed.FactionStates.Add(new FactionStateSnapshot
                        {
                            Id = fac.Id ?? string.Empty,
                            Name = fac.Name ?? string.Empty,
                            Status = TruncateString(fac.Status, maxChars),
                            ChapterId = fac.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.LocationStates != null && archive.LocationStates.Count > 0 && maxLocationStates > 0)
                {
                    var selected = focusLocationSet.Count > 0
                        ? archive.LocationStates.Where(l => !string.IsNullOrWhiteSpace(l.Id) && focusLocationSet.Contains(l.Id))
                        : archive.LocationStates.Where(l => !string.IsNullOrWhiteSpace(l.Status));

                    foreach (var locState in selected)
                    {
                        if (trimmed.LocationStates.Count >= maxLocationStates) break;
                        trimmed.LocationStates.Add(new LocationStateSnapshot
                        {
                            Id = locState.Id ?? string.Empty,
                            Name = locState.Name ?? string.Empty,
                            Status = TruncateString(locState.Status, maxChars),
                            ChapterId = locState.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ItemStates != null && archive.ItemStates.Count > 0)
                {
                    var selected = focusCharacterSet.Count > 0
                        ? archive.ItemStates.Where(i => !string.IsNullOrWhiteSpace(i.CurrentHolder) && focusCharacterSet.Contains(i.CurrentHolder))
                        : archive.ItemStates.Where(i => !string.IsNullOrWhiteSpace(i.Id));
                    var maxItems = LayeredContextConfig.ArchiveInjectMaxItemStates;
                    foreach (var item in selected)
                    {
                        if (trimmed.ItemStates.Count >= maxItems) break;
                        trimmed.ItemStates.Add(new ItemStateSnapshot
                        {
                            Id = item.Id ?? string.Empty,
                            Name = item.Name ?? string.Empty,
                            CurrentHolder = item.CurrentHolder ?? string.Empty,
                            Status = TruncateString(item.Status, maxChars),
                            ChapterId = item.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ForeshadowingStatus != null && archive.ForeshadowingStatus.Count > 0)
                {
                    var maxForeshadowing = LayeredContextConfig.ArchiveInjectMaxForeshadowingStatus;
                    var foreshadowingSelected = archive.ForeshadowingStatus
                        .Where(f => !f.IsResolved)
                        .OrderByDescending(f => f.IsOverdue)
                        .ThenByDescending(f => f.IsSetup);
                    foreach (var fs in foreshadowingSelected)
                    {
                        if (trimmed.ForeshadowingStatus.Count >= maxForeshadowing) break;
                        trimmed.ForeshadowingStatus.Add(new ForeshadowingStatusSnapshot
                        {
                            Id = fs.Id ?? string.Empty,
                            Name = fs.Name ?? string.Empty,
                            IsSetup = fs.IsSetup,
                            IsResolved = fs.IsResolved,
                            IsOverdue = fs.IsOverdue,
                            SetupChapterId = fs.SetupChapterId ?? string.Empty,
                            PayoffChapterId = fs.PayoffChapterId ?? string.Empty
                        });
                    }
                }

                result.Add(trimmed);
            }

            return result;
        }

        private readonly ConcurrentDictionary<string, Models.Design.Characters.CharacterRulesData> _characterCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Worldview.WorldRulesData> _worldRulesCache = new();
        private readonly ConcurrentDictionary<string, CreativeMaterialData> _templateCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Factions.FactionRulesData> _factionCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Location.LocationRulesData> _locationCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Plot.PlotRulesData> _plotRulesCache = new();
        private readonly ConcurrentDictionary<string, Models.Generate.StrategicOutline.OutlineData> _volumeCache = new();
        private readonly ConcurrentDictionary<string, ChapterData> _chapterPlanCache = new();
        private readonly ConcurrentDictionary<string, BlueprintData> _blueprintCache = new();
        private readonly ConcurrentDictionary<string, VolumeDesignData> _volumeDesignCache = new();

        private ContentGuide? _contentGuideCache;
        private readonly object _contentGuideCacheLock = new();
        private readonly SemaphoreSlim _contentGuideLoadLock = new(1, 1);

        private volatile ExpansionConfig? _expansionConfig;

        private volatile bool _cacheInitialized = false;
        private readonly SemaphoreSlim _cacheInitLock = new(1, 1);

        #region Cache

        public async Task InitializeCacheAsync()
        {
            if (_cacheInitialized) return;

            await _cacheInitLock.WaitAsync();
            try
            {
            if (_cacheInitialized) return;

            var worldRulesTask = LoadPackagedAsync<Models.Design.Worldview.WorldRulesData>("Design/globalsettings.json", "worldrules");
            var charactersTask = LoadPackagedAsync<Models.Design.Characters.CharacterRulesData>("Design/elements.json", "characterrules");
            var factionsTask = LoadPackagedAsync<Models.Design.Factions.FactionRulesData>("Design/elements.json", "factionrules");
            var locationsTask = LoadPackagedAsync<Models.Design.Location.LocationRulesData>("Design/elements.json", "locationrules");
            var plotRulesTask = LoadPackagedAsync<Models.Design.Plot.PlotRulesData>("Design/elements.json", "plotrules");
            var volumesTask = LoadPackagedAsync<Models.Generate.StrategicOutline.OutlineData>("Generate/globalsettings.json", "outline");
            var chapterPlansTask = LoadPackagedAsync<ChapterData>("Generate/elements.json", "chapter");
            var blueprintsTask = LoadPackagedAsync<BlueprintData>("Generate/elements.json", "blueprint");
            var volumeDesignsTask = LoadPackagedAsync<VolumeDesignData>("Generate/elements.json", "volumedesign");
            var templatesTask = LoadTemplatesAsync();

            await Task.WhenAll(
                worldRulesTask, charactersTask, factionsTask, locationsTask, plotRulesTask,
                volumesTask, chapterPlansTask, blueprintsTask, volumeDesignsTask, templatesTask);

            foreach (var w in await worldRulesTask) _worldRulesCache[w.Id] = w;
            foreach (var c in await charactersTask) _characterCache[c.Id] = c;
            foreach (var f in await factionsTask) _factionCache[f.Id] = f;
            foreach (var l in await locationsTask) _locationCache[l.Id] = l;
            foreach (var p in await plotRulesTask) _plotRulesCache[p.Id] = p;
            foreach (var v in await volumesTask) _volumeCache[v.Id] = v;

            foreach (var plan in await chapterPlansTask)
            {
                if (!string.IsNullOrWhiteSpace(plan.Id))
                    _chapterPlanCache[plan.Id] = plan;
            }

            foreach (var blueprint in await blueprintsTask)
            {
                if (!string.IsNullOrWhiteSpace(blueprint.Id))
                    _blueprintCache[blueprint.Id] = blueprint;
            }

            foreach (var volumeDesign in await volumeDesignsTask)
            {
                if (!string.IsNullOrWhiteSpace(volumeDesign.Id))
                    _volumeDesignCache[volumeDesign.Id] = volumeDesign;
            }

            _cacheInitialized = true;
            TM.App.Log("[GuideContextService] 缓存初始化完成（并行加载）");
            }
            finally
            {
                _cacheInitLock.Release();
            }
        }

        private async Task LoadTemplatesAsync()
        {
            try
            {
                var templatePath = StoragePathHelper.GetFilePath(
                    "Modules",
                    "Design/Templates/CreativeMaterials",
                    "creative_materials.json");
                if (File.Exists(templatePath))
                {
                    var json = await File.ReadAllTextAsync(templatePath);
                    var templates = JsonSerializer.Deserialize<List<CreativeMaterialData>>(json, JsonOptions) ?? new List<CreativeMaterialData>();
                    var currentSourceBookId = _workScopeService.CurrentSourceBookId;
                    foreach (var template in templates)
                    {
                        if (template == null || !template.IsEnabled)
                            continue;
                        if (!string.IsNullOrEmpty(currentSourceBookId) &&
                            !string.Equals(template.SourceBookId, currentSourceBookId, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(template.Id))
                        {
                            _templateCache[template.Id] = template;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载创作模板失败: {ex.Message}");
            }
        }

        private async Task<Models.Index.IndexItem?> BuildRelatedIndexItemAsync(
            string entityId, Models.Context.RelationStrength strength, string sourceBookId)
        {
            try
            {
                await InitializeCacheAsync();

                if (!_characterCache.TryGetValue(entityId, out var profile) || profile == null)
                    return null;

                if (!string.Equals(profile.SourceBookId ?? string.Empty, sourceBookId, StringComparison.Ordinal))
                    return null;

                var briefParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(profile.Identity)) briefParts.Add(profile.Identity);
                if (!string.IsNullOrWhiteSpace(profile.Want)) briefParts.Add($"目标:{profile.Want}");
                if (!string.IsNullOrWhiteSpace(profile.Need)) briefParts.Add($"需求:{profile.Need}");

                var deepParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(profile.FlawBelief)) deepParts.Add(profile.FlawBelief);
                if (!string.IsNullOrWhiteSpace(profile.GrowthPath)) deepParts.Add(profile.GrowthPath);
                if (!string.IsNullOrWhiteSpace(profile.SpecialAbilities)) deepParts.Add(profile.SpecialAbilities);

                return new Models.Index.IndexItem
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.CharacterType,
                    BriefSummary = TruncateString(string.Join("；", briefParts), 30),
                    DeepSummary = TruncateString(string.Join("。", deepParts), 80),
                    RelationStrength = strength.ToString()
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 构建关联实体索引失败（Scope={sourceBookId}）: {ex.Message}");
                return null;
            }
        }

        public void ClearCache()
        {
            _characterCache.Clear();
            _worldRulesCache.Clear();
            _templateCache.Clear();
            _factionCache.Clear();
            _locationCache.Clear();
            _plotRulesCache.Clear();
            _volumeCache.Clear();
            _chapterPlanCache.Clear();
            _blueprintCache.Clear();
            _volumeDesignCache.Clear();
            lock (_contentGuideCacheLock)
            {
                _contentGuideCache = null;
            }
            _expansionConfig = null;
            _summaryStore.InvalidateCache();
            _milestoneStore.InvalidateCache();
            ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache();
            ServiceLocator.Get<KeywordChapterIndexService>().InvalidateCache();
            ServiceLocator.Get<PlotPointsIndexService>().InvalidateCache();
            _cacheInitialized = false;
            lock (_chapterIdsCacheLock) { _chapterIdsCachedForPath = string.Empty; }
            TM.App.Log("[GuideContextService] 缓存已清除");
        }

        private static string[] GetCachedChapterIds(string chaptersPath)
        {
            lock (_chapterIdsCacheLock)
            {
                var now = DateTime.UtcNow;
                if (_chapterIdsCachedForPath == chaptersPath
                    && (now - _chapterIdsCachedAt).TotalSeconds < 30
                    && _cachedChapterIds.Length > 0)
                    return _cachedChapterIds;

                var ids = System.IO.Directory.Exists(chaptersPath)
                    ? System.IO.Directory.GetFiles(chaptersPath, "*.md", System.IO.SearchOption.TopDirectoryOnly)
                          .Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToArray()
                    : Array.Empty<string>();
                _cachedChapterIds = ids;
                _chapterIdsCachedForPath = chaptersPath;
                _chapterIdsCachedAt = now;
                return ids;
            }
        }

        #endregion

        #region ExpansionConfig

        private async Task<ExpansionConfig> GetExpansionConfigAsync()
        {
            if (_expansionConfig == null)
            {
                var path = Path.Combine(
                    StoragePathHelper.GetServicesStoragePath("Settings"),
                    "context_expansion_config.json");
                if (File.Exists(path))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(path);
                        _expansionConfig = JsonSerializer.Deserialize<ExpansionConfig>(json, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[GuideContextService] 加载扩展配置失败: {ex.Message}");
                    }
                }
                _expansionConfig ??= new ExpansionConfig { Enabled = false };
            }
            return _expansionConfig;
        }

        private async Task<bool> IsKeySceneAsync(ContentGuideEntry? chapterGuide)
        {
            var config = await GetExpansionConfigAsync();
            if (!config.Enabled || chapterGuide?.Scenes == null || chapterGuide.Scenes.Count == 0)
                return false;

            var maxCharacters = chapterGuide.Scenes.Max(s => s.CharacterIds?.Count ?? 0);
            if (maxCharacters > config.Rules.SceneCharactersThreshold)
                return true;

            foreach (var scene in chapterGuide.Scenes)
            {
                if (config.Rules.TriggerKeywords.Any(k => scene.Purpose?.Contains(k) == true))
                    return true;
            }

            return false;
        }

        private async Task TryExpandForKeySceneAsync(ContentTaskContext context, ContentGuideEntry? chapterGuide)
        {
            if (!await IsKeySceneAsync(chapterGuide) || chapterGuide == null)
                return;

            var config = await GetExpansionConfigAsync();

            var loadedCharIds = context.Characters.Select(c => c.Id).ToHashSet();
            var additionalCharIds = chapterGuide.Scenes
                .SelectMany(s => s.CharacterIds ?? new())
                .Distinct()
                .Where(id => !loadedCharIds.Contains(id))
                .Take(config.Limits.MaxAdditionalCharacters)
                .ToList();

            if (additionalCharIds.Count > 0)
            {
                var additionalChars = await ExtractCharactersAsync(additionalCharIds);
                context.ExpandedCharacters.AddRange(additionalChars);
            }

            if (context.ExpandedCharacters.Count > 0)
            {
                context.IsKeySceneExpanded = true;
                TM.App.Log($"[GuideContextService] 关键场景扩展: +{context.ExpandedCharacters.Count}角色");
            }
        }

        #endregion

        #region Extractors

        public async Task<List<Models.Design.Characters.CharacterRulesData>> ExtractCharactersAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Characters.CharacterRulesData>();
            var missing = ids.Where(id => !_characterCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 角色ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _characterCache.ContainsKey(id))
                .Select(id => _characterCache[id])
                .ToList();
        }

        public async Task<List<Models.Design.Characters.CharacterRulesData>> GetAllCharactersAsync()
        {
            await InitializeCacheAsync();
            return _characterCache.Values.ToList();
        }

        public async Task<List<Models.Design.Location.LocationRulesData>> ExtractLocationsAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Location.LocationRulesData>();
            var missing = ids.Where(id => !_locationCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 地点ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _locationCache.ContainsKey(id))
                .Select(id => _locationCache[id])
                .ToList();
        }

        public async Task<List<Models.Design.Location.LocationRulesData>> GetAllLocationsAsync()
        {
            await InitializeCacheAsync();
            return _locationCache.Values.ToList();
        }

        public async Task<List<Models.Design.Plot.PlotRulesData>> ExtractPlotRulesAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Plot.PlotRulesData>();
            var missing = ids.Where(id => !_plotRulesCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 剧情规则ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _plotRulesCache.ContainsKey(id))
                .Select(id => _plotRulesCache[id])
                .ToList();
        }

        public async Task<List<Models.Design.Plot.PlotRulesData>> GetAllPlotRulesAsync()
        {
            await InitializeCacheAsync();
            return _plotRulesCache.Values.ToList();
        }

        public async Task<List<Models.Design.Factions.FactionRulesData>> ExtractFactionsAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Factions.FactionRulesData>();
            var missing = ids.Where(id => !_factionCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 势力ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _factionCache.ContainsKey(id))
                .Select(id => _factionCache[id])
                .ToList();
        }

        public async Task<List<Models.Design.Factions.FactionRulesData>> GetAllFactionsAsync()
        {
            await InitializeCacheAsync();
            return _factionCache.Values.ToList();
        }

        public async Task<List<CreativeMaterialData>> ExtractTemplatesAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<CreativeMaterialData>();
            var missing = ids.Where(id => !_templateCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 创作模板ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _templateCache.ContainsKey(id))
                .Select(id => _templateCache[id])
                .ToList();
        }

        public async Task<List<CreativeMaterialData>> GetAllTemplatesAsync()
        {
            await InitializeCacheAsync();
            return _templateCache.Values.ToList();
        }

        public async Task<List<Models.Design.Worldview.WorldRulesData>> ExtractWorldRulesAsync(List<string>? ids)
        {
            await InitializeCacheAsync();
            if (ids == null || ids.Count == 0)
                return new List<Models.Design.Worldview.WorldRulesData>();
            var missing = ids.Where(id => !_worldRulesCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 世界观规则ID未找到: {string.Join(", ", missing)}");
            return ids
                .Where(id => _worldRulesCache.ContainsKey(id))
                .Select(id => _worldRulesCache[id])
                .ToList();
        }

        public async Task<List<Models.Design.Worldview.WorldRulesData>> GetAllWorldRulesAsync()
        {
            await InitializeCacheAsync();
            return _worldRulesCache.Values.ToList();
        }

        public async Task<Models.Generate.StrategicOutline.OutlineData> ExtractVolumeAsync(string volumeId)
        {
            await InitializeCacheAsync();

            if (_volumeCache.TryGetValue(volumeId, out var volume) && volume != null)
            {
                return volume;
            }

            return new Models.Generate.StrategicOutline.OutlineData();
        }

        public async Task<ChapterData?> ExtractChapterPlanAsync(string chapterPlanId)
        {
            await InitializeCacheAsync();
            if (string.IsNullOrWhiteSpace(chapterPlanId))
                return null;
            if (_chapterPlanCache.TryGetValue(chapterPlanId, out var plan))
                return plan;
            TM.App.Log($"[GuideContextService] 章节规划ID未找到: {chapterPlanId}");
            return null;
        }

        public async Task<List<BlueprintData>> ExtractBlueprintsAsync(List<string>? blueprintIds)
        {
            await InitializeCacheAsync();
            if (blueprintIds == null || blueprintIds.Count == 0)
                return new List<BlueprintData>();
            var missing = blueprintIds.Where(id => !_blueprintCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 章节蓝图ID未找到: {string.Join(", ", missing)}");
            return blueprintIds
                .Where(id => _blueprintCache.ContainsKey(id))
                .Select(id => _blueprintCache[id])
                .ToList();
        }

        public async Task<VolumeDesignData?> ExtractVolumeDesignAsync(string volumeDesignId)
        {
            await InitializeCacheAsync();
            if (string.IsNullOrWhiteSpace(volumeDesignId))
                return null;
            if (_volumeDesignCache.TryGetValue(volumeDesignId, out var volumeDesign))
                return volumeDesign;
            TM.App.Log($"[GuideContextService] 分卷设计ID未找到: {volumeDesignId}");
            return null;
        }

        public async Task<List<Models.Generate.StrategicOutline.OutlineData>> ExtractPreviousOutlinesAsync(List<string> outlineIds)
        {
            await InitializeCacheAsync();
            var missing = outlineIds.Where(id => !_volumeCache.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                TM.App.Log($"[GuideContextService] 大纲ID未找到: {string.Join(", ", missing)}");
            return outlineIds
                .Where(id => _volumeCache.ContainsKey(id))
                .Select(id => _volumeCache[id])
                .ToList();
        }

        public async Task<ContextIdValidationResult> ValidateContextIdsAsync(ContextIdCollection? contextIds)
        {
            if (contextIds == null)
                return ContextIdValidationResult.Success();

            await InitializeCacheAsync();
            var missingIds = new Dictionary<string, List<string>>();

            if (contextIds.Characters?.Count > 0)
            {
                var missing = contextIds.Characters.Where(id => !_characterCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Characters"] = missing;
            }

            if (contextIds.Locations?.Count > 0)
            {
                var missing = contextIds.Locations.Where(id => !_locationCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Locations"] = missing;
            }

            if (contextIds.Factions?.Count > 0)
            {
                var missing = contextIds.Factions.Where(id => !_factionCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Factions"] = missing;
            }

            if (contextIds.Conflicts?.Count > 0)
            {
                var missing = contextIds.Conflicts.Where(id => !_plotRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["Conflicts"] = missing;
            }

            if (contextIds.PlotRules?.Count > 0)
            {
                var missing = contextIds.PlotRules.Where(id => !_plotRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PlotRules"] = missing;
            }

            if (contextIds.TemplateIds == null || contextIds.TemplateIds.Count == 0)
            {
                TM.App.Log("[GuideContextService] Preflight提示：TemplateIds为空，将跳过创作模板注入（不阻断生成）");
            }
            else
            {
                var missing = contextIds.TemplateIds.Where(id => !_templateCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["TemplateIds"] = missing;
            }

            if (contextIds.WorldRuleIds == null || contextIds.WorldRuleIds.Count == 0)
            {
                TM.App.Log("[GuideContextService] Preflight提示：WorldRuleIds为空，将跳过世界观规则注入（不阻断生成）");
            }
            else
            {
                var missing = contextIds.WorldRuleIds.Where(id => !_worldRulesCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["WorldRuleIds"] = missing;
            }

            if (string.IsNullOrWhiteSpace(contextIds.ChapterPlanId))
            {
                TM.App.Log("[GuideContextService] Preflight提示：ChapterPlanId为空，将跳过章节规划注入（不阻断生成）");
            }
            else if (!_chapterPlanCache.ContainsKey(contextIds.ChapterPlanId))
            {
                missingIds["ChapterPlanId"] = new List<string> { contextIds.ChapterPlanId };
            }

            if (contextIds.BlueprintIds == null || contextIds.BlueprintIds.Count == 0)
            {
                missingIds["BlueprintIds"] = new List<string> { "空列表" };
            }
            else
            {
                var missing = contextIds.BlueprintIds.Where(id => !_blueprintCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["BlueprintIds"] = missing;
            }

            if (string.IsNullOrWhiteSpace(contextIds.VolumeDesignId))
            {
                var inferred = TryInferVolumeDesignIdFromContext(contextIds);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    contextIds.VolumeDesignId = inferred;
                    TM.App.Log($"[GuideContextService] Preflight已自动补全VolumeDesignId: {inferred}（ChapterBlueprint={contextIds.ChapterBlueprint}）");
                }
                else
                {
                    missingIds["VolumeDesignId"] = new List<string> { "空值" };
                }
            }
            else if (!_volumeDesignCache.ContainsKey(contextIds.VolumeDesignId))
            {
                missingIds["VolumeDesignId"] = new List<string> { contextIds.VolumeDesignId };
            }

            if (contextIds.ForeshadowingSetups?.Count > 0 || contextIds.ForeshadowingPayoffs?.Count > 0)
            {
                try
                {
                    var fowGuide = await ServiceLocator.Get<GuideManager>().GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                    if (contextIds.ForeshadowingSetups?.Count > 0)
                    {
                        var missing = contextIds.ForeshadowingSetups.Where(id => !fowGuide.Foreshadowings.ContainsKey(id)).ToList();
                        if (missing.Count > 0)
                            missingIds["ForeshadowingSetups"] = missing;
                    }
                    if (contextIds.ForeshadowingPayoffs?.Count > 0)
                    {
                        var missing = contextIds.ForeshadowingPayoffs.Where(id => !fowGuide.Foreshadowings.ContainsKey(id)).ToList();
                        if (missing.Count > 0)
                            missingIds["ForeshadowingPayoffs"] = missing;
                    }
                }
                catch (Exception ex) { TM.App.Log($"[GuideContextService] 伏笔ID校验失败: {ex.Message}"); }
            }

            if (!string.IsNullOrEmpty(contextIds.VolumeOutline))
            {
                if (!_volumeCache.ContainsKey(contextIds.VolumeOutline))
                    missingIds["VolumeOutline"] = new List<string> { contextIds.VolumeOutline };
            }

            if (!string.IsNullOrEmpty(contextIds.ChapterBlueprint))
            {
                var blueprintGuide = await LoadGuideAsync<Models.Guides.BlueprintGuide>("blueprint_guide.json");
                var found = blueprintGuide?.Chapters?.ContainsKey(contextIds.ChapterBlueprint) == true;
                if (!found)
                    missingIds["ChapterBlueprint"] = new List<string> { contextIds.ChapterBlueprint };
            }

            if (!string.IsNullOrEmpty(contextIds.PreviousChapter))
            {
                var contentGuide = await GetContentGuideAsync();
                var found = contentGuide?.Chapters?.ContainsKey(contextIds.PreviousChapter) == true;
                if (!found)
                    missingIds["PreviousChapter"] = new List<string> { contextIds.PreviousChapter };
            }

            if (contextIds.PreviousVolumes?.Count > 0)
            {
                var missing = contextIds.PreviousVolumes.Where(id => !_volumeCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PreviousVolumes"] = missing;
            }

            if (contextIds.PreviousOutlines?.Count > 0)
            {
                var missing = contextIds.PreviousOutlines.Where(id => !_volumeCache.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    missingIds["PreviousOutlines"] = missing;
            }

            if (missingIds.Count > 0)
            {
                var errors = missingIds.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}").ToList();
                TM.App.Log($"[GuideContextService] Preflight ContextIds 验证失败: {string.Join("; ", errors)}");
                return ContextIdValidationResult.Failed(missingIds);
            }

            return ContextIdValidationResult.Success();
        }

        private string? TryInferVolumeDesignIdFromContext(ContextIdCollection contextIds)
        {
            try
            {
                if (contextIds == null)
                {
                    return null;
                }

                var chapterId = contextIds.ChapterBlueprint;
                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    chapterId = contextIds.PreviousChapter;
                }

                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    return null;
                }

                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue || parsed.Value.volumeNumber <= 0)
                {
                    return null;
                }

                var volumeNumber = parsed.Value.volumeNumber;
                var scopeId = _workScopeService.CurrentSourceBookId;

                var candidates = _volumeDesignCache.Values
                    .Where(v => v != null
                                && !string.IsNullOrWhiteSpace(v.Id)
                                && v.VolumeNumber == volumeNumber
                                && (string.IsNullOrEmpty(scopeId) || string.Equals(v.SourceBookId, scopeId, StringComparison.Ordinal)))
                    .OrderByDescending(v => v.UpdatedAt)
                    .ToList();

                if (candidates.Count == 0)
                {
                    return null;
                }

                if (candidates.Count > 1)
                {
                    TM.App.Log($"[GuideContextService] Preflight检测到多个VolumeDesign候选，已取最新: vol={volumeNumber}, count={candidates.Count}");
                }

                return candidates[0].Id;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] Preflight推断VolumeDesignId失败: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region ModuleExtractors

        public async Task<OutlineTaskContext?> BuildOutlineContextAsync(string volumeId)
        {
            var guide = await LoadGuideAsync<OutlineGuide>("outline_guide.json");
            var volumeGuide = guide.Volumes.GetValueOrDefault(volumeId);

            if (volumeGuide == null) return null;

            var characterItems = await ExtractCharactersAsync(volumeGuide.ContextIds.Characters);
            var locationItems = await ExtractLocationsAsync(volumeGuide.ContextIds.Locations);
            var factionItems = await ExtractFactionsAsync(volumeGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleItems = await ExtractPlotRulesAsync(volumeGuide.ContextIds.PlotRules ?? new List<string>());
            var previousOutlineItems = await ExtractPreviousOutlinesAsync(volumeGuide.ContextIds.PreviousOutlines ?? new List<string>());

            var context = new OutlineTaskContext
            {
                VolumeId = volumeId,
                VolumeNumber = volumeGuide.VolumeNumber,
                Theme = volumeGuide.Theme
            };

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var l in locationItems)
            {
                context.Locations.Add(l);
            }

            foreach (var f in factionItems)
            {
                context.Factions.Add(f);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            foreach (var o in previousOutlineItems)
            {
                context.PreviousOutlines.Add(o);
            }

            return context;
        }

        public async Task<string?> GetChapterTitleAsync(string chapterId)
        {
            try
            {
                var guide = await GetContentGuideAsync();
                if (guide?.Chapters != null && guide.Chapters.TryGetValue(chapterId, out var entry))
                {
                    return ResolveChapterDisplayTitle(entry, chapterId);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] GetChapterTitleAsync失败: {ex.Message}");
            }
            return null;
        }

        private static string ResolveChapterDisplayTitle(ContentGuideEntry chapterGuide, string chapterId)
        {
            var title = chapterGuide?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = chapterGuide?.Scenes?.FirstOrDefault()?.Title;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                title = chapterId;
            }
            return title;
        }

        public async Task<PlanningTaskContext?> BuildPlanningContextAsync(string volumeId)
        {
            var guide = await LoadGuideAsync<PlanningGuide>("planning_guide.json");
            var volumeGuide = guide.Volumes.GetValueOrDefault(volumeId);

            if (volumeGuide == null) return null;

            var volumeItem = await ExtractVolumeAsync(volumeGuide.ContextIds.VolumeOutline);
            var characterItems = await ExtractCharactersAsync(volumeGuide.ContextIds.Characters);
            var plotRuleItems = await ExtractPlotRulesAsync(volumeGuide.ContextIds.PlotRules ?? new List<string>());

            var context = new PlanningTaskContext
            {
                VolumeId = volumeId,
                ChapterPlans = volumeGuide.Chapters
            };

            var volumeData = volumeItem;

            context.VolumeOutline = volumeData;

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            return context;
        }

        public async Task<BlueprintTaskContext?> BuildBlueprintContextAsync(string chapterId)
        {
            var guide = await LoadGuideAsync<BlueprintGuide>("blueprint_guide.json");
            var chapterGuide = guide.Chapters.GetValueOrDefault(chapterId);

            if (chapterGuide == null) return null;

            var volumeItem = await ExtractVolumeAsync(chapterGuide.ContextIds.VolumeOutline);
            var characterItems = await ExtractCharactersAsync(chapterGuide.ContextIds.Characters);
            var locationItems = await ExtractLocationsAsync(chapterGuide.ContextIds.Locations);
            var factionItems = await ExtractFactionsAsync(chapterGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleItems = await ExtractPlotRulesAsync(chapterGuide.ContextIds.PlotRules ?? new List<string>());

            var context = new BlueprintTaskContext
            {
                ChapterId = chapterId,
                Title = chapterGuide.Title,
                ChapterGoal = chapterGuide.ChapterGoal,
                PreviousChapterSummary = await GetChapterSummaryAsync(chapterGuide.ContextIds.PreviousChapter),
                Rhythm = chapterGuide.Rhythm
            };

            var volumeData = volumeItem;

            context.VolumeOutline = volumeData;

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var l in locationItems)
            {
                context.Locations.Add(l);
            }

            foreach (var f in factionItems)
            {
                context.Factions.Add(f);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            return context;
        }

        public async Task<ContentTaskContext?> BuildContentContextAsync(string chapterId)
        {
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}");
            var guide = await GetContentGuideAsync();

            ContentGuideEntry? chapterGuide = null;
            bool hasPackage = guide?.Chapters?.TryGetValue(chapterId, out chapterGuide) == true && chapterGuide != null;
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, hasPackage={hasPackage}");

            if (!hasPackage)
            {
                TM.App.Log($"[GuideContextService] 章节 {chapterId} 缺少打包数据");
                return null;
            }

            var prevChapterId = chapterGuide!.ContextIds.PreviousChapter;
            bool hasPrevMd = !string.IsNullOrEmpty(prevChapterId) && HasChapterMd(prevChapterId);

            if (hasPrevMd)
            {
                TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, mode=M1, prev={prevChapterId}");
                return await BuildFullContextAsync(chapterId, chapterGuide!, guide!, prevChapterId);
            }

            TM.App.Log($"[GuideContextService] 章节 {chapterId} 缺少MD上下文，使用备用模式");
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, mode=M2, prev={prevChapterId}");
            return await BuildPackageOnlyContextAsync(chapterId, chapterGuide!, guide!);
        }

        private async Task<ContentTaskContext> BuildFullContextAsync(
            string chapterId, 
            ContentGuideEntry chapterGuide, 
            ContentGuide guide,
            string prevChapterId)
        {
            var context = await LoadTaskLayerAsync(chapterId, chapterGuide, guide);
            context.ContextMode = ContentContextMode.Full;
            context.ContextIds = chapterGuide.ContextIds;

            var currentVol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

            var storeSummariesTask = _summaryStore.GetPreviousSummariesAsync(chapterId, LayeredContextConfig.PreviousSummaryCount);
            var mdSummariesTask = ExtractSummariesFromMdAsync(chapterId, LayeredContextConfig.PreviousSummaryCount);
            var tailTask = LoadChapterTailAsync(prevChapterId, LayeredContextConfig.PreviousChapterTailLength);
            var factSnapshotTask = ExtractFactSnapshotAsync(chapterId, chapterGuide.ContextIds);
            var milestonesTask = _milestoneStore.GetPreviousMilestonesAsync(currentVol);
            var archivesTask = currentVol > 1
                ? ServiceLocator.Get<VolumeFactArchiveStore>().GetPreviousArchivesAsync(currentVol)
                : Task.FromResult(new List<VolumeFactArchive>());

            await Task.WhenAll(storeSummariesTask, mdSummariesTask, tailTask, factSnapshotTask, milestonesTask, archivesTask);

            context.PreviousChapterSummaries = LoadPreviousChapterSummaries(
                chapterId, 
                await storeSummariesTask,
                LayeredContextConfig.PreviousSummaryCount);

            context.MdPreviousChapterSummaries = await mdSummariesTask;

            context.PreviousChapterId = prevChapterId;
            context.PreviousChapterTail = await tailTask;

            context.FactSnapshot = await factSnapshotTask;

            context.HistoricalMilestones = await milestonesTask;
            if (context.HistoricalMilestones.Count > 0)
                TM.App.Log($"[GuideContextService] 注入里程碑: {context.HistoricalMilestones.Count}卷");

            if (currentVol > 1)
            {
                context.PreviousVolumeArchives = BuildInjectableArchives(await archivesTask, chapterGuide.ContextIds);
                if (context.PreviousVolumeArchives.Count > 0)
                    TM.App.Log($"[GuideContextService] 注入前卷事实存档: {context.PreviousVolumeArchives.Count}卷");
            }

            await DetectStateDivergenceAsync(context);

            await DetectTrackingGapsAsync(context, chapterId);

            await ValidateVolumeEndChapterAsync(context, chapterId);

            context.VectorRecallDegraded = IsVectorRecallDegraded();
            if (context.VectorRecallDegraded)
                TM.App.Log($"[GuideContextService] {chapterId} 向量召回处于降级状态，远距离一致性精度下降");

            await TryExpandForKeySceneAsync(context, chapterGuide);

            return context;
        }

        private async Task<ContentTaskContext> BuildPackageOnlyContextAsync(
            string chapterId, 
            ContentGuideEntry chapterGuide, 
            ContentGuide guide)
        {
            var context = await LoadTaskLayerAsync(chapterId, chapterGuide, guide);
            context.ContextMode = ContentContextMode.PackageOnly;
            context.ContextIds = chapterGuide.ContextIds;

            var currentVolPO = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

            var storeSummariesPOTask = _summaryStore.GetPreviousSummariesAsync(chapterId, LayeredContextConfig.PreviousSummaryCount);
            var factSnapshotPOTask = ExtractFactSnapshotAsync(chapterId, chapterGuide.ContextIds);
            var milestonesPOTask = _milestoneStore.GetPreviousMilestonesAsync(currentVolPO);
            var archivesPOTask = currentVolPO > 1
                ? ServiceLocator.Get<VolumeFactArchiveStore>().GetPreviousArchivesAsync(currentVolPO)
                : Task.FromResult(new List<VolumeFactArchive>());

            await Task.WhenAll(storeSummariesPOTask, factSnapshotPOTask, milestonesPOTask, archivesPOTask);

            context.PreviousChapterSummaries = LoadPreviousChapterSummaries(
                chapterId, 
                await storeSummariesPOTask,
                LayeredContextConfig.PreviousSummaryCount);

            context.FactSnapshot = await factSnapshotPOTask;

            context.HistoricalMilestones = await milestonesPOTask;
            if (context.HistoricalMilestones.Count > 0)
                TM.App.Log($"[GuideContextService] 注入里程碑(PO): {context.HistoricalMilestones.Count}卷");

            if (currentVolPO > 1)
            {
                context.PreviousVolumeArchives = BuildInjectableArchives(await archivesPOTask, chapterGuide.ContextIds);
                if (context.PreviousVolumeArchives.Count > 0)
                    TM.App.Log($"[GuideContextService] 注入前卷事实存档(PO): {context.PreviousVolumeArchives.Count}卷");
            }

            await DetectStateDivergenceAsync(context);

            await DetectTrackingGapsAsync(context, chapterId);

            await ValidateVolumeEndChapterAsync(context, chapterId);

            context.VectorRecallDegraded = IsVectorRecallDegraded();

            await TryExpandForKeySceneAsync(context, chapterGuide);

            return context;
        }

        private async Task<ContentTaskContext> LoadTaskLayerAsync(
            string chapterId, 
            ContentGuideEntry chapterGuide, 
            ContentGuide guide)
        {
            var characterTask = ExtractCharactersAsync(chapterGuide.ContextIds.Characters);
            var locationTask = ExtractLocationsAsync(chapterGuide.ContextIds.Locations);
            var factionTask = ExtractFactionsAsync(chapterGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleTask = ExtractPlotRulesAsync(chapterGuide.ContextIds.PlotRules ?? new List<string>());
            var volumeTask = ExtractVolumeAsync(chapterGuide.ContextIds.VolumeOutline);
            var worldRuleTask = ExtractWorldRulesAsync(chapterGuide.ContextIds.WorldRuleIds ?? new List<string>());
            var templateTask = ExtractTemplatesAsync(chapterGuide.ContextIds.TemplateIds);
            var chapterPlanTask = ExtractChapterPlanAsync(chapterGuide.ContextIds.ChapterPlanId);
            var blueprintTask = ExtractBlueprintsAsync(chapterGuide.ContextIds.BlueprintIds ?? new List<string>());
            var volumeDesignTask = ExtractVolumeDesignAsync(chapterGuide.ContextIds.VolumeDesignId);

            await Task.WhenAll(
                characterTask, locationTask, factionTask, plotRuleTask,
                volumeTask, worldRuleTask, templateTask, chapterPlanTask, blueprintTask, volumeDesignTask);

            var characterItems = await characterTask;
            var locationItems = await locationTask;
            var factionItems = await factionTask;
            var plotRuleItems = await plotRuleTask;
            var volumeItem = await volumeTask;
            var worldRuleItems = await worldRuleTask;
            var templateItems = await templateTask;
            var chapterPlanItem = await chapterPlanTask;
            var blueprintItems = await blueprintTask;
            var volumeDesignItem = await volumeDesignTask;

            var context = new ContentTaskContext
            {
                ChapterId = chapterId,
                Title = ResolveChapterDisplayTitle(chapterGuide, chapterId),
                Summary = chapterGuide.Summary,
                VolumeOutline = volumeItem,
                ChapterPlan = chapterPlanItem,
                VolumeDesign = volumeDesignItem,
                Rhythm = chapterGuide.Rhythm,
                Scenes = chapterGuide.Scenes,
                PreviousChapterSummary = !string.IsNullOrEmpty(chapterGuide.ContextIds.PreviousChapter)
                    ? await _summaryStore.GetSummaryAsync(chapterGuide.ContextIds.PreviousChapter)
                    : string.Empty
            };

            foreach (var c in characterItems) context.Characters.Add(c);
            foreach (var l in locationItems) context.Locations.Add(l);
            foreach (var f in factionItems) context.Factions.Add(f);
            foreach (var p in plotRuleItems) context.PlotRules.Add(p);
            foreach (var w in worldRuleItems) context.WorldRules.Add(w);
            foreach (var t in templateItems) context.Templates.Add(t);
            foreach (var b in blueprintItems) context.Blueprints.Add(b);

            return context;
        }

        private List<ChapterSummaryEntry> LoadPreviousChapterSummaries(
            string currentChapterId,
            Dictionary<string, string> allSummaries,
            int count)
        {
            var result = new List<ChapterSummaryEntry>();

            if (allSummaries == null || allSummaries.Count == 0)
                return result;

            var currentParsed = ChapterParserHelper.ParseChapterId(currentChapterId);
            var currentVol = currentParsed?.volumeNumber ?? 1;

            var previousAll = allSummaries
                .Where(kv => ChapterParserHelper.CompareChapterId(kv.Key, currentChapterId) < 0)
                .ToList();

            var recentCount = LayeredContextConfig.SummaryRecentWindowCount;
            var recentKeys = previousAll
                .OrderByDescending(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .Take(recentCount)
                .Select(kv => kv.Key)
                .ToHashSet();

            var milestoneAnchorInterval = LayeredContextConfig.MilestoneAnchorInterval;
            var milestoneKeys  = new HashSet<string>();
            var volumeStartKeys = new HashSet<string>();
            var midpointKeys   = new HashSet<string>();
            foreach (var volGroup in previousAll
                .GroupBy(kv => ChapterParserHelper.ParseChapterId(kv.Key)?.volumeNumber ?? 0)
                .Where(g => g.Key > 0 && g.Key < currentVol))
            {
                var sortedChapters = volGroup
                    .OrderBy(kv => ChapterParserHelper.ParseChapterId(kv.Key)?.chapterNumber ?? 0)
                    .ToList();
                milestoneKeys.Add(sortedChapters.Last().Key);
                if (sortedChapters.Count <= 1) continue;
                volumeStartKeys.Add(sortedChapters[0].Key);
                for (int i = milestoneAnchorInterval; i < sortedChapters.Count - 1; i += milestoneAnchorInterval)
                    midpointKeys.Add(sortedChapters[i].Key);
            }

            var allAnchorKeys = new HashSet<string>(milestoneKeys.Concat(volumeStartKeys).Concat(midpointKeys));
            var selectedKeys = recentKeys.Union(allAnchorKeys).ToList();
            var selectedSummaries = previousAll
                .Where(kv => selectedKeys.Contains(kv.Key))
                .OrderBy(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .Take(Math.Max(count, recentCount + allAnchorKeys.Count))
                .ToList();

            foreach (var kv in selectedSummaries)
            {
                var parsed = ChapterParserHelper.ParseChapterId(kv.Key);
                var chapterNumber = parsed?.chapterNumber ?? 0;
                var isEndMilestone  = milestoneKeys.Contains(kv.Key)   && !recentKeys.Contains(kv.Key);
                var isVolumeStart   = volumeStartKeys.Contains(kv.Key) && !recentKeys.Contains(kv.Key);
                var isMidpoint      = midpointKeys.Contains(kv.Key)    && !recentKeys.Contains(kv.Key);
                var prefix = isEndMilestone ? $"[{parsed?.volumeNumber}卷收尾] "
                           : isVolumeStart  ? $"[{parsed?.volumeNumber}卷起始] "
                           : isMidpoint     ? $"[{parsed?.volumeNumber}卷中段] "
                           : string.Empty;
                result.Add(new ChapterSummaryEntry
                {
                    ChapterId = kv.Key,
                    ChapterNumber = chapterNumber,
                    Summary = prefix + kv.Value
                });
            }

            return result;
        }

        private async Task<List<ChapterSummaryEntry>> ExtractSummariesFromMdAsync(
            string currentChapterId, int count)
        {
            var result = new List<ChapterSummaryEntry>();

            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!Directory.Exists(chaptersPath))
                    return result;

                IEnumerable<string> allChapterIds;
                var guide = await GetContentGuideAsync();
                if (guide?.Chapters != null && guide.Chapters.Count > 0)
                {
                    allChapterIds = guide.Chapters.Keys;
                }
                else
                {
                    allChapterIds = Directory.GetFiles(chaptersPath, "vol*_ch*.md")
                        .Select(f => Path.GetFileNameWithoutExtension(f));
                }

                var previousChapterFiles = allChapterIds
                    .Where(id => ChapterParserHelper.CompareChapterId(id, currentChapterId) < 0)
                    .OrderByDescending(id => id, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                    .Take(count)
                    .Reverse()
                    .ToList();

                foreach (var chapId in previousChapterFiles)
                {
                    var mdPath = Path.Combine(chaptersPath, $"{chapId}.md");
                    if (!File.Exists(mdPath)) continue;

                    var fullContent = await ReadFileHeadAsync(mdPath, 8000);
                    var summary = ExtractSampledSummaryFromMd(fullContent, LayeredContextConfig.MdSummaryExtractLength);
                    var parsed = ChapterParserHelper.ParseChapterId(chapId);
                    result.Add(new ChapterSummaryEntry
                    {
                        ChapterId = chapId,
                        ChapterNumber = parsed?.chapterNumber ?? 0,
                        Summary = summary
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 从MD提取摘要失败: {ex.Message}");
            }

            return result;
        }

        private string ExtractHeadFromMd(string content, int maxLength)
        {
            var lines = content.Split('\n');
            var bodyStart = 0;
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                if (lines[i].TrimStart().StartsWith("#"))
                {
                    bodyStart = i + 1;
                    break;
                }
            }
            var body = string.Join(' ', lines.Skip(bodyStart))
                .Replace("\r", "").Replace("\n", " ").Trim();

            return body.Length <= maxLength ? body : body.Substring(0, maxLength) + "...";
        }

        private string ExtractSampledSummaryFromMd(string rawContent, int maxLength)
        {
            var lines = rawContent.Split('\n');
            var bodyStart = 0;
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                if (lines[i].TrimStart().StartsWith("#"))
                {
                    bodyStart = i + 1;
                    break;
                }
            }
            var body = string.Join(' ', lines.Skip(bodyStart))
                .Replace("\r", "").Replace("\n", " ").Trim();

            if (body.Length <= maxLength) return body;

            var headLen = maxLength * 2 / 5;
            var midLen  = maxLength * 3 / 10;
            var tailLen = maxLength - headLen - midLen;

            var head = body.Substring(0, headLen);
            var midStart = Math.Max(headLen, body.Length / 2 - midLen / 2);
            var mid  = body.Substring(midStart, Math.Min(midLen, body.Length - midStart));
            var tail = body.Length > tailLen ? body.Substring(body.Length - tailLen) : string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.Append(head).Append("……");
            if (!string.IsNullOrWhiteSpace(mid))  sb.Append("[中段]").Append(mid).Append("……");
            if (!string.IsNullOrWhiteSpace(tail)) sb.Append("[末段]").Append(tail);
            return sb.ToString();
        }

        private async Task<string> LoadChapterTailAsync(string chapterId, int tailLength)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");

            if (!File.Exists(chapterFile)) return string.Empty;

            try
            {
                var tail = await ReadFileTailAsync(chapterFile, tailLength);
                if (string.IsNullOrWhiteSpace(tail))
                    return string.Empty;

                if (tail.Length <= tailLength)
                    return tail.Trim();

                var startIndex = tail.Length - tailLength;
                var paragraphStart = tail.IndexOf("\n\n", startIndex, StringComparison.Ordinal);
                if (paragraphStart > startIndex && paragraphStart < tail.Length - 100)
                {
                    startIndex = paragraphStart + 2;
                }

                return tail.Substring(startIndex).Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载章节尾部失败 [{chapterId}]: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<string> ReadFileHeadAsync(string filePath, int expectedLength)
        {
            var bytesToRead = Math.Max(4096, Math.Min(65536, expectedLength * 8 + 2048));

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            if (stream.Length <= bytesToRead)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                return await reader.ReadToEndAsync();
            }

            var buffer = new byte[bytesToRead];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read <= 0) return string.Empty;
            return Encoding.UTF8.GetString(buffer, 0, read);
        }

        private static async Task<string> ReadFileTailAsync(string filePath, int expectedLength)
        {
            var bytesToRead = Math.Max(4096, Math.Min(131072, expectedLength * 8 + 4096));

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            if (stream.Length <= bytesToRead)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                var full = await reader.ReadToEndAsync();

                var lines = full.Split('\n');
                var bodyStart = 0;
                for (int i = 0; i < Math.Min(3, lines.Length); i++)
                {
                    if (lines[i].TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        bodyStart = i + 1;
                        break;
                    }
                }
                var body = string.Join('\n', lines.Skip(bodyStart));
                return body.Trim();
            }

            var start = Math.Max(0, stream.Length - bytesToRead - 4);
            stream.Seek(start, SeekOrigin.Begin);

            var remaining = stream.Length - start;
            var bufferSize = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
            var buffer = new byte[bufferSize];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read <= 0) return string.Empty;

            var tail = Encoding.UTF8.GetString(buffer, 0, read);
            return tail;
        }

        private bool HasChapterMd(string chapterId)
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
                return File.Exists(chapterFile);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 检查章节MD失败: {ex.Message}");
                return false;
            }
        }

        private int ParseChapterNumber(string chapterId)
        {
            return ChapterParserHelper.ExtractChapterNumberFromSuffix(chapterId);
        }

        private string BuildChapterId(string templateChapterId, int chapterNumber)
        {
            return ChapterParserHelper.ReplaceChapterNumber(templateChapterId, chapterNumber);
        }

        #endregion

        public async Task<int> GetVolumeMaxChapterAsync(int volumeNumber)
        {
            try
            {
                var contentGuide = await GetContentGuideAsync();
                if (contentGuide?.Chapters == null || contentGuide.Chapters.Count == 0)
                    return 0;

                var maxChapter = 0;
                foreach (var kvp in contentGuide.Chapters)
                {
                    var p = ChapterParserHelper.ParseChapterId(kvp.Key);
                    if (p.HasValue && p.Value.volumeNumber == volumeNumber)
                        maxChapter = Math.Max(maxChapter, p.Value.chapterNumber);
                }
                return maxChapter;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 推断第{volumeNumber}卷末章节号失败: {ex.Message}");
                return 0;
            }
        }

        #region SnapshotExtract

        public Task<Models.Tracking.FactSnapshot> ExtractFactSnapshotForChapterAsync(
            string chapterId,
            ContextIdCollection contextIds)
            => ExtractFactSnapshotAsync(chapterId, contextIds);

        private async Task<Models.Tracking.FactSnapshot> ExtractFactSnapshotAsync(
            string chapterId, 
            ContextIdCollection contextIds)
        {
            try
            {
                var characterIds = contextIds?.Characters ?? new List<string>();
                var locationIds = contextIds?.Locations ?? new List<string>();
                var conflictIds = contextIds?.Conflicts ?? new List<string>();
                var foreshadowingSetupIds = contextIds?.ForeshadowingSetups ?? new List<string>();
                var foreshadowingPayoffIds = contextIds?.ForeshadowingPayoffs ?? new List<string>();
                var worldRuleIds = contextIds?.WorldRuleIds ?? new List<string>();
                var factionIds = contextIds?.Factions ?? new List<string>();

                var snapshot = await _factSnapshotExtractor.ExtractSnapshotAsync(
                    chapterId,
                    characterIds,
                    locationIds,
                    conflictIds,
                    foreshadowingSetupIds,
                    foreshadowingPayoffIds,
                    worldRuleIds,
                    factionIds);

                TM.App.Log($"[GuideContextService] 势力注入: {snapshot.FactionStates?.Count ?? 0}条（关联{factionIds.Count}个优先）");

                TM.App.Log($"[GuideContextService] 物品注入: {snapshot.ItemStates?.Count ?? 0}条（FactSnapshotExtractor已过滤）");

                TM.App.Log($"[GuideContextService] 事实快照抽取完成: " +
                    $"角色{snapshot.CharacterStates.Count}, " +
                    $"冲突{snapshot.ConflictProgress.Count}, " +
                    $"伏笔{snapshot.ForeshadowingStatus.Count}, " +
                    $"情节{snapshot.PlotPoints.Count}");

                return snapshot;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 事实快照抽取失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Helpers

        public async Task<ContentGuide> GetContentGuideAsync()
        {
            ContentGuide? cached;
            lock (_contentGuideCacheLock)
            {
                cached = _contentGuideCache;
            }
            if (cached != null) return cached;

            await _contentGuideLoadLock.WaitAsync();
            try
            {
                lock (_contentGuideCacheLock) { cached = _contentGuideCache; }
                if (cached != null) return cached;

                var guidesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
                var shardFiles = Directory.Exists(guidesDir)
                    ? Directory.GetFiles(guidesDir, "content_guide_vol*.json")
                        .Select(f => {
                            var stem = Path.GetFileNameWithoutExtension(f);
                            const string prefix = "content_guide_vol";
                            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (Path: f, Vol: -1);
                            var suffix = stem.Substring(prefix.Length);
                            return int.TryParse(suffix, out var num) && num > 0 ? (Path: f, Vol: num) : (Path: f, Vol: -1);
                        })
                        .Where(x => x.Vol > 0)
                        .OrderBy(x => x.Vol)
                        .Select(x => x.Path)
                        .ToArray()
                    : Array.Empty<string>();

                ContentGuide merged;
                if (shardFiles.Length > 0)
                {
                    merged = new ContentGuide();
                    foreach (var shardFile in shardFiles)
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(shardFile);
                            var shard = JsonSerializer.Deserialize<ContentGuide>(json, JsonOptions);
                            if (shard == null) continue;
                            if (string.IsNullOrEmpty(merged.SourceBookId))
                                merged.SourceBookId = shard.SourceBookId;
                            foreach (var (k, v) in shard.Chapters)
                                merged.Chapters[k] = v;
                            foreach (var (k, v) in shard.ChapterSummaries)
                                merged.ChapterSummaries[k] = v;
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[GuideContextService] 加载分片失败 {Path.GetFileName(shardFile)}: {ex.Message}");
                        }
                    }

                    var currentScopeForGuide = await _workScopeService.GetCurrentScopeAsync();
                    if (!string.IsNullOrEmpty(currentScopeForGuide)
                        && !string.IsNullOrEmpty(merged.SourceBookId)
                        && !string.Equals(merged.SourceBookId, currentScopeForGuide, StringComparison.Ordinal))
                    {
                        TM.App.Log($"[GuideContextService] content_guide Scope不匹配: guide={merged.SourceBookId}, current={currentScopeForGuide}，请重新打包。");
                        merged = new ContentGuide();
                    }
                    else
                    {
                        TM.App.Log($"[GuideContextService] content_guide 聚合 {shardFiles.Length} 个分片，共 {merged.Chapters.Count} 章");
                    }
                }
                else
                {
                    merged = await LoadGuideAsync<ContentGuide>("content_guide.json");
                }

                lock (_contentGuideCacheLock)
                {
                    _contentGuideCache ??= merged;
                    return _contentGuideCache;
                }
            }
            finally
            {
                _contentGuideLoadLock.Release();
            }
        }

        public void InvalidateContentGuideCache()
        {
            lock (_contentGuideCacheLock)
            {
                _contentGuideCache = null;
            }
        }

        public async Task<T> LoadGuideAsync<T>(string fileName) where T : new()
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", fileName);

            if (!File.Exists(guidesPath))
            {
                TM.App.Log($"[GuideContextService] 指导文件不存在: {fileName}");
                return new T();
            }

            try
            {
                var json = await File.ReadAllTextAsync(guidesPath);
                var guide = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();

                var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();
                if (!string.IsNullOrEmpty(currentSourceBookId) && guide is TM.Framework.Common.Models.ISourceBookBound bound)
                {
                    var guideSourceBookId = bound.SourceBookId;
                    if (!string.IsNullOrEmpty(guideSourceBookId)
                        && !string.Equals(guideSourceBookId, currentSourceBookId, StringComparison.Ordinal))
                    {
                        TM.App.Log($"[GuideContextService] 指导文件Scope不匹配: file={fileName}, guide={guideSourceBookId}, current={currentSourceBookId}。请重新打包生成指导文件。");
                        return new T();
                    }
                }

                return guide;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载指导文件失败 [{fileName}]: {ex.Message}");
                return new T();
            }
        }

        private async Task<List<T>> LoadPackagedAsync<T>(string relativePath, string dataKey)
        {
            var filePath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), relativePath);
            var items = new List<T>();

            if (!File.Exists(filePath))
            {
                TM.App.Log($"[GuideContextService] 打包文件不存在: {relativePath}");
                return items;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    if (!dataProp.TryGetProperty(dataKey, out var keyProp))
                    {
                        if (dataKey.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                        {
                            var alt = dataKey.TrimEnd('s');
                            dataProp.TryGetProperty(alt, out keyProp);
                        }
                        else
                        {
                            var alt = dataKey + "s";
                            dataProp.TryGetProperty(alt, out keyProp);
                        }
                    }

                    if (keyProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var fileProp in keyProp.EnumerateObject())
                        {
                            if (string.Equals(fileProp.Name, "categories", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (fileProp.Value.ValueKind == JsonValueKind.Array)
                            {
                                var arrayJson = fileProp.Value.GetRawText();
                                var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                                if (arrayItems != null) items.AddRange(arrayItems);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载打包数据失败 [{relativePath}]: {ex.Message}");
            }

            return items;
        }

        public async Task<string> GetChapterSummaryAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return string.Empty;

            return await _summaryStore.GetSummaryAsync(chapterId);
        }

        private async Task<string> LoadChapterContentAsync(string chapterId)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");

            if (!File.Exists(chapterFile))
            {
                TM.App.Log($"[GuideContextService] 章节文件不存在: {chapterId}");
                return string.Empty;
            }

            try
            {
                return await File.ReadAllTextAsync(chapterFile);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载章节内容失败 [{chapterId}]: {ex.Message}");
                return string.Empty;
            }
        }

        #endregion

        #region RelationLoad

        public async Task<(List<Models.Index.IndexItem> Direct, List<Models.Index.IndexItem> Indirect)> 
            GetRelatedEntitiesAsync(string focusId, string layer)
        {
            var direct = new List<Models.Index.IndexItem>();
            var indirect = new List<Models.Index.IndexItem>();

            try
            {
                var relationsPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "CharacterRules", "relationships.json");

                if (!File.Exists(relationsPath))
                    return (direct, indirect);

                var json = await File.ReadAllTextAsync(relationsPath);
                var relations = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                if (relations == null)
                    return (direct, indirect);

                foreach (var rel in relations)
                {
                    var char1 = GetJsonString(rel, "Character1Id");
                    var char2 = GetJsonString(rel, "Character2Id");
                    var strength = GetJsonString(rel, "RelationshipType");

                    string? relatedId = null;
                    if (char1 == focusId) relatedId = char2;
                    else if (char2 == focusId) relatedId = char1;

                    if (relatedId is null) continue;

                    var relStrength = DetermineStrength(strength);
                    var indexItem = await BuildRelatedIndexItemAsync(relatedId, relStrength);

                    if (indexItem == null) continue;

                    if (relStrength == Models.Context.RelationStrength.Strong)
                    {
                        if (direct.Count < 5)
                            direct.Add(indexItem);
                    }
                    else
                    {
                        if (indirect.Count < 10)
                            indirect.Add(indexItem);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 获取关联实体失败: {ex.Message}");
            }

            return (direct, indirect);
        }

        public async Task<(List<Models.Index.IndexItem> Direct, List<Models.Index.IndexItem> Indirect)>
            GetRelatedEntitiesAsync(string focusId, string layer, string? sourceBookId)
        {
            if (string.IsNullOrEmpty(sourceBookId))
                return await GetRelatedEntitiesAsync(focusId, layer);

            var direct = new List<Models.Index.IndexItem>();
            var indirect = new List<Models.Index.IndexItem>();

            try
            {
                var relationsPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Elements", "CharacterRules", "relationships.json");

                if (!File.Exists(relationsPath))
                    return (direct, indirect);

                var json = await File.ReadAllTextAsync(relationsPath);
                var relations = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json, JsonOptions);

                if (relations == null)
                    return (direct, indirect);

                foreach (var rel in relations)
                {
                    if (GetJsonString(rel, "SourceBookId") != sourceBookId)
                        continue;

                    var char1 = GetJsonString(rel, "Character1Id");
                    var char2 = GetJsonString(rel, "Character2Id");
                    var strength = GetJsonString(rel, "RelationshipType");

                    string? relatedId = null;
                    if (char1 == focusId) relatedId = char2;
                    else if (char2 == focusId) relatedId = char1;

                    if (relatedId is null) continue;

                    var relStrength = DetermineStrength(strength);
                    var indexItem = await BuildRelatedIndexItemAsync(relatedId, relStrength, sourceBookId);

                    if (indexItem == null) continue;

                    if (relStrength == Models.Context.RelationStrength.Strong)
                    {
                        if (direct.Count < 5)
                            direct.Add(indexItem);
                    }
                    else
                    {
                        if (indirect.Count < 10)
                            indirect.Add(indexItem);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 获取关联实体失败（Scope={sourceBookId}）: {ex.Message}");
            }

            return (direct, indirect);
        }

        private Models.Context.RelationStrength DetermineStrength(string relationshipType)
        {
            var strongTypes = new[] { "师徒", "血亲", "宿敌", "挚友", "恋人", "主仆" };
            var mediumTypes = new[] { "同门", "盟友", "对手", "同伴" };

            if (strongTypes.Any(t => relationshipType?.Contains(t) == true))
                return Models.Context.RelationStrength.Strong;
            if (mediumTypes.Any(t => relationshipType?.Contains(t) == true))
                return Models.Context.RelationStrength.Medium;
            return Models.Context.RelationStrength.Weak;
        }

        private async Task<Models.Index.IndexItem?> BuildRelatedIndexItemAsync(
            string entityId, Models.Context.RelationStrength strength)
        {
            var currentSourceBookId = _workScopeService.CurrentSourceBookId ?? string.Empty;
            return await BuildRelatedIndexItemAsync(entityId, strength, currentSourceBookId);
        }

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        #endregion

        #region 风险4/5 辅助方法

        private static async Task DetectStateDivergenceAsync(ContentTaskContext context)
        {
            if (context.FactSnapshot == null || context.Characters.Count == 0)
                return;
            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();
                var _csVols = guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                var charGuide = new TM.Services.Modules.ProjectData.Models.Guides.CharacterStateGuide();
                foreach (var _v in _csVols.TakeLast(3))
                    {
                        var _g = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", _v));
                        foreach (var (_id, _entry) in _g.Characters)
                        {
                            if (!charGuide.Characters.ContainsKey(_id))
                                charGuide.Characters[_id] = new TM.Services.Modules.ProjectData.Models.Guides.CharacterStateEntry { Name = _entry.Name };
                            (charGuide.Characters[_id].DriftWarnings ??= new System.Collections.Generic.List<string>()).AddRange(_entry.DriftWarnings ?? new System.Collections.Generic.List<string>());
                        }
                }
                foreach (var _c in charGuide.Characters.Values)
                    if (_c.DriftWarnings.Count > 1)
                        _c.DriftWarnings = new System.Collections.Generic.List<string>(
                            System.Linq.Enumerable.Distinct(_c.DriftWarnings, System.StringComparer.Ordinal));
                foreach (var charData in context.Characters)
                {
                    if (string.IsNullOrWhiteSpace(charData.Id)) continue;
                    if (!charGuide.Characters.TryGetValue(charData.Id, out var entry)) continue;
                    if (entry.DriftWarnings.Count == 0) continue;
                    const int DriftEscalateThreshold = 3;
                    var warnMsg = entry.DriftWarnings.Count >= DriftEscalateThreshold
                        ? $"角色[{entry.Name}]: ⚠⚠ 累积漂移{entry.DriftWarnings.Count}条（已超严重阈值{DriftEscalateThreshold}），FactSnapshot.CharacterStates可能不完整，请优先以最新状态描述为准并警惕角色信息矛盾"
                        : $"角色[{entry.Name}]: 静态设定与运行状态存在{entry.DriftWarnings.Count}条漂移记录，请以FactSnapshot.CharacterStates为准";
                    context.StateDivergenceWarnings.Add(warnMsg);
                }
                if (context.StateDivergenceWarnings.Count > 0)
                    TM.App.Log($"[GuideContextService] 检测到{context.StateDivergenceWarnings.Count}个设定/状态分歧角色");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 状态分歧检测失败: {ex.Message}");
            }
        }

        private static bool IsVectorRecallDegraded()
        {
            try
            {
                var flagPath = System.IO.Path.Combine(
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetCurrentProjectPath(),
                    "vector_degraded.flag");
                return System.IO.File.Exists(flagPath);
            }
            catch { return false; }
        }

        private async Task DetectTrackingGapsAsync(ContentTaskContext context, string currentChapterId)
        {
            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();
                var _csVols2 = guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                var stateGuide = new CharacterStateGuide();
                foreach (var _v2 in _csVols2.TakeLast(5))
                    {
                        var _g2 = await guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", _v2));
                        foreach (var (_id2, _entry2) in _g2.Characters)
                        {
                            if (!stateGuide.Characters.ContainsKey(_id2))
                                stateGuide.Characters[_id2] = new CharacterStateEntry { Name = _entry2.Name };
                            stateGuide.Characters[_id2].StateHistory.AddRange(_entry2.StateHistory);
                        }
                }

                var trackedChapters = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in stateGuide.Characters.Values)
                    foreach (var state in entry.StateHistory)
                        if (!string.IsNullOrWhiteSpace(state.Chapter))
                            trackedChapters.Add(state.Chapter);

                try
                {
                    var _timelineVols = guideManager.GetExistingVolumeNumbers("timeline_guide.json");
                    foreach (var _vt in _timelineVols.TakeLast(5))
                    {
                        var _gt = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.TimelineGuide>(GuideManager.GetVolumeFileName("timeline_guide.json", _vt));
                        foreach (var _te in _gt.ChapterTimeline)
                            if (!string.IsNullOrWhiteSpace(_te.ChapterId))
                                trackedChapters.Add(_te.ChapterId);
                    }
                }
                catch (Exception _tlEx)
                {
                    TM.App.Log($"[GuideContextService] v1追踪空洞: 加载Timeline guide失败，降级为仅角色状态判断: {_tlEx.Message}");
                }

                if (trackedChapters.Count == 0) return;

                var chaptersPath = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetProjectChaptersPath();
                if (!System.IO.Directory.Exists(chaptersPath)) return;

                var allChapterIds = GetCachedChapterIds(chaptersPath);
                var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);

                var gapChapters = allChapterIds
                    .Where(id => ChapterParserHelper.CompareChapterId(id, currentChapterId) < 0)
                    .OrderByDescending(id => id, comparer)
                    .Take(10)
                    .Where(id => !trackedChapters.Contains(id))
                    .ToList();

                if (gapChapters.Count > 0)
                {
                    context.StateDivergenceWarnings.Add(
                        $"[v1追踪空洞] 近10章中以下章节有正文但无CHANGES记录，账本可能有空洞（FactSnapshot准确性下降）: {string.Join(", ", gapChapters)}");
                    TM.App.Log($"[GuideContextService] v1: 追踪空洞 {gapChapters.Count}章: {string.Join(", ", gapChapters)}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] v1追踪空洞检测失败: {ex.Message}");
            }
        }

        private static async Task ValidateVolumeEndChapterAsync(ContentTaskContext context, string chapterId)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (parsed == null) return;

                var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                await volumeService.InitializeAsync();
                var designs = volumeService.GetAllVolumeDesigns();
                var design = designs.FirstOrDefault(v => v.VolumeNumber == parsed.Value.volumeNumber);

                if (design != null && design.EndChapter <= 0)
                {
                    context.StateDivergenceWarnings.Add(
                        $"[v2卷末存档] 第{parsed.Value.volumeNumber}卷EndChapter未配置，卷末事实存档将永远不触发！跨卷角色基线无法建立，请在卷设计中配置EndChapter");
                    TM.App.Log($"[GuideContextService] v2: 第{parsed.Value.volumeNumber}卷EndChapter未配置");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] v2 EndChapter校验失败: {ex.Message}");
            }
        }

        #endregion
    }
}
