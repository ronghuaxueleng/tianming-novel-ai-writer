using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Plot;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class ExpandedContext
    {
        [System.Text.Json.Serialization.JsonPropertyName("BaseContext")] public DesignFocusContext BaseContext { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ExpansionReason")] public string ExpansionReason { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FullCharacters")] public List<CharacterRulesData> FullCharacters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("FullPlotRules")] public List<PlotRulesData> FullPlotRules { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ImplicitRelations")] public ImplicitRelations ImplicitRelations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("EstimatedTokens")] public int EstimatedTokens { get; set; }

        public bool IsExpanded => FullCharacters.Count > 0 || FullPlotRules.Count > 0;

        public IEnumerable<CharacterRulesData> GetFullCharacterProfiles()
        {
            return FullCharacters;
        }
    }

    public class ImplicitRelations
    {
        [System.Text.Json.Serialization.JsonPropertyName("Relations")] public List<ImplicitRelation> Relations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("AnalyzedAt")] public System.DateTime AnalyzedAt { get; set; } = System.DateTime.UtcNow;
    }

    public class ImplicitRelation
    {
        [System.Text.Json.Serialization.JsonPropertyName("Entity1Id")] public string Entity1Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Entity2Id")] public string Entity2Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("RelationType")] public string RelationType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Confidence")] public double Confidence { get; set; }
    }
}
