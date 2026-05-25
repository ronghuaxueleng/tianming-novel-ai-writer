using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models
{
    public class DataSyncResultDetailed
    {
        [JsonPropertyName("IsSuccess")]
        public bool IsSuccess { get; set; }
        [JsonPropertyName("Message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("SyncTime")]
        public DateTime SyncTime { get; set; } = DateTime.Now;
        [JsonPropertyName("Duration")]
        public TimeSpan Duration { get; set; }

        [JsonPropertyName("Statistics")]
        public SyncStatistics Statistics { get; set; } = new();

        [JsonPropertyName("SyncedModules")]
        public List<ModuleSyncInfo> SyncedModules { get; set; } = new();

        [JsonPropertyName("Conflicts")]
        public List<DataConflict> Conflicts { get; set; } = new();

        [JsonPropertyName("Logs")]
        public List<SyncLogEntry> Logs { get; set; } = new();
    }

    public class SyncStatistics
    {
        [JsonPropertyName("TotalModules")]
        public int TotalModules { get; set; }
        [JsonPropertyName("SuccessModules")]
        public int SuccessModules { get; set; }
        [JsonPropertyName("FailedModules")]
        public int FailedModules { get; set; }
        [JsonPropertyName("ConflictCount")]
        public int ConflictCount { get; set; }
        [JsonPropertyName("TotalDataSize")]
        public long TotalDataSize { get; set; }
        [JsonPropertyName("SyncedDataSize")]
        public long SyncedDataSize { get; set; }
    }

    public class ModuleSyncInfo
    {
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;
        [JsonPropertyName("IsSuccess")]
        public bool IsSuccess { get; set; }
        [JsonPropertyName("Message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("RecordsProcessed")]
        public int RecordsProcessed { get; set; }
        [JsonPropertyName("RecordsUpdated")]
        public int RecordsUpdated { get; set; }
        [JsonPropertyName("RecordsInserted")]
        public int RecordsInserted { get; set; }
        [JsonPropertyName("RecordsDeleted")]
        public int RecordsDeleted { get; set; }
        [JsonPropertyName("Duration")]
        public TimeSpan Duration { get; set; }
    }

    public class DataConflict
    {
        [JsonPropertyName("ConflictId")]
        public string ConflictId { get; set; } = string.Empty;
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;
        [JsonPropertyName("EntityType")]
        public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("EntityId")]
        public string EntityId { get; set; } = string.Empty;
        [JsonPropertyName("FieldPath")]
        public string FieldPath { get; set; } = string.Empty;

        [JsonPropertyName("LocalValue")]
        public object? LocalValue { get; set; }
        [JsonPropertyName("RemoteValue")]
        public object? RemoteValue { get; set; }
        [JsonPropertyName("LocalTimestamp")]
        public DateTime LocalTimestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("RemoteTimestamp")]
        public DateTime RemoteTimestamp { get; set; } = DateTime.Now;

        [JsonPropertyName("ConflictType")]
        public ConflictType ConflictType { get; set; } = ConflictType.ValueDifference;
        [JsonPropertyName("Resolution")]
        public ConflictResolutionStrategy Resolution { get; set; } = ConflictResolutionStrategy.Manual;
        [JsonPropertyName("ResolvedValue")]
        public object? ResolvedValue { get; set; }
    }

    public enum ConflictType
    {
        ValueDifference,
        TypeMismatch,
        StructureChange,
        Deletion,
        Creation,
        Permission
    }

    public enum ConflictResolutionStrategy
    {
        Manual,
        UseLocal,
        UseRemote,
        Merge,
        Skip,
        Timestamp
    }

    public class SyncLogEntry
    {
        [JsonPropertyName("Timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.Now;
        [JsonPropertyName("Level")]
        public LogLevel Level { get; set; } = LogLevel.Info;
        [JsonPropertyName("Message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;
        [JsonPropertyName("Details")] public Dictionary<string, object> Details { get; set; } = new();
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }
}
