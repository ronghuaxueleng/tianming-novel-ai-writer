using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Framework.AI.QueryRouting
{
    public class QueryRoutingService
    {
        private readonly GuideContextService _guideService;
        private readonly DataIndexService _dataIndex;
        private readonly QueryRouter _router;
        private readonly VectorSearchService _vectorSearch;
        private readonly IChangeDetectionService _changeDetection;
        private readonly IWorkScopeService _workScopeService;
        private volatile bool _cacheInitialized;
        private int _cacheEpoch;
        private readonly SemaphoreSlim _cacheInitLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public QueryRoutingService(
            GuideContextService guideService,
            DataIndexService dataIndex,
            QueryRouter router,
            VectorSearchService vectorSearch,
            IChangeDetectionService changeDetection,
            IWorkScopeService workScopeService)
        {
            _guideService = guideService;
            _dataIndex = dataIndex;
            _router = router;
            _vectorSearch = vectorSearch;
            _changeDetection = changeDetection;
            _workScopeService = workScopeService;

            _workScopeService.ScopeChanged += (_, _) => ClearCache();
            GuideContextService.CacheInvalidated += (_, _) => ClearCache();
        }

        public void ClearCache()
        {
            _guideService.ClearCache();
            _dataIndex.Clear();
            _cacheInitialized = false;
            Interlocked.Increment(ref _cacheEpoch);
        }

        private async Task EnsureCacheAsync()
        {
            if (_cacheInitialized)
                return;

            await _cacheInitLock.WaitAsync();
            try
            {
                if (_cacheInitialized)
                    return;

                var epoch = Volatile.Read(ref _cacheEpoch);

                await _guideService.InitializeCacheAsync();
                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                await _dataIndex.InitializeAsync();
                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                var allNames = _dataIndex.ListIdsByCategory(EntityCategory.Character)
                    .Select(id => _dataIndex.FindById(id)?.Name ?? "")
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.Location)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.Faction)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.PlotRule)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Concat(_dataIndex.ListIdsByCategory(EntityCategory.WorldRule)
                        .Select(id => _dataIndex.FindById(id)?.Name ?? ""))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                if (epoch != Volatile.Read(ref _cacheEpoch))
                    return;

                _router.UpdateNameIndex(allNames);
                _cacheInitialized = true;
                TM.App.Log($"[QueryRoutingService] 索引初始化完成: {allNames.Count} 个实体名称");
            }
            finally
            {
                _cacheInitLock.Release();
            }
        }

        public async Task<string> GetCharacterByIdAsync(string characterId)
        {
            await EnsureCacheAsync();
            var results = await _guideService.ExtractCharactersAsync(new List<string> { characterId });
            if (results.Count == 0)
                return $"[未找到] 角色ID: {characterId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetCharactersByIdsAsync(string characterIds)
        {
            await EnsureCacheAsync();
            var ids = characterIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractCharactersAsync(ids);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何角色";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        private string ResolveDisplay(string id)
        {
            var entry = _dataIndex.FindById(id);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                return id;
            return $"{entry.Name}({entry.Id})";
        }

        public async Task<string> GetLocationByIdAsync(string locationId)
        {
            await EnsureCacheAsync();
            var results = await _guideService.ExtractLocationsAsync(new List<string> { locationId });
            if (results.Count == 0)
                return $"[未找到] 地点ID: {locationId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetFactionByIdAsync(string factionId)
        {
            await EnsureCacheAsync();
            var results = await _guideService.ExtractFactionsAsync(new List<string> { factionId });
            if (results.Count == 0)
                return $"[未找到] 势力ID: {factionId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetPlotRuleByIdAsync(string plotRuleId)
        {
            await EnsureCacheAsync();
            var results = await _guideService.ExtractPlotRulesAsync(new List<string> { plotRuleId });
            if (results.Count == 0)
                return $"[未找到] 剧情规则ID: {plotRuleId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetWorldRuleByIdAsync(string worldRuleId)
        {
            await EnsureCacheAsync();
            var results = await _guideService.ExtractWorldRulesAsync(new List<string> { worldRuleId });
            if (results.Count == 0)
                return $"[未找到] 世界观规则ID: {worldRuleId}";
            return JsonSerializer.Serialize(results[0], JsonOptions);
        }

        public async Task<string> GetExpandedChapterContextAsync(string chapterId)
        {
            var ctx = await _guideService.BuildContentContextAsync(chapterId);
            if (ctx == null)
                return $"[未找到] 章节上下文: {chapterId}，请确认已执行打包";
            return JsonSerializer.Serialize(ctx, JsonOptions);
        }

        public async Task<string> GetChapterContextAsync(string chapterId)
        {
            await EnsureCacheAsync();
            var guide = await _guideService.GetContentGuideAsync();
            if (guide?.Chapters == null || !guide.Chapters.TryGetValue(chapterId, out var entry))
                return $"[未找到] 章节: {chapterId}";
            return JsonSerializer.Serialize(new { entry.ChapterId, entry.Title, entry.Summary, entry.ContextIds }, JsonOptions);
        }

        public async Task<string> GetLocationsByIdsAsync(string locationIds)
        {
            await EnsureCacheAsync();
            var ids = locationIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractLocationsAsync(ids);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何地点";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetFactionsByIdsAsync(string factionIds)
        {
            await EnsureCacheAsync();
            var ids = factionIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractFactionsAsync(ids);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何势力";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetPlotRulesByIdsAsync(string plotRuleIds)
        {
            await EnsureCacheAsync();
            var ids = plotRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractPlotRulesAsync(ids);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何剧情规则";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> GetWorldRulesByIdsAsync(string worldRuleIds)
        {
            await EnsureCacheAsync();
            var ids = worldRuleIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToList();
            var results = await _guideService.ExtractWorldRulesAsync(ids);
            if (results.Count == 0)
                return "[未找到] 未匹配到任何世界观规则";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> ListAvailableIdsAsync(string category)
        {
            await EnsureCacheAsync();
            var ids = category.ToLowerInvariant() switch
            {
                "characters" => _dataIndex.ListIdsByCategory(EntityCategory.Character),
                "locations" => _dataIndex.ListIdsByCategory(EntityCategory.Location),
                "factions" => _dataIndex.ListIdsByCategory(EntityCategory.Faction),
                "plotrules" => _dataIndex.ListIdsByCategory(EntityCategory.PlotRule),
                "worldrules" => _dataIndex.ListIdsByCategory(EntityCategory.WorldRule),
                _ => new List<string>()
            };
            var list = ids
                .Select(id => _dataIndex.FindById(id))
                .Where(entry => entry != null)
                .Select(entry => new { entry!.Id, entry.Name, Display = $"{entry.Name}({entry.Id})" })
                .ToList();
            if (list.Count == 0)
                return $"[未找到] 类别 {category} 无可用数据";
            return JsonSerializer.Serialize(list, JsonOptions);
        }

        public async Task<string> ValidateDataConsistencyAsync()
        {
            await _changeDetection.RefreshAllAsync();

            var changedModules = _changeDetection.GetChangedModules();
            if (changedModules.Count > 0)
            {
                return $"[警告] 以下模块有未打包变更：{string.Join(", ", changedModules)}";
            }
            return "[正常] 打包数据与原始数据一致";
        }

        public async Task<string> SearchCharactersAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync();

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Character, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults
                    .Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" })
                    .ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allCharacters = await _guideService.ExtractCharactersAsync(new List<string>());
            var fallbackMatched = allCharacters
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           c.Identity?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           c.FlawBelief?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(c => new { c.Id, c.Name, Display = $"{c.Name}({c.Id})", Brief = c.Identity ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配角色: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchLocationsAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync();

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Location, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allLocations = await _guideService.ExtractLocationsAsync(new List<string>());
            var fallbackMatched = allLocations
                .Where(l => l.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           l.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(l => new { l.Id, l.Name, Display = $"{l.Name}({l.Id})", Brief = l.Description ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配地点: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchFactionsAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync();

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.Faction, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allFactions = await _guideService.ExtractFactionsAsync(new List<string>());
            var fallbackMatched = allFactions
                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           f.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(f => new { f.Id, f.Name, Display = $"{f.Name}({f.Id})", Brief = f.Description ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配势力: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchWorldRulesAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync();

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.WorldRule, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allWorldRules = await _guideService.ExtractWorldRulesAsync(new List<string>());
            var fallbackMatched = allWorldRules
                .Where(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           w.OneLineSummary?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           w.PowerSystem?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(w => new { w.Id, w.Name, Display = $"{w.Name}({w.Id})", Brief = w.OneLineSummary ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配世界观规则: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchPlotRulesAsync(string query, int topK = 5)
        {
            await EnsureCacheAsync();

            var indexResults = _dataIndex.SearchByCategory(EntityCategory.PlotRule, query, topK);
            if (indexResults.Count > 0)
            {
                var matched = indexResults.Select(e => new { e.Id, e.Name, Display = $"{e.Name}({e.Id})", Brief = "" }).ToList();
                return JsonSerializer.Serialize(matched, JsonOptions);
            }

            var allPlotRules = await _guideService.ExtractPlotRulesAsync(new List<string>());
            var fallbackMatched = allPlotRules
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           p.Goal?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                           p.Conflict?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(topK)
                .Select(p => new { p.Id, p.Name, Display = $"{p.Name}({p.Id})", Brief = p.OneLineSummary ?? p.Goal ?? "" })
                .ToList();

            if (fallbackMatched.Count == 0)
                return $"[未找到] 无匹配剧情规则: {query}";
            return JsonSerializer.Serialize(fallbackMatched, JsonOptions);
        }

        public async Task<string> SearchContentAsync(string query, int topK = 5)
        {
            var results = await _vectorSearch.SearchAsync(query, topK);
            if (results.Count == 0)
                return $"[未找到] 无匹配正文内容: {query}";
            return JsonSerializer.Serialize(results, JsonOptions);
        }

        public async Task<string> FindRelatedChaptersAsync(string description)
        {
            var chapters = await _vectorSearch.FindRelatedChaptersAsync(description);
            if (chapters.Count == 0)
                return $"[未找到] 无相关章节: {description}";
            return JsonSerializer.Serialize(chapters, JsonOptions);
        }

        public async Task<string> SmartSearchAsync(string query)
        {
            await EnsureCacheAsync();
            var routeResult = _router.RouteWithDetails(query);

            return routeResult.Route switch
            {
                QueryRoute.Precise => await PreciseSearchAsync(routeResult),
                QueryRoute.Semantic => await SemanticSearchAsync(query),
                _ => await HybridSearchAsync(query, routeResult)
            };
        }

        private async Task<string> PreciseSearchAsync(QueryRouteResult routeResult)
        {
            await EnsureCacheAsync();
            var results = new List<object>();

            foreach (var chapterId in routeResult.ChapterIds)
            {
                var ctx = await GetExpandedChapterContextAsync(chapterId);
                if (!ctx.StartsWith("[未找到]"))
                    results.Add(new { Type = "chapter", Id = chapterId, Display = chapterId, Data = ctx });
            }

            foreach (var entityId in routeResult.EntityIds)
            {
                var display = ResolveDisplay(entityId);
                var character = await GetCharacterByIdAsync(entityId);
                if (!character.StartsWith("[未找到]"))
                {
                    results.Add(new { Type = "character", Id = entityId, Display = display, Data = character });
                    continue;
                }

                var location = await GetLocationByIdAsync(entityId);
                if (!location.StartsWith("[未找到]"))
                {
                    results.Add(new { Type = "location", Id = entityId, Display = display, Data = location });
                    continue;
                }

                var plotRule = await GetPlotRuleByIdAsync(entityId);
                if (!plotRule.StartsWith("[未找到]"))
                    results.Add(new { Type = "plotRule", Id = entityId, Display = display, Data = plotRule });
            }

            foreach (var name in routeResult.MatchedNames)
            {
                var characters = await SearchCharactersAsync(name, 1);
                if (!characters.StartsWith("[未找到]"))
                    results.Add(new { Type = "characterSearch", Query = name, Display = name, Data = characters });
            }

            if (results.Count == 0)
                return $"[未找到] 无匹配结果: {routeResult.Query}";

            return JsonSerializer.Serialize(results, JsonOptions);
        }

        private async Task<string> SemanticSearchAsync(string query)
        {
            return await SearchContentAsync(query, 5);
        }

        private async Task<string> HybridSearchAsync(string query, QueryRouteResult routeResult)
        {
            var preciseResults = await PreciseSearchAsync(routeResult);
            var semanticResults = await SemanticSearchAsync(query);

            return JsonSerializer.Serialize(new
            {
                Precise = preciseResults,
                Semantic = semanticResults
            }, JsonOptions);
        }
    }
}
