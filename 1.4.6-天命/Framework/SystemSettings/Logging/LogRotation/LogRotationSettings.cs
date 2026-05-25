using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TM.Framework.SystemSettings.Logging.LogRotation
{
    [Obfuscation(Exclude = true)]
    public enum RotationType
    {
        BySize,
        ByTime,
        Hybrid
    }

    [Obfuscation(Exclude = true)]
    public enum RotationTrigger
    {
        Manual,
        SizeLimit,
        TimeInterval,
        Scheduled
    }

    [Obfuscation(Exclude = true)]
    public enum StorageStatus
    {
        Normal,
        Warning,
        Critical
    }

    [Obfuscation(Exclude = true)]
    public enum TimeInterval
    {
        Hourly,
        Daily,
        Weekly,
        Monthly
    }

    [Obfuscation(Exclude = true)]
    public enum CompressionType
    {
        None,
        ZIP,
        GZIP
    }

    [Obfuscation(Exclude = true)]
    public enum CleanupStrategy
    {
        ByCount,
        ByTime,
        BySize
    }

    [Obfuscation(Exclude = true)]
    public enum FileNamingPattern
    {
        Timestamp,
        Sequential
    }

    public class RotationHistory
    {
        [JsonPropertyName("RotationTime")] public DateTime RotationTime { get; set; }
        [JsonPropertyName("Trigger")] public RotationTrigger Trigger { get; set; }
        [JsonPropertyName("OriginalFileName")] public string OriginalFileName { get; set; } = string.Empty;
        [JsonPropertyName("RotatedFileName")] public string RotatedFileName { get; set; } = string.Empty;
        [JsonPropertyName("FileSizeBytes")] public long FileSizeBytes { get; set; }
        [JsonPropertyName("WasCompressed")] public bool WasCompressed { get; set; }
        [JsonPropertyName("TotalFilesBeforeRotation")] public int TotalFilesBeforeRotation { get; set; }
        [JsonPropertyName("TotalFilesAfterRotation")] public int TotalFilesAfterRotation { get; set; }
        [JsonPropertyName("Notes")] public string Notes { get; set; } = string.Empty;
        [JsonPropertyName("DurationMs")] public long DurationMs { get; set; }
        [JsonPropertyName("Success")] public bool Success { get; set; } = true;
    }

    public class RotationPrediction
    {
        [JsonPropertyName("PredictedNextRotation")] public DateTime PredictedNextRotation { get; set; }
        [JsonPropertyName("TimeUntilNextRotation")] public TimeSpan TimeUntilNextRotation { get; set; }
        [JsonPropertyName("PredictedFileSizeMB")] public long PredictedFileSizeMB { get; set; }
        [JsonPropertyName("PredictedStorageUsageMB")] public long PredictedStorageUsageMB { get; set; }
        [JsonPropertyName("AverageDailyGrowthMB")] public double AverageDailyGrowthMB { get; set; }
        [JsonPropertyName("RecommendedAction")] public string RecommendedAction { get; set; } = string.Empty;
    }

    public class StorageSpaceInfo
    {
        [JsonPropertyName("DrivePath")] public string DrivePath { get; set; } = string.Empty;
        [JsonPropertyName("TotalSpaceGB")] public long TotalSpaceGB { get; set; }
        [JsonPropertyName("FreeSpaceGB")] public long FreeSpaceGB { get; set; }
        [JsonPropertyName("UsedSpaceGB")] public long UsedSpaceGB { get; set; }
        [JsonPropertyName("UsagePercentage")] public double UsagePercentage { get; set; }
        [JsonPropertyName("LogsSpaceMB")] public long LogsSpaceMB { get; set; }
        [JsonPropertyName("Status")] public StorageStatus Status { get; set; }
        [JsonPropertyName("StatusMessage")] public string StatusMessage { get; set; } = string.Empty;
        [JsonPropertyName("LastChecked")] public DateTime LastChecked { get; set; }
    }

    public class CleanupRecommendation
    {
        [JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("EstimatedSpaceToFree")] public long EstimatedSpaceToFree { get; set; }
        [JsonPropertyName("FilesToDelete")] public int FilesToDelete { get; set; }
        [JsonPropertyName("Priority")] public string Priority { get; set; } = string.Empty;
    }

    public class LogRotationSettings
    {
        [JsonPropertyName("RotationType")] public RotationType RotationType { get; set; } = RotationType.Hybrid;
        [JsonPropertyName("EnableSizeRotation")] public bool EnableSizeRotation { get; set; } = true;
        [JsonPropertyName("MaxFileSizeMB")] public int MaxFileSizeMB { get; set; } = 10;
        [JsonPropertyName("EnableTimeRotation")] public bool EnableTimeRotation { get; set; } = true;
        [JsonPropertyName("TimeInterval")] public TimeInterval TimeInterval { get; set; } = TimeInterval.Daily;
        [JsonPropertyName("MaxRetainCount")] public int MaxRetainCount { get; set; } = 1;
        [JsonPropertyName("MaxRetainDays")] public int MaxRetainDays { get; set; } = 1;
        [JsonPropertyName("MaxRetainSizeMB")] public int MaxRetainSizeMB { get; set; } = 1024;
        [JsonPropertyName("EnableCompression")] public bool EnableCompression { get; set; } = true;
        [JsonPropertyName("CompressionType")] public CompressionType CompressionType { get; set; } = CompressionType.ZIP;
        [JsonPropertyName("CompressAfterDays")] public int CompressAfterDays { get; set; } = 1;
        [JsonPropertyName("EnableAutoCleanup")] public bool EnableAutoCleanup { get; set; } = true;
        [JsonPropertyName("CleanupStrategy")] public CleanupStrategy CleanupStrategy { get; set; } = CleanupStrategy.ByCount;
        [JsonPropertyName("ArchivePath")] public string ArchivePath { get; set; } = "Logs/Archive";
        [JsonPropertyName("FileNamingPattern")] public FileNamingPattern FileNamingPattern { get; set; } = FileNamingPattern.Timestamp;
        [JsonPropertyName("RotationHistoryRecords")] public List<RotationHistory> RotationHistoryRecords { get; set; } = new List<RotationHistory>();
        [JsonPropertyName("EnableStorageMonitoring")] public bool EnableStorageMonitoring { get; set; } = true;
        [JsonPropertyName("WarningThresholdPercentage")] public int WarningThresholdPercentage { get; set; } = 80;
        [JsonPropertyName("CriticalThresholdPercentage")] public int CriticalThresholdPercentage { get; set; } = 90;
    }
}

