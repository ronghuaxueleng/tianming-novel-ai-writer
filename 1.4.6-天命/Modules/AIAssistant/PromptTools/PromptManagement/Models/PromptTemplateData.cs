using System;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;

public class PromptTemplateData : IDataItem
{
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Icon")] public string Icon { get; set; } = "";
    [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = false;
    [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
    [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;

    [JsonPropertyName("SystemPrompt")] public string SystemPrompt { get; set; } = string.Empty;

    [JsonPropertyName("UserTemplate")] public string UserTemplate { get; set; } = string.Empty;

    [JsonPropertyName("Variables")] public string Variables { get; set; } = string.Empty;

    [JsonPropertyName("Tags")] public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;

    [JsonPropertyName("IsBuiltIn")] public bool IsBuiltIn { get; set; } = false;

    [JsonPropertyName("IsDefault")] public bool IsDefault { get; set; } = false;
}
