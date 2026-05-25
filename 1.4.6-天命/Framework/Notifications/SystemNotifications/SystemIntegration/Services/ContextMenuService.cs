using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services
{
    public class ContextMenuService
    {
        private const string MenuName = "TM";
        private const string MenuDisplayName = "使用天命打开";

        public static bool AddToFileContextMenu()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                if (!File.Exists(exePath))
                {
                    App.Log($"[ContextMenu] 可执行文件不存在: {exePath}");
                    return false;
                }

                using (var shellKey = Registry.ClassesRoot.CreateSubKey(@"*\shell\" + MenuName))
                {
                    if (shellKey == null)
                    {
                        App.Log("[ContextMenu] 无法创建文件菜单注册表项（可能需要管理员权限）");
                        return false;
                    }

                    shellKey.SetValue("", MenuDisplayName);

                    shellKey.SetValue("Icon", $"\"{exePath}\",0");

                    using (var commandKey = shellKey.CreateSubKey("command"))
                    {
                        commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                App.Log("[ContextMenu] 成功添加到文件右键菜单");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[ContextMenu] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 添加文件右键菜单失败: {ex.Message}");
                return false;
            }
        }

        public static bool AddToDirectoryContextMenu()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                if (!File.Exists(exePath))
                {
                    App.Log($"[ContextMenu] 可执行文件不存在: {exePath}");
                    return false;
                }

                using (var shellKey = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\" + MenuName))
                {
                    if (shellKey == null)
                    {
                        App.Log("[ContextMenu] 无法创建文件夹菜单注册表项（可能需要管理员权限）");
                        return false;
                    }

                    shellKey.SetValue("", MenuDisplayName);
                    shellKey.SetValue("Icon", $"\"{exePath}\",0");

                    using (var commandKey = shellKey.CreateSubKey("command"))
                    {
                        commandKey?.SetValue("", $"\"{exePath}\" --folder \"%1\"");
                    }
                }

                using (var backgroundKey = Registry.ClassesRoot.CreateSubKey(@"Directory\Background\shell\" + MenuName))
                {
                    if (backgroundKey != null)
                    {
                        backgroundKey.SetValue("", "在此处打开天命");
                        backgroundKey.SetValue("Icon", $"\"{exePath}\",0");

                        using (var commandKey = backgroundKey.CreateSubKey("command"))
                        {
                            commandKey?.SetValue("", $"\"{exePath}\" --folder \"%V\"");
                        }
                    }
                }

                App.Log("[ContextMenu] 成功添加到文件夹右键菜单");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[ContextMenu] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 添加文件夹右键菜单失败: {ex.Message}");
                return false;
            }
        }

        public static bool AddToContextMenu()
        {
            bool fileSuccess = AddToFileContextMenu();
            bool dirSuccess = AddToDirectoryContextMenu();

            return fileSuccess && dirSuccess;
        }

        public static bool RemoveFromFileContextMenu()
        {
            try
            {
                using (var checkKey = Registry.ClassesRoot.OpenSubKey(@"*\shell\" + MenuName))
                {
                    if (checkKey != null)
                    {
                        Registry.ClassesRoot.DeleteSubKeyTree(@"*\shell\" + MenuName, false);
                        App.Log("[ContextMenu] 已从文件右键菜单移除");
                    }
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[ContextMenu] 权限不足，需要管理员权限");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 移除文件右键菜单失败: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveFromDirectoryContextMenu()
        {
            try
            {
                bool success = true;

                try
                {
                    using (var checkKey = Registry.ClassesRoot.OpenSubKey(@"Directory\shell\" + MenuName))
                    {
                        if (checkKey != null)
                        {
                            Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\" + MenuName, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[ContextMenu] 移除文件夹菜单失败: {ex.Message}");
                    success = false;
                }

                try
                {
                    using (var checkKey = Registry.ClassesRoot.OpenSubKey(@"Directory\Background\shell\" + MenuName))
                    {
                        if (checkKey != null)
                        {
                            Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\Background\shell\" + MenuName, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[ContextMenu] 移除背景菜单失败: {ex.Message}");
                    success = false;
                }

                if (success)
                {
                    App.Log("[ContextMenu] 已从文件夹右键菜单移除");
                }

                return success;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[ContextMenu] 权限不足，需要管理员权限");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 移除文件夹右键菜单失败: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveFromContextMenu()
        {
            bool fileSuccess = RemoveFromFileContextMenu();
            bool dirSuccess = RemoveFromDirectoryContextMenu();

            return fileSuccess && dirSuccess;
        }

        public static bool IsAddedToContextMenu()
        {
            try
            {
                using var fileKey = Registry.ClassesRoot.OpenSubKey(@"*\shell\" + MenuName);
                using var dirKey = Registry.ClassesRoot.OpenSubKey(@"Directory\shell\" + MenuName);

                return fileKey != null || dirKey != null;
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 检查右键菜单状态失败: {ex.Message}");
                return false;
            }
        }

        public static void HandleContextMenuAction(string path, bool isFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    App.Log("[ContextMenu] 路径为空");
                    return;
                }

                if (isFolder && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    App.Log($"[ContextMenu] 已打开文件夹: {path}");
                    ToastNotification.ShowSuccess("右键菜单", $"已打开文件夹：\n{Path.GetFileName(path)}");
                }
                else if (File.Exists(path))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    App.Log($"[ContextMenu] 已打开文件: {path}");
                    ToastNotification.ShowSuccess("右键菜单", $"已打开文件：\n{Path.GetFileName(path)}");
                }
                else
                {
                    App.Log($"[ContextMenu] 路径不存在: {path}");
                    ToastNotification.ShowError("错误", "指定的路径不存在");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ContextMenu] 处理右键菜单操作失败: {ex.Message}");
                ToastNotification.ShowError("错误", $"操作失败：{ex.Message}");
            }
        }
    }
}

