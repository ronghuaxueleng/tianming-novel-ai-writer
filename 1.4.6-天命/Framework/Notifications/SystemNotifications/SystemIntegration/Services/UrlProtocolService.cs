using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services
{
    public class UrlProtocolService
    {
        private const string ProtocolName = "TM";
        private const string ProtocolDescription = "TM Protocol";

        public static bool RegisterUrlProtocol()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                if (!File.Exists(exePath))
                {
                    App.Log($"[UrlProtocol] 可执行文件不存在: {exePath}");
                    return false;
                }

                using (var protocolKey = Registry.ClassesRoot.CreateSubKey(ProtocolName))
                {
                    if (protocolKey == null)
                    {
                        App.Log("[UrlProtocol] 无法创建注册表项（可能需要管理员权限）");
                        return false;
                    }

                    protocolKey.SetValue("", $"URL:{ProtocolDescription}");
                    protocolKey.SetValue("URL Protocol", "");

                    using (var iconKey = protocolKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey?.SetValue("", $"\"{exePath}\",0");
                    }

                    using (var commandKey = protocolKey.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                App.Log($"[UrlProtocol] 成功注册URL协议: {ProtocolName}://");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[UrlProtocol] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[UrlProtocol] 注册URL协议失败: {ex.Message}");
                return false;
            }
        }

        public static bool UnregisterUrlProtocol()
        {
            try
            {
                using (var checkKey = Registry.ClassesRoot.OpenSubKey(ProtocolName))
                {
                    if (checkKey == null)
                    {
                        App.Log("[UrlProtocol] URL协议未注册，无需删除");
                        return true;
                    }
                }

                Registry.ClassesRoot.DeleteSubKeyTree(ProtocolName, false);
                App.Log($"[UrlProtocol] 成功取消注册URL协议: {ProtocolName}://");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[UrlProtocol] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[UrlProtocol] 取消注册URL协议失败: {ex.Message}");
                return false;
            }
        }

        public static bool IsUrlProtocolRegistered()
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(ProtocolName);
                if (key == null)
                    return false;

                var urlProtocol = key.GetValue("URL Protocol");
                return urlProtocol != null;
            }
            catch (Exception ex)
            {
                App.Log($"[UrlProtocol] 检查URL协议状态失败: {ex.Message}");
                return false;
            }
        }

        public static void HandleUrlProtocol(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return;

                var prefix = $"{ProtocolName}://";
                if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    App.Log($"[UrlProtocol] 无效的URL格式: {url}");
                    return;
                }

                var path = url.Substring(prefix.Length);
                App.Log($"[UrlProtocol] 接收到URL调用: {path}");

                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                {
                    App.Log("[UrlProtocol] URL路径为空");
                    return;
                }

                var action = parts[0];

                switch (action.ToLower())
                {
                    case "open":
                        App.Log("[UrlProtocol] 执行操作: 打开主窗口");
                        ToastNotification.ShowInfo("URL协议", "通过URL协议打开了程序");
                        break;

                    case "settings":
                        App.Log("[UrlProtocol] 执行操作: 打开设置");
                        ToastNotification.ShowInfo("URL协议", "打开设置功能");
                        break;

                    case "theme":
                        if (parts.Length >= 2)
                        {
                            var themeName = Uri.UnescapeDataString(parts[1]);
                            App.Log($"[UrlProtocol] 执行操作: 切换主题 - {themeName}");
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                try
                                {
                                    ServiceLocator.Get<TM.Framework.Appearance.ThemeManagement.ThemeManager>().ApplyTheme(themeName);
                                    ToastNotification.ShowSuccess("主题切换", $"已切换到 {themeName}");
                                }
                                catch (Exception ex)
                                {
                                    ToastNotification.ShowError("主题切换失败", ex.Message);
                                }
                            });
                        }
                        break;

                    case "user":
                        App.Log("[UrlProtocol] 执行操作: 打开用户资料");
                        ToastNotification.ShowInfo("URL协议", "打开用户资料功能");
                        break;

                    case "notify":
                        if (parts.Length >= 2)
                        {
                            var message = Uri.UnescapeDataString(parts[1]);
                            App.Log($"[UrlProtocol] 执行操作: 显示通知 - {message}");
                            ToastNotification.ShowInfo("URL协议通知", message);
                        }
                        break;

                    default:
                        App.Log($"[UrlProtocol] 未知的操作: {action}");
                        ToastNotification.ShowWarning("URL协议", $"未知的操作: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[UrlProtocol] 处理URL协议失败: {ex.Message}");
            }
        }
    }
}

