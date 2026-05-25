using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyChain
{
    public class ProxyChainConfig
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Nodes")] public List<ProxyNode> Nodes { get; set; } = new();
        [JsonPropertyName("Strategy")] public ChainStrategy Strategy { get; set; } = ChainStrategy.Sequential;
        [JsonPropertyName("AutoFailover")] public bool AutoFailover { get; set; } = true;
        [JsonPropertyName("HealthCheckInterval")] public int HealthCheckInterval { get; set; } = 60;
        [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("LastUsedTime")] public DateTime LastUsedTime { get; set; }
        [JsonPropertyName("UsageCount")] public int UsageCount { get; set; }
    }

    public class ProxyNode
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Config")] public Services.ProxyConfig Config { get; set; } = new();
        [JsonPropertyName("Order")] public int Order { get; set; }
        [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("Latency")] public int Latency { get; set; }
        [JsonPropertyName("IsAvailable")] public bool IsAvailable { get; set; } = true;
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ChainStrategy
    {
        Sequential,
        Failover,
        LoadBalance
    }

    public class ProxyChainHistory
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("ChainId")] public string ChainId { get; set; } = string.Empty;
        [JsonPropertyName("ChainName")] public string ChainName { get; set; } = string.Empty;
        [JsonPropertyName("StartTime")] public DateTime StartTime { get; set; } = DateTime.Now;
        [JsonPropertyName("EndTime")] public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("ErrorMessage")] public string ErrorMessage { get; set; } = string.Empty;
        [JsonPropertyName("TotalNodes")] public int TotalNodes { get; set; }
        [JsonPropertyName("SuccessfulNodes")] public int SuccessfulNodes { get; set; }
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
    }

    public class ProxyChainPerformance
    {
        [JsonPropertyName("ChainId")] public string ChainId { get; set; } = string.Empty;
        [JsonPropertyName("ChainName")] public string ChainName { get; set; } = string.Empty;
        [JsonPropertyName("TotalUses")] public int TotalUses { get; set; }
        [JsonPropertyName("SuccessfulUses")] public int SuccessfulUses { get; set; }
        [JsonPropertyName("FailedUses")] public int FailedUses { get; set; }
        public double SuccessRate => TotalUses > 0 ? (SuccessfulUses * 100.0 / TotalUses) : 0;
        [JsonPropertyName("AverageTotalLatency")] public double AverageTotalLatency { get; set; }
        [JsonPropertyName("NodePerformances")] public List<NodePerformance> NodePerformances { get; set; } = new();
        [JsonPropertyName("FirstUsed")] public DateTime FirstUsed { get; set; }
        [JsonPropertyName("LastUsed")] public DateTime LastUsed { get; set; }
    }

    public class NodePerformance
    {
        [JsonPropertyName("NodeId")] public string NodeId { get; set; } = string.Empty;
        [JsonPropertyName("NodeName")] public string NodeName { get; set; } = string.Empty;
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("SuccessCount")] public int SuccessCount { get; set; }
        [JsonPropertyName("FailCount")] public int FailCount { get; set; }
        public double SuccessRate => (SuccessCount + FailCount) > 0 
            ? (SuccessCount * 100.0 / (SuccessCount + FailCount)) : 0;
    }

    public class ProxyChainComparison
    {
        [JsonPropertyName("ComparisonTime")] public DateTime ComparisonTime { get; set; } = DateTime.Now;
        [JsonPropertyName("Items")] public List<ChainComparisonItem> Items { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("BestChainId")] public string BestChainId { get; set; } = string.Empty;
        [JsonPropertyName("BestChainName")] public string BestChainName { get; set; } = string.Empty;
    }

    public class ChainComparisonItem
    {
        [JsonPropertyName("ChainId")] public string ChainId { get; set; } = string.Empty;
        [JsonPropertyName("ChainName")] public string ChainName { get; set; } = string.Empty;
        [JsonPropertyName("SuccessRate")] public double SuccessRate { get; set; }
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("TotalUses")] public int TotalUses { get; set; }
        [JsonPropertyName("Rank")] public int Rank { get; set; }
        [JsonPropertyName("PerformanceGrade")] public string PerformanceGrade { get; set; } = string.Empty;
    }

    public class ProxyChainOptimization
    {
        [JsonPropertyName("ChainId")] public string ChainId { get; set; } = string.Empty;
        [JsonPropertyName("ChainName")] public string ChainName { get; set; } = string.Empty;
        [JsonPropertyName("Suggestions")] public List<string> Suggestions { get; set; } = new();
        [JsonPropertyName("NodeOptimizations")] public List<NodeOptimization> NodeOptimizations { get; set; } = new();
        [JsonPropertyName("Priority")] public OptimizationPriority Priority { get; set; }
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
    }

    public class NodeOptimization
    {
        [JsonPropertyName("NodeId")] public string NodeId { get; set; } = string.Empty;
        [JsonPropertyName("NodeName")] public string NodeName { get; set; } = string.Empty;
        [JsonPropertyName("RecommendedAction")] public NodeAction RecommendedAction { get; set; }
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum NodeAction
    {
        Keep,
        MoveUp,
        MoveDown,
        Remove,
        Replace
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum OptimizationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class ProxyChainReport
    {
        [JsonPropertyName("GeneratedTime")] public DateTime GeneratedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("TotalChains")] public int TotalChains { get; set; }
        [JsonPropertyName("ActiveChains")] public int ActiveChains { get; set; }
        [JsonPropertyName("TopPerformers")] public List<ProxyChainPerformance> TopPerformers { get; set; } = new();
        [JsonPropertyName("PoorPerformers")] public List<ProxyChainPerformance> PoorPerformers { get; set; } = new();
        [JsonPropertyName("Comparison")] public ProxyChainComparison Comparison { get; set; } = new();
        [JsonPropertyName("Optimizations")] public List<ProxyChainOptimization> Optimizations { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("HealthScore")] public int HealthScore { get; set; }
    }

    public class ProxyChainSettings
    {
        [JsonPropertyName("ActiveChainId")] public string ActiveChainId { get; set; } = string.Empty;
        [JsonPropertyName("Chains")] public List<ProxyChainConfig> Chains { get; set; } = new();
        [JsonPropertyName("History")] public List<ProxyChainHistory> History { get; set; } = new();
        [JsonPropertyName("Performance")] public List<ProxyChainPerformance> Performance { get; set; } = new();
        [JsonPropertyName("LastUpdated")] public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}

