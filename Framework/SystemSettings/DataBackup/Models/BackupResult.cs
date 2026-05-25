using System.Reflection;

namespace TM.Framework.SystemSettings.DataBackup.Models
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class BackupResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public string? OutputPath { get; set; }

        public string? SafetyCopyPath { get; set; }

        public long FileSizeBytes { get; set; }

        public static BackupResult Ok(string message, string? outputPath = null, long fileSize = 0, string? safetyCopy = null) => new()
        {
            Success = true,
            Message = message,
            OutputPath = outputPath,
            FileSizeBytes = fileSize,
            SafetyCopyPath = safetyCopy
        };

        public static BackupResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class BackupValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public BackupManifest? Manifest { get; set; }

        public static BackupValidationResult Valid(BackupManifest manifest) => new()
        {
            IsValid = true,
            Message = "校验通过",
            Manifest = manifest
        };

        public static BackupValidationResult Invalid(string message) => new()
        {
            IsValid = false,
            Message = message
        };
    }
}
