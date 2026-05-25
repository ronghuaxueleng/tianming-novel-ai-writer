using System;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

public class UserConfigurationData : IDataItem
{
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Icon")] public string Icon { get; set; } = "🤖";
    [JsonPropertyName("Category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("CategoryId")] public string CategoryId { get; set; } = string.Empty;
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
    [JsonPropertyName("ModifiedTime")] public DateTime ModifiedTime { get; set; } = DateTime.Now;

    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("ModelName")] public string ModelName { get; set; } = string.Empty;
    [JsonPropertyName("ApiEndpoint")] public string ApiEndpoint { get; set; } = string.Empty;
    [JsonIgnore] public string ApiKey { get; set; } = string.Empty;
    [JsonPropertyName("IsActive")] public bool IsActive { get; set; }

    [JsonPropertyName("ProviderName")] public string ProviderName { get; set; } = string.Empty;
    [JsonPropertyName("ModelVersion")] public string ModelVersion { get; set; } = string.Empty;
    [JsonPropertyName("ContextLength")] public string ContextLength { get; set; } = string.Empty;
    [JsonPropertyName("TrainingDataCutoff")] public string TrainingDataCutoff { get; set; } = string.Empty;
    [JsonPropertyName("InputPrice")] public string InputPrice { get; set; } = string.Empty;
    [JsonPropertyName("OutputPrice")] public string OutputPrice { get; set; } = string.Empty;
    [JsonPropertyName("SupportedFeatures")] public string SupportedFeatures { get; set; } = string.Empty;

    [JsonPropertyName("Temperature")] public double Temperature { get; set; } = 0.7;
    [JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 4096;
    [JsonPropertyName("TopP")] public double TopP { get; set; } = 1.0;
    [JsonPropertyName("FrequencyPenalty")] public double FrequencyPenalty { get; set; } = 0.0;
    [JsonPropertyName("PresencePenalty")] public double PresencePenalty { get; set; } = 0.0;
    [JsonPropertyName("RateLimitRPM")] public int RateLimitRPM { get; set; } = 0;
    [JsonPropertyName("RateLimitTPM")] public int RateLimitTPM { get; set; } = 0;
    [JsonPropertyName("MaxConcurrency")] public int MaxConcurrency { get; set; } = 5;
    [JsonPropertyName("Seed")] public string Seed { get; set; } = string.Empty;
    [JsonPropertyName("StopSequences")] public string StopSequences { get; set; } = string.Empty;

    [JsonPropertyName("RetryCount")] public int RetryCount { get; set; } = 3;
    [JsonPropertyName("TimeoutSeconds")] public int TimeoutSeconds { get; set; } = 30;
    [JsonPropertyName("EnableStreaming")] public bool EnableStreaming { get; set; } = true;
}
