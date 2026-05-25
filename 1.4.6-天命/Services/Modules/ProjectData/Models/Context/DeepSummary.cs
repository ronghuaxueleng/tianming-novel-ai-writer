using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class CharacterDeepSummary
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Role")] public string Role { get; set; } = string.Empty;

        [JsonPropertyName("Type")] public string Type 
        { 
            get => Role; 
            set => Role = value; 
        }

        [JsonPropertyName("CoreTraits")] public string CoreTraits { get; set; } = string.Empty;

        [JsonPropertyName("AbilityOrigin")] public string AbilityOrigin { get; set; } = string.Empty;

        [JsonPropertyName("KeyConstraint")] public string KeyConstraint { get; set; } = string.Empty;

        [JsonPropertyName("KeyRelationships")] public string KeyRelationships { get; set; } = string.Empty;

        public string BriefSummary => $"{Name}({Role})";

        public string DeepSummary => ToSummaryText();

        public string ToSummaryText()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(CoreTraits))
                parts.Add(CoreTraits);
            if (!string.IsNullOrEmpty(AbilityOrigin))
                parts.Add(AbilityOrigin);
            if (!string.IsNullOrEmpty(KeyConstraint))
                parts.Add(KeyConstraint);
            return string.Join("。", parts);
        }
    }

    public class LocationDeepSummary
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;

        [JsonPropertyName("Climate")] public string Climate { get; set; } = string.Empty;

        [JsonPropertyName("KeyFeature")] public string KeyFeature { get; set; } = string.Empty;

        [JsonPropertyName("StoryRole")] public string StoryRole { get; set; } = string.Empty;

        public string ToSummaryText()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Climate))
                parts.Add(Climate);
            if (!string.IsNullOrEmpty(KeyFeature))
                parts.Add(KeyFeature);
            if (!string.IsNullOrEmpty(StoryRole))
                parts.Add($"作用：{StoryRole}");
            return string.Join("。", parts);
        }
    }

    public class ConflictDeepSummary
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("ConflictType")] public string ConflictType { get; set; } = string.Empty;

        [JsonPropertyName("CoreTension")] public string CoreTension { get; set; } = string.Empty;

        [JsonPropertyName("Stakes")] public string Stakes { get; set; } = string.Empty;

        [JsonPropertyName("Resolution")] public string Resolution { get; set; } = string.Empty;

        public string ToSummaryText()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(CoreTension))
                parts.Add(CoreTension);
            if (!string.IsNullOrEmpty(Stakes))
                parts.Add($"利害：{Stakes}");
            if (!string.IsNullOrEmpty(Resolution))
                parts.Add($"解决：{Resolution}");
            return string.Join("。", parts);
        }
    }
}
