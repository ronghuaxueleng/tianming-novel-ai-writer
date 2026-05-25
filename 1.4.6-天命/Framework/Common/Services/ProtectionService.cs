using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public static class ProtectionService
    {
        private static bool _initialized;
        private static CancellationTokenSource? _cts;
        private static int _violationCount;

        private static long _startupToken;
        private static int _scPassHash;
        private static int _cmBaselineMethods;
        private static int _cmBaselineFields;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            Debug.WriteLine($"[ProtectionService] {key}: {ex.Message}");
        }

        #region Win32 API

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll")]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool isDebuggerPresent);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
            ref IntPtr processInformation, int processInformationLength, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern void OutputDebugString(string lpOutputString);

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern bool GetThreadContext(IntPtr hThread, ref CONTEXT lpContext);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationThread(IntPtr ThreadHandle, int ThreadInformationClass, IntPtr ThreadInformation, int ThreadInformationLength);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, IntPtr lpWindowName);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass,
            ref int processInformation, int processInformationLength);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool TMP_Init();
        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int TMP_FullCheck();
        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void TMP_RegisterStartupCheck();
        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool TMP_VerifyStartupCheck();
        [DllImport("TMProtect.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern bool TMP_VerifyMemory(IntPtr addr, uint size, string expectedHashHex);

        private static bool _nativeProtectAvailable;
        private static volatile string? _startupBlockReason;
        private static volatile string? _nativeProtectIssue;

        public static string? StartupBlockReason => _startupBlockReason;
        public static string? NativeProtectIssue => _nativeProtectIssue;
        private static void InitNativeProtect()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dllPath = Path.Combine(baseDir, "TMProtect.dll");
                if (!File.Exists(dllPath))
                {
                    _nativeProtectAvailable = false;
                    _nativeProtectIssue = $"TMProtect.dll 缺失（可能被杀软隔离）: {dllPath}";
                    TM.App.Log($"[ProtectionService] TMProtect missing: {dllPath}");
                    return;
                }

                _nativeProtectAvailable = TMP_Init();
                if (_nativeProtectAvailable)
                {
                    _nativeProtectIssue = null;
                    TM.App.Log("[ProtectionService] ext loaded");
                }
            }
            catch (DllNotFoundException ex)
            {
                _nativeProtectAvailable = false;
                _nativeProtectIssue = $"TMProtect.dll 加载失败（可能被杀软隔离）: {ex.Message}";
                TM.App.Log($"[ProtectionService] TMProtect load fail: {ex.Message}");
            }
            catch (BadImageFormatException ex)
            {
                _nativeProtectAvailable = false;
                _nativeProtectIssue = $"TMProtect.dll 无法加载（位数/损坏）: {ex.Message}";
                TM.App.Log($"[ProtectionService] TMProtect bad image: {ex.Message}");
            }
            catch (Exception ex)
            {
                _nativeProtectAvailable = false;
                _nativeProtectIssue = $"TMProtect 初始化异常: {ex.Message}";
                TM.App.Log($"[ProtectionService] TMProtect init err: {ex.Message}");
            }
        }

        private const uint CONTEXT_DEBUG_REGISTERS = 0x00010010;

        [StructLayout(LayoutKind.Sequential)]
        private struct CONTEXT
        {
            public uint ContextFlags;
            public uint Dr0, Dr1, Dr2, Dr3, Dr6, Dr7;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[]? ExtendedRegisters;
        }

        #endregion

        #region 配置

        public static PunishmentLevel PL { get; private set; } = PunishmentLevel.Terminate;

        public static int CheckIntervalSeconds { get; set; } = 30;

#if DEBUG
        public static bool IsEnabled => false;
#else
        public static bool IsEnabled => true;
