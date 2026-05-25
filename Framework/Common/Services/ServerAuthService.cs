using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Management;

namespace TM.Framework.Common.Services
{
    public class ServerAuthService : IDisposable
    {
        private bool _disposed;
        private readonly HttpClient _httpClient;
        private string? _deviceId;
        private string? _lastChallenge;
        private string? _lastIdentityNonce;
        private string? _lastHbNonce;
        private string? _lastMemNonce;
        private string? _lastMemModule;
        private static readonly string ClientVersion = GetClientVersionStatic();
        private static string GetClientVersionStatic()
        {
            try
            {
                var v = typeof(ServerAuthService).Assembly.GetName().Version;
                if (v == null) return "0.0.0";
                return v.Revision > 0
                    ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                    : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "0.0.0"; }
        }

        public static event Action? OnSessionUnauthorized;

        public static event Action? OnFallbackSwitched;

        private static readonly byte _xorKey = 0xA7;
        private static readonly long _ticksXorKey = 0x5A3C_F1E2_B4D6_789AL;
        private byte[]? _tokenXor;
        private long _expireTicksXor = _ticksXorKey;

        private string? _accessToken
        {
            get
            {
                if (_tokenXor == null) return null;
                var bytes = new byte[_tokenXor.Length];
                for (int i = 0; i < _tokenXor.Length; i++)
                    bytes[i] = (byte)(_tokenXor[i] ^ _xorKey);
                return Encoding.UTF8.GetString(bytes);
            }
            set
            {
                if (value == null) { _tokenXor = null; _featureAuthCache.Clear(); return; }
                var raw = Encoding.UTF8.GetBytes(value);
                _tokenXor = new byte[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    _tokenXor[i] = (byte)(raw[i] ^ _xorKey);
            }
        }

        private readonly Dictionary<string, (bool Result, DateTime ExpiresAt)> _featureAuthCache = new();
        private static readonly TimeSpan FeatureAuthCacheTtl = TimeSpan.FromMinutes(5);

        private DateTime _tokenExpireTime
        {
            get => new DateTime(_expireTicksXor ^ _ticksXorKey);
            set => _expireTicksXor = value.Ticks ^ _ticksXorKey;
        }

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

            System.Diagnostics.Debug.WriteLine($"[ServerAuthService] {key}: {ex.Message}");
        }

        #region R1

        private string _primaryUrl = "https://api.example.com";

        public string FallbackUrl { get; set; } = "https://api-t.example.com";

        private static volatile bool _usingFallback;
        private static DateTime _fallbackTime = DateTime.MinValue;

        public string BaseUrl
        {
            get
            {
                if (_usingFallback && !string.IsNullOrEmpty(FallbackUrl)
                    && (DateTime.UtcNow - _fallbackTime).TotalMinutes >= 2)
                {
                    _usingFallback = false;
                    TM.App.Log("[SA] 探测主通道...");
                }
                return (_usingFallback && !string.IsNullOrEmpty(FallbackUrl)) ? FallbackUrl : _primaryUrl;
            }
            set => _primaryUrl = value;
        }

        private void TrySwitchFallback(Exception ex)
        {
            if (ex is not (HttpRequestException or TaskCanceledException)) return;
            if (_usingFallback || string.IsNullOrEmpty(FallbackUrl)) return;

            _usingFallback = true;
            _fallbackTime = DateTime.UtcNow;
            TM.App.Log("[SA] 切换备用通道");
            try { OnFallbackSwitched?.Invoke(); } catch { }
        }

        private async Task<bool> TryRefreshAndContinueAsync()
        {
            try
            {
                var tokenManager = ServiceLocator.Get<User.Services.AuthTokenManager>();
                if (tokenManager == null || !tokenManager.HasRefreshToken)
                {
                    TM.App.Log("[SA] hb refresh 跳过：无 refresh_token");
                    return false;
                }

                var apiService = ServiceLocator.Get<User.Services.ApiService>();
                if (apiService == null)
                {
                    TM.App.Log("[SA] hb refresh 跳过：ApiService 未注册");
                    return false;
                }

                TM.App.Log("[SA] hb 触发 refresh 自愈...");
                var refreshResult = await apiService.RefreshTokenAsync();
                if (refreshResult.Success)
                {
                    return true;
                }

                TM.App.Log($"[SA] hb refresh 失败：{refreshResult.ErrorCode ?? "unknown"}");
                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] hb refresh 异常：{ex.Message}");
                return false;
            }
        }

