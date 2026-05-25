using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public partial class SystemInfoViewModel
    {
        private async Task ExportInfo()
        {
            try
            {
                var exportPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Info/SystemInfo",
                    $"system_info_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var sb = new StringBuilder();
                sb.AppendLine("==================== 系统信息报告 ====================");
                sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                sb.AppendLine("【操作系统信息】");
                sb.AppendLine($"  操作系统: {OSName}");
                sb.AppendLine($"  版本: {OSVersion}");
                sb.AppendLine($"  架构: {OSArchitecture}");
                sb.AppendLine($"  计算机名: {ComputerName}");
                sb.AppendLine($"  用户名: {UserName}");
                sb.AppendLine();

                sb.AppendLine("【CPU信息】");
                sb.AppendLine($"  处理器: {CPUName}");
                sb.AppendLine($"  物理核心: {CPUCores}");
                sb.AppendLine($"  逻辑处理器: {CPULogicalProcessors}");
                sb.AppendLine();

                sb.AppendLine("【内存信息】");
                sb.AppendLine($"  总内存: {TotalMemory}");
                sb.AppendLine();

                sb.AppendLine("【磁盘信息】");
                foreach (var drive in DriveInfos)
                {
                    sb.AppendLine($"  驱动器 {drive.DriveName}:");
                    sb.AppendLine($"    总容量: {drive.TotalSize}");
                    sb.AppendLine($"    已用: {drive.UsedSize}");
                    sb.AppendLine($"    剩余: {drive.FreeSize}");
                    sb.AppendLine($"    使用率: {drive.UsagePercent}%");
                }
                sb.AppendLine();

                sb.AppendLine("【显示器信息】");
                sb.AppendLine($"  显示器数量: {ScreenCount}");
                sb.AppendLine($"  主显示器分辨率: {ScreenResolution}");
                sb.AppendLine();

                sb.AppendLine("【网络适配器】");
                foreach (var adapter in NetworkAdapters)
                {
                    sb.AppendLine($"  {adapter.Name}:");
                    sb.AppendLine($"    描述: {adapter.Description}");
                    sb.AppendLine($"    IP地址: {adapter.IPAddress}");
                    sb.AppendLine($"    MAC地址: {adapter.MACAddress}");
                    sb.AppendLine($"    网关: {adapter.Gateway}");
                }

                await File.WriteAllTextAsync(exportPath, sb.ToString());

                TM.App.Log($"[SystemInfo] 导出系统信息成功: {exportPath}");
                GlobalToast.Success("导出成功", $"系统信息已导出到: {exportPath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 导出系统信息失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"导出失败：{ex.Message}");
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
