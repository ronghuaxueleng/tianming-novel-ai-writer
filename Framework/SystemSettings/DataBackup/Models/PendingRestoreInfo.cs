using System;
using System.Reflection;

namespace TM.Framework.SystemSettings.DataBackup.Models
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class PendingRestoreInfo
    {
        public string ZipPath { get; set; } = string.Empty;

        public DateTime ScheduledAtUtc { get; set; } = DateTime.UtcNow;

        public string ProjectName { get; set; } = string.Empty;

        public int RetryCount { get; set; }

        public const int MaxRetryCount = 3;
    }
}
