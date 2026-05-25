using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Validation
{
    public class ValidationReport
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("ChapterTitle")] public string ChapterTitle { get; set; } = string.Empty;
        [JsonPropertyName("ValidatedTime")] public DateTime ValidatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("Result")] public ValidationResult Result { get; set; } = ValidationResult.Pending;
        [JsonPropertyName("Items")] public List<ValidationItem> Items { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("DependencyModuleVersions")] public Dictionary<string, int> DependencyModuleVersions { get; set; } = new();

        public int PassCount => Items.FindAll(i => i.Result == ValidationItemResult.Pass).Count;

        public int WarningCount => Items.FindAll(i => i.Result == ValidationItemResult.Warning).Count;

        public int ErrorCount => Items.FindAll(i => i.Result == ValidationItemResult.Error).Count;
    }

    public enum ValidationResult
    {
        Pending,

        Pass,

        Warning,

        Error
    }

    public class ValidationItem
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("ValidationType")] public string ValidationType { get; set; } = string.Empty;
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Result")] public ValidationItemResult Result { get; set; } = ValidationItemResult.Pending;
        [JsonPropertyName("Details")] public string Details { get; set; } = string.Empty;
        [JsonPropertyName("Suggestion")] public string Suggestion { get; set; } = string.Empty;
        [JsonPropertyName("Location")] public string Location { get; set; } = string.Empty;
    }

    public enum ValidationItemResult
    {
        Pending,

        Pass,

        Warning,

        Error,

        Skipped
    }
}
