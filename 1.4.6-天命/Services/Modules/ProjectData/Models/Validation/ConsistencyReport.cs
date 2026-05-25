using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class ConsistencyReport
    {
        [JsonPropertyName("GeneratedAt")]
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public bool Passed => Errors.Count == 0;

        [JsonPropertyName("Errors")]
        public List<ConsistencyError> Errors { get; set; } = new();

        [JsonPropertyName("Warnings")]
        public List<ConsistencyWarning> Warnings { get; set; } = new();

        [JsonPropertyName("Duplicates")]
        public List<DuplicateElement> Duplicates { get; set; } = new();
    }

    public class ConsistencyError
    {
        [JsonPropertyName("ErrorCode")]
        public string ErrorCode { get; set; } = string.Empty;
        [JsonPropertyName("EntityId")]
        public string EntityId { get; set; } = string.Empty;
        [JsonPropertyName("EntityType")]
        public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("Message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("Suggestion")]
        public string Suggestion { get; set; } = string.Empty;
    }

    public class ConsistencyWarning
    {
        [JsonPropertyName("WarningCode")]
        public string WarningCode { get; set; } = string.Empty;
        [JsonPropertyName("EntityId")]
        public string EntityId { get; set; } = string.Empty;
        [JsonPropertyName("EntityType")]
        public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("Message")]
        public string Message { get; set; } = string.Empty;
    }

    public class DuplicateElement
    {
        [JsonPropertyName("ElementType")]
        public string ElementType { get; set; } = string.Empty;
        [JsonPropertyName("ElementName")]
        public string ElementName { get; set; } = string.Empty;
        [JsonPropertyName("OccurrenceIds")]
        public List<string> OccurrenceIds { get; set; } = new();
        [JsonPropertyName("Suggestion")]
        public string Suggestion { get; set; } = string.Empty;
    }
}
