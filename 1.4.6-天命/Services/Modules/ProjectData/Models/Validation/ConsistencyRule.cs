using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class ConsistencyRule
    {
        [JsonPropertyName("RuleId")]
        public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("TargetId")]
        public string TargetId { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
    }
}
