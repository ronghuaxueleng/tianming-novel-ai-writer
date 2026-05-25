using System;

namespace TM.Framework.SystemSettings.Info.DiagnosticInfo
{
    public class DiagnosticInfoSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("RefreshIntervalSeconds")] public int RefreshIntervalSeconds { get; set; } = 3;
        [System.Text.Json.Serialization.JsonPropertyName("EnableAutoRefresh")] public bool EnableAutoRefresh { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("DiskSpaceWarningThresholdPercent")] public int DiskSpaceWarningThresholdPercent { get; set; } = 90;
        [System.Text.Json.Serialization.JsonPropertyName("MemoryWarningThresholdPercent")] public int MemoryWarningThresholdPercent { get; set; } = 85;
        [System.Text.Json.Serialization.JsonPropertyName("CPUWarningThresholdPercent")] public int CPUWarningThresholdPercent { get; set; } = 90;
        [System.Text.Json.Serialization.JsonPropertyName("ReportTemplatePath")] public string ReportTemplatePath { get; set; } = "";
    }
}

