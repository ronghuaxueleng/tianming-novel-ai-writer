using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class IdReferenceReport
    {
        [JsonPropertyName("GeneratedAt")]
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public bool Passed => DanglingReferences.Count == 0 && DuplicateIds.Count == 0;

        [JsonPropertyName("DanglingReferences")]
        public List<DanglingReference> DanglingReferences { get; set; } = new();

        [JsonPropertyName("DuplicateIds")]
        public List<DuplicateId> DuplicateIds { get; set; } = new();

        [JsonPropertyName("CircularReferences")]
        public List<CircularReference> CircularReferences { get; set; } = new();

        [JsonPropertyName("TotalEntitiesScanned")]
        public int TotalEntitiesScanned { get; set; }

        [JsonPropertyName("TotalReferencesChecked")]
        public int TotalReferencesChecked { get; set; }

        public string Summary => Passed 
            ? $"校验通过：扫描 {TotalEntitiesScanned} 个实体，{TotalReferencesChecked} 个引用，无问题" 
            : $"校验失败：{DanglingReferences.Count} 个悬空引用，{DuplicateIds.Count} 个重复ID";
    }

    public class DanglingReference
    {
        [JsonPropertyName("SourceId")]
        public string SourceId { get; set; } = string.Empty;

        [JsonPropertyName("SourceName")]
        public string SourceName { get; set; } = string.Empty;

        [JsonPropertyName("SourceType")]
        public string SourceType { get; set; } = string.Empty;

        [JsonPropertyName("ReferenceField")]
        public string ReferenceField { get; set; } = string.Empty;

        [JsonPropertyName("TargetId")]
        public string TargetId { get; set; } = string.Empty;

        [JsonPropertyName("ExpectedTargetType")]
        public string ExpectedTargetType { get; set; } = string.Empty;

        [JsonPropertyName("Suggestion")]
        public string Suggestion { get; set; } = string.Empty;
    }

    public class DuplicateId
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("EntityType")]
        public string EntityType { get; set; } = string.Empty;

        [JsonPropertyName("EntityNames")]
        public List<string> EntityNames { get; set; } = new();

        [JsonPropertyName("Suggestion")]
        public string Suggestion { get; set; } = "请为重复的实体分配唯一 ID";
    }

    public class CircularReference
    {
        [JsonPropertyName("Path")]
        public List<string> Path { get; set; } = new();

        [JsonPropertyName("EntityType")]
        public string EntityType { get; set; } = string.Empty;

        public string Description => $"循环引用：{string.Join(" → ", Path)}";
    }
}
