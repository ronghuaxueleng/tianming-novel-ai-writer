using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyRules
{
    public class RuleMatchHistory
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleName")] public string RuleName { get; set; } = string.Empty;
        [JsonPropertyName("MatchedUrl")] public string MatchedUrl { get; set; } = string.Empty;
        [JsonPropertyName("MatchedHost")] public string MatchedHost { get; set; } = string.Empty;
        [JsonPropertyName("Action")] public ProxyAction Action { get; set; }
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("LatencyMs")] public int LatencyMs { get; set; }
        [JsonPropertyName("ErrorMessage")] public string ErrorMessage { get; set; } = string.Empty;
    }

    public class RuleUsageStatistics
    {
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleName")] public string RuleName { get; set; } = string.Empty;
        [JsonPropertyName("TotalMatches")] public int TotalMatches { get; set; }
        [JsonPropertyName("SuccessfulMatches")] public int SuccessfulMatches { get; set; }
        [JsonPropertyName("FailedMatches")] public int FailedMatches { get; set; }
        public double SuccessRate => TotalMatches > 0 ? (SuccessfulMatches * 100.0 / TotalMatches) : 0;
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("FirstUsed")] public DateTime FirstUsed { get; set; }
        [JsonPropertyName("LastUsed")] public DateTime LastUsed { get; set; }
        [JsonPropertyName("UsageFrequency")] public int UsageFrequency { get; set; }
        [JsonPropertyName("MatchTimestamps")] public List<DateTime> MatchTimestamps { get; set; } = new();
    }

    public class RuleEffectiveness
    {
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("RuleName")] public string RuleName { get; set; } = string.Empty;
        [JsonPropertyName("HitRate")] public double HitRate { get; set; }
        [JsonPropertyName("Accuracy")] public double Accuracy { get; set; }
        [JsonPropertyName("Level")] public EffectivenessLevel Level { get; set; }
        [JsonPropertyName("OptimizationSuggestions")] public List<string> OptimizationSuggestions { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("ImpactScore")] public int ImpactScore { get; set; }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum EffectivenessLevel
    {
        Excellent,
        Good,
        Fair,
        Poor
    }

    public class RuleConflictAnalysis
    {
        [JsonPropertyName("ConflictId")] public string ConflictId { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Conflicts")] public List<ConflictingRulePair> Conflicts { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        public int TotalConflicts => Conflicts.Count;
    }

    public class ConflictingRulePair
    {
        [JsonPropertyName("Rule1")] public ProxyRule Rule1 { get; set; } = new();
        [JsonPropertyName("Rule2")] public ProxyRule Rule2 { get; set; } = new();
        [JsonPropertyName("Type")] public ConflictType Type { get; set; }
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("Severity")] public ConflictSeverity Severity { get; set; }
        [JsonPropertyName("Resolution")] public string Resolution { get; set; } = string.Empty;
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ConflictType
    {
        PatternOverlap,
        PriorityConflict,
        ActionConflict
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ConflictSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class RuleRecommendation
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("SuggestedRule")] public ProxyRule SuggestedRule { get; set; } = new();
        [JsonPropertyName("Type")] public RecommendationType Type { get; set; }
        [JsonPropertyName("Priority")] public int Priority { get; set; }
        [JsonPropertyName("Benefits")] public List<string> Benefits { get; set; } = new();
        [JsonPropertyName("ConfidenceScore")] public double ConfidenceScore { get; set; }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum RecommendationType
    {
        NewRule,
        RuleOptimization,
        RuleConsolidation,
        RuleRemoval
    }

    public class RulePerformanceMetrics
    {
        [JsonPropertyName("RuleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("AverageProcessingTime")] public double AverageProcessingTime { get; set; }
        [JsonPropertyName("AverageLatency")] public double AverageLatency { get; set; }
        [JsonPropertyName("MemoryUsage")] public long MemoryUsage { get; set; }
        [JsonPropertyName("CpuUsage")] public double CpuUsage { get; set; }
        [JsonPropertyName("ResourceImpact")] public int ResourceImpact { get; set; }
    }

    public class RuleReport
    {
        [JsonPropertyName("GeneratedTime")] public DateTime GeneratedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("TotalRules")] public int TotalRules { get; set; }
        [JsonPropertyName("EnabledRules")] public int EnabledRules { get; set; }
        [JsonPropertyName("DisabledRules")] public int DisabledRules { get; set; }
        [JsonPropertyName("TopRules")] public List<RuleUsageStatistics> TopRules { get; set; } = new();
        [JsonPropertyName("LowEfficiencyRules")] public List<RuleEffectiveness> LowEfficiencyRules { get; set; } = new();
        [JsonPropertyName("ConflictAnalysis")] public RuleConflictAnalysis ConflictAnalysis { get; set; } = new();
        [JsonPropertyName("Recommendations")] public List<RuleRecommendation> Recommendations { get; set; } = new();
        [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("HealthScore")] public int HealthScore { get; set; }
    }

    public class ProxyRulesSettings
    {
        [JsonPropertyName("MatchHistory")] public List<RuleMatchHistory> MatchHistory { get; set; } = new();
        [JsonPropertyName("UsageStatistics")] public List<RuleUsageStatistics> UsageStatistics { get; set; } = new();
        [JsonPropertyName("EffectivenessData")] public List<RuleEffectiveness> EffectivenessData { get; set; } = new();
        [JsonPropertyName("LastUpdated")] public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}

