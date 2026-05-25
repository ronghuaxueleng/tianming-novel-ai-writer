using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models
{
    public class ProjectContext
    {
        [JsonPropertyName("ProjectId")]
        public string ProjectId { get; set; } = string.Empty;
        [JsonPropertyName("ProjectName")]
        public string ProjectName { get; set; } = string.Empty;
        [JsonPropertyName("CreatedTime")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("LastModified")]
        public DateTime LastModified { get; set; } = DateTime.Now;

        public Dictionary<string, ModuleDataInfo> ModulesData { get; set; } = new();

        public Dictionary<string, object> Metadata { get; set; } = new();

        [JsonPropertyName("Version")]
        public VersionInfo Version { get; set; } = new();
    }

    public class ModuleDataInfo
    {
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;
        [JsonPropertyName("Version")]
        public int Version { get; set; } = 1;
        [JsonPropertyName("LastModified")]
        public DateTime LastModified { get; set; } = DateTime.Now;
        public Dictionary<string, object> Data { get; set; } = new();
        public Dictionary<string, int> Dependencies { get; set; } = new();
    }

    public class VersionInfo
    {
        [JsonPropertyName("MajorVersion")]
        public int MajorVersion { get; set; } = 1;
        [JsonPropertyName("MinorVersion")]
        public int MinorVersion { get; set; } = 0;
        [JsonPropertyName("BuildVersion")]
        public int BuildVersion { get; set; } = 0;
        public string VersionString => $"{MajorVersion}.{MinorVersion}.{BuildVersion}";

        [JsonPropertyName("CreatedTime")]

        public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("CreatedBy")]
        public string CreatedBy { get; set; } = "System";
        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
    }

    public class AIContextData
    {
        [JsonPropertyName("ContextType")]
        public string ContextType { get; set; } = string.Empty;
        public Dictionary<string, object> DesignData { get; set; } = new();
        public Dictionary<string, object> GenerateData { get; set; } = new();
        public Dictionary<string, object> ValidateData { get; set; } = new();

        public Dictionary<string, int> DataPriority { get; set; } = new();

        [JsonPropertyName("FilterRules")]
        public List<DataFilterRule> FilterRules { get; set; } = new();
    }

    public class DataFilterRule
    {
        [JsonPropertyName("RuleType")]
        public string RuleType { get; set; } = string.Empty;
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;
        [JsonPropertyName("FieldPath")]
        public string FieldPath { get; set; } = string.Empty;
        [JsonPropertyName("Operation")]
        public FilterOperation Operation { get; set; } = FilterOperation.Include;
        [JsonPropertyName("FilterValue")]
        public object? FilterValue { get; set; }
    }

    public enum FilterOperation
    {
        Include,
        Exclude,
        Transform,
        Aggregate
    }
}