        public string ApiVersion { get; set; } = "v1";

        public int TimeoutSeconds { get; set; } = 10;

        public int HeartbeatIntervalSeconds { get; set; } = 60;

        public event Action<string>? OnForceLogout;

        public event Action<string>? OnAnnouncementReceived;

        public event Action<string>? OnForceUpdateRequired;

        #endregion

        public ServerAuthService()
        {
            var handler = SslPinningHandler.CreatePinnedHandler(useProxy: false);
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            _ = Task.Run(() => { _deviceId = GetDeviceId(); });
        }

        public void SyncToken(string accessToken, DateTime expiresAt)
        {
            _accessToken = accessToken;
            _tokenExpireTime = expiresAt;
            TM.App.Log("[SA] sync");
        }

        public void ClearToken()
        {
            _accessToken = null;
            _tokenExpireTime = DateTime.MinValue;
            TM.App.Log("[SA] clr");
        }

        private void ClearNoncesForResync()
        {
            _lastChallenge = null;
            _lastIdentityNonce = null;
            _lastHbNonce = null;
            _lastMemNonce = null;
            _lastMemModule = null;
        }

        #region R2
        private string GetDeviceId()
        {
            try
            {
                var cpuId = GetWmiValue("Win32_Processor", "ProcessorId");
                var boardSerial = GetWmiValue("Win32_BaseBoard", "SerialNumber");
                if (string.IsNullOrWhiteSpace(cpuId) || string.IsNullOrWhiteSpace(boardSerial))
                {
                    var fallback = $"{Environment.MachineName}|{Environment.UserName}";
                    using var fallbackSha256 = SHA256.Create();
                    var fallbackHash = fallbackSha256.ComputeHash(Encoding.UTF8.GetBytes(fallback));
                    return Convert.ToHexString(fallbackHash).Substring(0, 32);
                }
                var machineInfo = $"{cpuId}|{boardSerial}";
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
                return Convert.ToHexString(hash).Substring(0, 32);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetDeviceId), ex);
                var fallback = $"{Environment.MachineName}|{Environment.UserName}";
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallback));
                return Convert.ToHexString(hash).Substring(0, 32);
            }
        }

        private static string GetWmiValue(string wmiClass, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var val = obj[property]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }
            return string.Empty;
        }

        #endregion

        #region R3

        private static async Task<T?> ReadVerifiedJsonAsync<T>(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                TM.App.Log("[SA] 401 Unauthorized - session revoked by server, triggering logout");
                try { OnSessionUnauthorized?.Invoke(); } catch { }
                return default;
            }

            var body = await response.Content.ReadAsStringAsync();

            bool hasSig = response.Headers.TryGetValues("X-Response-Sig", out var sigValues);
            string? sig = hasSig ? sigValues!.FirstOrDefault() : null;

            if (!string.IsNullOrEmpty(sig))
            {
                bool ok = ProtectionService.VerifyResponseSignature(body, sig);
                if (!ok)
                {
                    TM.App.Log("[SA] C2: response signature invalid - body ignored");
                    return default;
                }
            }
            else
            {
#if !DEBUG
                if (TM.Framework.Common.Services.IntegrityHash.IsSigned)
                {
                    TM.App.Log("[SA] C2 strict: X-Response-Sig missing in production - body rejected");
                    return default;
                }
#endif
            }

            if (string.IsNullOrEmpty(body)) return default;
            return JsonSerializer.Deserialize<T>(body);
        }

        private void AddCommonHeaders(HttpRequestMessage request, string body = "")
        {
            request.Headers.Add("X-Device-Id", _deviceId ?? string.Empty);

            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
            }

#if DEBUG
            request.Headers.TryAddWithoutValidation("X-Build-Mode", "Debug");
