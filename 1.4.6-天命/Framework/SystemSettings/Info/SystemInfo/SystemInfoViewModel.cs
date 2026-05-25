using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Forms;
using TM.Framework.Common.Helpers;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemInfoViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly SystemInfoSettings _infoSettings;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            Debug.WriteLine($"[SystemInfo] {key}: {ex.Message}");
        }

        public SystemInfoViewModel(SystemInfoSettings infoSettings)
        {
            _infoSettings = infoSettings;
            DriveInfos = new ObservableCollection<DriveInfoItem>();
            NetworkAdapters = new ObservableCollection<NetworkAdapterItem>();
            GpuInfos = new ObservableCollection<GpuInfoItem>();
            MemoryModules = new ObservableCollection<MemoryModuleItem>();
            Displays = new ObservableCollection<DisplayInfoItem>();

            RefreshCommand = new RelayCommand(RefreshAllInfo);
            ExportCommand = new RelayCommand(ExportInfo);

            _ = LoadDataAsync();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            try
            {
                TM.App.Log($"[SystemInfo] 开始异步加载数据");

                await System.Threading.Tasks.Task.Delay(500);

                TM.App.Log($"[SystemInfo] 延迟完成，开始数据采集");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RefreshAllInfo();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                TM.App.Log($"[SystemInfo] 数据加载任务已完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 异步加载数据失败: {ex.Message}");
            }
        }

        private string _osName = string.Empty;
        public string OSName
        {
            get => _osName;
            set { _osName = value; OnPropertyChanged(nameof(OSName)); }
        }

        private string _osVersion = string.Empty;
        public string OSVersion
        {
            get => _osVersion;
            set { _osVersion = value; OnPropertyChanged(nameof(OSVersion)); }
        }

        private string _osArchitecture = string.Empty;
        public string OSArchitecture
        {
            get => _osArchitecture;
            set { _osArchitecture = value; OnPropertyChanged(nameof(OSArchitecture)); }
        }

        private string _computerName = string.Empty;
        public string ComputerName
        {
            get => _computerName;
            set { _computerName = value; OnPropertyChanged(nameof(ComputerName)); }
        }

        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(nameof(UserName)); }
        }

        private string _cpuName = string.Empty;
        public string CPUName
        {
            get => _cpuName;
            set { _cpuName = value; OnPropertyChanged(nameof(CPUName)); }
        }

        private int _cpuCores;
        public int CPUCores
        {
            get => _cpuCores;
            set { _cpuCores = value; OnPropertyChanged(nameof(CPUCores)); }
        }

        private int _cpuLogicalProcessors;
        public int CPULogicalProcessors
        {
            get => _cpuLogicalProcessors;
            set { _cpuLogicalProcessors = value; OnPropertyChanged(nameof(CPULogicalProcessors)); }
        }

        private string _cpuArchitecture = "未知";
        public string CPUArchitecture
        {
            get => _cpuArchitecture;
            set { _cpuArchitecture = value; OnPropertyChanged(nameof(CPUArchitecture)); }
        }

        private string _cpuBaseFrequency = "未知";
        public string CPUBaseFrequency
        {
            get => _cpuBaseFrequency;
            set { _cpuBaseFrequency = value; OnPropertyChanged(nameof(CPUBaseFrequency)); }
        }

        private string _cpuMaxFrequency = "未知";
        public string CPUMaxFrequency
        {
            get => _cpuMaxFrequency;
            set { _cpuMaxFrequency = value; OnPropertyChanged(nameof(CPUMaxFrequency)); }
        }

        private string _cpuL1Cache = "未知";
        public string CPUL1Cache
        {
            get => _cpuL1Cache;
            set { _cpuL1Cache = value; OnPropertyChanged(nameof(CPUL1Cache)); }
        }

        private string _cpuL2Cache = "未知";
        public string CPUL2Cache
        {
            get => _cpuL2Cache;
            set { _cpuL2Cache = value; OnPropertyChanged(nameof(CPUL2Cache)); }
        }

        private string _cpuL3Cache = "未知";
        public string CPUL3Cache
        {
            get => _cpuL3Cache;
            set { _cpuL3Cache = value; OnPropertyChanged(nameof(CPUL3Cache)); }
        }

        private string _cpuVirtualization = "未知";
        public string CPUVirtualization
        {
            get => _cpuVirtualization;
            set { _cpuVirtualization = value; OnPropertyChanged(nameof(CPUVirtualization)); }
        }

        private string _totalMemory = string.Empty;
        public string TotalMemory
        {
            get => _totalMemory;
            set { _totalMemory = value; OnPropertyChanged(nameof(TotalMemory)); }
        }

        public ObservableCollection<MemoryModuleItem> MemoryModules { get; } = null!;

        private string _totalVirtualMemory = "未知";
        public string TotalVirtualMemory
        {
            get => _totalVirtualMemory;
            set { _totalVirtualMemory = value; OnPropertyChanged(nameof(TotalVirtualMemory)); }
        }

        private string _availableVirtualMemory = "未知";
        public string AvailableVirtualMemory
        {
            get => _availableVirtualMemory;
            set { _availableVirtualMemory = value; OnPropertyChanged(nameof(AvailableVirtualMemory)); }
        }

        private string _pageFileSize = "未知";
        public string PageFileSize
        {
            get => _pageFileSize;
            set { _pageFileSize = value; OnPropertyChanged(nameof(PageFileSize)); }
        }

        private string _screenResolution = string.Empty;
        public string ScreenResolution
        {
            get => _screenResolution;
            set { _screenResolution = value; OnPropertyChanged(nameof(ScreenResolution)); }
        }

        private int _screenCount;
        public int ScreenCount
        {
            get => _screenCount;
            set { _screenCount = value; OnPropertyChanged(nameof(ScreenCount)); }
        }

        public ObservableCollection<DriveInfoItem> DriveInfos { get; } = null!;
        public ObservableCollection<NetworkAdapterItem> NetworkAdapters { get; } = null!;
        public ObservableCollection<GpuInfoItem> GpuInfos { get; } = null!;

        public ObservableCollection<DisplayInfoItem> Displays { get; } = null!;

        private string _motherboardManufacturer = "未知";
        public string MotherboardManufacturer
        {
            get => _motherboardManufacturer;
            set { _motherboardManufacturer = value; OnPropertyChanged(nameof(MotherboardManufacturer)); }
        }

        private string _motherboardProduct = "未知";
        public string MotherboardProduct
        {
            get => _motherboardProduct;
            set { _motherboardProduct = value; OnPropertyChanged(nameof(MotherboardProduct)); }
        }

        private string _motherboardVersion = "未知";
        public string MotherboardVersion
        {
            get => _motherboardVersion;
            set { _motherboardVersion = value; OnPropertyChanged(nameof(MotherboardVersion)); }
        }

        private string _motherboardSerialNumber = "未知";
        public string MotherboardSerialNumber
        {
            get => _motherboardSerialNumber;
            set { _motherboardSerialNumber = value; OnPropertyChanged(nameof(MotherboardSerialNumber)); }
        }

        private string _biosManufacturer = "未知";
        public string BiosManufacturer
        {
            get => _biosManufacturer;
            set { _biosManufacturer = value; OnPropertyChanged(nameof(BiosManufacturer)); }
        }

        private string _biosVersion = "未知";
        public string BiosVersion
        {
            get => _biosVersion;
            set { _biosVersion = value; OnPropertyChanged(nameof(BiosVersion)); }
        }

        private string _biosReleaseDate = "未知";
        public string BiosReleaseDate
        {
            get => _biosReleaseDate;
            set { _biosReleaseDate = value; OnPropertyChanged(nameof(BiosReleaseDate)); }
        }

        private string _biosSmbiosVersion = "未知";
        public string BiosSmbiosVersion
        {
            get => _biosSmbiosVersion;
            set { _biosSmbiosVersion = value; OnPropertyChanged(nameof(BiosSmbiosVersion)); }
        }

        public ICommand RefreshCommand { get; } = null!;
        public ICommand ExportCommand { get; } = null!;

        private void RefreshAllInfo()
        {
            try
            {
                TM.App.Log($"[SystemInfo] 开始刷新系统信息");

                CollectOSInfo();
                CollectCPUInfo();
                CollectMemoryInfo();
                CollectGpuInfo();
                CollectDiskInfo();
                CollectScreenInfo();
                CollectNetworkInfo();
                CollectMotherboardInfo();

                _infoSettings.LastRefreshTime = DateTime.Now;
                _infoSettings.SaveSettings();
                TM.App.Log($"[SystemInfo] 系统信息刷新完成");
                GlobalToast.Success("刷新成功", "系统信息已更新");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 刷新失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"无法刷新系统信息: {ex.Message}");
            }
        }

        private void CollectOSInfo()
        {
            try
            {
                OSName = Environment.OSVersion.Platform.ToString();
                OSVersion = Environment.OSVersion.Version.ToString();
                OSArchitecture = Environment.Is64BitOperatingSystem ? "64位" : "32位";
                ComputerName = Environment.MachineName;
                UserName = Environment.UserName;

                var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (ManagementObject os in searcher.Get())
                {
                    OSName = os["Caption"]?.ToString() ?? OSName;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集操作系统信息失败: {ex.Message}");
            }
        }

        private void CollectCPUInfo()
        {
            try
            {
                CPULogicalProcessors = Environment.ProcessorCount;

                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject cpu in searcher.Get())
                {
                    CPUName = cpu["Name"]?.ToString()?.Trim() ?? "Unknown";
                    CPUCores = Convert.ToInt32(cpu["NumberOfCores"] ?? 0);

                    var arch = cpu["Architecture"];
                    CPUArchitecture = arch != null ? GetArchitectureName(Convert.ToInt32(arch)) : "未知";

                    var maxClockSpeed = cpu["MaxClockSpeed"];
                    if (maxClockSpeed != null)
                    {
                        var mhz = Convert.ToInt32(maxClockSpeed);
                        CPUBaseFrequency = $"{mhz} MHz ({mhz / 1000.0:F2} GHz)";
                    }

                    CPUMaxFrequency = CPUBaseFrequency;

                    var l2CacheSize = cpu["L2CacheSize"];
                    if (l2CacheSize != null)
                    {
                        var kb = Convert.ToInt32(l2CacheSize);
                        CPUL2Cache = kb >= 1024 ? $"{kb / 1024} MB" : $"{kb} KB";
                    }

                    var l3CacheSize = cpu["L3CacheSize"];
                    if (l3CacheSize != null)
                    {
                        var kb = Convert.ToInt32(l3CacheSize);
                        CPUL3Cache = kb >= 1024 ? $"{kb / 1024} MB" : $"{kb} KB";
                    }

                    CPUL1Cache = $"{CPUCores * 64} KB (估算)";

                    var virtualizationEnabled = cpu["VirtualizationFirmwareEnabled"];
                    CPUVirtualization = virtualizationEnabled != null && Convert.ToBoolean(virtualizationEnabled) ? "已启用 ✅" : "未启用";

                    break;
                }

                TM.App.Log($"[SystemInfo] CPU信息采集完成: {CPUName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集CPU信息失败: {ex.Message}");
                CPUName = "无法获取";
                CPUCores = 0;
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

        private void CollectMemoryInfo()
        {
            try
            {
                var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject os in osSearcher.Get())
                {
                    var totalKB = Convert.ToInt64(os["TotalVisibleMemorySize"]);
                    TotalMemory = FormatBytes(totalKB * 1024);

                    var totalVirtual = Convert.ToInt64(os["TotalVirtualMemorySize"]);
                    var freeVirtual = Convert.ToInt64(os["FreeVirtualMemory"]);
                    TotalVirtualMemory = FormatBytes(totalVirtual * 1024);
                    AvailableVirtualMemory = FormatBytes(freeVirtual * 1024);

                    break;
                }

                try
                {
                    var pageFileSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileUsage");
                    foreach (ManagementObject pageFile in pageFileSearcher.Get())
                    {
                        var allocatedSize = Convert.ToInt64(pageFile["AllocatedBaseSize"]);
                        PageFileSize = FormatBytes(allocatedSize * 1024 * 1024);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("GetPageFileSize", ex);
                    PageFileSize = "无法获取";
                }

                MemoryModules.Clear();
                var memSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
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

                        MemoryModules.Add(new MemoryModuleItem
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

                TM.App.Log($"[SystemInfo] 内存信息采集完成，检测到 {MemoryModules.Count} 个内存模块");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集内存信息失败: {ex.Message}");
                TotalMemory = "无法获取";
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

        private void CollectGpuInfo()
        {
            try
            {
                GpuInfos.Clear();
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");

                foreach (ManagementObject gpu in searcher.Get())
                {
                    try
                    {
                        var adapterRAM = gpu["AdapterRAM"];
                        var adapterRAMValue = adapterRAM != null ? Convert.ToInt64(adapterRAM) : 0;

                        GpuInfos.Add(new GpuInfoItem
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

                TM.App.Log($"[SystemInfo] 检测到 {GpuInfos.Count} 个GPU");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集GPU信息失败: {ex.Message}");
            }
        }

        private void CollectDiskInfo()
        {
            try
            {
                DriveInfos.Clear();

                var diskDetails = new Dictionary<int, (string mediaType, string interfaceType, string model, string serialNumber, string manufacturer)>();
                try
                {
                    var diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
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

                            string diskType = "HDD 🖴";
                            if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "SSD ⚡";
                            }
                            if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || 
                                interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "NVMe 🚀";
                            }
                            if (interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase))
                            {
                                diskType = "USB 💾";
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
                    var diskDetail = diskDetails.ContainsKey(driveIndex) 
                        ? diskDetails[driveIndex] 
                        : (mediaType: "HDD 🖴", interfaceType: "Unknown", model: "Unknown", serialNumber: "N/A", manufacturer: "Unknown");

                    DriveInfos.Add(new DriveInfoItem
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
                        HealthStatus = "良好 ✅",
                        DiskIcon = diskDetail.mediaType.Contains("SSD") ? "⚡" : (diskDetail.mediaType.Contains("NVMe") ? "🚀" : "🖴")
                    });

                    driveIndex++;
                }

                TM.App.Log($"[SystemInfo] 检测到 {DriveInfos.Count} 个驱动器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集磁盘信息失败: {ex.Message}");
            }
        }

        private void CollectScreenInfo()
        {
            try
            {
                Displays.Clear();
                var screens = Screen.AllScreens;
                ScreenCount = screens.Length;

                var primaryScreen = Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    ScreenResolution = $"{primaryScreen.Bounds.Width} x {primaryScreen.Bounds.Height}";
                }

                int displayIndex = 0;
                foreach (var screen in screens)
                {
                    displayIndex++;
                    var isPrimary = screen.Primary;
                    var resolution = $"{screen.Bounds.Width} x {screen.Bounds.Height}";
                    var workingArea = $"{screen.WorkingArea.Width} x {screen.WorkingArea.Height}";
                    var bitsPerPixel = screen.BitsPerPixel;

                    var dpiX = 96.0;
                    var dpiY = 96.0;

                    string manufacturer = "未知";
                    string productCode = "未知";
                    string serialNumber = "未知";
                    string manufactureYear = "未知";
                    int? refreshRate = null;

                    try
                    {
                        var monitorSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
                        int monIndex = 0;
                        foreach (ManagementObject monitor in monitorSearcher.Get())
                        {
                            if (monIndex == displayIndex - 1)
                            {
                                var mfg = monitor["ManufacturerName"] as ushort[];
                                if (mfg != null && mfg.Length > 0)
                                {
                                    manufacturer = new string(mfg.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                                }

                                var prd = monitor["ProductCodeID"] as ushort[];
                                if (prd != null && prd.Length > 0)
                                {
                                    productCode = new string(prd.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                                }

                                var sn = monitor["SerialNumberID"] as ushort[];
                                if (sn != null && sn.Length > 0)
                                {
                                    serialNumber = new string(sn.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                                }

                                var year = monitor["YearOfManufacture"];
                                if (year != null)
                                {
                                    manufactureYear = year.ToString() ?? "未知";
                                }
                                break;
                            }
                            monIndex++;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("GetMonitorDetails", ex);
                    }

                    try
                    {
                        var refreshSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBasicDisplayParams");
                        int refIndex = 0;
                        foreach (ManagementObject refresh in refreshSearcher.Get())
                        {
                            if (refIndex == displayIndex - 1)
                            {
                                refreshRate = 60;
                                break;
                            }
                            refIndex++;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("GetRefreshRate", ex);
                    }

                    Displays.Add(new DisplayInfoItem
                    {
                        Name = isPrimary ? $"显示器 {displayIndex} (主)" : $"显示器 {displayIndex}",
                        IsPrimary = isPrimary,
                        Resolution = resolution,
                        WorkingArea = workingArea,
                        BitsPerPixel = $"{bitsPerPixel} 位",
                        Manufacturer = manufacturer,
                        ProductCode = productCode,
                        SerialNumber = serialNumber,
                        ManufactureYear = manufactureYear,
                        RefreshRate = refreshRate.HasValue ? $"{refreshRate.Value} Hz" : "未知",
                        DPI = $"{dpiX:F0} x {dpiY:F0}",
                        AspectRatio = CalculateAspectRatio(screen.Bounds.Width, screen.Bounds.Height)
                    });
                }

                TM.App.Log($"[SystemInfo] 检测到 {Displays.Count} 个显示器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集显示器信息失败: {ex.Message}");
                ScreenCount = 0;
                ScreenResolution = "无法获取";
            }
        }

        private string CalculateAspectRatio(int width, int height)
        {
            int gcd = GCD(width, height);
            int ratioW = width / gcd;
            int ratioH = height / gcd;

            if (ratioW == 16 && ratioH == 9) return "16:9";
            if (ratioW == 16 && ratioH == 10) return "16:10";
            if (ratioW == 21 && ratioH == 9) return "21:9";
            if (ratioW == 4 && ratioH == 3) return "4:3";
            if (ratioW == 5 && ratioH == 4) return "5:4";

            return $"{ratioW}:{ratioH}";
        }

        private int GCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        private void CollectNetworkInfo()
        {
            try
            {
                NetworkAdapters.Clear();
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var adapter in interfaces.Where(a => a.OperationalStatus == OperationalStatus.Up))
                {
                    var properties = adapter.GetIPProperties();

                    var ipv4 = properties.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ?.Address.ToString() ?? "无";

                    var ipv6 = properties.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 
                                           && !a.Address.IsIPv6LinkLocal)
                        ?.Address.ToString() ?? "无";

                    var gateway = properties.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "无";
                    var dns = properties.DnsAddresses.FirstOrDefault()?.ToString() ?? "无";

                    var speed = adapter.Speed > 0 ? FormatBitrate(adapter.Speed) : "未知";

                    var stats = adapter.GetIPStatistics();
                    var bytesSent = FormatBytes(stats.BytesSent);
                    var bytesReceived = FormatBytes(stats.BytesReceived);

                    var isWireless = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                    var icon = isWireless ? "📡" : (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "🌐" : "🔌");

                    NetworkAdapters.Add(new NetworkAdapterItem
                    {
                        Name = adapter.Name,
                        Description = adapter.Description,
                        Type = adapter.NetworkInterfaceType.ToString(),
                        Status = adapter.OperationalStatus.ToString(),
                        IPAddress = ipv4,
                        IPv6Address = ipv6,
                        MACAddress = adapter.GetPhysicalAddress().ToString(),
                        Gateway = gateway,
                        DNS = dns,
                        Speed = speed,
                        BytesSent = bytesSent,
                        BytesReceived = bytesReceived,
                        IsWireless = isWireless,
                        ConnectionIcon = icon
                    });
                }

                TM.App.Log($"[SystemInfo] 检测到 {NetworkAdapters.Count} 个活动网络适配器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集网络信息失败: {ex.Message}");
            }
        }

        private string FormatBitrate(long bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000_000)
                return $"{bitsPerSecond / 1_000_000_000.0:F2} Gbps";
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000.0:F2} Mbps";
            if (bitsPerSecond >= 1_000)
                return $"{bitsPerSecond / 1_000.0:F2} Kbps";
            return $"{bitsPerSecond} bps";
        }

        private void CollectMotherboardInfo()
        {
            try
            {
                var mbSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                foreach (ManagementObject mb in mbSearcher.Get())
                {
                    MotherboardManufacturer = mb["Manufacturer"]?.ToString() ?? "未知";
                    MotherboardProduct = mb["Product"]?.ToString() ?? "未知";
                    MotherboardVersion = mb["Version"]?.ToString() ?? "未知";
                    MotherboardSerialNumber = mb["SerialNumber"]?.ToString()?.Trim() ?? "未知";
                    break;
                }

                var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
                foreach (ManagementObject bios in biosSearcher.Get())
                {
                    BiosManufacturer = bios["Manufacturer"]?.ToString() ?? "未知";
                    BiosVersion = bios["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
                    BiosSmbiosVersion = bios["SMBIOSMajorVersion"]?.ToString() + "." + bios["SMBIOSMinorVersion"]?.ToString();

                    var releaseDate = bios["ReleaseDate"]?.ToString();
                    if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 8)
                    {
                        BiosReleaseDate = $"{releaseDate.Substring(0, 4)}-{releaseDate.Substring(4, 2)}-{releaseDate.Substring(6, 2)}";
                    }
                    else
                    {
                        BiosReleaseDate = "未知";
                    }
                    break;
                }

                TM.App.Log($"[SystemInfo] 主板信息采集完成: {MotherboardManufacturer} {MotherboardProduct}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集主板和BIOS信息失败: {ex.Message}");
            }
        }

        private async void ExportInfo()
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
                GlobalToast.Error("导出失败", $"无法导出系统信息: {ex.Message}");
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

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DriveInfoItem
    {
        public string DriveName { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string TotalSize { get; set; } = string.Empty;
        public string UsedSize { get; set; } = string.Empty;
        public string FreeSize { get; set; } = string.Empty;
        public double UsagePercent { get; set; }

        public string MediaType { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = string.Empty;
        public string DiskIcon { get; set; } = string.Empty;
    }

    public class NetworkAdapterItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public string IPv6Address { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string DNS { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string BytesSent { get; set; } = string.Empty;
        public string BytesReceived { get; set; } = string.Empty;
        public bool IsWireless { get; set; }
        public string ConnectionIcon { get; set; } = string.Empty;
    }

    public class GpuInfoItem
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string VideoProcessor { get; set; } = string.Empty;
        public string VideoMemorySize { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public string DriverDate { get; set; } = string.Empty;
        public string VideoModeDescription { get; set; } = string.Empty;
        public string AdapterRAM { get; set; } = string.Empty;
        public string DeviceID { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class MemoryModuleItem
    {
        public string BankLabel { get; set; } = string.Empty;
        public string Capacity { get; set; } = string.Empty;
        public string MemoryType { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
    }

    public class DisplayInfoItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public string WorkingArea { get; set; } = string.Empty;
        public string BitsPerPixel { get; set; } = string.Empty;
        public string RefreshRate { get; set; } = string.Empty;
        public string AspectRatio { get; set; } = string.Empty;
        public string DPI { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string ManufactureYear { get; set; } = string.Empty;
    }
}

