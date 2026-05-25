using System;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Windows;
using TM.Framework.Appearance.Font.Models;
using Microsoft.Win32;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontImportExportService
    {
        public FontImportExportService() { }

        public bool ExportConfiguration(string? filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "字体配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                        DefaultExt = ".json",
                        FileName = $"FontConfig_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return false;
                    }

                    filePath = dialog.FileName;
                }

                var config = FontManager.LoadConfiguration();
                var json = JsonSerializer.Serialize(config, JsonHelper.Default);

                var tmpFie = filePath + ".tmp";
                File.WriteAllText(tmpFie, json);
                File.Move(tmpFie, filePath, overwrite: true);

                TM.App.Log($"[FontImportExport] 导出配置成功: {filePath}");
                GlobalToast.Success("导出成功", $"字体配置已保存到:\n{Path.GetFileName(filePath)}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 导出失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败: {ex.Message}", "错误");
                return false;
            }
        }

        public async System.Threading.Tasks.Task<bool> ExportConfigurationAsync(string? filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "字体配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                        DefaultExt = ".json",
                        FileName = $"FontConfig_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return false;
                    }

                    filePath = dialog.FileName;
                }

                var config = FontManager.LoadConfiguration();
                var json = JsonSerializer.Serialize(config, JsonHelper.Default);

                var tmpFieA = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpFieA, json);
                File.Move(tmpFieA, filePath, overwrite: true);

                TM.App.Log($"[FontImportExport] 异步导出配置成功: {filePath}");
                GlobalToast.Success("导出成功", $"字体配置已保存到:\n{Path.GetFileName(filePath)}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 异步导出失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败: {ex.Message}", "错误");
                return false;
            }
        }

        public bool ImportConfiguration(string? filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    var dialog = new OpenFileDialog
                    {
                        Filter = "字体配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                        DefaultExt = ".json"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return false;
                    }

                    filePath = dialog.FileName;
                }

                if (!File.Exists(filePath))
                {
                    StandardDialog.ShowError("文件不存在", "错误");
                    return false;
                }

                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<FontConfiguration>(json);

                if (config == null)
                {
                    StandardDialog.ShowError("配置文件格式无效", "错误");
                    return false;
                }

                var confirm = StandardDialog.ShowConfirm(
                    "导入配置将覆盖当前字体设置，是否继续？",
                    "确认导入"
                );

                if (!confirm)
                {
                    return false;
                }

                FontManager.SaveConfiguration(config);
                FontManager.ApplyUIFont(config.UIFont);
                FontManager.ApplyEditorFont(config.EditorFont);

                TM.App.Log($"[FontImportExport] 导入配置成功: {filePath}");
                GlobalToast.Success("导入成功", "字体配置已应用");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 导入失败: {ex.Message}");
                StandardDialog.ShowError($"导入失败: {ex.Message}", "错误");
                return false;
            }
        }

        public bool ExportAsShareable(string? filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "字体配置分享包 (*.fontshare)|*.fontshare|所有文件 (*.*)|*.*",
                        DefaultExt = ".fontshare",
                        FileName = $"FontShare_{DateTime.Now:yyyyMMdd_HHmmss}.fontshare"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return false;
                    }

                    filePath = dialog.FileName;
                }

                var config = FontManager.LoadConfiguration();
                var sharePackage = new
                {
                    Version = "1.0",
                    ExportTime = DateTime.Now,
                    ExportBy = Environment.UserName,
                    Configuration = config
                };

                var json = JsonSerializer.Serialize(sharePackage, JsonHelper.Default);

                var tmpFieSh = filePath + ".tmp";
                File.WriteAllText(tmpFieSh, json);
                File.Move(tmpFieSh, filePath, overwrite: true);

                TM.App.Log($"[FontImportExport] 导出分享包成功: {filePath}");
                GlobalToast.Success("导出成功", $"分享包已创建:\n{Path.GetFileName(filePath)}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 导出分享包失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败: {ex.Message}", "错误");
                return false;
            }
        }
    }
}

