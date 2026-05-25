using System.Text.Json.Serialization;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Models;

public class TriggerPolicy
{
    [JsonPropertyName("OnAnyError")] public bool OnAnyError { get; set; } = false;

    [JsonPropertyName("OnConsecutiveFailures")] public bool OnConsecutiveFailures { get; set; } = true;

    [JsonPropertyName("ConsecutiveFailureThreshold")] public int ConsecutiveFailureThreshold { get; set; } = 3;

    [JsonPropertyName("OnTaskAborted")] public bool OnTaskAborted { get; set; } = true;

    [JsonPropertyName("CooldownMinutes")] public int CooldownMinutes { get; set; } = 5;
}
