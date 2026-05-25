using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services
{
    public class FileTypeAssociationService
    {
        private const string FileExtension = ".tm";
        private const string ProgId = "TM.Document";
        private const string FileDescription = "天命文档";

        public static bool AssociateFileType()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");

                if (!File.Exists(exePath))
                {
                    App.Log($"[FileAssociation] 可执行文件不存在: {exePath}");
                    return false;
                }

                using (var extKey = Registry.ClassesRoot.CreateSubKey(FileExtension))
                {
                    if (extKey == null)
                    {
                        App.Log("[FileAssociation] 无法创建扩展名注册表项（可能需要管理员权限）");
                        return false;
                    }

                    extKey.SetValue("", ProgId);
                }

                using (var progIdKey = Registry.ClassesRoot.CreateSubKey(ProgId))
                {
                    if (progIdKey == null)
                    {
                        App.Log("[FileAssociation] 无法创建ProgId注册表项");
                        return false;
                    }

                    progIdKey.SetValue("", FileDescription);

                    using (var iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey?.SetValue("", $"\"{exePath}\",0");
                    }

                    using (var commandKey = progIdKey.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                    }

                    using (var editKey = progIdKey.CreateSubKey(@"shell\edit\command"))
                    {
                        editKey?.SetValue("", $"\"{exePath}\" --edit \"%1\"");
                    }
                }

                App.Log($"[FileAssociation] 成功关联文件类型: {FileExtension}");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[FileAssociation] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[FileAssociation] 关联文件类型失败: {ex.Message}");
                return false;
            }
        }

        public static bool UnassociateFileType()
        {
            try
            {
                bool success = true;

                try
                {
                    using (var checkKey = Registry.ClassesRoot.OpenSubKey(FileExtension))
                    {
                        if (checkKey != null)
                        {
                            Registry.ClassesRoot.DeleteSubKeyTree(FileExtension, false);
                            App.Log($"[FileAssociation] 已删除扩展名键: {FileExtension}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[FileAssociation] 删除扩展名键失败: {ex.Message}");
                    success = false;
                }

                try
                {
                    using (var checkKey = Registry.ClassesRoot.OpenSubKey(ProgId))
                    {
                        if (checkKey != null)
                        {
                            Registry.ClassesRoot.DeleteSubKeyTree(ProgId, false);
                            App.Log($"[FileAssociation] 已删除ProgId键: {ProgId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[FileAssociation] 删除ProgId键失败: {ex.Message}");
                    success = false;
                }

                if (success)
                {
                    App.Log($"[FileAssociation] 成功取消文件类型关联: {FileExtension}");
                }

                return success;
            }
            catch (UnauthorizedAccessException)
            {
                App.Log("[FileAssociation] 权限不足，需要管理员权限才能修改HKEY_CLASSES_ROOT");
                return false;
            }
            catch (Exception ex)
            {
                App.Log($"[FileAssociation] 取消文件类型关联失败: {ex.Message}");
                return false;
            }
        }

        public static bool IsFileTypeAssociated()
        {
            try
            {
                using var extKey = Registry.ClassesRoot.OpenSubKey(FileExtension);
                if (extKey == null)
                    return false;

                var progId = extKey.GetValue("")?.ToString();
                return progId == ProgId;
            }
            catch (Exception ex)
            {
                App.Log($"[FileAssociation] 检查文件类型关联状态失败: {ex.Message}");
                return false;
            }
        }

        public static void HandleFileOpen(string filePath, bool isEdit = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    App.Log($"[FileAssociation] 文件不存在: {filePath}");
                    ToastNotification.ShowError("文件错误", "指定的文件不存在");
                    return;
                }

                var fileName = Path.GetFileName(filePath);
                var mode = isEdit ? "编辑" : "打开";

                if (isEdit)
                {
                    System.Diagnostics.Process.Start("notepad.exe", filePath);
                    App.Log($"[FileAssociation] 已在编辑器中打开文件: {filePath}");
                    ToastNotification.ShowSuccess($"{mode}文件", $"已在编辑器中打开：\n{fileName}");
                }
                else
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true,
                        Verb = "open"
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    App.Log($"[FileAssociation] 已打开文件: {filePath}");
                    ToastNotification.ShowSuccess($"{mode}文件", $"已打开：\n{fileName}");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[FileAssociation] 处理文件打开失败: {ex.Message}");
                ToastNotification.ShowError("文件错误", $"无法打开文件：{ex.Message}");
            }
        }

        public static void RefreshIconCache()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ie4uinit.exe",
                    Arguments = "-show",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                App.Log("[FileAssociation] 已刷新图标缓存");
            }
            catch (Exception ex)
            {
                App.Log($"[FileAssociation] 刷新图标缓存失败: {ex.Message}");
            }
        }
    }
}

