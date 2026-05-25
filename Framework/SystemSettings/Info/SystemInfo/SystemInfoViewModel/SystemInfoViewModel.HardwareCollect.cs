using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public partial class SystemInfoViewModel
    {
        private async System.Threading.Tasks.Task CollectOSInfo()
        {
            try
            {
                var osName = Environment.OSVersion.Platform.ToString();
                var osVersion = Environment.OSVersion.Version.ToString();
                var osArch = Environment.Is64BitOperatingSystem ? "64位" : "32位";
                var computerName = Environment.MachineName;
                var userName = Environment.UserName;

                using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (ManagementObject os in searcher.Get())
                {
                    osName = os["Caption"]?.ToString() ?? osName;
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OSName = osName;
                    OSVersion = osVersion;
                    OSArchitecture = osArch;
                    ComputerName = computerName;
                    UserName = userName;
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集操作系统信息失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CollectCPUInfo()
        {
            try
            {
                var logicalProcessors = Environment.ProcessorCount;
                var cpuName = "Unknown";
                var cpuCores = 0;
                var cpuArch = "未知";
                var cpuBaseFreq = "";
                var cpuMaxFreq = "";
                var cpuL1 = "";
                var cpuL2 = "";
                var cpuL3 = "";
                var cpuVirt = "未启用";

                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject cpu in searcher.Get())
                {
                    cpuName = cpu["Name"]?.ToString()?.Trim() ?? "Unknown";
                    cpuCores = Convert.ToInt32(cpu["NumberOfCores"] ?? 0);

                    var arch = cpu["Architecture"];
                    cpuArch = arch != null ? GetArchitectureName(Convert.ToInt32(arch)) : "未知";

                    var maxClockSpeed = cpu["MaxClockSpeed"];
                    if (maxClockSpeed != null)
                    {
                        var mhz = Convert.ToInt32(maxClockSpeed);
                        cpuBaseFreq = $"{mhz} MHz ({mhz / 1000.0:F2} GHz)";
                    }

                    cpuMaxFreq = cpuBaseFreq;

                    var l2CacheSize = cpu["L2CacheSize"];
                    if (l2CacheSize != null)
                    {
                        var kb = Convert.ToInt32(l2CacheSize);
                        cpuL2 = kb >= 1024 ? $"{kb / 1024} MB" : $"{kb} KB";
                    }

                    var l3CacheSize = cpu["L3CacheSize"];
                    if (l3CacheSize != null)
                    {
                        var kb = Convert.ToInt32(l3CacheSize);
                        cpuL3 = kb >= 1024 ? $"{kb / 1024} MB" : $"{kb} KB";
                    }

                    cpuL1 = $"{cpuCores * 64} KB (估算)";

                    var virtualizationEnabled = cpu["VirtualizationFirmwareEnabled"];
                    cpuVirt = virtualizationEnabled != null && Convert.ToBoolean(virtualizationEnabled) ? "已启用 ✓" : "未启用";

                    break;
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CPULogicalProcessors = logicalProcessors;
                    CPUName = cpuName;
                    CPUCores = cpuCores;
                    CPUArchitecture = cpuArch;
                    CPUBaseFrequency = cpuBaseFreq;
                    CPUMaxFrequency = cpuMaxFreq;
                    CPUL1Cache = cpuL1;
                    CPUL2Cache = cpuL2;
                    CPUL3Cache = cpuL3;
                    CPUVirtualization = cpuVirt;
                });

                TM.App.Log($"[SystemInfo] CPU信息采集完成: {cpuName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集CPU信息失败: {ex.Message}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CPUName = "无法获取";
                    CPUCores = 0;
                });
            }
        }

        private string GetArchitectureName(int arch)
        {
            return arch switch
            {
                0 => "x86",
                1 => "MIPS",
                2 => "Alpha",
                3 => "PowerPC",
                5 => "ARM",
                6 => "ia64",
                9 => "x64 (AMD64)",
                12 => "ARM64",
                _ => $"未知 ({arch})"
            };
        }

        private async System.Threading.Tasks.Task CollectMemoryInfo()
        {
            try
            {
                string totalMemory = "无法获取";
                string totalVirtualMemory = "无法获取";
                string availableVirtualMemory = "无法获取";
                string pageFileSize = "无法获取";

                using var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject os in osSearcher.Get())
                {
                    var totalKB = Convert.ToInt64(os["TotalVisibleMemorySize"]);
                    totalMemory = FormatBytes(totalKB * 1024);

                    var totalVirtual = Convert.ToInt64(os["TotalVirtualMemorySize"]);
                    var freeVirtual = Convert.ToInt64(os["FreeVirtualMemory"]);
                    totalVirtualMemory = FormatBytes(totalVirtual * 1024);
                    availableVirtualMemory = FormatBytes(freeVirtual * 1024);

                    break;
                }

                try
                {
                    using var pageFileSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileUsage");
                    foreach (ManagementObject pageFile in pageFileSearcher.Get())
                    {
                        var allocatedSize = Convert.ToInt64(pageFile["AllocatedBaseSize"]);
                        pageFileSize = FormatBytes(allocatedSize * 1024 * 1024);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("GetPageFileSize", ex);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TotalMemory = totalMemory;
                    TotalVirtualMemory = totalVirtualMemory;
                    AvailableVirtualMemory = availableVirtualMemory;
                    PageFileSize = pageFileSize;
                });

                var memoryModulesList = new List<MemoryModuleItem>();
                using var memSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                foreach (ManagementObject mem in memSearcher.Get())
                {
                    try
                    {
                        var capacity = mem["Capacity"];
                        var capacityBytes = capacity != null ? Convert.ToInt64(capacity) : 0;

                        var memoryType = mem["SMBIOSMemoryType"];
                        var memoryTypeStr = GetMemoryTypeName(memoryType != null ? Convert.ToInt32(memoryType) : 0);

                        var speed = mem["Speed"];
                        var speedStr = speed != null ? $"{speed} MHz" : "未知";

                        memoryModulesList.Add(new MemoryModuleItem
                        {
                            BankLabel = mem["BankLabel"]?.ToString() ?? mem["DeviceLocator"]?.ToString() ?? "未知",
                            Capacity = FormatBytes(capacityBytes),
                            MemoryType = memoryTypeStr,
                            Speed = speedStr,
                            Manufacturer = mem["Manufacturer"]?.ToString()?.Trim() ?? "未知",
                            PartNumber = mem["PartNumber"]?.ToString()?.Trim() ?? "未知",
                            SerialNumber = mem["SerialNumber"]?.ToString()?.Trim() ?? "未知"
                        });
                    }
                    catch (Exception memEx)
                    {
                        TM.App.Log($"[SystemInfo] 处理内存模块信息时出错: {memEx.Message}");
                    }
                }
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MemoryModules.ReplaceAll(memoryModulesList);
                });
                TM.App.Log($"[SystemInfo] 内存信息采集完成，检测到 {memoryModulesList.Count} 个内存模块");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集内存信息失败: {ex.Message}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TotalMemory = "无法获取";
                });
            }
        }

        private string GetMemoryTypeName(int type)
        {
            return type switch
            {
                20 => "DDR",
                21 => "DDR2",
                22 => "DDR2 FB-DIMM",
                24 => "DDR3",
                26 => "DDR4",
                30 => "LPDDR4",
                34 => "DDR5",
                _ => type > 0 ? $"类型 {type}" : "未知"
            };
        }

        private async System.Threading.Tasks.Task CollectGpuInfo()
        {
            try
            {
                var gpuList = new List<GpuInfoItem>();
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");

                foreach (ManagementObject gpu in searcher.Get())
                {
                    try
                    {
                        var adapterRAM = gpu["AdapterRAM"];
                        var adapterRAMValue = adapterRAM != null ? Convert.ToInt64(adapterRAM) : 0;

                        gpuList.Add(new GpuInfoItem
                        {
                            Name = gpu["Name"]?.ToString() ?? "Unknown GPU",
                            Manufacturer = gpu["AdapterCompatibility"]?.ToString() ?? "Unknown",
                            VideoProcessor = gpu["VideoProcessor"]?.ToString() ?? "N/A",
                            VideoMemorySize = adapterRAMValue > 0 ? FormatBytes(adapterRAMValue) : "N/A",
                            DriverVersion = gpu["DriverVersion"]?.ToString() ?? "N/A",
                            DriverDate = gpu["DriverDate"]?.ToString() ?? "N/A",
                            VideoModeDescription = gpu["VideoModeDescription"]?.ToString() ?? "N/A",
                            AdapterRAM = adapterRAMValue > 0 ? FormatBytes(adapterRAMValue) : "N/A",
                            DeviceID = gpu["DeviceID"]?.ToString() ?? "N/A",
                            Status = gpu["Status"]?.ToString() ?? "Unknown"
                        });
                    }
                    catch (Exception gpuEx)
                    {
                        TM.App.Log($"[SystemInfo] 处理GPU信息时出错: {gpuEx.Message}");
                    }
                }
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    GpuInfos.ReplaceAll(gpuList);
                });
                TM.App.Log($"[SystemInfo] 检测到 {gpuList.Count} 个GPU");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集GPU信息失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CollectDiskInfo()
        {
            try
            {
                var driveList = new List<DriveInfoItem>();

                var diskDetails = new Dictionary<int, (string mediaType, string interfaceType, string model, string serialNumber, string manufacturer)>();
                try
                {
                    using var diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                    int diskIndex = 0;
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                        try
                        {
                            var mediaType = disk["MediaType"]?.ToString() ?? "";
                            var interfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown";
                            var model = disk["Model"]?.ToString() ?? "Unknown";
                            var serialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "N/A";
                            var manufacturer = disk["Manufacturer"]?.ToString() ?? "Unknown";

                            string diskType = "HDD";
                            if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "SSD ⚡";
                            }
                            if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                                interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "NVMe";
                            }
                            if (interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "USB";
                            }

                            diskDetails[diskIndex] = (diskType, interfaceType, model, serialNumber, manufacturer);
                            diskIndex++;
                        }
                        catch (Exception ex)
                        {
                            DebugLogOnce("GetDiskDetails", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SystemInfo] 获取物理磁盘详情失败: {ex.Message}");
                }

                var drives = DriveInfo.GetDrives();
                int driveIndex = 0;

                foreach (var drive in drives.Where(d => d.IsReady))
                {
                    var diskDetail = diskDetails.TryGetValue(driveIndex, out var detail)
                        ? detail
                        : (mediaType: "HDD", interfaceType: "Unknown", model: "Unknown", serialNumber: "N/A", manufacturer: "Unknown");

                    driveList.Add(new DriveInfoItem
                    {
                        DriveName = drive.Name,
                        DriveType = drive.DriveType.ToString(),
                        FileSystem = drive.DriveFormat,
                        TotalSize = FormatBytes(drive.TotalSize),
                        UsedSize = FormatBytes(drive.TotalSize - drive.AvailableFreeSpace),
                        FreeSize = FormatBytes(drive.AvailableFreeSpace),
                        UsagePercent = Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100, 2),
                        MediaType = diskDetail.mediaType,
                        InterfaceType = diskDetail.interfaceType,
                        Model = diskDetail.model,
                        SerialNumber = diskDetail.serialNumber,
                        Manufacturer = diskDetail.manufacturer,
                        HealthStatus = "良好",
                        DiskIcon = diskDetail.mediaType.Contains("SSD") ? "SSD" : (diskDetail.mediaType.Contains("NVMe") ? "NVMe" : "HDD")
                    });

                    driveIndex++;
                }
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DriveInfos.ReplaceAll(driveList);
                });
                TM.App.Log($"[SystemInfo] 检测到 {driveList.Count} 个驱动器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集磁盘信息失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CollectMotherboardInfo()
        {
            try
            {
                var mbManufacturer = "未知";
                var mbProduct = "未知";
                var mbVersion = "未知";
                var mbSerial = "未知";
                var biosManufacturer = "未知";
                var biosVersion = "未知";
                var biosSmbios = "";
                var biosDate = "未知";

                using var mbSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                foreach (ManagementObject mb in mbSearcher.Get())
                {
                    mbManufacturer = mb["Manufacturer"]?.ToString() ?? "未知";
                    mbProduct = mb["Product"]?.ToString() ?? "未知";
                    mbVersion = mb["Version"]?.ToString() ?? "未知";
                    mbSerial = mb["SerialNumber"]?.ToString()?.Trim() ?? "未知";
                    break;
                }

                using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
                foreach (ManagementObject bios in biosSearcher.Get())
                {
                    biosManufacturer = bios["Manufacturer"]?.ToString() ?? "未知";
                    biosVersion = bios["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
                    biosSmbios = bios["SMBIOSMajorVersion"]?.ToString() + "." + bios["SMBIOSMinorVersion"]?.ToString();

                    var releaseDate = bios["ReleaseDate"]?.ToString();
                    if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 8)
                    {
                        biosDate = $"{releaseDate.Substring(0, 4)}-{releaseDate.Substring(4, 2)}-{releaseDate.Substring(6, 2)}";
                    }
                    break;
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MotherboardManufacturer = mbManufacturer;
                    MotherboardProduct = mbProduct;
                    MotherboardVersion = mbVersion;
                    MotherboardSerialNumber = mbSerial;
                    BiosManufacturer = biosManufacturer;
                    BiosVersion = biosVersion;
                    BiosSmbiosVersion = biosSmbios;
                    BiosReleaseDate = biosDate;
                });

                TM.App.Log($"[SystemInfo] 主板信息采集完成: {mbManufacturer} {mbProduct}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集主板和BIOS信息失败: {ex.Message}");
            }
        }
    }
}
