using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TM.Framework.SystemSettings.Logging.LogLevel
{
    [Obfuscation(Exclude = true)]
    public enum LogLevelEnum
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5
    }

    public class LogLevelSettings
    {
        [JsonPropertyName("GlobalLevel")] public LogLevelEnum GlobalLevel { get; set; } = LogLevelEnum.Info;
        [JsonPropertyName("MinimumLevel")] public LogLevelEnum MinimumLevel { get; set; } = LogLevelEnum.Trace;
        [JsonPropertyName("ModuleLevels")] public Dictionary<string, LogLevelEnum> ModuleLevels { get; set; } = new Dictionary<string, LogLevelEnum>();
        [JsonPropertyName("LevelColors")] public Dictionary<LogLevelEnum, string> LevelColors { get; set; } = new Dictionary<LogLevelEnum, string>
        {
            { LogLevelEnum.Trace, "#808080" },
            { LogLevelEnum.Debug, "#00BFFF" },
            { LogLevelEnum.Info, "#00FF00" },
            { LogLevelEnum.Warning, "#FFA500" },
            { LogLevelEnum.Error, "#FF0000" },
            { LogLevelEnum.Fatal, "#8B0000" }
        };
    }

    public class ModuleLevelItem
    {
        [JsonPropertyName("ModuleName")] public string ModuleName { get; set; } = string.Empty;
        [JsonPropertyName("Level")] public LogLevelEnum Level { get; set; }
    }

    public class LevelChangeRecord
    {
        [JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("Target")] public string Target { get; set; } = string.Empty;
        [JsonPropertyName("OldLevel")] public LogLevelEnum OldLevel { get; set; }
        [JsonPropertyName("NewLevel")] public LogLevelEnum NewLevel { get; set; }
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("User")] public string User { get; set; } = Environment.UserName;
    }

    public class LevelStatistics
    {
        [JsonPropertyName("LevelCounts")] public Dictionary<LogLevelEnum, int> LevelCounts { get; set; } = new Dictionary<LogLevelEnum, int>();
        [JsonPropertyName("LastUpdated")] public DateTime LastUpdated { get; set; } = DateTime.Now;
        [JsonPropertyName("TotalLogs")] public int TotalLogs { get; set; } = 0;

        public void IncrementLevel(LogLevelEnum level)
        {
            if (!LevelCounts.ContainsKey(level))
                LevelCounts[level] = 0;
            LevelCounts[level]++;
            TotalLogs++;
            LastUpdated = DateTime.Now;
        }

        public double GetLevelPercentage(LogLevelEnum level)
        {
            if (TotalLogs == 0) return 0;
            return LevelCounts.ContainsKey(level) ? (LevelCounts[level] * 100.0 / TotalLogs) : 0;
        }
    }

    public class LevelPreset
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("GlobalLevel")] public LogLevelEnum GlobalLevel { get; set; }
        [JsonPropertyName("MinimumLevel")] public LogLevelEnum MinimumLevel { get; set; }
        [JsonPropertyName("ModuleLevels")] public Dictionary<string, LogLevelEnum> ModuleLevels { get; set; } = new Dictionary<string, LogLevelEnum>();

        public static readonly Dictionary<string, LevelPreset> StandardPresets = new Dictionary<string, LevelPreset>
        {
            ["Development"] = new LevelPreset
            {
                Name = "开发环境",
                Description = "详细调试，全部启用Trace级别",
                GlobalLevel = LogLevelEnum.Trace,
                MinimumLevel = LogLevelEnum.Trace,
                ModuleLevels = new Dictionary<string, LogLevelEnum>()
            },
            ["Testing"] = new LevelPreset
            {
                Name = "测试环境",
                Description = "适度调试，Debug级别",
                GlobalLevel = LogLevelEnum.Debug,
                MinimumLevel = LogLevelEnum.Debug,
                ModuleLevels = new Dictionary<string, LogLevelEnum>()
            },
            ["Production"] = new LevelPreset
            {
                Name = "生产环境",
                Description = "最小日志，Info/Warning级别",
                GlobalLevel = LogLevelEnum.Info,
                MinimumLevel = LogLevelEnum.Warning,
                ModuleLevels = new Dictionary<string, LogLevelEnum>()
            }
        };
    }
}

