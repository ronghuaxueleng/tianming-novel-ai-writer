using System;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public static partial class ProtectionService
    {
        public static string? StartupBlockReason => null;
        public static string? NativeProtectIssue => null;
        public static PunishmentLevel PL { get; private set; } = PunishmentLevel.None;
        public static int CheckIntervalSeconds { get; set; } = 30;
        public static bool IsEnabled => false;

        #region Server Integration

        private static bool _serverInitialized;
        private static int _svFp, _shFp, _faFp;

        private static int DelegateFp(Delegate? d)
        {
            if (d == null) return 0;
            var m = d.Method;
            return HashCode.Combine(m.MetadataToken, m.DeclaringType?.FullName?.GetHashCode() ?? 0);
        }

        public static Func<Task<SVR>>? SV { get; set; }
        public static Func<Task<bool>>? SH { get; set; }
        public static Func<string, Task<bool?>>? FA { get; set; }

        public class SVR
        {
            public bool IsValid { get; set; }
            public string? Message { get; set; }
            public DateTime? ExpirationTime { get; set; }
        }

        public static void MSI()
        {
            _svFp = DelegateFp(SV);
            _shFp = DelegateFp(SH);
            _faFp = DelegateFp(FA);
            _serverInitialized = true;
        }

        public static async Task<bool?> CheckFeatureAuthorizationAsync(string featureId)
        {
            if (FA == null) return !_serverInitialized;
            try { return await FA(featureId); }
            catch { return null; }
        }

        #endregion

        public static bool SC() => true;

        public static void Initialize() { }

        public static void Stop() { }

        public static void SetOriginalHash(string hash) { }

        public static Task LoadOriginalHashAsync() => Task.CompletedTask;

        public static void LoadOriginalHashFromFile() { }

        public static ProtectionCheckResult PerformCheck() => new ProtectionCheckResult { IsSafe = true };

        public static bool VerifyServerSignature(string data, string sigBase64) => true;

        public static bool VerifyResponseSignature(string body, string sigBase64) => true;

        public static bool SetSslPins(string pinsBase64, string sigBase64) => false;

        public static string? SignMemoryChallenge(string moduleName, string nonce) => null;

        public static string? SignIdentity(string nonce, string versionUtf8) => null;

        public static string? SignHeartbeat(string hbNonce) => null;

        public static int? GetHeartbeatCount() => null;

        public enum PunishmentLevel { None, Warning, DataWipe, Terminate }

    }

    public class ProtectionCheckResult
    {
        public bool IsSafe { get; set; }
        public bool DebuggerDetected { get; set; }
        public bool IntegrityCompromised { get; set; }
        public bool DelegateCompromised { get; set; }

        public string GetThreatSummary()
        {
            var threats = new System.Collections.Generic.List<string>();
            if (DebuggerDetected) threats.Add("D");
            if (IntegrityCompromised) threats.Add("I");
            if (DelegateCompromised) threats.Add("DL");
            return threats.Count > 0 ? string.Join(",", threats) : "-";
        }
    }
}