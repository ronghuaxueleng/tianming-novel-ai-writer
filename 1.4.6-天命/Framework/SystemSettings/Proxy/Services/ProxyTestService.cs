using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace TM.Framework.SystemSettings.Proxy.Services
{
    public class ProxyTestResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsConnected")] public bool IsConnected { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Latency")] public int Latency { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DownloadSpeed")] public double DownloadSpeed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExitIP")] public string ExitIP { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Location")] public string Location { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DNSLeakDetected")] public bool DNSLeakDetected { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AnonymityScore")] public int AnonymityScore { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Issues")] public List<string> Issues { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TestTime")] public DateTime TestTime { get; set; } = DateTime.Now;
    }

    public class ProxyOneClickVerificationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("TestTime")] public DateTime TestTime { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonPropertyName("DirectSuccess")] public bool DirectSuccess { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DirectIP")] public string DirectIP { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DirectError")] public string DirectError { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ProxySuccess")] public bool ProxySuccess { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ProxyIP")] public string ProxyIP { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ProxyError")] public string ProxyError { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsProxyEffective")] public bool IsProxyEffective { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
    }

    public class ProxyTestService
    {

        private readonly string _historyFile;
        private List<ProxyTestResult> _testHistory = new();

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

            System.Diagnostics.Debug.WriteLine($"[ProxyTestService] {key}: {ex.Message}");
        }

        private const string TestUrl = "https://www.google.com";
        private const string IPCheckUrl = "https://api.ipify.org?format=json";
        private const string SpeedTestUrl = "https://speed.cloudflare.com/__down?bytes=10000000";
        private const int MaxHistoryRecords = 100;

        private readonly ProxyService _proxyService;

        public ProxyTestService(ProxyService proxyService)
        {
            _proxyService = proxyService;
            _historyFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy", "test_history.json");
            LoadHistory();
        }

        public async Task<ProxyTestResult> TestAll(ProxyConfig config)
        {
            var result = new ProxyTestResult();

            try
            {
                var connectivityResult = await TestConnectivity(config);
                result.IsConnected = connectivityResult.IsConnected;
                result.Latency = connectivityResult.Latency;

                if (!result.IsConnected)
                {
                    result.Issues.Add("代理服务器无法连接");
                    result.AnonymityScore = 0;
                    SaveToHistory(result);
                    return result;
                }

                var ipResult = await TestIPAddress(config);
                result.ExitIP = ipResult.ExitIP;
                result.Location = ipResult.Location;

                result.DownloadSpeed = await TestSpeed(config);

                result.DNSLeakDetected = await TestDNSLeak(config);
                if (result.DNSLeakDetected)
                {
                    result.Issues.Add("检测到DNS泄漏");
                }

                result.AnonymityScore = CalculateAnonymityScore(result);

            }
            catch (Exception ex)
            {
                result.Issues.Add($"测试过程出错: {ex.Message}");
                TM.App.Log($"[ProxyTestService] 测试失败: {ex.Message}");
            }

            SaveToHistory(result);
            return result;
        }

        public async Task<ProxyOneClickVerificationResult> VerifyApplicationProxyAsync()
        {
            var result = new ProxyOneClickVerificationResult();

            try
            {
                var direct = await GetPublicIpDirectAsync();
                result.DirectSuccess = direct.Success;
                result.DirectIP = direct.Ip;
                result.DirectError = direct.Error;

                var proxied = await GetPublicIpViaApplicationProxyAsync();
                result.ProxySuccess = proxied.Success;
                result.ProxyIP = proxied.Ip;
                result.ProxyError = proxied.Error;

                if (result.DirectSuccess && result.ProxySuccess)
                {
                    result.IsProxyEffective = !string.Equals(result.DirectIP, result.ProxyIP, StringComparison.OrdinalIgnoreCase);
                    result.Summary = result.IsProxyEffective
                        ? $"应用内代理已生效（直连IP={result.DirectIP}，代理IP={result.ProxyIP}）"
                        : $"直连IP与代理IP相同（{result.DirectIP}），可能未走代理或代理出口与直连一致";
                }
                else if (!result.DirectSuccess && result.ProxySuccess)
                {
                    result.IsProxyEffective = true;
                    result.Summary = $"直连失败，但应用内代理可用（代理IP={result.ProxyIP}）";
                }
                else if (result.DirectSuccess && !result.ProxySuccess)
                {
                    result.IsProxyEffective = false;
                    result.Summary = $"直连可用（IP={result.DirectIP}），但应用内代理请求失败：{result.ProxyError}";
                }
                else
                {
                    result.IsProxyEffective = false;
                    result.Summary = $"直连与代理均失败：直连={result.DirectError}；代理={result.ProxyError}";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.DirectSuccess = false;
                result.ProxySuccess = false;
                result.IsProxyEffective = false;
                result.Summary = $"验证失败：{ex.Message}";
                TM.App.Log($"[ProxyTestService] 一键验证失败: {ex.Message}");
                return result;
            }
        }

        public async Task<ProxyTestResult> TestConnectivity(ProxyConfig config)
        {
            var result = new ProxyTestResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var handler = CreateProxyHandler(config);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

                var response = await client.GetAsync(TestUrl);
                sw.Stop();

                result.IsConnected = response.IsSuccessStatusCode;
                result.Latency = (int)sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TestConnectivity), ex);
                sw.Stop();
                result.IsConnected = false;
                result.Latency = (int)sw.ElapsedMilliseconds;
                result.Issues.Add("连接失败");
            }

            return result;
        }

        public async Task<int> TestLatency(ProxyConfig config, int count = 3)
        {
            var latencies = new List<int>();

            for (int i = 0; i < count; i++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var handler = CreateProxyHandler(config);
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    await client.GetAsync(TestUrl);
                    sw.Stop();
                    latencies.Add((int)sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(TestLatency), ex);
                    sw.Stop();
                    latencies.Add(9999);
                }

                await Task.Delay(500);
            }

            return latencies.Any(l => l < 9999) ? (int)latencies.Where(l => l < 9999).Average() : 9999;
        }

        public async Task<double> TestSpeed(ProxyConfig config)
        {
            try
            {
                var handler = CreateProxyHandler(config);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

                var sw = Stopwatch.StartNew();
                var response = await client.GetAsync(SpeedTestUrl);
                var data = await response.Content.ReadAsByteArrayAsync();
                sw.Stop();

                var bytesPerSecond = data.Length / sw.Elapsed.TotalSeconds;
                return bytesPerSecond / (1024 * 1024);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TestSpeed), ex);
                return 0;
            }
        }

        public async Task<ProxyTestResult> TestIPAddress(ProxyConfig config)
        {
            var result = new ProxyTestResult();

            try
            {
                var handler = CreateProxyHandler(config);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

                var response = await client.GetAsync(IPCheckUrl);
                var json = await response.Content.ReadAsStringAsync();
                var ipData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (ipData != null && ipData.ContainsKey("ip"))
                {
                    result.ExitIP = ipData["ip"];

                    result.Location = await GetIPLocation(result.ExitIP);
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add($"IP检测失败: {ex.Message}");
            }

            return result;
        }

        public async Task<bool> TestDNSLeak(ProxyConfig config)
        {
            try
            {
                var handler = CreateProxyHandler(config);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

                var response = await client.GetAsync("https://dnsleaktest.com");
                var content = await response.Content.ReadAsStringAsync();

                return false;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TestDNSLeak), ex);
                return true;
            }
        }

        private int CalculateAnonymityScore(ProxyTestResult result)
        {
            int score = 100;

            if (!result.IsConnected) score -= 100;

            if (result.Latency > 1000) score -= 20;
            else if (result.Latency > 500) score -= 10;

            if (result.DNSLeakDetected) score -= 30;

            if (result.DownloadSpeed < 0.1) score -= 10;

            return Math.Max(0, score);
        }

        private async Task<string> GetIPLocation(string ip)
        {
            try
            {
                using var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync($"https://ipapi.co/{ip}/json/");
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (data != null)
                {
                    var country = data.ContainsKey("country_name") ? data["country_name"].ToString() : "";
                    var city = data.ContainsKey("city") ? data["city"].ToString() : "";
                    return $"{country} {city}".Trim();
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetIPLocation), ex);
            }

            return "未知";
        }

        private async Task<(bool Success, string Ip, string Error)> GetPublicIpDirectAsync()
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    Proxy = null
                };

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync(IPCheckUrl);
                var json = await response.Content.ReadAsStringAsync();
                var ipData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (ipData != null && ipData.TryGetValue("ip", out var ip) && !string.IsNullOrWhiteSpace(ip))
                {
                    return (true, ip, string.Empty);
                }

                return (false, string.Empty, "无法解析IP响应");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        private async Task<(bool Success, string Ip, string Error)> GetPublicIpViaApplicationProxyAsync()
        {
            try
            {
                using var client = _proxyService.CreateHttpClient(TimeSpan.FromSeconds(10));
                var response = await client.GetAsync(IPCheckUrl);
                var json = await response.Content.ReadAsStringAsync();
                var ipData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (ipData != null && ipData.TryGetValue("ip", out var ip) && !string.IsNullOrWhiteSpace(ip))
                {
                    return (true, ip, string.Empty);
                }

                return (false, string.Empty, "无法解析IP响应");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        private HttpClientHandler CreateProxyHandler(ProxyConfig config)
        {
            var handler = new HttpClientHandler();

            if (!string.IsNullOrEmpty(config.Server))
            {
                var proxyUri = new Uri($"{config.Type.ToString().ToLower()}://{config.Server}:{config.Port}");
                handler.Proxy = new WebProxy(proxyUri);

                if (config.RequiresAuth && !string.IsNullOrEmpty(config.Username))
                {
                    handler.Proxy.Credentials = new NetworkCredential(config.Username, config.Password);
                }

                handler.UseProxy = true;
            }

            return handler;
        }

        public List<ProxyTestResult> GetHistory() => new List<ProxyTestResult>(_testHistory);

        private void SaveToHistory(ProxyTestResult result)
        {
            _testHistory.Insert(0, result);

            while (_testHistory.Count > MaxHistoryRecords)
            {
                _testHistory.RemoveAt(_testHistory.Count - 1);
            }

            SaveHistory();
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFile))
                {
                    var json = File.ReadAllText(_historyFile);
                    var history = JsonSerializer.Deserialize<List<ProxyTestResult>>(json);
                    if (history != null)
                    {
                        _testHistory = history;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyTestService] 加载测试历史失败: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(_testHistory, JsonHelper.CnDefault);
                File.WriteAllText(_historyFile, json);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyTestService] 保存测试历史失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveHistoryAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_testHistory, JsonHelper.CnDefault);
                await File.WriteAllTextAsync(_historyFile, json);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyTestService] 异步保存测试历史失败: {ex.Message}");
            }
        }
    }
}

