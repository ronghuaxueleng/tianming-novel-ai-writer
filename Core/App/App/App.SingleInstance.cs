using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace TM
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class App
    {
        private const string SingleInstanceMutexName = @"Local\TM_TianMing_SingleInstance_v1";

        private const string SingleInstanceActivateMessage = "TM_TianMing_ActivateInstance_v1";

        public static uint SingleInstanceMessageId { get; private set; }

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int ASFW_ANY = -1;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private Mutex? _singleInstanceMutex;

        private bool _isSingleInstanceOwner;

        private bool TryAcquireSingleInstance()
        {
            try
            {
                SingleInstanceMessageId = RegisterWindowMessageW(SingleInstanceActivateMessage);
            }
            catch (Exception ex)
            {
                Log($"[SingleInstance] RegisterWindowMessage 失败（将跳过激活广播）: {ex.Message}");
                SingleInstanceMessageId = 0;
            }

            bool createdNew = false;
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out createdNew);
            }
            catch (Exception ex)
            {
                Log($"[SingleInstance] Mutex 创建异常（保守放行为首个实例）: {ex.Message}");
                _singleInstanceMutex = null;
                _isSingleInstanceOwner = false;
                return true;
            }

            if (createdNew)
            {
                _isSingleInstanceOwner = true;
                Log("[SingleInstance] 当前为首个实例，已占用 Mutex");
                return true;
            }

            _isSingleInstanceOwner = false;
            Log("[SingleInstance] 检测到已有实例在运行，广播激活消息后将退出");

            try
            {
                try { AllowSetForegroundWindow(ASFW_ANY); }
                catch { }

                if (SingleInstanceMessageId != 0)
                {
                    PostMessage(HWND_BROADCAST, SingleInstanceMessageId, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Log($"[SingleInstance] 广播激活消息失败: {ex.Message}");
            }

            try
            {
                _singleInstanceMutex?.Dispose();
            }
            catch { }
            _singleInstanceMutex = null;

            return false;
        }

        private void ReleaseSingleInstance()
        {
            if (_singleInstanceMutex == null) return;

            if (_isSingleInstanceOwner)
            {
                try { _singleInstanceMutex.ReleaseMutex(); }
                catch (Exception ex) { Log($"[SingleInstance] ReleaseMutex 异常: {ex.Message}"); }
            }

            try { _singleInstanceMutex.Dispose(); }
            catch { }

            _singleInstanceMutex = null;
            _isSingleInstanceOwner = false;
        }
    }
}
