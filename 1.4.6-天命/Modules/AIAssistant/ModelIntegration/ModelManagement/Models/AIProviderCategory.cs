using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
using TM.Services.Framework.AI.Core;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

public class AIProviderCategory : ICategory, ILogoPathHost
{
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Icon")] public string Icon { get; set; } = string.Empty;
    [JsonPropertyName("ParentCategory")] public string? ParentCategory { get; set; }
    [JsonPropertyName("Level")] public int Level { get; set; } = 1;
    [JsonPropertyName("Order")] public int Order { get; set; } = 0;
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = false;
    [JsonPropertyName("IsBuiltIn")] public bool IsBuiltIn { get; set; } = false;

    [JsonPropertyName("LogoPath")] public string? LogoPath { get; set; }
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("ApiEndpoint")] public string? ApiEndpoint { get; set; }
    [JsonPropertyName("ApiKeys")] public List<ApiKeyEntry>? ApiKeys { get; set; }

    [JsonIgnore]
    public string? ApiKey
    {
        get => ApiKeys?.Find(k => k.IsEnabled && !string.IsNullOrWhiteSpace(k.Key))?.Key;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (ApiKeys == null || ApiKeys.Count == 0)
            {
                ApiKeys = new List<ApiKeyEntry>
                {
                    new() { Id = ShortIdGenerator.New("K"), Key = value, Remark = "默认", IsEnabled = true }
                };
            }
            else
            {
                ApiKeys[0].Key = value;
            }
        }
    }
    [JsonPropertyName("ModelsEndpoint")] public string? ModelsEndpoint { get; set; }
    [JsonPropertyName("ChatEndpoint")] public string? ChatEndpoint { get; set; }
    [JsonPropertyName("EndpointVerifiedAt")] public DateTime? EndpointVerifiedAt { get; set; }
    [JsonPropertyName("EndpointSignature")] public string? EndpointSignature { get; set; }
    [JsonPropertyName("RequiresApiKey")] public bool RequiresApiKey { get; set; }
    [JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }

}
