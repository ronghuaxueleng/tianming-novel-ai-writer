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
        get
        {
            var raw = ApiKeys?.Find(k => k.IsEnabled && !string.IsNullOrWhiteSpace(k.Key))?.Key;
            return raw == null ? null : LocalKeyProtector.TryUnprotect(raw);
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (ApiKeys != null && ApiKeys.Count == 1)
                {
                    ApiKeys[0].Key = string.Empty;
                    ApiKeys[0].IsEnabled = false;
                }
                return;
            }
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
                ApiKeys[0].IsEnabled = true;
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
    [JsonPropertyName("IsKeyExhausted")] public bool IsKeyExhausted { get; set; }

}

public static class AIProviderCategoryLogHelper
{
    public static bool IsTianmingPrivate(this AIProviderCategory? category)
        => IsTianmingPrivateId(category?.Id);

    public static bool IsTianmingPrivateId(string? id)
        => TianmingProviderIdentity.IsTianmingPrivate(id);

    public static void LogIfPublic(this AIProviderCategory? category, string message)
    {
        if (category.IsTianmingPrivate()) return;
        TM.App.Log(message);
    }
}
