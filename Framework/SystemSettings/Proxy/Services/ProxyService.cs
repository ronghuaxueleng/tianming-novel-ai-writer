using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using TM.Framework.SystemSettings.Proxy.ProxyChain;

namespace TM.Framework.SystemSettings.Proxy.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ProxyType
    {
        HTTP,
        HTTPS,
        SOCKS5,
        SOCKS4
    }

    public class ProxyConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public ProxyType Type { get; set; } = ProxyType.HTTP;
        [System.Text.Json.Serialization.JsonPropertyName("Server")] public string Server { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Port")] public int Port { get; set; } = 8080;
        [System.Text.Json.Serialization.JsonPropertyName("RequiresAuth")] public bool RequiresAuth { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Username")] public string Username { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Password")] public string Password { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("EnableSystemProxy")] public bool EnableSystemProxy { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("BypassList")] public List<string> BypassList { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PACScript")] public string PACScript { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PACEnabled")] public bool PACEnabled { get; set; }
    }

    public class ProxyService
    {

        private readonly string _configFile;
        private readonly string _chainSettingsFile;
        private ProxyConfig _config = new();
        private ProxyChainSettings _chainSettings = new();
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

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

            System.Diagnostics.Debug.WriteLine($"[ProxyService] {key}: {ex.Message}");
        }

        public event EventHandler? ConfigChanged;

        private readonly ProxyRuleService _ruleService;

        public ProxyService(ProxyRuleService ruleService)
        {
            _ruleService = ruleService;
            _configFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy", "proxy_config.json");
            _chainSettingsFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy/ProxyChain", "chain_settings.json");
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await LoadConfigAsync().ConfigureAwait(false);
                await LoadChainSettingsAsync().ConfigureAwait(false);
                ApplyApplicationProxy();
            });
        }

        public ProxyConfig GetConfig() => _config;

        public void SaveConfig(ProxyConfig config)
        {
            try
            {
                _config = config;

                var configToSave = CloneConfig(config);
                if (!string.IsNullOrEmpty(configToSave.Password))
                    configToSave.Password = EncryptPassword(configToSave.Password);

                var json = JsonSerializer.Serialize(configToSave, JsonHelper.CnDefault);
                var configFile = _configFile;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await _saveLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var tmp = configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                        File.Move(tmp, configFile, overwrite: true);
                        TM.App.Log($"[ProxyService] 代理配置已保存");
                    }
                    catch (Exception ex2)
                    {
                        TM.App.Log($"[ProxyService] 写盘失败: {ex2.Message}");
                    }
                    finally
                    {
                        _saveLock.Release();
                    }
                });

                if (_config.EnableSystemProxy)
                    EnableSystemProxy();
                else
                    DisableSystemProxy();

                ApplyApplicationProxy();
                ConfigChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 保存配置失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task SaveConfigAsync(ProxyConfig config)
        {
            try
            {
                _config = config;

                var configToSave = CloneConfig(config);
                if (!string.IsNullOrEmpty(configToSave.Password))
                {
                    configToSave.Password = EncryptPassword(configToSave.Password);
                }

                await _saveLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var tmp = _configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await using (var stream = File.Create(tmp))
                    {
                        await JsonSerializer.SerializeAsync(stream, configToSave, JsonHelper.CnDefault);
                    }
                    File.Move(tmp, _configFile, overwrite: true);
                }
                finally
                {
                    _saveLock.Release();
                }

                if (_config.EnableSystemProxy)
                {
                    EnableSystemProxy();
                }
                else
                {
                    DisableSystemProxy();
                }

                ApplyApplicationProxy();
                ConfigChanged?.Invoke(this, EventArgs.Empty);

                TM.App.Log($"[ProxyService] 代理配置已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 异步保存配置失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task RefreshProxyAsync(ProxyChainSettings? settings = null)
        {
            try
            {
                if (settings != null)
                    _chainSettings = settings;
                else
                    await LoadChainSettingsAsync().ConfigureAwait(false);
                ApplyApplicationProxy();
                ConfigChanged?.Invoke(this, EventArgs.Empty);
                TM.App.Log("[ProxyService] 已刷新应用内代理");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 刷新应用内代理失败: {ex.Message}");
            }
        }

        public void ApplyApplicationProxy()
        {
            try
            {
                var proxy = CreateRoutingProxy();
                HttpClient.DefaultProxy = proxy;
                WebRequest.DefaultWebProxy = proxy;

                var source = "直连";
                var chainConfig = TryGetActiveChainProxyConfig();
                if (chainConfig != null && !string.IsNullOrWhiteSpace(chainConfig.Server))
                {
                    source = $"代理链({chainConfig.Server}:{chainConfig.Port})";
                }
                else if (!string.IsNullOrWhiteSpace(_config.Server))
                {
                    source = $"自定义代理({_config.Server}:{_config.Port})";
                }

                TM.App.Log($"[ProxyService] 已应用应用内代理（支持规则/代理链），实际来源: {source}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 应用应用内代理失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadChainSettingsAsync()
        {
            try
            {
                if (File.Exists(_chainSettingsFile))
                {
                    var json = await File.ReadAllTextAsync(_chainSettingsFile).ConfigureAwait(false);
                    var settings = JsonSerializer.Deserialize<ProxyChainSettings>(json);
                    if (settings != null)
                    {
                        _chainSettings = settings;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 异步加载代理链设置失败: {ex.Message}");
                _chainSettings = new ProxyChainSettings();
            }
        }

        private ProxyConfig? TryGetActiveChainProxyConfig()
        {
            if (string.IsNullOrWhiteSpace(_chainSettings.ActiveChainId))
            {
                return null;
            }

            var chain = _chainSettings.Chains.FirstOrDefault(c => c.Id == _chainSettings.ActiveChainId && c.Enabled);
            if (chain == null)
            {
                return null;
            }

            IEnumerable<TM.Framework.SystemSettings.Proxy.ProxyChain.ProxyNode> candidates = chain.Nodes.Where(n => n.Enabled && n.IsAvailable);
            if (!candidates.Any())
            {
                candidates = chain.Nodes.Where(n => n.Enabled);
            }

            TM.Framework.SystemSettings.Proxy.ProxyChain.ProxyNode? node;
            if (chain.Strategy == ChainStrategy.LoadBalance)
            {
                node = candidates.MinBy(n => (n.Latency, n.Order));
            }
            else
            {
                node = candidates.MinBy(n => n.Order);
            }

            return node?.Config;
        }

        private IWebProxy CreateRoutingProxy()
        {
            return new RoutingWebProxy(this);
        }

        private IWebProxy? ResolveBaseProxyForProxyAction()
        {
            var chainConfig = TryGetActiveChainProxyConfig();
            var chainProxy = chainConfig != null ? CreateWebProxy(chainConfig) : null;
            if (chainProxy != null)
            {
                return chainProxy;
            }

            var configProxy = CreateWebProxy(_config);
            if (configProxy != null)
            {
                return configProxy;
            }

            return null;
        }

        private sealed class RoutingWebProxy : IWebProxy
        {
            private readonly ProxyService _service;
            private ICredentials? _credentials;
            [ThreadStatic]
            private static int _resolveDepth;

            public RoutingWebProxy(ProxyService service)
            {
                _service = service;
            }

            public ICredentials? Credentials
            {
                get => _credentials;
                set => _credentials = value;
            }

            public Uri GetProxy(Uri destination)
            {
                if (_resolveDepth > 0)
                {
                    return destination;
                }

                _resolveDepth++;
                try
                {
                    var host = destination.Host;
                    var rule = _service._ruleService.MatchRuleDetail(host);

                    if (rule != null && rule.Action == ProxyAction.Block)
                    {
                        TM.App.Log($"[ProxyRules] 已屏蔽请求: {host} (type={rule.Type}, pattern={rule.Pattern}, priority={rule.Priority})");
                        throw new HttpRequestException($"请求被代理规则屏蔽: {host}");
                    }

                    if (rule != null && rule.Action == ProxyAction.Direct)
                    {
                        return destination;
                    }

                    var proxy = _service.ResolveBaseProxyForProxyAction();
                    if (proxy == null || ReferenceEquals(proxy, this))
                    {
                        return destination;
                    }
                    _credentials = proxy.Credentials;
                    return proxy.GetProxy(destination) ?? destination;
                }
                finally
                {
                    _resolveDepth--;
                }
            }

            public bool IsBypassed(Uri host)
            {
                if (_resolveDepth > 0)
                {
                    return false;
                }

                _resolveDepth++;
                try
                {
                    var target = host.Host;
                    var rule = _service._ruleService.MatchRuleDetail(target);

                    if (rule != null && rule.Action == ProxyAction.Direct)
                    {
                        return true;
                    }

                    if (rule != null && rule.Action == ProxyAction.Block)
                    {
                        return false;
                    }

                    var proxy = _service.ResolveBaseProxyForProxyAction();
                    if (proxy == null || ReferenceEquals(proxy, this))
                    {
                        return true;
                    }
                    _credentials = proxy.Credentials;
                    return proxy.IsBypassed(host);
                }
                finally
                {
                    _resolveDepth--;
                }
            }
        }

        public HttpMessageHandler CreateHttpMessageHandler()
        {
            try
            {
                return new SocketsHttpHandler
                {
                    UseProxy = true,
                    Proxy = CreateRoutingProxy(),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    MaxConnectionsPerServer = 10,
                    EnableMultipleHttp2Connections = true,
                    ConnectTimeout = TimeSpan.FromSeconds(30),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] SocketsHttpHandler err, fallback: {ex.Message}");
                return new HttpClientHandler
                {
                    UseProxy = true,
                    Proxy = CreateRoutingProxy()
                };
            }
        }

        public HttpClient CreateHttpClient(TimeSpan? timeout = null)
        {
            HttpMessageHandler handler;
            try
            {
                handler = new SocketsHttpHandler
                {
                    UseProxy = true,
                    Proxy = CreateRoutingProxy(),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    MaxConnectionsPerServer = 10,
                    EnableMultipleHttp2Connections = true,
                    ConnectTimeout = TimeSpan.FromSeconds(30),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] SocketsHttpHandler err, fallback: {ex.Message}");
                handler = new HttpClientHandler
                {
                    UseProxy = true,
                    Proxy = CreateRoutingProxy()
                };
            }

            var client = new HttpClient(handler, disposeHandler: true);
            if (timeout != null)
            {
                client.Timeout = timeout.Value;
            }
            return client;
        }

        public void EnableSystemProxy()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key == null) return;

                var proxyServer = $"{_config.Server}:{_config.Port}";

                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);

                if (_config.BypassList.Any())
                {
                    var bypassString = string.Join(";", _config.BypassList);
                    key.SetValue("ProxyOverride", bypassString, RegistryValueKind.String);
                }

                TM.App.Log($"[ProxyService] 系统代理已启用: {proxyServer}");

                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 启用系统代理失败: {ex.Message}");
                throw;
            }
        }

        public void DisableSystemProxy()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key == null) return;

                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                TM.App.Log($"[ProxyService] 系统代理已禁用");

                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 禁用系统代理失败: {ex.Message}");
                throw;
            }
        }

        public bool IsSystemProxyEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key == null) return false;

                var proxyEnable = key.GetValue("ProxyEnable");
                return proxyEnable != null && (int)proxyEnable == 1;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(IsSystemProxyEnabled), ex);
                return false;
            }
        }

        private async System.Threading.Tasks.Task LoadConfigAsync()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = await File.ReadAllTextAsync(_configFile).ConfigureAwait(false);
                    var config = JsonSerializer.Deserialize<ProxyConfig>(json);
                    if (config != null)
                    {
                        _config = config;

                        if (!string.IsNullOrEmpty(_config.Password))
                        {
                            _config.Password = DecryptPassword(_config.Password);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyService] 异步加载配置失败: {ex.Message}");
            }
        }

        private static ProxyConfig CloneConfig(ProxyConfig config)
        {
            return new ProxyConfig
            {
                Type = config.Type,
                Server = config.Server,
                Port = config.Port,
                RequiresAuth = config.RequiresAuth,
                Username = config.Username,
                Password = config.Password,
                EnableSystemProxy = config.EnableSystemProxy,
                BypassList = config.BypassList?.ToList() ?? new List<string>(),
                PACScript = config.PACScript,
                PACEnabled = config.PACEnabled
            };
        }

        private static IWebProxy? CreateWebProxy(ProxyConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.Server))
                return null;

            if (config.Type != ProxyType.HTTP && config.Type != ProxyType.HTTPS)
            {
                TM.App.Log($"[ProxyService] 当前不支持 {config.Type} 代理协议，仅支持 HTTP/HTTPS。已回退到直连。");
                return null;
            }

            var proxyUri = new Uri($"{config.Type.ToString().ToLowerInvariant()}://{config.Server}:{config.Port}");
            var bypass = config.BypassList?.ToArray() ?? Array.Empty<string>();
            var proxy = new WebProxy(proxyUri, false, bypass);

            if (config.RequiresAuth && !string.IsNullOrEmpty(config.Username))
            {
                proxy.Credentials = new NetworkCredential(config.Username, config.Password);
            }

            return proxy;
        }

        private string EncryptPassword(string password)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(password);
                var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(EncryptPassword), ex);
                return password;
            }
        }

        private string DecryptPassword(string encryptedPassword)
        {
            try
            {
                var data = Convert.FromBase64String(encryptedPassword);
                var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(DecryptPassword), ex);
                return encryptedPassword;
            }
        }

        [System.Runtime.InteropServices.DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    }
}

