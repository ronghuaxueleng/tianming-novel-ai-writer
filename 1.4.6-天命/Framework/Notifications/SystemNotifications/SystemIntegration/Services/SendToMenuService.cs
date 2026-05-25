using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using File = System.IO.File;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services
{
    public class SendToMenuService
    {
        private const string ShortcutName = "天命.lnk";

        private static string GetSendToFolder()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        }

        private static string GetShortcutPath()
        {
            return Path.Combine(GetSendToFolder(), ShortcutName);
        }

        public static bool AddToSendToMenu()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                if (!File.Exists(exePath))
                {
                    App.Log($"[SendToMenu] 可执行文件不存在: {exePath}");
                    return false;
                }

                var sendToFolder = GetSendToFolder();
                if (!Directory.Exists(sendToFolder))
                {
                    App.Log($"[SendToMenu] 发送到文件夹不存在: {sendToFolder}");
                    return false;
                }

                var shortcutPath = GetShortcutPath();

                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    App.Log("[SendToMenu] 删除旧的快捷方式");
                }

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    App.Log("[SendToMenu] 无法获取WScript.Shell COM对象");
                    return false;
                }

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    App.Log("[SendToMenu] 无法创建WScript.Shell实例");
                    return false;
                }

                try
                {
                    dynamic? shortcut = shell.CreateShortcut(shortcutPath);
                    if (shortcut == null)
                    {
                        App.Log("[SendToMenu] 无法创建快捷方式对象");
                        return false;
                    }

                    shortcut.TargetPath = exePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                    shortcut.Description = "使用天命打开文件";
                    shortcut.IconLocation = $"{exePath},0";
                    shortcut.Arguments = "\"%1\"";
                    shortcut.Save();

                    Marshal.ReleaseComObject(shortcut);
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }

                App.Log($"[SendToMenu] 成功添加到发送到菜单: {shortcutPath}");
                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[SendToMenu] 添加到发送到菜单失败: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveFromSendToMenu()
        {
            try
            {
                var shortcutPath = GetShortcutPath();

                if (!File.Exists(shortcutPath))
                {
                    App.Log("[SendToMenu] 快捷方式不存在，无需删除");
                    return true;
                }

                File.Delete(shortcutPath);
                App.Log($"[SendToMenu] 成功从发送到菜单移除: {shortcutPath}");
                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[SendToMenu] 从发送到菜单移除失败: {ex.Message}");
                return false;
            }
        }

        public static bool IsAddedToSendToMenu()
        {
            try
            {
                var shortcutPath = GetShortcutPath();
                return File.Exists(shortcutPath);
            }
            catch (Exception ex)
            {
                App.Log($"[SendToMenu] 检查发送到菜单状态失败: {ex.Message}");
                return false;
            }
        }

        public static void HandleSendToAction(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    App.Log("[SendToMenu] 文件路径为空");
                    return;
                }

                if (!File.Exists(filePath))
                {
                    App.Log($"[SendToMenu] 文件不存在: {filePath}");
                    ToastNotification.ShowError("文件错误", "指定的文件不存在");
                    return;
                }

                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath).ToLower();

                App.Log($"[SendToMenu] 通过发送到处理文件: {filePath}");

                switch (extension)
                {
                    case ".txt":
                    case ".md":
                    case ".json":
                    case ".xml":
                        var content = File.ReadAllText(filePath);
                        ToastNotification.ShowInfo("文本文件", $"{fileName}\n已读取 {content.Length} 个字符");
                        App.Log($"[SendToMenu] 读取文本文件，长度: {content.Length}");
                        break;

                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                    case ".bmp":
                        ToastNotification.ShowInfo("图片文件", $"{fileName}\n可设置为用户头像或主题背景");
                        App.Log($"[SendToMenu] 检测到图片文件: {fileName}");
                        break;

                    default:
                        var importsPath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Imports");
                        Directory.CreateDirectory(importsPath);
                        var targetPath = Path.Combine(importsPath, fileName);
                        File.Copy(filePath, targetPath, true);

                        ToastNotification.ShowSuccess("文件已导入", $"{fileName}\n已保存到应用数据目录");
                        App.Log($"[SendToMenu] 文件已复制到: {targetPath}");
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SendToMenu] 处理发送到操作失败: {ex.Message}");
                ToastNotification.ShowError("错误", $"操作失败：{ex.Message}");
            }
        }
    }
}

