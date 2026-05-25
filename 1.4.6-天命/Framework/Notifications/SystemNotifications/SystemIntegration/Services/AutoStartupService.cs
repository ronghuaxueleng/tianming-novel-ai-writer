using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services
{
    public class AutoStartupService
    {
        private const string AppName = "天命";
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool SetAutoStartup(bool enable, string startupMode = "Normal", int delaySeconds = 0)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    App.Log("[AutoStartup] 无法打开注册表项");
                    return false;
                }

                if (enable)
                {
                    var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                    if (!File.Exists(exePath))
                    {
                        App.Log($"[AutoStartup] 可执行文件不存在: {exePath}");
                        return false;
                    }

                    var startupCommand = $"\"{exePath}\"";

                    if (startupMode == "1")
                    {
                        startupCommand += " --minimized";
                    }

                    if (delaySeconds > 0)
                    {
                        startupCommand += $" --delay {delaySeconds}";
                    }

                    key.SetValue(AppName, startupCommand);
                    App.Log($"[AutoStartup] 已设置开机自启动: {startupCommand}");
                    return true;
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName);
                        App.Log("[AutoStartup] 已取消开机自启动");
                    }
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[AutoStartup] 权限不足，无法修改注册表");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[AutoStartup] 设置开机自启动失败: {ex.Message}");
                return false;
            }
        }

        public static bool IsAutoStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                if (key == null)
                    return false;

                var value = key.GetValue(AppName);
                return value != null;
            }
            catch (Exception ex)
            {
                App.Log($"[AutoStartup] 检查开机自启动状态失败: {ex.Message}");
                return false;
            }
        }

        public static string? GetStartupCommand()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                if (key == null)
                    return null;

                return key.GetValue(AppName)?.ToString();
            }
            catch (Exception ex)
            {
                App.Log($"[AutoStartup] 获取启动命令失败: {ex.Message}");
                return null;
            }
        }
    }
}