#endif
        }

        public async Task<AuthResult> ValidateTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "未登录",
                    ErrorCode = "NOT_LOGGED_IN"
                };
            }

            try
            {
                var url = $"{BaseUrl}/{ApiVersion}/auth/validate";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddCommonHeaders(request);

                var response = await _httpClient.SendAsync(request);
                var result = await ReadVerifiedJsonAsync<AuthResponse>(response);

                if (result?.Success == true)
                {
                    if (result.Data?.ExpiresIn > 0)
                    {
                        _tokenExpireTime = DateTime.UtcNow.AddSeconds(result.Data.ExpiresIn);
                    }

                    return new AuthResult { Success = true };
                }

                _accessToken = null;

                return new AuthResult
                {
                    Success = false,
                    Message = result?.Message ?? "Token无效",
                    ErrorCode = result?.ErrorCode ?? "INVALID_TOKEN"
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] val err: {ex.Message}");
                TrySwitchFallback(ex);
                return new AuthResult
                {
                    Success = false,
                    Message = "网络连接失败",
                    ErrorCode = "NETWORK_ERROR"
                };
            }
        }

        #endregion

        #region R4

        public Task<HeartbeatResult> SendHeartbeatAsync() => SendHeartbeatInternalAsync(allowRefresh: true);

        private async Task<HeartbeatResult> SendHeartbeatInternalAsync(bool allowRefresh)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                return new HeartbeatResult { Success = false };
            }

            try
            {
                var url = $"{BaseUrl}/{ApiVersion}/auth/heartbeat";
                string? challengeResponse = null;
                if (!string.IsNullOrEmpty(_lastChallenge))
                {
                    var sessionKey = ServiceLocator.Get<User.Services.AuthTokenManager>()?.SessionKey;
                    if (!string.IsNullOrEmpty(sessionKey))
                    {
                        using var hmac = new System.Security.Cryptography.HMACSHA256(
                            Encoding.UTF8.GetBytes(sessionKey));
                        challengeResponse = Convert.ToBase64String(
                            hmac.ComputeHash(Encoding.UTF8.GetBytes(_lastChallenge)));
                    }
                }

                string? identitySig = null;
                if (!string.IsNullOrEmpty(_lastIdentityNonce))
                {
                    identitySig = ProtectionService.SignIdentity(_lastIdentityNonce, ClientVersion);
                }

                string? hbSig = null;
                if (!string.IsNullOrEmpty(_lastHbNonce))
                {
                    hbSig = ProtectionService.SignHeartbeat(_lastHbNonce);
                }

                string? memSig = null;
                if (!string.IsNullOrEmpty(_lastMemNonce) && !string.IsNullOrEmpty(_lastMemModule))
                {
                    memSig = ProtectionService.SignMemoryChallenge(_lastMemModule, _lastMemNonce);
                }

                var requestBody = new HeartbeatRequest
                {
                    DeviceId = _deviceId!,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ChallengeResponse = challengeResponse,
                    Version = ClientVersion,
                    IdentitySig = identitySig,
                    HbSig = hbSig,
                    MemSig = memSig,
                    MemModule = _lastMemModule,
                    HbCount = ProtectionService.GetHeartbeatCount()
                };

                var json = JsonSerializer.Serialize(requestBody);
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                AddCommonHeaders(request, json);

                var response = await _httpClient.SendAsync(request);
                var result = await ReadVerifiedJsonAsync<HeartbeatResponse>(response);
                if (result == null && response.IsSuccessStatusCode)
                {
                    TM.App.Log("[SA] hb response rejected - nonce resync will be requested");
                    ClearNoncesForResync();
                }

                if (result?.Success == true)
                {
                    _lastChallenge = result.Data?.Challenge;
                    _lastIdentityNonce = result.Data?.IdentityNonce;
                    _lastHbNonce = result.Data?.HbNonce;
                    _lastMemNonce = result.Data?.MemNonce;
                    _lastMemModule = result.Data?.MemModule;

                    if (result.Data != null && !string.IsNullOrEmpty(result.Data.ServerTimeSig))
                    {
                        var serverTimeStr = result.Data.ServerTime.ToString();
                        bool sigOk = ProtectionService.VerifyServerSignature(serverTimeStr, result.Data.ServerTimeSig);
                        if (!sigOk)
                        {
                            TM.App.Log("[SA] T8: serverTime signature invalid - possible MITM or tampering");
                            return new HeartbeatResult { Success = false };
                        }
                    }

                    if (result.Data != null
                        && !string.IsNullOrEmpty(result.Data.ExtraSslPins)
                        && !string.IsNullOrEmpty(result.Data.ExtraSslPinsSig))
                    {
                        bool setOk = ProtectionService.SetSslPins(result.Data.ExtraSslPins, result.Data.ExtraSslPinsSig);
                        TM.App.Log(setOk
                            ? "[SA] T4: 动态 SSL pins 已写入 native"
                            : "[SA] T4: 动态 SSL pins 签名验证失败（已忽略）");
                    }

                    var heartbeatResult = new HeartbeatResult
                    {
                        Success = true,
                        Announcement = result.Data?.Announcement,
                        Announcements = result.Data?.Announcements ?? new List<AnnouncementItem>(),
                        ForceUpdate = result.Data?.ForceUpdate ?? false,
                        MinVersion = result.Data?.MinVersion,
                        SubscriptionValid = result.Data?.SubscriptionValid ?? false,
                        SubscriptionExpireTime = result.Data?.SubscriptionExpireTime,
                        AllowedFeatures = result.Data?.AllowedFeatures ?? new List<string>(),
                        RecoveredFromSuspended = result.Data?.RecoveredFromSuspended ?? false
                    };

                    if (!string.IsNullOrWhiteSpace(heartbeatResult.Announcement))
                    {
                        OnAnnouncementReceived?.Invoke(heartbeatResult.Announcement);
                    }

                    if (heartbeatResult.ForceUpdate)
                    {
                        OnForceUpdateRequired?.Invoke(heartbeatResult.MinVersion ?? "");
                    }

                    return heartbeatResult;
                }

                TM.App.Log("[SA] hb fail");

                if (result?.ErrorCode == "INVALID_TOKEN" || result?.ErrorCode == "AUTH_DEVICE_KICKED")
                {
                    if (allowRefresh && await TryRefreshAndContinueAsync())
                    {
                        TM.App.Log("[SA] hb refresh 自愈成功，重发心跳");
                        return await SendHeartbeatInternalAsync(allowRefresh: false);
                    }

                    _accessToken = null;
                    var msg = result?.ErrorCode == "AUTH_DEVICE_KICKED"
                        ? "您的账号已在其他设备登录"
                        : "登录已过期，请重新登录";
                    OnForceLogout?.Invoke(msg);
                }

                if (result?.ErrorCode == "USER_INVALID")
                {
                    _accessToken = null;
                    OnForceLogout?.Invoke(result?.Message ?? "账号状态异常");
                }

                if (result?.ErrorCode == "VERSION_BLOCKED"
                 || result?.ErrorCode == "VERSION_TOO_LOW"
                 || result?.ErrorCode == "VERSION_INVALID"
                 || result?.ErrorCode == "VERSION_MISSING")
                {
                    TM.App.Log($"[SA] security {result.ErrorCode} - forcing logout (不可恢复)");
                    _accessToken = null;
                    try { OnSessionUnauthorized?.Invoke(); } catch { }
                }
                else if (result?.ErrorCode == "IDENTITY_SIG_FAILED"
                      || result?.ErrorCode == "HB_SIG_FAILED"
                      || result?.ErrorCode == "MEM_SIG_FAILED"
                      || result?.ErrorCode == "CHALLENGE_FAILED"
                      || result?.ErrorCode == "NATIVE_STATE_REPLAY" || result?.ErrorCode == "NATIVE_STATE_MISSING"
                      || result?.ErrorCode == "FINGERPRINT_MISMATCH" || result?.ErrorCode == "FINGERPRINT_MISSING"
                      || result?.ErrorCode == "MEM_CHALLENGE_FAILED"
                      || result?.ErrorCode == "RESYNC_RATE_LIMIT")
                {
                    TM.App.Log($"[SA] security {result.ErrorCode} - 尝试 refresh + nonce 软重置自愈");
                    ClearNoncesForResync();
                    if (allowRefresh && await TryRefreshAndContinueAsync())
                    {
                        TM.App.Log("[SA] security refresh 自愈成功，重发心跳");
                        return await SendHeartbeatInternalAsync(allowRefresh: false);
                    }

                    TM.App.Log($"[SA] security {result.ErrorCode} 自愈失败 - forcing logout");
                    _accessToken = null;
                    try { OnSessionUnauthorized?.Invoke(); } catch { }
                }

                return new HeartbeatResult { Success = false, ErrorCode = result?.ErrorCode };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] hb err: {ex.Message}");
                TrySwitchFallback(ex);
                return new HeartbeatResult { Success = false };
            }
        }

        #endregion

        #region R5
        public async Task<bool?> CheckFeatureAuthAsync(string featureId)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                return false;
            }

            if (_featureAuthCache.TryGetValue(featureId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
            {
                return cached.Result;
            }

            try
            {
                var url = $"{BaseUrl}/{ApiVersion}/auth/feature/{featureId}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddCommonHeaders(request);

                var response = await _httpClient.SendAsync(request);
                var result = await ReadVerifiedJsonAsync<FeatureAuthResponse>(response);

                if (result?.ErrorCode == "AUTH_DEVICE_KICKED"
                 || result?.ErrorCode == "IDENTITY_SIG_FAILED"
                 || result?.ErrorCode == "HB_SIG_FAILED"
                 || result?.ErrorCode == "MEM_SIG_FAILED"
                 || result?.ErrorCode == "VERSION_BLOCKED"
                 || result?.ErrorCode == "VERSION_TOO_LOW"
                 || result?.ErrorCode == "NATIVE_STATE_REPLAY" || result?.ErrorCode == "NATIVE_STATE_MISSING"
                 || result?.ErrorCode == "FINGERPRINT_MISMATCH" || result?.ErrorCode == "FINGERPRINT_MISSING"
                 || result?.ErrorCode == "MEM_CHALLENGE_FAILED")
                {
                    TM.App.Log($"[SA] fa: session revoked ({result.ErrorCode}) - forcing logout");
                    _accessToken = null;
                    try { OnSessionUnauthorized?.Invoke(); } catch { }
                    return false;
                }

                var authorized = result?.Success == true && result.Data?.Authorized == true;

                if (authorized && !string.IsNullOrEmpty(result?.Data?.FeatureToken))
                {
                    var token = result.Data.FeatureToken;
                    var lastBar = token.LastIndexOf('|');
                    if (lastBar > 0 && lastBar < token.Length - 1)
                    {
                        var payload = token.Substring(0, lastBar);
                        var sig = token.Substring(lastBar + 1);
                        bool sigOk = ProtectionService.VerifyServerSignature(payload, sig);
                        if (!sigOk)
                        {
                            TM.App.Log($"[SA] T2: feature_token 验签失败 ({featureId}) - 返回未授权");
                            authorized = false;
                        }
                        else
                        {
                            var parts = payload.Split('|');
                            if (parts.Length >= 3 && long.TryParse(parts[2], out var expAt))
                            {
                                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expAt)
                                {
                                    TM.App.Log($"[SA] T2: feature_token 过期 ({featureId})");
                                    authorized = false;
                                }
                            }

                            if (authorized && parts.Length >= 4 && !string.IsNullOrEmpty(_deviceId))
                            {
                                if (!string.Equals(parts[3], _deviceId, StringComparison.Ordinal))
                                {
                                    TM.App.Log($"[SA] T2: feature_token deviceId 不匹配 ({featureId}) - 拒绝");
                                    authorized = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        TM.App.Log($"[SA] T2: feature_token 格式异常 ({featureId})");
                        authorized = false;
                    }
                }
                else if (authorized)
                {
#if !DEBUG
                    if (TM.Framework.Common.Services.IntegrityHash.IsSigned)
                    {
                        TM.App.Log($"[SA] T2: 服务端未下发 feature_token ({featureId}) - 拒绝信任");
                        authorized = false;
                    }
#endif
                }

                _featureAuthCache[featureId] = (authorized, DateTime.UtcNow.Add(FeatureAuthCacheTtl));
                return authorized;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] fa err: {ex.Message}");
                TrySwitchFallback(ex);
                return null;
            }
        }

        #endregion

        #region R6

        private string GetClientVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly();
                return assembly?.GetName().Version?.ToString() ?? "1.0.0";
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetClientVersion), ex);
                return "1.0.0";
            }
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpireTime;

        public void Logout()
        {
            _accessToken = null;
            _tokenExpireTime = DateTime.MinValue;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    #region 请求/响应模型

    public class HeartbeatRequest
    {
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = "";
        [JsonPropertyName("version")]
        public string? Version { get; set; }
        [JsonPropertyName("identitySig")]
        public string? IdentitySig { get; set; }
        [JsonPropertyName("hbSig")]
        public string? HbSig { get; set; }
        [JsonPropertyName("memSig")]
        public string? MemSig { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("challengeResponse")]
        public string? ChallengeResponse { get; set; }

        [JsonPropertyName("fingerprint")]
        public string? Fingerprint { get; set; }

        [JsonPropertyName("nativeState")]
        public string? NativeState { get; set; }

        [JsonPropertyName("memHash")]
        public string? MemHash { get; set; }

        [JsonPropertyName("memModule")]
        public string? MemModule { get; set; }

        [JsonPropertyName("hbCount")]
        public int? HbCount { get; set; }
    }

    public class BaseResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    public class HeartbeatResponse : BaseResponse
    {
        [JsonPropertyName("data")]
        public HeartbeatResponseData? Data { get; set; }
    }

    public class HeartbeatResponseData
    {
        [JsonPropertyName("serverTime")]
        public long ServerTime { get; set; }

        [JsonPropertyName("serverTimeSig")]
        public string? ServerTimeSig { get; set; }

        [JsonPropertyName("announcement")]
        public string? Announcement { get; set; }

        [JsonPropertyName("announcements")]
        public List<AnnouncementItem> Announcements { get; set; } = new();

        [JsonPropertyName("forceUpdate")]
        public bool ForceUpdate { get; set; }

        [JsonPropertyName("minVersion")]
        public string? MinVersion { get; set; }

        [JsonPropertyName("challenge")]
        public string? Challenge { get; set; }

        [JsonPropertyName("fpNonce")]
        public string? FpNonce { get; set; }

        [JsonPropertyName("identityNonce")]
        public string? IdentityNonce { get; set; }
        [JsonPropertyName("hbNonce")]
        public string? HbNonce { get; set; }

        [JsonPropertyName("extraSslPins")]
        public string? ExtraSslPins { get; set; }
        [JsonPropertyName("extraSslPinsSig")]
        public string? ExtraSslPinsSig { get; set; }

        [JsonPropertyName("memNonce")]
        public string? MemNonce { get; set; }
        [JsonPropertyName("memModule")]
        public string? MemModule { get; set; }

        [JsonPropertyName("subscriptionValid")]
        public bool SubscriptionValid { get; set; }

        [JsonPropertyName("subscriptionExpireTime")]
        public long? SubscriptionExpireTime { get; set; }

        [JsonPropertyName("allowedFeatures")]
        public List<string> AllowedFeatures { get; set; } = new();

        [JsonPropertyName("recoveredFromSuspended")]
        public bool? RecoveredFromSuspended { get; set; }
    }

    public class AuthResponse : BaseResponse
    {
        [JsonPropertyName("data")]
        public AuthResponseData? Data { get; set; }
    }

    public class AuthResponseData
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }
    }

    public class FeatureAuthResponse : BaseResponse
    {
        [JsonPropertyName("data")]
        public FeatureAuthData? Data { get; set; }
    }

    public class FeatureAuthData
    {
        [JsonPropertyName("authorized")]
        public bool Authorized { get; set; }

        [JsonPropertyName("expiresAt")]
        public long? ExpiresAt { get; set; }

        [JsonPropertyName("featureToken")]
        public string? FeatureToken { get; set; }
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class HeartbeatResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? Announcement { get; set; }
        public List<AnnouncementItem> Announcements { get; set; } = new();
        public bool ForceUpdate { get; set; }
        public string? MinVersion { get; set; }
        public bool SubscriptionValid { get; set; }
        public long? SubscriptionExpireTime { get; set; }
        public List<string> AllowedFeatures { get; set; } = new();
        public bool RecoveredFromSuspended { get; set; }
    }

    public class AnnouncementItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
        [JsonPropertyName("type")]
        public string Type { get; set; } = "info";
    }

    #endregion
}
