using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class ValidationIssue
    {
        [JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("Severity")] public string Severity { get; set; } = string.Empty;
        [JsonPropertyName("Message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("Location")] public string Location { get; set; } = string.Empty;
        [JsonPropertyName("Suggestion")] public string Suggestion { get; set; } = string.Empty;
    }
}
