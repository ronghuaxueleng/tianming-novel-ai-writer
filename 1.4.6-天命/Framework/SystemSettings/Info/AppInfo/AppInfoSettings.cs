using System;

namespace TM.Framework.SystemSettings.Info.AppInfo
{
    public class AppInfoSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("UpdateServerUrl")] public string UpdateServerUrl { get; set; } = "https://update.example.com";
        [System.Text.Json.Serialization.JsonPropertyName("AutoCheckUpdate")] public bool AutoCheckUpdate { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("UpdateCheckIntervalDays")] public int UpdateCheckIntervalDays { get; set; } = 7;
        [System.Text.Json.Serialization.JsonPropertyName("LastUpdateCheckTime")] public DateTime LastUpdateCheckTime { get; set; } = DateTime.MinValue;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentVersion")] public string CurrentVersion { get; set; } = "1.0.0.0";
    }
}

