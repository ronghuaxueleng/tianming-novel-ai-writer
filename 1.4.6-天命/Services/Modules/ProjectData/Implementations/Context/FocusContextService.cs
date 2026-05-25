using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class FocusContextService : IFocusContextService
    {
        private readonly IndexService _indexService;
        private readonly RelationStrengthService _relationStrengthService;
        private readonly ProgressiveSummaryService _progressiveSummaryService;
        private readonly GuideContextService _guideContextService;
        private readonly GlobalSummaryService _globalSummaryService;
        private readonly IWorkScopeService _workScopeService;

        private readonly object _cacheLock = new();
        private readonly Dictionary<string, (DateTime Time, DesignFocusContext Context)> _designContextCache = new();
        private readonly Dictionary<string, (DateTime Time, GenerateFocusContext Context)> _generateContextCache = new();
        private readonly TimeSpan _contextCacheExpiry = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public FocusContextService(
            IndexService indexService,
            RelationStrengthService relationStrengthService,
            ProgressiveSummaryService progressiveSummaryService,
            GuideContextService guideContextService,
            GlobalSummaryService globalSummaryService,
            IWorkScopeService workScopeService)
        {
            _indexService = indexService;
            _relationStrengthService = relationStrengthService;
            _progressiveSummaryService = progressiveSummaryService;
            _guideContextService = guideContextService;
            _globalSummaryService = globalSummaryService;
            _workScopeService = workScopeService;

            _workScopeService.ScopeChanged += (_, __) => InvalidateCache();

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 订阅项目切换事件失败: {ex.Message}");
            }

            GuideContextService.CacheInvalidated += (_, _) => InvalidateCache();
        }

        public async Task<DesignFocusContext> GetDesignContextAsync(string focusId, string targetLayer)
        {
            var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();
            return await GetDesignContextAsync(focusId, targetLayer, currentSourceBookId);
        }

        public async Task<DesignFocusContext> GetDesignContextAsync(string focusId, string targetLayer, string? sourceBookId)
        {
            var cacheKey = BuildCacheKey("Design", focusId, targetLayer, sourceBookId);
            lock (_cacheLock)
            {
                if (_designContextCache.TryGetValue(cacheKey, out var cached)
                    && DateTime.UtcNow - cached.Time < _contextCacheExpiry)
                {
                    return cached.Context;
                }
            }

            var context = new DesignFocusContext
            {
                GlobalSummary = !string.IsNullOrEmpty(sourceBookId)
                    ? await _globalSummaryService.GetGlobalSummaryAsync(sourceBookId)
                    : await GetGlobalSummaryAsync(),
                TrackingStatus = await GetTrackingStatusAsync(),
                UpstreamIndex = await _indexService.BuildUpstreamIndexAsync(targetLayer, sourceBookId)
            };

            context.Focus = await BuildFocusContextAsync(focusId, targetLayer, sourceBookId);

            TM.App.Log($"[FocusContextService] 设计上下文已构建: targetLayer={targetLayer}, sourceBookId={sourceBookId ?? "null"}, 上下文长度≈{EstimateContextLength(context)}字符");

            lock (_cacheLock)
            {
                _designContextCache[cacheKey] = (DateTime.UtcNow, context);
            }

            return context;
        }

        public async Task<GenerateFocusContext> GetGenerateContextAsync(string focusId, string targetLayer)
        {
            var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();
            return await GetGenerateContextAsync(focusId, targetLayer, currentSourceBookId);
        }

        public async Task<GenerateFocusContext> GetGenerateContextAsync(string focusId, string targetLayer, string? sourceBookId)
        {
            var cacheKey = BuildCacheKey("Generate", focusId, targetLayer, sourceBookId);
            lock (_cacheLock)
            {
                if (_generateContextCache.TryGetValue(cacheKey, out var cached)
                    && DateTime.UtcNow - cached.Time < _contextCacheExpiry)
                {
                    return cached.Context;
                }
            }

            var context = new GenerateFocusContext
            {
                GlobalSummary = !string.IsNullOrEmpty(sourceBookId)
                    ? await _globalSummaryService.GetGlobalSummaryAsync(sourceBookId)
                    : await GetGlobalSummaryAsync(),
                TrackingStatus = await GetTrackingStatusAsync(),
                UpstreamIndex = await _indexService.BuildUpstreamIndexAsync(targetLayer, sourceBookId)
            };

            context.Focus = await BuildFocusContextAsync(focusId, targetLayer, sourceBookId);

            context.TaskContext = await LoadTaskContextAsync(focusId, targetLayer);

            TM.App.Log($"[FocusContextService] 创作上下文已构建: targetLayer={targetLayer}, sourceBookId={sourceBookId ?? "null"}, 上下文长度≈{EstimateContextLength(context)}字符");

            lock (_cacheLock)
            {
                _generateContextCache[cacheKey] = (DateTime.UtcNow, context);
            }

            return context;
        }

        public async Task<GlobalSummary> GetGlobalSummaryAsync()
        {
            return await _globalSummaryService.GetGlobalSummaryAsync();
        }

        public async Task<TrackingStatus> GetTrackingStatusAsync()
        {
            var statusPath = Path.Combine(
                StoragePathHelper.GetProjectConfigPath(),
                "tracking_status.json");

            if (File.Exists(statusPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(statusPath);
                    return JsonSerializer.Deserialize<TrackingStatus>(json, JsonOptions)
                           ?? new TrackingStatus();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[FocusContextService] 加载TrackingStatus失败: {ex.Message}");
                }
            }

            return await BuildTrackingStatusRealtimeAsync();
        }

        private async Task<FocusContext> BuildFocusContextAsync(string focusId, string targetLayer, string? sourceBookId)
        {
            var focus = new FocusContext
            {
                FocusId = focusId,
                FocusType = targetLayer,
                Layer = targetLayer,
                DirectRelations = new List<IndexItem>(),
                IndirectRelations = new List<IndexItem>()
            };

            if (!string.IsNullOrEmpty(focusId))
            {
                var focusItem = await _indexService.GetIndexItemAsync(focusId, targetLayer, sourceBookId);
                if (focusItem != null)
                {
                    focus.FocusEntity = focusItem;
                }

                var (direct, indirect) = await _guideContextService.GetRelatedEntitiesAsync(focusId, targetLayer, sourceBookId);
                focus.DirectRelations = direct;
                focus.IndirectRelations = indirect;

                if (focus.DirectRelations.Count == 0 && focus.IndirectRelations.Count == 0)
                {
                    await LoadRelationsViaStrengthServiceAsync(focus, focusId, sourceBookId);
                }
            }

            focus.UpstreamIndex = await _indexService.BuildUpstreamIndexAsync(targetLayer, sourceBookId);

            return focus;
        }

        private static string BuildCacheKey(string kind, string focusId, string targetLayer, string? sourceBookId)
            => $"{kind}|{targetLayer}|{sourceBookId ?? "null"}|{focusId}";

        private async Task LoadRelationsViaStrengthServiceAsync(FocusContext focus, string focusId, string? sourceBookId)
        {
            var allCharacterIds = await GetAllCharacterIdsAsync();
            foreach (var charId in allCharacterIds.Where(id => id != focusId))
            {
                var strength = await _relationStrengthService.GetStrengthAsync(focusId, charId);
                if (strength == RelationStrength.Strong && focus.DirectRelations.Count < 5)
                {
                    var item = await _indexService.GetIndexItemAsync(charId, "Characters", sourceBookId);
                    if (item != null)
                    {
                        item.RelationStrength = "Strong";
                        focus.DirectRelations.Add(item);
                    }
                }
                else if (strength == RelationStrength.Medium && focus.IndirectRelations.Count < 10)
                {
                    var item = await _indexService.GetIndexItemAsync(charId, "Characters", sourceBookId);
                    if (item != null)
                    {
                        item.RelationStrength = "Medium";
                        focus.IndirectRelations.Add(item);
                    }
                }
            }
        }

        private async Task<List<string>> GetAllCharacterIdsAsync()
        {
            try
            {
                var allChars = await _guideContextService.GetAllCharactersAsync();
                return allChars
                    .Select(c => c.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 加载角色ID列表失败: {ex.Message}");
                return new List<string>();
            }
        }

        #region 辅助方法

        private static string GetJsonString(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            return string.Empty;
        }

        #endregion

        private async Task<TrackingStatus> BuildTrackingStatusRealtimeAsync()
        {
            var status = new TrackingStatus();

            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();

                var characterGuide = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.CharacterStateGuide>(
                    "character_state_guide.json");
                var conflictGuide = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ConflictProgressGuide>(
                    "conflict_progress_guide.json");
                var foreshadowingGuide = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ForeshadowingStatusGuide>(
                    "foreshadowing_status_guide.json");
                var plotPointService = ServiceLocator.Get<PlotPointsIndexService>();
                var recentPlotEntries = new System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Tracking.PlotPointEntry>();
                foreach (var vol in plotPointService.GetExistingVolumeNumbers())
                    recentPlotEntries.AddRange(await plotPointService.GetVolumeEntriesAsync(vol));
                var plotPoints = new Models.Guides.PlotPointsIndex { PlotPoints = recentPlotEntries };

                status.CharacterStates = characterGuide.Characters
                    .Select(kvp =>
                    {
                        var last = kvp.Value.StateHistory.LastOrDefault();
                        return new CharacterState
                        {
                            CharacterId = kvp.Key,
                            CharacterName = kvp.Value.Name,
                            CurrentStatus = last == null
                                ? string.Empty
                                : string.Join("/", new[] { last.Phase, last.Level, last.MentalState }.Where(s => !string.IsNullOrWhiteSpace(s))),
                            LastAppearanceChapter = last?.Chapter ?? string.Empty,
                            CurrentGoal = string.Empty
                        };
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s.CharacterId))
                    .OrderByDescending(s => s.LastAppearanceChapter, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                    .Take(30)
                    .ToList();

                static int MapProgressPercent(string? status)
                {
                    return (status ?? string.Empty).ToLowerInvariant() switch
                    {
                        "resolved" => 100,
                        "climax" => 80,
                        "active" => 50,
                        _ => 0
                    };
                }

                status.ConflictProgress = conflictGuide.Conflicts
                    .Select(kvp => new ConflictProgress
                    {
                        ConflictId = kvp.Key,
                        ConflictName = kvp.Value.Name,
                        ProgressPercent = MapProgressPercent(kvp.Value.Status),
                        CurrentPhase = kvp.Value.Status ?? string.Empty,
                        NextExpectedEvent = string.Empty
                    })
                    .Where(c => !string.IsNullOrWhiteSpace(c.ConflictId))
                    .Take(30)
                    .ToList();

                status.ForeshadowingStats = new ForeshadowingStats
                {
                    Total = foreshadowingGuide.Summary.Total,
                    Planted = foreshadowingGuide.Summary.Setup,
                    Resolved = foreshadowingGuide.Summary.Payoff,
                    PendingResolution = foreshadowingGuide.PendingList
                        .Select(p => p.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .Take(50)
                        .ToList()
                };

                status.PlotPoints = plotPoints;

                TM.App.Log("[FocusContextService] TrackingStatus实时构建完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FocusContextService] 构建TrackingStatus失败: {ex.Message}");
            }

            return status;
        }

        private async Task<object?> LoadTaskContextAsync(string focusId, string targetLayer)
        {
            return targetLayer switch
            {
                "Blueprint" => await _guideContextService.BuildBlueprintContextAsync(focusId),
                "Content" => await _guideContextService.BuildContentContextAsync(focusId),
                _ => null
            };
        }

        public void InvalidateCache()
        {
            _globalSummaryService.InvalidateCache();
            _relationStrengthService.InvalidateCache();

            lock (_cacheLock)
            {
                _designContextCache.Clear();
                _generateContextCache.Clear();
            }
        }

        #region 按层级入口方法

        public Task<DesignFocusContext> GetSmartParsingContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "SmartParsing");

        public Task<DesignFocusContext> GetTemplatesContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Templates");

        public Task<DesignFocusContext> GetWorldviewContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Worldview");

        public Task<DesignFocusContext> GetCharactersContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Characters");

        public Task<DesignFocusContext> GetFactionsContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Factions");

        public Task<DesignFocusContext> GetPlotContextAsync(string focusId)
            => GetDesignContextAsync(focusId, "Plot");

        public Task<GenerateFocusContext> GetOutlineContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Outline");

        public Task<GenerateFocusContext> GetPlanningContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Planning");

        public Task<GenerateFocusContext> GetBlueprintContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Blueprint");

        public Task<GenerateFocusContext> GetContentContextAsync(string focusId)
            => GetGenerateContextAsync(focusId, "Content");

        #endregion

        private int EstimateContextLength(DesignFocusContext context)
        {
            var length = 0;
            if (context.GlobalSummary != null)
                length += (context.GlobalSummary.ToString()?.Length ?? 0);
            if (context.UpstreamIndex != null)
                length += EstimateUpstreamIndexLength(context.UpstreamIndex);
            return length;
        }

        private int EstimateContextLength(GenerateFocusContext context)
        {
            var length = 0;
            if (context.GlobalSummary != null)
                length += (context.GlobalSummary.ToString()?.Length ?? 0);
            if (context.UpstreamIndex != null)
                length += EstimateUpstreamIndexLength(context.UpstreamIndex);
            return length;
        }

        private int EstimateUpstreamIndexLength(Models.Index.UpstreamIndex index)
        {
            var length = 0;
            length += index.SmartParsing?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Templates?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Worldview?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Characters?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Factions?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Plot?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Outline?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Planning?.Sum(i => i.BriefSummary.Length) ?? 0;
            length += index.Blueprint?.Sum(i => i.BriefSummary.Length) ?? 0;
            return length;
        }
    }
}
