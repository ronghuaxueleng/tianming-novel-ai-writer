using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class GlobalSummary
    {

        [JsonPropertyName("StorySummary")] public string StorySummary { get; set; } = string.Empty;
        [JsonPropertyName("MainCharacters")] public List<CharacterDeepSummary> MainCharacters { get; set; } = new();
        [JsonPropertyName("CoreRules")] public List<IndexItem> CoreRules { get; set; } = new();
        [JsonPropertyName("CoreFactions")] public List<IndexItem> CoreFactions { get; set; } = new();
        [JsonPropertyName("MainConflict")] public string MainConflict { get; set; } = string.Empty;
        [JsonPropertyName("Progress")] public ProgressInfo Progress { get; set; } = new();
        [JsonPropertyName("Decisions")] public DecisionChain Decisions { get; set; } = new();
        [JsonPropertyName("UsedElements")] public UsedElementsIndex UsedElements { get; set; } = new();
        [JsonPropertyName("IsEmpty")] public bool IsEmpty { get; set; } = true;
        [JsonPropertyName("CompletedLayers")] public List<string> CompletedLayers { get; set; } = new();
    }

    public class ProgressInfo
    {
        [JsonPropertyName("TotalChapters")] public int TotalChapters { get; set; }
        [JsonPropertyName("CompletedChapters")] public int CompletedChapters { get; set; }
        [JsonPropertyName("CurrentPhase")] public string CurrentPhase { get; set; } = string.Empty;

        public string CompletionRate => TotalChapters > 0
            ? $"{CompletedChapters * 100 / TotalChapters}%"
            : "0%";
    }

    public class DecisionChain
    {
        [JsonPropertyName("CoreInspiration")] public string CoreInspiration { get; set; } = string.Empty;
        [JsonPropertyName("CoreWorldRules")] public List<RuleDecision> CoreWorldRules { get; set; } = new();
        [JsonPropertyName("CharacterDecisions")] public List<CharacterDecision> CharacterDecisions { get; set; } = new();
        [JsonPropertyName("PlotDecisions")] public List<PlotDecision> PlotDecisions { get; set; } = new();
    }

    public class RuleDecision
    {
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleName")] public string RuleName { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("Impact")] public string Impact { get; set; } = string.Empty;
    }

    public class CharacterDecision
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("CharacterName")] public string CharacterName { get; set; } = string.Empty;
        [JsonPropertyName("DesignReason")] public string DesignReason { get; set; } = string.Empty;
        [JsonPropertyName("RelatedRules")] public string RelatedRules { get; set; } = string.Empty;
    }

    public class PlotDecision
    {
        [JsonPropertyName("PlotPointId")] public string PlotPointId { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("ExpectedImpact")] public string ExpectedImpact { get; set; } = string.Empty;
    }

    public class UsedElementsIndex
    {
        [JsonPropertyName("UsedAbilities")] public List<string> UsedAbilities { get; set; } = new();
        [JsonPropertyName("UsedPlotPatterns")] public List<string> UsedPlotPatterns { get; set; } = new();
        [JsonPropertyName("PlantedForeshadowings")] public List<string> PlantedForeshadowings { get; set; } = new();
        [JsonPropertyName("ResolvedForeshadowings")] public List<string> ResolvedForeshadowings { get; set; } = new();
    }
}
