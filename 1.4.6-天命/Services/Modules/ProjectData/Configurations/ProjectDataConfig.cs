using System;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Configurations
{
    public class ProjectDataConfig
    {
        public DataStorageConfig Storage { get; set; } = new();

        [Obsolete("DataSyncConfig 整个类未注入使用，同步由事件驱动实现。修改此配置不产生任何效果。")]
        public DataSyncConfig Sync { get; set; } = new();

        public AIContextConfig AIContext { get; set; } = new();

        public CacheConfig Cache { get; set; } = new();

        public PerformanceConfig Performance { get; set; } = new();
    }

    public class DataStorageConfig
    {
        public string StorageBasePath { get; set; } = "Storage/Services/ProjectDataService";
        [Obsolete("备份路径由 PublishService 内联逻辑控制，此字段未被读取。")]
        public string BackupPath { get; set; } = "Storage/Backups/ProjectData";
        public bool EnableAutoBackup { get; set; } = true;
        public TimeSpan BackupInterval { get; set; } = TimeSpan.FromHours(1);
        public int MaxBackupFiles { get; set; } = 10;

        public List<string> SupportedModules { get; set; } = new()
        {
            "Design", "Generate", "Validate"
        };
    }

    [Obsolete("DataSyncConfig 整个类未注入使用，同步由事件驱动实现。修改此配置不产生任何效果。")]
    public class DataSyncConfig
    {
        public bool EnableRealTimeSync { get; set; } = true;
        public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

        public ConflictResolutionPolicy ConflictResolution { get; set; } = new();

        public List<SyncFilterRule> FilterRules { get; set; } = new();
    }

    public class ConflictResolutionPolicy
    {
        public string DefaultStrategy { get; set; } = "Manual";
        public bool AllowAutoResolve { get; set; } = false;
        public TimeSpan ConflictTimeout { get; set; } = TimeSpan.FromMinutes(30);

        public Dictionary<string, string> FieldStrategies { get; set; } = new()
        {
            { "Timestamp", "UseLatest" },
            { "Version", "UseHigher" },
            { "Content", "Manual" }
        };
    }

    public class SyncFilterRule
    {
        public string RuleName { get; set; } = string.Empty;
        public string ModuleType { get; set; } = string.Empty;
        public string FieldPattern { get; set; } = string.Empty;
        public string Action { get; set; } = "Include";
        public bool IsEnabled { get; set; } = true;
    }

    public class AIContextConfig
    {
        public bool EnableContextCaching { get; set; } = true;
        public TimeSpan ContextCacheExpiry { get; set; } = TimeSpan.FromMinutes(30);
        [Obsolete("上下文大小由 LayeredContextConfig 控制，此字段未被读取。")]
        public int MaxContextSize { get; set; } = 1024 * 1024;
        public bool EnableDataFiltering { get; set; } = true;

        public List<ContextBuildRule> BuildRules { get; set; } = new();
    }

    public class ContextBuildRule
    {
        public string RuleName { get; set; } = string.Empty;
        public string TargetModule { get; set; } = string.Empty;
        public List<string> RequiredData { get; set; } = new();
        public List<string> OptionalData { get; set; } = new();
        public int Priority { get; set; } = 0;
        public bool IsEnabled { get; set; } = true;
    }

    public class CacheConfig
    {
        public bool EnableMemoryCache { get; set; } = true;
        public int MaxMemoryCacheSize { get; set; } = 100;
        public TimeSpan DefaultCacheExpiry { get; set; } = TimeSpan.FromMinutes(15);
        public bool EnableDiskCache { get; set; } = false;
        public string DiskCachePath { get; set; } = "Storage/Cache/ProjectData";
    }

    public class PerformanceConfig
    {
        public int MaxConcurrentOperations { get; set; } = 10;
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableBatchProcessing { get; set; } = true;
        public int BatchSize { get; set; } = 100;
        public bool EnableCompressionTesting { get; set; } = false;
    }
}
