using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class QualityRule
    {
        [JsonPropertyName("RuleId")]
        public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Threshold")]
        public string Threshold { get; set; } = string.Empty;
    }
}
