using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.SystemSettings.Proxy.ProxySetup
{
    public class ProxyConfigHistory
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("ConfigBefore")] public string ConfigBefore { get; set; } = string.Empty;
        [JsonPropertyName("ConfigAfter")] public string ConfigAfter { get; set; } = string.Empty;
        [JsonPropertyName("ModifyReason")] public string ModifyReason { get; set; } = string.Empty;
        [JsonPropertyName("ModifiedFields")] public string ModifiedFields { get; set; } = string.Empty;
        [JsonPropertyName("UserNote")] public string UserNote { get; set; } = string.Empty;
    }

    public class ProxyUsageStatistics
    {
        [JsonPropertyName("TotalEnableCount")] public int TotalEnableCount { get; set; }
        [JsonPropertyName("SuccessfulConnections")] public int SuccessfulConnections { get; set; }
        [JsonPropertyName("FailedConnections")] public int FailedConnections { get; set; }
        public double SuccessRate => TotalEnableCount > 0 ? (SuccessfulConnections * 100.0 / TotalEnableCount) : 0;
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("TotalTrafficBytes")] public long TotalTrafficBytes { get; set; }
        public string TotalTrafficFormatted => FormatBytes(TotalTrafficBytes);
        [JsonPropertyName("FirstUsedTime")] public DateTime FirstUsedTime { get; set; }
        [JsonPropertyName("LastUsedTime")] public DateTime LastUsedTime { get; set; }
        [JsonPropertyName("TypeUsageCount")] public Dictionary<string, int> TypeUsageCount { get; set; } = new();
        [JsonPropertyName("DailyStats")] public List<DailyUsage> DailyStats { get; set; } = new();

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class DailyUsage
    {
        [JsonPropertyName("Date")] public DateTime Date { get; set; }
        [JsonPropertyName("ConnectionCount")] public int ConnectionCount { get; set; }
        [JsonPropertyName("SuccessCount")] public int SuccessCount { get; set; }
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("TrafficBytes")] public long TrafficBytes { get; set; }
    }

    public class ProxyConfigPreset
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Icon")] public string Icon { get; set; } = "🌐";
        [JsonPropertyName("Config")] public Services.ProxyConfig Config { get; set; } = new();
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("LastUsedTime")] public DateTime LastUsedTime { get; set; }
        [JsonPropertyName("UsageCount")] public int UsageCount { get; set; }
        [JsonPropertyName("IsFavorite")] public bool IsFavorite { get; set; }
    }

    public class ProxyConfigComparison
    {
        [JsonPropertyName("Config1Name")] public string Config1Name { get; set; } = string.Empty;
        [JsonPropertyName("Config2Name")] public string Config2Name { get; set; } = string.Empty;
        [JsonPropertyName("Differences")] public List<ConfigDifference> Differences { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
    }

    public class ConfigDifference
    {
        [JsonPropertyName("FieldName")] public string FieldName { get; set; } = string.Empty;
        [JsonPropertyName("Config1Value")] public string Config1Value { get; set; } = string.Empty;
        [JsonPropertyName("Config2Value")] public string Config2Value { get; set; } = string.Empty;
        [JsonPropertyName("Level")] public DifferenceLevel Level { get; set; }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum DifferenceLevel
    {
        Minor,
        Moderate,
        Major
    }

    public class ProxyRecommendation
    {
        [JsonPropertyName("RecommendationType")] public string RecommendationType { get; set; } = string.Empty;
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("SuggestedConfig")] public Services.ProxyConfig SuggestedConfig { get; set; } = new();
        [JsonPropertyName("Priority")] public int Priority { get; set; }
        [JsonPropertyName("Benefits")] public List<string> Benefits { get; set; } = new();
    }

    public class ProxyConfigReport
    {
        [JsonPropertyName("GeneratedTime")] public DateTime GeneratedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("CurrentConfig")] public Services.ProxyConfig CurrentConfig { get; set; } = new();
        [JsonPropertyName("Statistics")] public ProxyUsageStatistics Statistics { get; set; } = new();
        [JsonPropertyName("RecentHistory")] public List<ProxyConfigHistory> RecentHistory { get; set; } = new();
        [JsonPropertyName("Recommendations")] public List<ProxyRecommendation> Recommendations { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("HealthIssues")] public List<string> HealthIssues { get; set; } = new();
        [JsonPropertyName("HealthScore")] public int HealthScore { get; set; }
    }

    public class ProxySetupSettings
    {
        [JsonPropertyName("CurrentConfig")] public Services.ProxyConfig CurrentConfig { get; set; } = new();
        [JsonPropertyName("History")] public List<ProxyConfigHistory> History { get; set; } = new();
        [JsonPropertyName("Statistics")] public ProxyUsageStatistics Statistics { get; set; } = new();
        [JsonPropertyName("Presets")] public List<ProxyConfigPreset> Presets { get; set; } = new();
        [JsonPropertyName("LastUpdated")] public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}

