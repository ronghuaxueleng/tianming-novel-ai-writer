using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Models;

public class AlertConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = false;

    [JsonPropertyName("TriggerPolicy")] public TriggerPolicy TriggerPolicy { get; set; } = new();

    [JsonPropertyName("Email")] public EmailChannelConfig Email { get; set; } = new();
}

public class EmailChannelConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;

    [JsonPropertyName("SmtpHost")] public string SmtpHost { get; set; } = "smtp.qq.com";

    [JsonPropertyName("SmtpPort")] public int SmtpPort { get; set; } = 587;

    [JsonPropertyName("EnableSsl")] public bool EnableSsl { get; set; } = true;

    [JsonPropertyName("SenderEmail")] public string SenderEmail { get; set; } = string.Empty;

    [JsonPropertyName("SenderDisplayName")] public string SenderDisplayName { get; set; } = "天命AI告警";

    [JsonPropertyName("AuthCode")] public string AuthCode { get; set; } = string.Empty;

    [JsonPropertyName("Recipients")] public List<string> Recipients { get; set; } = new();
}
