using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class ChapterValidationResult
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        public bool IsValid => !Issues.Any(i => i.Severity == "Error");
        [JsonPropertyName("Issues")]
        public List<ValidationIssue> Issues { get; set; } = new();

        public int ErrorCount => Issues.Count(i => i.Severity == "Error");
        public int WarningCount => Issues.Count(i => i.Severity == "Warning");
        public int InfoCount => Issues.Count(i => i.Severity == "Info");
    }
}
