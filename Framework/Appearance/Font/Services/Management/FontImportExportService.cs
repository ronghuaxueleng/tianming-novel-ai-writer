using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontImportExportService
    {
        public FontImportExportService() { }

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

                var tmpFieA = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpFieA))
                {
                    await JsonSerializer.SerializeAsync(stream, config, JsonHelper.Default);
                }
                File.Move(tmpFieA, filePath, overwrite: true);

                TM.App.Log($"[FontImportExport] 异步导出配置成功: {filePath}");
                GlobalToast.Success("导出成功", $"字体配置已保存到:\n{Path.GetFileName(filePath)}");

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 异步导出失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败：{ex.Message}", "导出失败");
                return false;
            }
        }

        public async System.Threading.Tasks.Task<bool> ImportConfigurationAsync(string? filePath = null)
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
                        return false;
                    filePath = dialog.FileName;
                }

                if (!File.Exists(filePath))
                {
                    StandardDialog.ShowError("文件不存在", "错误");
                    return false;
                }

                var confirm = StandardDialog.ShowConfirm("导入配置将覆盖当前字体设置，是否继续？", "确认导入");
                if (!confirm)
                    return false;

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var result = System.Text.Json.JsonSerializer.Deserialize<TM.Framework.Appearance.Font.Models.FontConfiguration>(json, JsonHelper.Default);

                if (result == null)
                {
                    StandardDialog.ShowError("配置文件格式无效", "错误");
                    return false;
                }

                FontManager.SaveConfiguration(result);
                FontManager.ApplyUIFont(result.UIFont);
                FontManager.ApplyEditorFont(result.EditorFont);

                TM.App.Log($"[FontImportExport] 导入配置成功: {filePath}");
                GlobalToast.Success("导入成功", "字体配置已应用");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 导入失败: {ex.Message}");
                StandardDialog.ShowError($"导入失败：{ex.Message}", "导入失败");
                return false;
            }
        }

        public async System.Threading.Tasks.Task<bool> ExportAsShareableAsync(string? filePath = null)
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
                        return false;
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

                var tmpFieSh = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpFieSh))
                {
                    await System.Text.Json.JsonSerializer.SerializeAsync(stream, sharePackage, JsonHelper.Default);
                }
                File.Move(tmpFieSh, filePath, overwrite: true);

                TM.App.Log($"[FontImportExport] 导出分享包成功: {filePath}");
                GlobalToast.Success("导出成功", $"分享包已创建:\n{Path.GetFileName(filePath)}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontImportExport] 导出分享包失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败：{ex.Message}", "导出失败");
                return false;
            }
        }

    }
}

