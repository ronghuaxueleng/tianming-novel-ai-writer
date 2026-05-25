using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.SystemSettings.Proxy.ProxyTest
{
    public class TestStatistics
    {
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("MinLatency")] public double MinLatency { get; set; }
        [JsonPropertyName("MaxLatency")] public double MaxLatency { get; set; }
        [JsonPropertyName("AverageSpeed")] public double AverageSpeed { get; set; }
        [JsonPropertyName("TotalTests")] public int TotalTests { get; set; }
        [JsonPropertyName("SuccessfulTests")] public int SuccessfulTests { get; set; }
        [JsonPropertyName("FailedTests")] public int FailedTests { get; set; }
        public double SuccessRate => TotalTests > 0 ? (SuccessfulTests * 100.0 / TotalTests) : 0;
    }

    public class TestTrendAnalysis
    {
        [JsonPropertyName("LatencyTrend")] public List<TrendPoint> LatencyTrend { get; set; } = new();
        [JsonPropertyName("SpeedTrend")] public List<TrendPoint> SpeedTrend { get; set; } = new();
        [JsonPropertyName("TrendDirection")] public string TrendDirection { get; set; } = "稳定";
        [JsonPropertyName("Prediction")] public string Prediction { get; set; } = string.Empty;
    }

    public class TrendPoint
    {
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [JsonPropertyName("Value")] public double Value { get; set; }
    }

    public class TestComparison
    {
        [JsonPropertyName("Test1")] public Services.ProxyTestResult? Test1 { get; set; }
        [JsonPropertyName("Test2")] public Services.ProxyTestResult? Test2 { get; set; }
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("Differences")] public List<string> Differences { get; set; } = new();
    }

    public class ScheduledTest
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("Interval")] public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
        [JsonPropertyName("NextRunTime")] public DateTime NextRunTime { get; set; }
        [JsonPropertyName("LastRunTime")] public DateTime LastRunTime { get; set; }
    }

    public class BatchTestResult
    {
        [JsonPropertyName("StartTime")] public DateTime StartTime { get; set; } = DateTime.Now;
        [JsonPropertyName("EndTime")] public DateTime EndTime { get; set; }
        [JsonPropertyName("Results")] public List<Services.ProxyTestResult> Results { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        public int SuccessCount => Results.Count(r => r.IsConnected);
        public int FailCount => Results.Count - SuccessCount;
    }

    public class TestReport
    {
        [JsonPropertyName("GeneratedTime")] public DateTime GeneratedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("Statistics")] public TestStatistics Statistics { get; set; } = new();
        [JsonPropertyName("TrendAnalysis")] public TestTrendAnalysis TrendAnalysis { get; set; } = new();
        [JsonPropertyName("RecentTests")] public List<Services.ProxyTestResult> RecentTests { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("HealthScore")] public int HealthScore { get; set; }
    }

    public class ProxyTestSettings
    {
        [JsonPropertyName("ScheduledTests")] public List<ScheduledTest> ScheduledTests { get; set; } = new();
        [JsonPropertyName("LastUpdated")] public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}

