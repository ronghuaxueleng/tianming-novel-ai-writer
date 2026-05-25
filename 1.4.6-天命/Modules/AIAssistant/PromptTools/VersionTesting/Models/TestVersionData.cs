using System;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;

public class TestVersionData : IDataItem
{
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("PromptId")] public string PromptId { get; set; } = string.Empty;
    [JsonPropertyName("VersionNumber")] public string VersionNumber { get; set; } = "1.0";
    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("TestInput")] public string TestInput { get; set; } = string.Empty;
    [JsonPropertyName("ExpectedOutput")] public string ExpectedOutput { get; set; } = string.Empty;
    [JsonPropertyName("TestScenario")] public string TestScenario { get; set; } = string.Empty;

    [JsonPropertyName("ActualOutput")] public string ActualOutput { get; set; } = string.Empty;
    [JsonPropertyName("Rating")] public int Rating { get; set; } = 0;
    [JsonPropertyName("TestNotes")] public string TestNotes { get; set; } = string.Empty;
    [JsonPropertyName("TestStatus")] public string TestStatus { get; set; } = "未测试";
    [JsonPropertyName("TestTime")] public DateTime? TestTime { get; set; }

    [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
    [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;
}
