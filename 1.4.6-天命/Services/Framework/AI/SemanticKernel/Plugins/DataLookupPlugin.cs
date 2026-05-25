using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.QueryRouting;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true)]
    public class DataLookupPlugin
    {
        private QueryRoutingService RoutingService => ServiceLocator.Get<QueryRoutingService>();

        [KernelFunction("GetCharacterById")]
        [Description("根据角色ID获取完整角色信息")]
        public async Task<string> GetCharacterByIdAsync(
            [Description("角色ID（GUID格式）")] string characterId)
        {
            return await RoutingService.GetCharacterByIdAsync(characterId);
        }

        [KernelFunction("GetCharactersByIds")]
        [Description("批量获取多个角色的完整信息")]
        public async Task<string> GetCharactersByIdsAsync(
            [Description("角色ID列表，逗号分隔")] string characterIds)
        {
            return await RoutingService.GetCharactersByIdsAsync(characterIds);
        }

        [KernelFunction("GetLocationById")]
        [Description("根据地点ID获取完整地点信息")]
        public async Task<string> GetLocationByIdAsync(
            [Description("地点ID（GUID格式）")] string locationId)
        {
            return await RoutingService.GetLocationByIdAsync(locationId);
        }

        [KernelFunction("GetFactionById")]
        [Description("根据势力ID获取完整势力信息")]
        public async Task<string> GetFactionByIdAsync(
            [Description("势力ID（GUID格式）")] string factionId)
        {
            return await RoutingService.GetFactionByIdAsync(factionId);
        }

        [KernelFunction("GetPlotRuleById")]
        [Description("根据剧情规则ID获取完整剧情规则信息")]
        public async Task<string> GetPlotRuleByIdAsync(
            [Description("剧情规则ID（GUID格式）")] string plotRuleId)
        {
            return await RoutingService.GetPlotRuleByIdAsync(plotRuleId);
        }

        [KernelFunction("GetWorldRuleById")]
        [Description("根据世界观规则ID获取完整世界观信息")]
        public async Task<string> GetWorldRuleByIdAsync(
            [Description("世界观规则ID（GUID格式）")] string worldRuleId)
        {
            return await RoutingService.GetWorldRuleByIdAsync(worldRuleId);
        }

        [KernelFunction("GetExpandedChapterContext")]
        [Description("获取已展开的章节上下文（生成用，包含角色/地点/剧情规则详情）")]
        public async Task<string> GetExpandedChapterContextAsync(
            [Description("章节ID（如vol1_ch5）")] string chapterId)
        {
            return await RoutingService.GetExpandedChapterContextAsync(chapterId);
        }

        [KernelFunction("GetChapterContext")]
        [Description("获取章节上下文（轻量版，仅返回索引信息）")]
        public async Task<string> GetChapterContextAsync(
            [Description("章节ID（如vol1_ch5）")] string chapterId)
        {
            return await RoutingService.GetChapterContextAsync(chapterId);
        }

        [KernelFunction("GetLocationsByIds")]
        [Description("批量获取多个地点的完整信息")]
        public async Task<string> GetLocationsByIdsAsync(
            [Description("地点ID列表，逗号分隔")] string locationIds)
        {
            return await RoutingService.GetLocationsByIdsAsync(locationIds);
        }

        [KernelFunction("GetFactionsByIds")]
        [Description("批量获取多个势力的完整信息")]
        public async Task<string> GetFactionsByIdsAsync(
            [Description("势力ID列表，逗号分隔")] string factionIds)
        {
            return await RoutingService.GetFactionsByIdsAsync(factionIds);
        }

        [KernelFunction("GetPlotRulesByIds")]
        [Description("批量获取多个剧情规则的完整信息")]
        public async Task<string> GetPlotRulesByIdsAsync(
            [Description("剧情规则ID列表，逗号分隔")] string plotRuleIds)
        {
            return await RoutingService.GetPlotRulesByIdsAsync(plotRuleIds);
        }

        [KernelFunction("GetWorldRulesByIds")]
        [Description("批量获取多个世界观规则的完整信息")]
        public async Task<string> GetWorldRulesByIdsAsync(
            [Description("世界观规则ID列表，逗号分隔")] string worldRuleIds)
        {
            return await RoutingService.GetWorldRulesByIdsAsync(worldRuleIds);
        }

        [KernelFunction("ListAvailableIds")]
        [Description("列出某类别所有可用ID")]
        public async Task<string> ListAvailableIdsAsync(
            [Description("类别: characters/locations/factions/plotrules/worldrules")] string category)
        {
            return await RoutingService.ListAvailableIdsAsync(category);
        }

        [KernelFunction("ValidateDataConsistency")]
        [Description("检查打包数据是否与原始数据一致")]
        public async Task<string> ValidateDataConsistencyAsync()
        {
            return await RoutingService.ValidateDataConsistencyAsync();
        }

        [KernelFunction("SearchCharacters")]
        [Description("搜索角色，返回匹配的角色ID和名称列表")]
        public async Task<string> SearchCharactersAsync(
            [Description("搜索关键词（角色名/身份/描述）")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            return await RoutingService.SearchCharactersAsync(query, topK);
        }

        [KernelFunction("SearchLocations")]
        [Description("搜索地点，返回匹配的地点ID和名称列表")]
        public async Task<string> SearchLocationsAsync(
            [Description("搜索关键词（地点名/描述）")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            return await RoutingService.SearchLocationsAsync(query, topK);
        }

        [KernelFunction("SearchFactions")]
        [Description("搜索势力，返回匹配的势力ID和名称列表")]
        public async Task<string> SearchFactionsAsync(
            [Description("搜索关键词（势力名/描述）")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            return await RoutingService.SearchFactionsAsync(query, topK);
        }

        [KernelFunction("SearchWorldRules")]
        [Description("搜索世界观规则，返回匹配的规则ID和名称列表")]
        public async Task<string> SearchWorldRulesAsync(
            [Description("搜索关键词（规则名/描述）")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            return await RoutingService.SearchWorldRulesAsync(query, topK);
        }

        [KernelFunction("SearchPlotRules")]
        [Description("搜索剧情规则，返回匹配的规则ID和名称列表")]
        public async Task<string> SearchPlotRulesAsync(
            [Description("搜索关键词（规则名/目标/冲突）")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            return await RoutingService.SearchPlotRulesAsync(query, topK);
        }

        [KernelFunction("SearchContent")]
        [Description("在百万字正文中语义搜索相关内容")]
        public async Task<string> SearchContentAsync(
            [Description("搜索内容描述")] string query,
            [Description("返回结果数量，默认5")] int topK = 5)
        {
            return await RoutingService.SearchContentAsync(query, topK);
        }

        [KernelFunction("FindRelatedChapters")]
        [Description("查找与指定内容相关的章节")]
        public async Task<string> FindRelatedChaptersAsync(
            [Description("内容描述")] string description)
        {
            return await RoutingService.FindRelatedChaptersAsync(description);
        }

        [KernelFunction("SmartSearch")]
        [Description("智能搜索：自动选择精准或语义检索")]
        public async Task<string> SmartSearchAsync(
            [Description("查询内容")] string query)
        {
            return await RoutingService.SmartSearchAsync(query);
        }
    }
}
