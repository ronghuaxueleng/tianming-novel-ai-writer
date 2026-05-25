using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Services
{
    public class ApiService
    {

        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        private AuthTokenManager? _tokenManager;
        private AuthTokenManager TokenManager => _tokenManager ??= ServiceLocator.Get<AuthTokenManager>();

        #region 配置

        public static string BaseUrl { get; set; } = "https://api.example.com";

        public static int TimeoutSeconds { get; set; } = 30;

        public static string UserAgent { get; set; } = "TianMing/1.0";

        public static bool UseSystemProxy { get; set; } = true;

        #endregion

        public ApiService()
        {
            var handler = SslPinningHandler.CreatePinnedHandler(UseSystemProxy);
            if (UseSystemProxy)
            {
                handler.Proxy = WebRequest.GetSystemWebProxy();
                if (handler.Proxy != null)
                    handler.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            _jsonOptions = JsonHelper.Web;

            TM.App.Log("[ApiService] init");
        }

        #region 认证接口

        public async Task<ApiResponse<LoginResult>> LoginAsync(LoginRequest request)
        {
            return await PostAsync<LoginResult>("/api/auth/login", request, requiresAuth: false, requiresSign: false);
        }

        public async Task<ApiResponse<RegisterResult>> RegisterAsync(RegisterRequest request)
        {
            return await PostAsync<RegisterResult>("/api/auth/register", request, requiresAuth: false, requiresSign: false);
        }

        public async Task<ApiResponse> LogoutAsync()
        {
            var result = await PostAsync<object>("/api/auth/logout", null, requiresAuth: true, requiresSign: true);
            if (result.Success)
            {
                TokenManager.ClearTokens();
            }
            return result;
        }

        public async Task<ApiResponse<RefreshTokenResult>> RefreshTokenAsync()
        {
            var request = new RefreshTokenRequest
            {
                RefreshToken = TokenManager.RefreshToken ?? ""
            };

            var result = await PostAsync<RefreshTokenResult>("/api/auth/refresh", request, requiresAuth: false, requiresSign: true);
            if (result.Success && result.Data != null)
            {
                TokenManager.UpdateTokens(result.Data);
            }
            return result;
        }

        #endregion

        #region 账号管理接口

        public async Task<ApiResponse> VerifyPasswordAsync(string password)
        {
            return await PostAsync<object>("/api/account/verify-password", new { Password = password });
        }

        public async Task<ApiResponse> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            var request = new ChangePasswordRequest
            {
                OldPassword = oldPassword,
                NewPassword = newPassword
            };
            return await PutAsync<object>("/api/account/password", request);
        }

        public async Task<ApiResponse<UserProfile>> GetProfileAsync()
        {
            return await GetAsync<UserProfile>("/api/account/profile");
        }

        public async Task<ApiResponse> UpdateProfileAsync(UserProfile profile)
        {
            return await PutAsync<object>("/api/account/profile", profile);
        }

        public async Task<ApiResponse<LockoutStatus>> GetLockoutStatusAsync()
        {
            return await GetAsync<LockoutStatus>("/api/account/lockout");
        }

        public async Task<ApiResponse> UnlockAccountAsync()
        {
            return await PostAsync<object>("/api/account/unlock", null);
        }

        public async Task<ApiResponse<DeletionStatusDto>> RequestDeletionAsync(DeletionRequestDto request)
        {
            return await PostAsync<DeletionStatusDto>("/api/account/deletion", request);
        }

        public async Task<ApiResponse> CancelDeletionAsync()
        {
            return await DeleteAsync<object>("/api/account/deletion");
        }

        public async Task<ApiResponse<LoginHistoryResult>> GetLoginHistoryAsync(int page = 1, int pageSize = 20)
        {
            return await GetAsync<LoginHistoryResult>($"/api/account/login-history?page={page}&pageSize={pageSize}");
        }

        #endregion

        #region 第三方绑定接口

        public async Task<ApiResponse<BindingsResult>> GetBindingsAsync()
        {
            return await GetAsync<BindingsResult>("/api/account/bindings");
        }

        public async Task<ApiResponse<BindingInfo>> BindAccountAsync(string platform, OAuthRequest request)
        {
            return await PostAsync<BindingInfo>($"/api/account/bindings/{platform}", request);
        }

        public async Task<ApiResponse> UnbindAccountAsync(string platform)
        {
            return await DeleteAsync<object>($"/api/account/bindings/{platform}");
        }

        [System.Obsolete("服务端 OAuth 端点尚未实现，调用会返回404")]
        public async Task<ApiResponse<OAuthLoginResult>> OAuthLoginAsync(string platform, OAuthRequest request)
        {
            return await PostAsync<OAuthLoginResult>($"/api/auth/oauth/{platform}", request, requiresAuth: false, requiresSign: false);
        }

        #endregion

        #region 会员接口

        public async Task<ApiResponse<SubscriptionInfo>> GetSubscriptionAsync()
        {
            return await GetAsync<SubscriptionInfo>("/api/subscription");
        }

        public async Task<ApiResponse<ActivationResult>> ActivateCardKeyAsync(string cardKey)
        {
            var request = new ActivateCardKeyRequest { CardKey = cardKey };
            return await PostAsync<ActivationResult>("/api/subscription/activate", request);
        }

        public async Task<ApiResponse<ActivationHistoryResult>> GetActivationHistoryAsync()
        {
            return await GetAsync<ActivationHistoryResult>("/api/subscription/history");
        }

        public async Task<ApiResponse<ActivationResult>> RenewAccountWithCardKeyAsync(string account, string cardKey)
        {
            var request = new { Account = account, CardKey = cardKey };
            return await PostAsync<ActivationResult>("/api/subscription/renew", request, requiresAuth: false);
        }

        #endregion

        #region HTTP方法

        private async Task<ApiResponse<T>> GetAsync<T>(string path, bool requiresAuth = true, bool requiresSign = true)
        {
            return await SendAsync<T>(HttpMethod.Get, path, null, requiresAuth, requiresSign);
        }

        private async Task<ApiResponse<T>> PostAsync<T>(string path, object? body, bool requiresAuth = true, bool requiresSign = true)
        {
            return await SendAsync<T>(HttpMethod.Post, path, body, requiresAuth, requiresSign);
        }

        private async Task<ApiResponse<T>> PutAsync<T>(string path, object? body, bool requiresAuth = true, bool requiresSign = true)
        {
            return await SendAsync<T>(HttpMethod.Put, path, body, requiresAuth, requiresSign);
        }

        private async Task<ApiResponse<T>> DeleteAsync<T>(string path, bool requiresAuth = true, bool requiresSign = true)
        {
            return await SendAsync<T>(HttpMethod.Delete, path, null, requiresAuth, requiresSign);
        }

        private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool requiresAuth, bool requiresSign, bool isRetry = false)
        {
            try
            {
                if (requiresAuth && !isRetry && TokenManager.IsAccessTokenExpired && TokenManager.HasRefreshToken)
                {
                    TM.App.Log("[ApiService] refreshing...");
                    var refreshResult = await RefreshTokenAsync();
                    if (!refreshResult.Success)
                    {
                        TM.App.Log("[ApiService] refresh fail");
                        return ApiResponse<T>.Fail("登录已过期，请重新登录", ApiErrorCodes.AUTH_EXPIRED);
                    }
                }

                var url = BaseUrl.TrimEnd('/') + path;
                var request = new HttpRequestMessage(method, url);

                request.Headers.Add("X-Client-Id", TokenManager.ClientId);

                string? bodyJson = null;
                if (body != null)
                {
                    bodyJson = JsonSerializer.Serialize(body, _jsonOptions);
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                }

                if (requiresAuth && !string.IsNullOrEmpty(TokenManager.AccessToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenManager.AccessToken);
                }

                if (requiresSign)
                {
                    var signHeaders = TokenManager.GenerateSignatureHeaders(method.Method, path, bodyJson);
                    request.Headers.Add("X-Timestamp", signHeaders.Timestamp);
                    request.Headers.Add("X-Nonce", signHeaders.Nonce);
                    request.Headers.Add("X-Sign", signHeaders.Signature);
                }

                var response = await _httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                TM.App.Log($"[ApiService] {method} {path} -> {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<ApiResponse<T>>(responseJson, _jsonOptions);
                    return result ?? ApiResponse<T>.Fail("响应解析失败");
                }
                else
                {
                    ApiResponse<T>? errorResult = null;
                    try
                    {
                        errorResult = JsonSerializer.Deserialize<ApiResponse<T>>(responseJson, _jsonOptions);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ApiService] 错误响应解析失败: {ex.Message}");
                    }

                    if ((int)response.StatusCode == 401 && !isRetry && TokenManager.HasRefreshToken)
                    {
                        TM.App.Log("[ApiService] 401, retrying...");
                        var refreshResult = await RefreshTokenAsync();
                        if (refreshResult.Success)
                        {
                            return await SendAsync<T>(method, path, body, requiresAuth, requiresSign, isRetry: true);
                        }

                        if (refreshResult.ErrorCode == ApiErrorCodes.AUTH_DEVICE_KICKED)
                        {
                            OnDeviceKicked?.Invoke();
                            return ApiResponse<T>.Fail("您的账号已在其他设备登录", ApiErrorCodes.AUTH_DEVICE_KICKED);
                        }
                    }

                    if (errorResult?.ErrorCode == ApiErrorCodes.AUTH_DEVICE_KICKED)
                    {
                        OnDeviceKicked?.Invoke();
                    }

                    if (errorResult != null)
                    {
                        return errorResult;
                    }

                    return ApiResponse<T>.Fail($"请求失败: {response.StatusCode}", ApiErrorCodes.SERVER_ERROR);
                }
            }
            catch (HttpRequestException ex)
            {
                TM.App.Log($"[ApiService] 网络错误: {ex.Message}");
                return ApiResponse<T>.Fail("网络连接失败，请检查网络后重试", ApiErrorCodes.NETWORK_ERROR);
            }
            catch (TaskCanceledException)
            {
                TM.App.Log("[ApiService] 请求超时");
                return ApiResponse<T>.Fail("请求超时，请稍后重试", ApiErrorCodes.NETWORK_ERROR);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ApiService] 请求异常: {ex.Message}");
                return ApiResponse<T>.Fail($"请求失败: {ex.Message}", ApiErrorCodes.SERVER_ERROR);
            }
        }

        public static event Action? OnDeviceKicked;

        #endregion
    }
}
