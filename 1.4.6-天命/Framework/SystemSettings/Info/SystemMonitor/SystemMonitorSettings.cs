using System;

namespace TM.Framework.SystemSettings.Info.SystemMonitor
{
    public class SystemMonitorSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("LastRefreshTime")] public DateTime LastRefreshTime { get; set; } = DateTime.Now;
    }
}

