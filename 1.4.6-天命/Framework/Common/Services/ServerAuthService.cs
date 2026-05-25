using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.Services
{
    public class ServerAuthService
    {

        private readonly HttpClient _httpClient;
        private string? _deviceId;
        private string? _lastChallenge;

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

        public string BaseUrl { get; set; } = "https://api.example.com";

        public string ApiVersion { get; set; } = "v1";

        public int TimeoutSeconds { get; set; } = 10;

        public int HeartbeatIntervalSeconds { get; set; } = 60;

        public event Action<string>? OnForceLogout;

        public event Action<string>? OnAnnouncementReceived;

        public event Action<string>? OnForceUpdateRequired;

        #endregion

        public ServerAuthService()
        {
            var handler = SslPinningHandler.CreatePinnedHandler();
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            _deviceId = GetDeviceId();
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

        #region R2
        private string GetDeviceId()
        {
            try
            {
                var machineInfo = $"{Environment.MachineName}|{Environment.UserName}";
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
                return Convert.ToHexString(hash).Substring(0, 32);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetDeviceId), ex);
                return ShortIdGenerator.NewGuid().ToString("N");
            }
        }

        #endregion

        #region R3
        private void AddCommonHeaders(HttpRequestMessage request, string body = "")
        {
            request.Headers.Add("X-Device-Id", _deviceId);

            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
            }
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
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddCommonHeaders(request);

                var response = await _httpClient.SendAsync(request);
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>();

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

        public async Task<HeartbeatResult> SendHeartbeatAsync()
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

                var requestBody = new HeartbeatRequest
                {
                    DeviceId = _deviceId!,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ChallengeResponse = challengeResponse
                };

                var json = JsonSerializer.Serialize(requestBody);
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                AddCommonHeaders(request, json);

                var response = await _httpClient.SendAsync(request);
                var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>();

                if (result?.Success == true)
                {
                    _lastChallenge = result.Data?.Challenge;

                    var heartbeatResult = new HeartbeatResult
                    {
                        Success = true,
                        Announcement = result.Data?.Announcement,
                        ForceUpdate = result.Data?.ForceUpdate ?? false,
                        MinVersion = result.Data?.MinVersion,
                        SubscriptionValid = result.Data?.SubscriptionValid ?? false,
                        SubscriptionExpireTime = result.Data?.SubscriptionExpireTime,
                        AllowedFeatures = result.Data?.AllowedFeatures ?? new List<string>()
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

                if (result?.ErrorCode == "INVALID_TOKEN")
                {
                    _accessToken = null;
                    OnForceLogout?.Invoke("登录已过期，请重新登录");
                }

                if (result?.ErrorCode == "AUTH_DEVICE_KICKED")
                {
                    _accessToken = null;
                    OnForceLogout?.Invoke("您的账号已在其他设备登录");
                }

                if (result?.ErrorCode == "USER_INVALID")
                {
                    _accessToken = null;
                    OnForceLogout?.Invoke(result?.Message ?? "账号状态异常");
                }

                return new HeartbeatResult { Success = false, ErrorCode = result?.ErrorCode };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] hb err: {ex.Message}");
                return new HeartbeatResult { Success = false };
            }
        }

        #endregion

        #region R5
        public async Task<bool> CheckFeatureAuthAsync(string featureId)
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
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddCommonHeaders(request);

                var response = await _httpClient.SendAsync(request);
                var result = await response.Content.ReadFromJsonAsync<FeatureAuthResponse>();

                var authorized = result?.Success == true && result.Data?.Authorized == true;
                _featureAuthCache[featureId] = (authorized, DateTime.UtcNow.Add(FeatureAuthCacheTtl));
                return authorized;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SA] fa err: {ex.Message}");
                return false;
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
    }

    #region 请求/响应模型

    public class HeartbeatRequest
    {
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("challengeResponse")]
        public string? ChallengeResponse { get; set; }
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

        [JsonPropertyName("announcement")]
        public string? Announcement { get; set; }

        [JsonPropertyName("forceUpdate")]
        public bool ForceUpdate { get; set; }

        [JsonPropertyName("minVersion")]
        public string? MinVersion { get; set; }

        [JsonPropertyName("challenge")]
        public string? Challenge { get; set; }

        [JsonPropertyName("subscriptionValid")]
        public bool SubscriptionValid { get; set; }

        [JsonPropertyName("subscriptionExpireTime")]
        public long? SubscriptionExpireTime { get; set; }

        [JsonPropertyName("allowedFeatures")]
        public List<string> AllowedFeatures { get; set; } = new();
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
        public bool ForceUpdate { get; set; }
        public string? MinVersion { get; set; }
        public bool SubscriptionValid { get; set; }
        public long? SubscriptionExpireTime { get; set; }
        public List<string> AllowedFeatures { get; set; } = new();
    }

    #endregion
}
