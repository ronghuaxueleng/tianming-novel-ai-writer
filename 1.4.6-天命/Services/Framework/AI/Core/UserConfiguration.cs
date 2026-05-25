using System;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.AI.Core;

public class UserConfiguration
{
    [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("ProviderId")] public string ProviderId { get; set; } = string.Empty;
    [JsonPropertyName("ModelId")] public string ModelId { get; set; } = string.Empty;
    [JsonIgnore] public string ApiKey { get; set; } = string.Empty;
    [JsonPropertyName("CustomEndpoint")] public string? CustomEndpoint { get; set; }
    [JsonPropertyName("Temperature")] public double Temperature { get; set; } = 0.7;
    [JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 4096;
    [JsonPropertyName("ContextWindow")] public int ContextWindow { get; set; }
    [JsonPropertyName("IsActive")] public bool IsActive { get; set; }
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("DeveloperMessage")] public string? DeveloperMessage { get; set; }

    public string GetDisplayName()
    {
        return string.IsNullOrEmpty(Name) ? $"{ProviderId}/{ModelId}" : Name;
    }
}
