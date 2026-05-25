using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public partial class SystemInfoViewModel
    {
        private async System.Threading.Tasks.Task CollectScreenInfo()
        {
            try
            {
                var displayList = new List<DisplayInfoItem>();
                var screens = Screen.AllScreens;
                var screenCount = screens.Length;

                var primaryScreen = Screen.PrimaryScreen;
                var screenResolution = primaryScreen != null
                    ? $"{primaryScreen.Bounds.Width} x {primaryScreen.Bounds.Height}"
                    : ScreenResolution;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ScreenCount = screenCount;
                    ScreenResolution = screenResolution;
                });

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
                        using var monitorSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
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
                        using var refreshSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBasicDisplayParams");
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

                    displayList.Add(new DisplayInfoItem
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
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Displays.ReplaceAll(displayList);
                });
                TM.App.Log($"[SystemInfo] 检测到 {displayList.Count} 个显示器");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 采集显示器信息失败: {ex.Message}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ScreenCount = 0;
                    ScreenResolution = "无法获取";
                });
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

        private async System.Threading.Tasks.Task CollectNetworkInfo()
        {
            try
            {
                var adapterList = new List<NetworkAdapterItem>();
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

                    var icon = isWireless ? "Icon.Cloud" : (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Icon.Globe" : "Icon.Plugin");

                    adapterList.Add(new NetworkAdapterItem
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
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NetworkAdapters.ReplaceAll(adapterList);
                });
                TM.App.Log($"[SystemInfo] 检测到 {adapterList.Count} 个活动网络适配器");
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
    }
}