#endif

        #endregion

        #region H

        private static string? _originalHash;

        public static void SetOriginalHash(string hash)
        {
            _originalHash = hash;
        }

        public static void LoadOriginalHash()
        {
            try
            {
                if (IntegrityHash.IsSigned && !string.IsNullOrEmpty(IntegrityHash.MainAssemblyHash))
                {
                    _originalHash = IntegrityHash.MainAssemblyHash;
                    TM.App.Log($"[ProtectionService] H1: {_originalHash?.Substring(0, 16)}...");
                    return;
                }

                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var hashFile = Path.Combine(Path.GetDirectoryName(exePath)!, "integrity.hash");
                if (File.Exists(hashFile))
                {
                    _originalHash = File.ReadAllText(hashFile).Trim();
                    TM.App.Log($"[ProtectionService] H2: {_originalHash?.Substring(0, 16)}...");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] H err: {ex.Message}");
            }
        }

        public static void LoadOriginalHashFromFile() => LoadOriginalHash();

        #endregion

        #region S

        private static bool _serverInitialized;
        private static int _svFp, _shFp, _faFp;

        private static int DelegateFp(Delegate? d)
        {
            if (d == null) return 0;
            var m = d.Method;
            return HashCode.Combine(m.MetadataToken, m.DeclaringType?.FullName?.GetHashCode() ?? 0);
        }

        public static void MSI()
        {
            _svFp = DelegateFp(SV);
            _shFp = DelegateFp(SH);
            _faFp = DelegateFp(FA);
            _serverInitialized = true;
        }

        private static bool CheckDelegateIntegrity()
        {
            if (!_serverInitialized) return true;
            if (SV == null || SH == null || FA == null) return false;
            return DelegateFp(SV) == _svFp && DelegateFp(SH) == _shFp && DelegateFp(FA) == _faFp;
        }

        public static Func<Task<SVR>>? SV { get; set; }
        public static Func<Task<bool>>? SH { get; set; }
        public static Func<string, Task<bool>>? FA { get; set; }

        public class SVR
        {
            public bool IsValid { get; set; }
            public string? Message { get; set; }
            public DateTime? ExpirationTime { get; set; }
        }

        private static async Task<bool> PSV()
        {
            if (SV == null)
            {
                return !_serverInitialized;
            }

            try
            {
                var result = await SV();
                if (!result.IsValid)
                {
                    TM.App.Log($"[ProtectionService] SV fail: {result.Message}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] SV err: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CheckFeatureAuthorizationAsync(string featureId)
        {
            if (FA == null)
            {
                return !_serverInitialized;
            }

            try
            {
                return await FA(featureId);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckFeatureAuthorizationAsync), ex);
                return false;
            }
        }

        #endregion

        #region Init

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (!IsEnabled)
            {
                TM.App.Log("[ProtectionService] off");
                return;
            }

            bool nativeVerified = false;
            try { nativeVerified = _nativeProtectAvailable && TMP_VerifyStartupCheck(); } catch { }
            var scHashMatch = _scPassHash == ComputeScPassHash();
            if ((!nativeVerified && _startupToken == 0) || !scHashMatch)
            {
                TM.App.Log($"[ProtectionService] 延迟惩罚已触发: nativeAvail={_nativeProtectAvailable}, nativeVerified={nativeVerified}, startupToken={_startupToken}, scHashMatch={scHashMatch}");
                _ = Task.Run(async () =>
                {
                    var delaySec = new Random().Next(30_000, 120_000);
                    TM.App.Log($"[ProtectionService] 延迟惩罚将在 {delaySec / 1000}s 后执行");
                    await Task.Delay(delaySec);
                    var fakeResult = new ProtectionCheckResult { DebuggerDetected = true };
                    await TriggerPunishmentAsync(fakeResult);
                });
            }

            CaptureMethodBaseline();
            ApplyAntiDump();
            InitNativeProtect();
            _cts = new CancellationTokenSource();
            _ = StartProtectionLoopAsync(_cts.Token);

            TM.App.Log("[ProtectionService] started");
        }

        public static bool SC()
        {
            if (!IsEnabled)
            {
                TM.App.Log("[ProtectionService] SC: off");
                return true;
            }

            try
            {
                _startupBlockReason = null;
                TM.App.Log("[ProtectionService] SC: start");

                try
                {
                    if (IntegrityHash.IsSigned)
                    {
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        var dllPath = Path.Combine(baseDir, "TMProtect.dll");
                        if (!File.Exists(dllPath))
                        {
                            _startupBlockReason = $"缺少 TMProtect.dll（可能被杀软隔离）。请将安装目录加入白名单，或重新安装覆盖。\n路径: {dllPath}";
                            TM.App.Log($"[ProtectionService] SC: TMProtect missing: {dllPath}");
                            return false;
                        }
                    }
                }
                catch { }

                if (CheckDebugger())
                {
                    TM.App.Log("[ProtectionService] SC: f1");
                    return false;
                }

                if (CheckIntegrity())
                {
                    TM.App.Log("[ProtectionService] SC: f2");
                    return false;
                }

                if (CheckApiHook())
                {
                    TM.App.Log("[ProtectionService] SC: f3");
                    return false;
                }

                if (CheckParentProcess())
                {
                    TM.App.Log("[ProtectionService] SC: f4");
                    return false;
                }

                TM.App.Log("[ProtectionService] SC: ok");
                _startupToken = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ^ 0x544D_5365_6375_7265L;
                _scPassHash = ComputeScPassHash();
                try { if (_nativeProtectAvailable) TMP_RegisterStartupCheck(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] SC err: {ex.Message}");
                return false;
            }
        }

        private static int ComputeScPassHash()
        {
            var scMethod = ((Func<bool>)SC).Method;
            int hash = scMethod.MetadataToken;
            hash ^= typeof(ProtectionService).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Length;
            hash ^= 0x54_4D_50_53;
            return hash;
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _initialized = false;
            TM.App.Log("[ProtectionService] stopped");
        }

        public static ProtectionCheckResult PerformCheck()
        {
            var result = new ProtectionCheckResult();

            if (!IsEnabled)
            {
                result.IsSafe = true;
                return result;
            }

            try
            {
                result.DebuggerDetected = CheckDebugger();
                result.IntegrityCompromised = CheckIntegrity();
                result.DelegateCompromised = !CheckDelegateIntegrity();
                result.VirtualMachineDetected = CheckVirtualMachine();
                result.SandboxDetected = CheckSandbox();
                result.TimingAnomalyDetected = CheckTimingAnomaly();
                if (_nativeProtectAvailable)
                {
                    try
                    {
                        int threats = TMP_FullCheck();
                        if ((threats & 0x01) != 0) result.DebuggerDetected = true;
                        if ((threats & 0x02) != 0) result.ApiHookDetected = true;
                    }
                    catch { result.IntegrityCompromised = true; }
                }
                else
                {
                    bool isSigned = IntegrityHash.IsSigned;
                    if (isSigned) result.IntegrityCompromised = true;
                }

                result.IsSafe = !result.DebuggerDetected &&
                               !result.IntegrityCompromised &&
                               !result.DelegateCompromised &&
                               !result.VirtualMachineDetected &&
                               !result.SandboxDetected &&
                               !result.TimingAnomalyDetected;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] PC err: {ex.Message}");
                result.IsSafe = false;
            }

            return result;
        }

        public static ProtectionCheckResult EC()
        {
            var result = PerformCheck();

            if (!IsEnabled || result.IsSafe == false)
            {
                return result;
            }

            try
            {
                result.ApiHookDetected = CheckApiHook();
                result.ClrHookDetected = CheckClrHook();
                result.HardwareBreakpointDetected = CheckHardwareBreakpoints();
                result.SuspiciousParentDetected = CheckParentProcess();
                result.PebDebugFlag = CheckPebDebugFlag();
                result.CallChainCompromised = !ValidateCallChain();
                result.CriticalMethodsMissing = !CheckCriticalMethods();

                result.IsSafe = result.IsSafe &&
                               !result.ApiHookDetected &&
                               !result.ClrHookDetected &&
                               !result.HardwareBreakpointDetected &&
                               !result.SuspiciousParentDetected &&
                               !result.PebDebugFlag &&
                               !result.CallChainCompromised &&
                               !result.CriticalMethodsMissing;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] EC err: {ex.Message}");
                result.IsSafe = false;
            }

            return result;
        }

        private const int ViolationThreshold = 3;

        private static async Task StartProtectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), ct);

                    var result = EC();
                    if (!result.IsSafe)
                    {
                        _violationCount++;
                        TM.App.Log($"[ProtectionService] v{_violationCount}/{ViolationThreshold}: {result.GetThreatSummary()}");

                        if (_violationCount >= ViolationThreshold)
                        {
                            await TriggerPunishmentAsync(result);
                        }
                    }
                    else if (_violationCount > 0)
                    {
                        TM.App.Log($"[ProtectionService] 检测通过，重置违规计数(was {_violationCount})");
                        _violationCount = 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProtectionService] loop err: {ex.Message}");
                }
            }
        }

        #endregion

        #region C1

        private static bool CheckDebugger()
        {
            try
            {
                if (Debugger.IsAttached)
                    return true;

                if (IsDebuggerPresent())
                    return true;

                CheckRemoteDebuggerPresent(GetCurrentProcess(), out bool remoteDebugger);
                if (remoteDebugger)
                    return true;

                IntPtr debugPort = IntPtr.Zero;
                if (NtQueryInformationProcess(GetCurrentProcess(), 7, ref debugPort, IntPtr.Size, out _) == 0)
                {
                    if (debugPort != IntPtr.Zero)
                        return true;
                }

                var debuggerProcesses = new[] { "dnspy", "x64dbg", "x32dbg", "ollydbg",
                    "windbg", "dotpeek", "ilspy", "de4dot", "megadumper", "cheatengine",
                    "ghidra", "radare2", "processhacker", "httpdebugger" };
                var exactMatchProcesses = new[] { "ida", "ida64", "cutter" };

                var signatureModules = new[] { "dnspy.exe", "dnlib.dll",
                    "x64dbg.dll", "x32dbg.dll", "ollydbg.exe", "ida.wll", "ida64.wll",
                    "titanhide.dll", "scyllahide.dll", "sharpmonoinjector.dll" };

                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var name = proc.ProcessName.ToLower();
                        foreach (var debugger in debuggerProcesses)
                        {
                            if (name.Contains(debugger))
                                return true;
                        }
                        foreach (var exact in exactMatchProcesses)
                        {
                            if (name == exact)
                                return true;
                        }

                        try
                        {
                            foreach (ProcessModule mod in proc.Modules)
                            {
                                var modName = Path.GetFileName(mod.FileName).ToLower();
                                foreach (var sig in signatureModules)
                                {
                                    if (modName == sig)
                                        return true;
                                }
                            }
                        }
                        catch {}
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("CheckDebugger_Process", ex);
                        continue;
                    }
                }

                var suspiciousClasses = new[] { "HxD" };
                foreach (var cls in suspiciousClasses)
                {
                    if (FindWindow(cls, IntPtr.Zero) != IntPtr.Zero)
                        return true;
                }

                var tick1 = GetTickCount();
                OutputDebugString("Detection");
                var tick2 = GetTickCount();
                if (tick2 - tick1 > 50)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckDebugger), ex);
                return false;
            }
        }

        #endregion

        #region C2

        private static bool CheckVirtualMachine()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (var item in searcher.Get())
                {
                    var manufacturer = item["Manufacturer"]?.ToString()?.ToLower() ?? "";
                    var model = item["Model"]?.ToString()?.ToLower() ?? "";

                    if (manufacturer.Contains("vmware") || manufacturer.Contains("innotek") ||
                        manufacturer.Contains("xen") || manufacturer.Contains("qemu") ||
                        manufacturer.Contains("oracle vm") ||
                        model.Contains("vmware") || model.Contains("virtualbox") ||
                        model.Contains("kvm"))
                    {
                        return true;
                    }
                }

                var vmProcesses = new[] { "vmtoolsd", "vmwaretray", "vboxservice", "vboxtray",
                    "xenservice", "qemu-ga", "vmusrvc", "vmsrvc" };

                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var name = proc.ProcessName.ToLower();
                        foreach (var vmProc in vmProcesses)
                        {
                            if (name.Contains(vmProc))
                                return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("CheckVirtualMachine_Process", ex);
                        continue;
                    }
                }

                var vmMacPrefixes = new[] { "00:0C:29", "00:50:56", "08:00:27", "00:1C:42", "00:16:3E" };

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        var nicName = nic.Name ?? "";
                        var nicDesc = nic.Description ?? "";
                        if (nicName.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nicName.IndexOf("VMnet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nicName.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nicDesc.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nicDesc.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        var mac = nic.GetPhysicalAddress().ToString();
                        if (mac.Length >= 6)
                        {
                            var prefix = $"{mac.Substring(0, 2)}:{mac.Substring(2, 2)}:{mac.Substring(4, 2)}";
                            foreach (var vmPrefix in vmMacPrefixes)
                            {
                                if (prefix.Equals(vmPrefix, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("CheckVirtualMachine_Mac", ex);
                        continue;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckVirtualMachine), ex);
                return false;
            }
        }

        #endregion

        #region C3

        private static bool CheckTimingAnomaly()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                long sum = 0;
                for (int i = 0; i < 1000; i++)
                {
                    sum += i * i;
                }

                sw.Stop();

                return sw.ElapsedMilliseconds > 50;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckTimingAnomaly), ex);
                return false;
            }
        }

        #endregion

        #region C4

        private static readonly byte[] _ecdsaPubXor = new byte[] {
            0x17, 0x1C, 0x31, 0x2D, 0x1F, 0x2D, 0x03, 0x12, 0x11, 0x35, 0x00, 0x13, 0x20, 0x30, 0x6A,
            0x19, 0x1B, 0x0B, 0x03, 0x13, 0x11, 0x35, 0x00, 0x13, 0x20, 0x30, 0x6A, 0x1E, 0x1B, 0x0B,
            0x39, 0x1E, 0x0B, 0x3D, 0x1B, 0x1F, 0x6A, 0x2F, 0x1B, 0x63, 0x37, 0x18, 0x22, 0x35, 0x38,
            0x6D, 0x08, 0x15, 0x68, 0x23, 0x30, 0x12, 0x14, 0x3E, 0x36, 0x68, 0x0D, 0x02, 0x28, 0x16,
            0x75, 0x39, 0x0B, 0x63, 0x32, 0x1C, 0x1F, 0x14, 0x15, 0x32, 0x18, 0x6F, 0x2E, 0x20, 0x2B,
            0x63, 0x62, 0x75, 0x71, 0x34, 0x0B, 0x62, 0x62, 0x2C, 0x6D, 0x2A, 0x23, 0x18, 0x1C, 0x28,
            0x33, 0x1F, 0x3C, 0x29, 0x3D, 0x3B, 0x0B, 0x39, 0x1E, 0x69, 0x17, 0x3E, 0x3E, 0x69, 0x12,
            0x10, 0x39, 0x39, 0x1F, 0x22, 0x6F, 0x0A, 0x03, 0x3C, 0x09, 0x6E, 0x2D, 0x08, 0x36, 0x6C,
            0x36, 0x2D, 0x67, 0x67
        };
        private static string GetEcdsaPubKey()
        {
            var b = new byte[_ecdsaPubXor.Length];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(_ecdsaPubXor[i] ^ 0x5A);
            return System.Text.Encoding.UTF8.GetString(b);
        }

        private static bool CheckIntegrity()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    return false;

                var hashFile = Path.Combine(Path.GetDirectoryName(exePath)!, "integrity.hash");
                if (!File.Exists(hashFile))
                {
                    bool isSigned = IntegrityHash.IsSigned;

                    if (isSigned)
                    {
                        TM.App.Log("[ProtectionService] H file missing");
                        _startupBlockReason = $"缺少 integrity.hash（可能被杀软隔离）。请将安装目录加入白名单，或重新安装覆盖。\n路径: {hashFile}";
                        return true;
                    }
                    return false;
                }

                var content = File.ReadAllText(hashFile).Trim();
                if (string.IsNullOrEmpty(content))
                {
                    _startupBlockReason ??= $"integrity.hash 内容为空（可能被杀软破坏/隔离）。\n路径: {hashFile}";
                    return true;
                }

                var parts = content.Split('|');
                if (parts.Length < 2)
                {
                    _startupBlockReason ??= $"integrity.hash 格式异常（可能被篡改/破坏）。\n路径: {hashFile}";
                    return true;
                }

                var expectedHash = parts[0].Trim();
                var signatureBase64 = parts[1].Trim();

                using var ecdsa = System.Security.Cryptography.ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(GetEcdsaPubKey()), out _);
                var hashBytes = System.Text.Encoding.UTF8.GetBytes(expectedHash);
                var sigBytes = Convert.FromBase64String(signatureBase64);

                if (!ecdsa.VerifyData(hashBytes, sigBytes, System.Security.Cryptography.HashAlgorithmName.SHA256))
                {
                    TM.App.Log("[ProtectionService] sig fail");
                    _startupBlockReason ??= $"integrity.hash 签名校验失败（文件可能被篡改）。\n路径: {hashFile}";
                    return true;
                }

                var appDir = Path.GetDirectoryName(exePath)!;
                var filesToHash = new List<string>();

                var coreDll = Path.Combine(appDir, "天命.dll");
                if (File.Exists(coreDll)) filesToHash.Add(coreDll);
                var depsJson = Path.Combine(appDir, "天命.deps.json");
                if (File.Exists(depsJson)) filesToHash.Add(depsJson);
                var runtimeConfig = Path.Combine(appDir, "天命.runtimeconfig.json");
                if (File.Exists(runtimeConfig)) filesToHash.Add(runtimeConfig);

                foreach (var dll in Directory.GetFiles(appDir, "*.dll").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(dll);
                    if (name == "天命.dll" || name == "TMProtect.dll") continue;
                    filesToHash.Add(dll);
                }

                var allHashes = "";
                using var sha256 = SHA256.Create();
                foreach (var f in filesToHash)
                {
                    if (!File.Exists(f)) continue;
                    using var fs = File.OpenRead(f);
                    allHashes += Convert.ToHexString(sha256.ComputeHash(fs));
                }

                if (string.IsNullOrEmpty(allHashes))
                    return false;

                var combinedHash = Convert.ToHexString(
                    sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(allHashes)));

                var compromised = !combinedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
                if (compromised)
                {
                    _startupBlockReason ??= "完整性校验失败（文件可能被篡改或被杀软替换/隔离）。请将安装目录加入白名单或重新安装。";
                }
                return compromised;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckIntegrity), ex);
                _startupBlockReason ??= $"完整性校验异常: {ex.Message}";
                return true;
            }
        }

        #endregion

        #region C5

        private static bool CheckSandbox()
        {
            try
            {
                var username = Environment.UserName.ToLower();
                var sandboxUsers = new[] { "sandbox", "virus", "malware", "sample" };
                foreach (var su in sandboxUsers)
                {
                    if (username == su)
                        return true;
                }

                var computerName = Environment.MachineName.ToLower();
                if (computerName.Contains("sandbox") || computerName.Contains("virus") ||
                    computerName.Contains("malware") || computerName.Contains("sample"))
                {
                    return true;
                }

                var drives = DriveInfo.GetDrives();
                long totalSize = 0;
                foreach (var drive in drives)
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        totalSize += drive.TotalSize;
                    }
                }
                if (totalSize > 0 && totalSize < 50L * 1024 * 1024 * 1024)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckSandbox), ex);
                return false;
            }
        }

        #endregion

        #region C6

        private static bool ValidateCallChain()
        {
            try
            {
                var stackTrace = new StackTrace();
                var frames = stackTrace.GetFrames();

                if (frames == null || frames.Length < 2)
                    return false;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

                var baseDirFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var runtimeDirFull = string.IsNullOrWhiteSpace(runtimeDir) ? string.Empty : Path.GetFullPath(runtimeDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    if (method == null) continue;

                    var declaringType = method.DeclaringType;
                    if (declaringType == null) continue;

                    var asm = declaringType.Assembly;
                    if (asm == null) continue;

                    if (asm.IsDynamic)
                    {
                        TM.App.Log($"[ProtectionService] cc: {declaringType.FullName}.{method.Name}");
                        return false;
                    }

                    string location;
                    try { location = asm.Location; }
                    catch { location = string.Empty; }

                    if (string.IsNullOrWhiteSpace(location))
                    {
                        continue;
                    }

                    var asmDir = Path.GetDirectoryName(location);
                    if (string.IsNullOrWhiteSpace(asmDir))
                    {
                        continue;
                    }

                    var asmDirFull = Path.GetFullPath(asmDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    if (asmDirFull.StartsWith(baseDirFull, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(runtimeDirFull) && asmDirFull.StartsWith(runtimeDirFull, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TM.App.Log($"[ProtectionService] cc: {declaringType.FullName}.{method.Name}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ValidateCallChain), ex);
                return true;
            }
        }

        private static void CaptureMethodBaseline()
        {
            try
            {
                var type = typeof(ProtectionService);
                _cmBaselineMethods = type.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance).Length;
                _cmBaselineFields = type.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static).Length;
            }
            catch (Exception ex)
            {
                DebugLogOnce("CaptureBaseline", ex);
            }
        }

        private static bool CheckCriticalMethods()
        {
            try
            {
                if (_cmBaselineMethods == 0) return true;

                var type = typeof(ProtectionService);
                var currentMethods = type.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Instance).Length;

                if (currentMethods != _cmBaselineMethods)
                {
                    TM.App.Log($"[ProtectionService] cm1: {currentMethods}/{_cmBaselineMethods}");
                    return false;
                }

                var currentFields = type.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static).Length;

                if (currentFields != _cmBaselineFields)
                {
                    TM.App.Log($"[ProtectionService] cm2: {currentFields}/{_cmBaselineFields}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogOnce("CriticalCheck", ex);
                return false;
            }
        }

        #endregion

        #region C7

        private static void ApplyAntiDump()
        {
            try
            {
                var erasePeHeader = false;
                if (erasePeHeader)
                {
                    var baseAddr = System.Runtime.InteropServices.Marshal.GetHINSTANCE(
                        typeof(ProtectionService).Module);
                    if (baseAddr != IntPtr.Zero)
                    {
                        if (VirtualProtect(baseAddr, (UIntPtr)4096, 0x40, out uint oldProtect))
                        {
                            Marshal.WriteByte(baseAddr, 0, 0);
                            Marshal.WriteByte(baseAddr, 1, 0);
                            Marshal.WriteInt32(baseAddr, 0x3C, 0);
                            VirtualProtect(baseAddr, (UIntPtr)4096, oldProtect, out _);
                        }
                    }
                }

                var dbghelp = GetModuleHandle("dbghelp.dll");
                if (dbghelp != IntPtr.Zero)
                {
                    var miniDumpAddr = GetProcAddress(dbghelp, "MiniDumpWriteDump");
                    if (miniDumpAddr != IntPtr.Zero)
                    {
                        if (VirtualProtect(miniDumpAddr, (UIntPtr)1, 0x40, out uint old2))
                        {
                            Marshal.WriteByte(miniDumpAddr, 0xC3);
                            VirtualProtect(miniDumpAddr, (UIntPtr)1, old2, out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ApplyAntiDump), ex);
            }
        }

        #endregion

        #region C8

        private static bool CheckApiHook()
        {
            try
            {
                var kernel32 = GetModuleHandle("kernel32.dll");
                var ntdll = GetModuleHandle("ntdll.dll");

                var target = GetProcAddress(kernel32, "IsDebuggerPresent");
                if (target != IntPtr.Zero && IsHooked(target)) return true;

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckApiHook), ex);
                return false;
            }
        }

        private static bool IsHooked(IntPtr funcAddr)
        {
            byte b0 = Marshal.ReadByte(funcAddr);
            byte b1 = Marshal.ReadByte(funcAddr + 1);

            if (b0 == 0xE9) return true;
            if (b0 == 0xCC) return true;
            if (b0 == 0x68) return true;
            if (b0 == 0xFF && b1 == 0x25) return true;
            if (b0 == 0x48 && b1 == 0xB8)
            {
                byte b10 = Marshal.ReadByte(funcAddr + 10);
                byte b11 = Marshal.ReadByte(funcAddr + 11);
                if (b10 == 0xFF && b11 == 0xE0) return true;
            }
            if (b0 == 0x49 && b1 == 0xBB)
            {
                byte b10 = Marshal.ReadByte(funcAddr + 10);
                byte b11 = Marshal.ReadByte(funcAddr + 11);
                byte b12 = Marshal.ReadByte(funcAddr + 12);
                if (b10 == 0x41 && b11 == 0xFF && b12 == 0xE3) return true;
            }

            return false;
        }

        private static bool CheckClrHook()
        {
            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in loadedAssemblies)
                {
                    var name = asm.GetName().Name;
                    if (name != null && (
                        name.Contains("Harmony", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("MonoMod", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Detours", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckClrHook), ex);
                return false;
            }
        }

        private static bool CheckHardwareBreakpoints()
        {
            try
            {
                var context = new CONTEXT();
                context.ContextFlags = CONTEXT_DEBUG_REGISTERS;
                context.ExtendedRegisters = new byte[512];

                var currentThread = GetCurrentThread();
                if (GetThreadContext(currentThread, ref context))
                {
                    if (context.Dr0 != 0 || context.Dr1 != 0 ||
                        context.Dr2 != 0 || context.Dr3 != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckHardwareBreakpoints), ex);
                return false;
            }
        }

        private static bool CheckParentProcess()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var parentId = GetParentProcessId(currentProcess.Id);

                if (parentId > 0)
                {
                    var parentProcess = Process.GetProcessById(parentId);
                    var parentName = parentProcess.ProcessName.ToLower();

                    var suspiciousParents = new[]
                    {
                        "dnspy", "x64dbg", "x32dbg", "ollydbg", "ida", "ida64",
                        "windbg", "processhacker",
                        "cheatengine", "ce", "ghidra", "radare2", "cutter"
                    };

                    foreach (var suspicious in suspiciousParents)
                    {
                        if (parentName.Contains(suspicious))
                            return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckParentProcess), ex);
                return false;
            }
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                var query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}";
                using var searcher = new ManagementObjectSearcher(query);
                foreach (var obj in searcher.Get())
                {
                    return Convert.ToInt32(obj["ParentProcessId"]);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] pp err: {ex.Message}");
            }
            return -1;
        }

        private static bool CheckPebDebugFlag()
        {
            try
            {
                IntPtr debugFlags = IntPtr.Zero;
                int status = NtQueryInformationProcess(
                    GetCurrentProcess(),
                    0x1F,
                    ref debugFlags,
                    IntPtr.Size,
                    out _);

                if (status == 0)
                {
                    return debugFlags == IntPtr.Zero;
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(CheckPebDebugFlag), ex);
                return false;
            }
        }

        #endregion

        #region R

        public enum PunishmentLevel
        {
            None,
            Warning,
            DataWipe,
            Terminate
        }

        private static async Task TriggerPunishmentAsync(ProtectionCheckResult result)
        {
            switch (PL)
            {
                case PunishmentLevel.None:
                case PunishmentLevel.Warning:
                    break;

                case PunishmentLevel.DataWipe:
                    WipeLocalData();
                    break;

                case PunishmentLevel.Terminate:
                    var summary = result.GetThreatSummary();
                    TM.App.Log($"[ProtectionService] Terminate: {summary}, violationCount={_violationCount}");
                    try { TM.Framework.Common.Helpers.GlobalToast.Error("安全检测", "程序因安全验证失败即将退出"); } catch { }
                    await Task.Delay(Random.Shared.Next(1000, 5000));
                    TM.App.Log($"[ProtectionService] Exit now: {summary}");
                    await Task.Delay(200).ConfigureAwait(false);
                    Environment.Exit(-1);
                    break;
            }
        }

        private static void WipeLocalData()
        {
            try
            {
                var tokenPath = StoragePathHelper.GetFilePath("Framework", "User/Services", "auth_token.dat");
                if (File.Exists(tokenPath))
                    File.Delete(tokenPath);

                var settingsPath = StoragePathHelper.GetFilePath("Framework", "SystemSettings", "settings.json");
                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);

            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProtectionService] wipe err: {ex.Message}");
            }
        }

        #endregion
    }

    public class ProtectionCheckResult
    {
        public bool IsSafe { get; set; }
        public bool DebuggerDetected { get; set; }
        public bool VirtualMachineDetected { get; set; }
        public bool TimingAnomalyDetected { get; set; }
        public bool IntegrityCompromised { get; set; }
        public bool DelegateCompromised { get; set; }
        public bool SandboxDetected { get; set; }

        public bool ApiHookDetected { get; set; }
        public bool ClrHookDetected { get; set; }
        public bool HardwareBreakpointDetected { get; set; }
        public bool SuspiciousParentDetected { get; set; }
        public bool PebDebugFlag { get; set; }

        public bool CallChainCompromised { get; set; }
        public bool CriticalMethodsMissing { get; set; }

        public string GetThreatSummary()
        {
            var threats = new System.Collections.Generic.List<string>();
            if (DebuggerDetected) threats.Add("D");
            if (VirtualMachineDetected) threats.Add("V");
            if (TimingAnomalyDetected) threats.Add("T");
            if (IntegrityCompromised) threats.Add("I");
            if (DelegateCompromised) threats.Add("DL");
            if (SandboxDetected) threats.Add("S");
            if (ApiHookDetected) threats.Add("AH");
            if (ClrHookDetected) threats.Add("CH");
            if (HardwareBreakpointDetected) threats.Add("HB");
            if (SuspiciousParentDetected) threats.Add("SP");
            if (PebDebugFlag) threats.Add("PF");
            if (CallChainCompromised) threats.Add("CC");
            if (CriticalMethodsMissing) threats.Add("CM");
            return threats.Count > 0 ? string.Join(",", threats) : "-";
        }
    }
}
