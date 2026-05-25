using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

public class UserConfigurationData : IDataItem
{
    [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Icon")] public string Icon { get; set; } = "Icon.Robot";
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
    [JsonPropertyName("MaxTokens")] public int MaxTokens { get; set; } = 0;
    [JsonPropertyName("TopP")] public double TopP { get; set; } = 1.0;
    [JsonPropertyName("FrequencyPenalty")] public double FrequencyPenalty { get; set; } = 0.1;
    [JsonPropertyName("PresencePenalty")] public double PresencePenalty { get; set; } = 0.0;
    [JsonPropertyName("BatchTier")] public string BatchTier { get; set; } = "64K";
    [JsonPropertyName("RateLimitRPM")] public int RateLimitRPM { get; set; } = 0;
    [JsonPropertyName("RateLimitTPM")] public int RateLimitTPM { get; set; } = 0;
    [JsonPropertyName("MaxConcurrency")] public int MaxConcurrency { get; set; } = 5;
    [JsonPropertyName("Seed")] public string Seed { get; set; } = string.Empty;
    [JsonPropertyName("StopSequences")] public string StopSequences { get; set; } = string.Empty;

    [JsonPropertyName("RetryCount")] public int RetryCount { get; set; } = 3;
    [JsonPropertyName("TimeoutSeconds")] public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("AutoDisabledBySystem")] public bool AutoDisabledBySystem { get; set; }

    [JsonPropertyName("ThinkingEnabled")] public bool? ThinkingEnabled { get; set; }
    [JsonPropertyName("ReasoningEffort")] public string ReasoningEffort { get; set; } = string.Empty;
    [JsonPropertyName("EnableLongContext")] public bool? EnableLongContext { get; set; }

    [JsonPropertyName("SupportsReasoningEffort")] public bool SupportsReasoningEffort { get; set; }

    [JsonPropertyName("SupportedEffortLevels")] public List<string>? SupportedEffortLevels { get; set; }
    [JsonPropertyName("SupportsThinking")] public bool SupportsThinking { get; set; }
    [JsonPropertyName("SupportsLongContext")] public bool SupportsLongContext { get; set; }
    [JsonPropertyName("LongContextWindow")] public int LongContextWindow { get; set; } = 1_000_000;
    [JsonPropertyName("ThinkingPassthrough")] public bool? ThinkingPassthrough { get; set; }
    [JsonPropertyName("SupportsVision")] public bool SupportsVision { get; set; }
    [JsonPropertyName("SupportsImageGeneration")] public bool SupportsImageGeneration { get; set; }
    [JsonPropertyName("SupportsTools")] public bool SupportsTools { get; set; }
    [JsonPropertyName("SupportsStreaming")] public bool SupportsStreaming { get; set; }
    [JsonPropertyName("CapabilitiesDetected")] public bool CapabilitiesDetected { get; set; }
}
