using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using IP2Region.Net.XDB;

namespace TM.Framework.Common.Helpers.Utility
{
    public static class IpLocationHelper
    {
        private static readonly object _initLock = new();
        private static Searcher? _searcher;
        private static bool _initAttempted;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[IpLocationHelper] {key}: {ex.Message}");
        }

        private static Searcher? GetSearcher()
        {
            if (_searcher != null)
                return _searcher;

            if (_initAttempted)
                return null;

            lock (_initLock)
            {
                if (_searcher != null)
                    return _searcher;

                if (_initAttempted)
                    return null;

                _initAttempted = true;

                try
                {
                    var xdbPath = StoragePathHelper.GetFilePath("Framework", "Common/IpLocation", "ip2region_v4.xdb");

                    if (!File.Exists(xdbPath))
                    {
                        TM.App.Log($"[IpLocationHelper] ip2region_v4.xdb 未找到: {xdbPath}");
                        return null;
                    }

                    _searcher = new Searcher(CachePolicy.Content, xdbPath);
                    TM.App.Log("[IpLocationHelper] IP2Region 初始化成功");
                    return _searcher;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[IpLocationHelper] IP2Region 初始化失败: {ex.Message}");
                    DebugLogOnce(nameof(GetSearcher), ex);
                    return null;
                }
            }
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ipAddress = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

                return ipAddress?.ToString() ?? "127.0.0.1";
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetLocalIpAddress), ex);
                return "127.0.0.1";
            }
        }

        public static string GetLocation(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return "未知地区";

            if (IsPrivateIp(ipAddress))
                return "本地网络";

            try
            {
                var searcher = GetSearcher();
                if (searcher == null)
                    return "未知地区";

                var region = searcher.Search(ipAddress);

                if (string.IsNullOrWhiteSpace(region))
                    return "未知地区";

                return FormatRegion(region);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetLocation), ex);
                return "未知地区";
            }
        }

        public static DeviceInfo GetDeviceInfo()
        {
            return new DeviceInfo
            {
                DeviceType = "Windows PC",
                DeviceName = Environment.MachineName,
                OperatingSystem = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                SystemVersion = Environment.OSVersion.VersionString
            };
        }

        public static bool IsSameRegion(string ip1, string ip2)
        {
            return GetLocation(ip1) == GetLocation(ip2);
        }

        private static bool IsPrivateIp(string ipAddress)
        {
            if (ipAddress == "127.0.0.1" || ipAddress == "::1" || ipAddress == "0.0.0.0")
                return true;

            if (ipAddress.StartsWith("192.168.") || ipAddress.StartsWith("10."))
                return true;

            if (ipAddress.StartsWith("172."))
            {
                var segments = ipAddress.Split('.');
                if (segments.Length >= 2 && int.TryParse(segments[1], out var second))
                {
                    if (second >= 16 && second <= 31)
                        return true;
                }
            }

            return false;
        }

        private static string FormatRegion(string region)
        {
            var parts = region.Split('|');
            if (parts.Length < 5)
                return region;

            var country = parts[0] == "0" ? "" : parts[0];
            var province = parts[2] == "0" ? "" : parts[2];
            var city = parts[3] == "0" ? "" : parts[3];
            var isp = parts[4] == "0" ? "" : parts[4];

            if (country == "中国" && !string.IsNullOrEmpty(province))
            {
                var location = province;
                if (!string.IsNullOrEmpty(city) && city != province)
                    location += " " + city;
                return location;
            }

            if (!string.IsNullOrEmpty(country))
                return country;

            return "未知地区";
        }
    }

    public class DeviceInfo
    {
        public string DeviceType { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public string SystemVersion { get; set; } = string.Empty;
    }
}

