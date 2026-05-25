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
using System.Text.Json;
using System.Windows.Input;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.SystemSettings.Info.SystemMonitor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemMonitorViewModel : INotifyPropertyChanged
    {
        private SystemMonitorSettings _settings = null!;
        private readonly string _settingsFilePath = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SystemMonitorViewModel()
        {
            TM.App.Log($"[SystemMonitor] ViewModel初始化开始");

            _settings = new SystemMonitorSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Info/SystemMonitor",
                "settings.json"
            );

            AsyncSettingsLoader.LoadOrDefer<SystemMonitorSettings>(_settingsFilePath, s => { _settings = s; }, "SystemMonitor");

            DiskUsages = new ObservableCollection<DiskUsageItem>();
            NetworkTraffics = new ObservableCollection<NetworkTrafficItem>();
            Sensors = new ObservableCollection<SensorItem>();

            RefreshCommand = new RelayCommand(RefreshAllData);

            _ = LoadDataAsync();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            try
            {
                TM.App.Log($"[SystemMonitor] 开始异步加载数据");

                await System.Threading.Tasks.Task.Delay(500);

                TM.App.Log($"[SystemMonitor] 延迟完成，开始数据采集");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RefreshAllData();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                TM.App.Log($"[SystemMonitor] 数据加载任务已完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 异步加载数据失败: {ex.Message}");
            }
        }

        private string _cpuCurrentFrequency = "未知";
        public string CPUCurrentFrequency
        {
            get => _cpuCurrentFrequency;
            set { _cpuCurrentFrequency = value; OnPropertyChanged(nameof(CPUCurrentFrequency)); }
        }

        private string _cpuTemperature = "不可用";
        public string CPUTemperature
        {
            get => _cpuTemperature;
            set { _cpuTemperature = value; OnPropertyChanged(nameof(CPUTemperature)); }
        }

        private double _cpuUsagePercent;
        public double CPUUsagePercent
        {
            get => _cpuUsagePercent;
            set { _cpuUsagePercent = value; OnPropertyChanged(nameof(CPUUsagePercent)); }
        }

        private string _availableMemory = "未知";
        public string AvailableMemory
        {
            get => _availableMemory;
            set { _availableMemory = value; OnPropertyChanged(nameof(AvailableMemory)); }
        }

        private double _memoryUsagePercent;
        public double MemoryUsagePercent
        {
            get => _memoryUsagePercent;
            set { _memoryUsagePercent = value; OnPropertyChanged(nameof(MemoryUsagePercent)); }
        }

        public ObservableCollection<DiskUsageItem> DiskUsages { get; } = null!;
        public ObservableCollection<NetworkTrafficItem> NetworkTraffics { get; } = null!;
        public ObservableCollection<SensorItem> Sensors { get; } = null!;

        public ICommand RefreshCommand { get; } = null!;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpSmv = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpSmv, json);
                File.Move(tmpSmv, _settingsFilePath, overwrite: true);
                TM.App.Log($"[SystemMonitor] 保存设置成功");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 保存设置失败: {ex.Message}");
            }
        }

        private void RefreshAllData()
        {
            _ = RefreshAllDataAsync();
        }

        private async System.Threading.Tasks.Task RefreshAllDataAsync()
        {
            try
            {
                TM.App.Log($"[SystemMonitor] 开始刷新监控数据");

                await CollectCPUDynamicInfoAsync();
                await CollectMemoryDynamicInfoAsync();
                await CollectDiskUsageInfoAsync();
                await CollectNetworkTrafficInfoAsync();
                await CollectSensorInfoAsync();

                _settings.LastRefreshTime = DateTime.Now;
                SaveSettings();

                TM.App.Log($"[SystemMonitor] 监控数据刷新完成");
                GlobalToast.Success("刷新成功", "系统监控数据已更新");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 刷新数据失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"刷新监控数据时出错: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CollectCPUDynamicInfoAsync()
        {
            string freq = "无法获取";
            double usage = 0;
            string temp = "不可用";
            try
            {
                (freq, usage, temp) = await System.Threading.Tasks.Task.Run(async () =>
                {
                    string f = "无法获取";
                    double u = 0;
                    string t = "不可用";
                    try
                    {
                        var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                        foreach (ManagementObject cpu in searcher.Get())
                        {
                            var currentClockSpeed = cpu["CurrentClockSpeed"];
                            if (currentClockSpeed != null)
                            {
                                var mhz = Convert.ToInt32(currentClockSpeed);
                                f = $"{mhz} MHz ({mhz / 1000.0:F2} GHz)";
                            }
                            break;
                        }
                    }
                    catch { }
                    try
                    {
                        var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                        cpuCounter.NextValue();
                        await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
                        u = Math.Round(cpuCounter.NextValue(), 2);
                    }
                    catch { }
                    t = GetCPUTemperatureOnBackground();
                    return (f, u, t);
                });
                TM.App.Log($"[SystemMonitor] CPU动态信息采集完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 采集CPU动态信息失败: {ex.Message}");
            }
            CPUCurrentFrequency = freq;
            CPUUsagePercent = usage;
            CPUTemperature = temp;
        }

        private static string GetCPUTemperatureOnBackground()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["CurrentTemperature"] != null &&
                        double.TryParse(obj["CurrentTemperature"].ToString(), out double tempKelvin))
                    {
                        var celsius = (tempKelvin / 10.0) - 273.15;
                        if (celsius >= 0 && celsius <= 150)
                            return $"{celsius:F1} °C";
                        TM.App.Log($"[SystemMonitor] 温度数据异常: {celsius:F1}°C");
                        return "异常数据";
                    }
                    return "无法读取";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 获取CPU温度失败: {ex.Message}");
            }
            return "不可用";
        }

        private async System.Threading.Tasks.Task CollectMemoryDynamicInfoAsync()
        {
            string avail = "无法获取";
            double usagePct = 0;
            try
            {
                (avail, usagePct) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (ManagementObject os in searcher.Get())
                    {
                        var totalKB = Convert.ToInt64(os["TotalVisibleMemorySize"]);
                        var freeKB  = Convert.ToInt64(os["FreePhysicalMemory"]);
                        var usedKB  = totalKB - freeKB;
                        return (FormatBytesStatic(freeKB * 1024), Math.Round((double)usedKB / totalKB * 100, 2));
                    }
                    return ("未知", 0.0);
                });
                TM.App.Log($"[SystemMonitor] 内存动态信息采集完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 采集内存动态信息失败: {ex.Message}");
            }
            AvailableMemory    = avail;
            MemoryUsagePercent = usagePct;
        }

        private async System.Threading.Tasks.Task CollectDiskUsageInfoAsync()
        {
            List<DiskUsageItem> items = new();
            try
            {
                items = await System.Threading.Tasks.Task.Run(() =>
                {
                    var list = new List<DiskUsageItem>();
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        var usedBytes    = drive.TotalSize - drive.AvailableFreeSpace;
                        var usagePercent = Math.Round((double)usedBytes / drive.TotalSize * 100, 2);
                        list.Add(new DiskUsageItem
                        {
                            DriveName    = drive.Name,
                            DriveType    = drive.DriveType.ToString(),
                            TotalSize    = FormatBytesStatic(drive.TotalSize),
                            UsedSize     = FormatBytesStatic(usedBytes),
                            FreeSize     = FormatBytesStatic(drive.AvailableFreeSpace),
                            UsagePercent = usagePercent
                        });
                    }
                    return list;
                });
                TM.App.Log($"[SystemMonitor] 磁盘使用率采集完成，检测到 {items.Count} 个驱动器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 采集磁盘使用率失败: {ex.Message}");
            }
            DiskUsages.Clear();
            foreach (var item in items) DiskUsages.Add(item);
        }

        private async System.Threading.Tasks.Task CollectNetworkTrafficInfoAsync()
        {
            List<NetworkTrafficItem> items = new();
            try
            {
                items = await System.Threading.Tasks.Task.Run(() =>
                {
                    var list = new List<NetworkTrafficItem>();
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                                           .Where(a => a.OperationalStatus == OperationalStatus.Up))
                    {
                        var stats = adapter.GetIPStatistics();
                        list.Add(new NetworkTrafficItem
                        {
                            Name          = adapter.Name,
                            Description   = adapter.Description,
                            BytesSent     = FormatBytesStatic(stats.BytesSent),
                            BytesReceived = FormatBytesStatic(stats.BytesReceived),
                            CurrentSpeed  = adapter.Speed > 0 ? FormatBitrateStatic(adapter.Speed) : "未知",
                            Type          = adapter.NetworkInterfaceType.ToString()
                        });
                    }
                    return list;
                });
                TM.App.Log($"[SystemMonitor] 网络流量采集完成，检测到 {items.Count} 个适配器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 采集网络流量失败: {ex.Message}");
            }
            NetworkTraffics.Clear();
            foreach (var item in items) NetworkTraffics.Add(item);
        }

        private async System.Threading.Tasks.Task CollectSensorInfoAsync()
        {
            List<SensorItem> items = new();
            try
            {
                items = await System.Threading.Tasks.Task.Run(() =>
                {
                    var list = new List<SensorItem>();

                    try
                    {
                        var tempSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                        int zoneIndex = 0;
                        foreach (ManagementObject obj in tempSearcher.Get())
                        {
                            if (obj["CurrentTemperature"] != null &&
                                double.TryParse(obj["CurrentTemperature"].ToString(), out double tempKelvin))
                            {
                                var celsius = (tempKelvin / 10.0) - 273.15;
                                if (celsius >= 0 && celsius <= 150)
                                {
                                    list.Add(new SensorItem
                                    {
                                        Name   = $"温度区域 {zoneIndex}",
                                        Type   = "温度",
                                        Value  = $"{celsius:F1} °C",
                                        Status = celsius > 80 ? "⚠️ 偏高" : (celsius > 60 ? "🟡 正常" : "✅ 良好"),
                                        Icon   = "🌡️"
                                    });
                                    zoneIndex++;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { TM.App.Log($"[SystemMonitor] 收集温度传感器失败: {ex.Message}"); }

                    try
                    {
                        var batterySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                        foreach (ManagementObject battery in batterySearcher.Get())
                        {
                            var estimatedChargeRemaining = battery["EstimatedChargeRemaining"];
                            if (estimatedChargeRemaining != null)
                            {
                                var charge = Convert.ToInt32(estimatedChargeRemaining);
                                list.Add(new SensorItem
                                {
                                    Name   = "电池电量",
                                    Type   = "电源",
                                    Value  = $"{charge}%",
                                    Status = charge > 20 ? "✅ 正常" : "⚠️ 低电量",
                                    Icon   = charge > 80 ? "🔋" : (charge > 20 ? "🔋" : "🪫")
                                });
                            }
                            break;
                        }
                    }
                    catch (Exception ex) { TM.App.Log($"[SystemMonitor] 获取电池信息失败: {ex.Message}"); }

                    try
                    {
                        var fanSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Fan");
                        int fanIndex = 0;
                        foreach (ManagementObject fan in fanSearcher.Get())
                        {
                            var status = fan["Status"]?.ToString() ?? "未知";
                            list.Add(new SensorItem
                            {
                                Name   = $"系统风扇 {fanIndex}",
                                Type   = "风扇",
                                Value  = status == "OK" ? "运转正常" : status,
                                Status = status == "OK" ? "✅ 正常" : "⚠️ 异常",
                                Icon   = "🌀"
                            });
                            fanIndex++;
                        }
                    }
                    catch (Exception ex) { TM.App.Log($"[SystemMonitor] 获取风扇信息失败: {ex.Message}"); }

                    if (list.Count == 0)
                        list.Add(new SensorItem { Name = "传感器监控", Type = "系统", Value = "不可用", Status = "ℹ️ 需要管理员权限或硬件不支持", Icon = "📊" });

                    return list;
                });
                TM.App.Log($"[SystemMonitor] 传感器信息采集完成，检测到 {items.Count} 个传感器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemMonitor] 采集传感器信息失败: {ex.Message}");
            }
            Sensors.Clear();
            foreach (var item in items) Sensors.Add(item);
        }

        private static string FormatBytesStatic(long bytes)
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

        private string FormatBytes(long bytes) => FormatBytesStatic(bytes);

        private static string FormatBitrateStatic(long bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000_000)
                return $"{bitsPerSecond / 1_000_000_000.0:F2} Gbps";
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000.0:F2} Mbps";
            if (bitsPerSecond >= 1_000)
                return $"{bitsPerSecond / 1_000.0:F2} Kbps";
            return $"{bitsPerSecond} bps";
        }

        private string FormatBitrate(long bitsPerSecond) => FormatBitrateStatic(bitsPerSecond);
    }

    public class DiskUsageItem
    {
        public string DriveName { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public string TotalSize { get; set; } = string.Empty;
        public string UsedSize { get; set; } = string.Empty;
        public string FreeSize { get; set; } = string.Empty;
        public double UsagePercent { get; set; }
    }

    public class NetworkTrafficItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string BytesSent { get; set; } = string.Empty;
        public string BytesReceived { get; set; } = string.Empty;
        public string CurrentSpeed { get; set; } = string.Empty;
    }

    public class SensorItem
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }
}

