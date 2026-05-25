using System;
using System.Collections.Generic;
using System.Reflection;

namespace TM.Framework.SystemSettings.DataBackup.Models
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class BackupManifest
    {
        public string Signature { get; set; } = "TM_PROJECT_BACKUP";

        public string Type { get; set; } = "full_backup";

        public int ManifestVersion { get; set; } = 2;

        public string ProjectName { get; set; } = string.Empty;

        public string AppVersion { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public long OriginalSizeBytes { get; set; }

        public int ChapterCount { get; set; }

        public List<BackupScopeEntry> Scopes { get; set; } = new();
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class BackupScopeEntry
    {
        public string LogicalKey { get; set; } = string.Empty;

        public string ZipPrefix { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long SizeBytes { get; set; }
    }

    public static class BackupTypes
    {
        public const string FullBackup = "full_backup";
        public const string ChaptersExport = "chapters_export";
    }

    public static class BackupScopeKeys
    {
        public const string Project = "project";
        public const string ModulesDesign = "modules-design";
        public const string ModulesGenerate = "modules-generate";
    }
}
