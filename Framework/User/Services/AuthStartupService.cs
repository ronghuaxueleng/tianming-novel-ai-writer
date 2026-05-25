using System;
using System.Threading.Tasks;
using System.Windows;

namespace TM.Framework.User.Services
{
    public static class AuthStartupService
    {
        private static bool _initialized;
        private static AuthTokenManager? _tokenManager;
        private static AuthTokenManager TokenManager =>
            _tokenManager ??= ServiceLocator.Get<AuthTokenManager>();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ApiService.OnDeviceKicked += HandleDeviceKicked;

            TM.App.Log("[AuthStartupService] 认证服务已初始化");
        }

        public static async Task<bool> CheckLoginStatusAsync()
        {
            try
            {
                var tokenManager = ServiceLocator.Get<AuthTokenManager>();
                var apiService = ServiceLocator.Get<ApiService>();

                if (!tokenManager.IsLoggedIn)
                {
                    TM.App.Log("[AuthStartupService] 未检测到登录状态");
                    return false;
                }

                if (tokenManager.IsAccessTokenExpired)
                {
                    if (tokenManager.HasRefreshToken)
                    {
                        TM.App.Log("[AuthStartupService] refreshing...");
                        var refreshResult = await apiService.RefreshTokenAsync();

                        if (refreshResult.Success)
                        {
                            TM.App.Log("[AuthStartupService] refresh ok");
                            return true;
                        }

                        if (refreshResult.ErrorCode == ApiErrorCodes.AUTH_DEVICE_KICKED)
                        {
                            TM.App.Log("[AuthStartupService] 检测到被顶下线");
                            tokenManager.ClearTokens();
                            return false;
                        }

                        TM.App.Log("[AuthStartupService] refresh fail");
                        return false;
                    }

                    TM.App.Log("[AuthStartupService] no refresh");
                    return false;
                }

                TM.App.Log($"[AuthStartupService] 登录状态有效，用户: {tokenManager.Username}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AuthStartupService] 检查登录状态异常: {ex.Message}");
                return false;
            }
        }

        private static void HandleDeviceKicked()
        {
            TM.App.Log("[AuthStartupService] 收到被顶下线通知");

            ServiceLocator.Get<AuthTokenManager>().ClearTokens();
            ServiceLocator.Get<SubscriptionService>().ClearCache();

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                try
                {
                    StandardDialog.ShowWarning(
                        "您的账号已在其他设备登录，当前会话已失效。\n请重新登录。",
                        "登录提醒");

                    TM.App.Log("[AuthStartupService] 已提示用户被顶下线");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AuthStartupService] 显示被顶下线提示失败: {ex.Message}");
                }
            });
        }

        public static async Task LogoutAsync()
        {
            try
            {
                await ServiceLocator.Get<ApiService>().LogoutAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AuthStartupService] 服务器登出失败: {ex.Message}");
            }
            finally
            {
                ServiceLocator.Get<AuthTokenManager>().ClearTokens();
                ServiceLocator.Get<SubscriptionService>().ClearCache();
                TM.App.Log("[AuthStartupService] 已登出");
            }
        }

        public static string? CurrentUsername => TokenManager.Username;

        public static string? CurrentUserId => TokenManager.UserId;

        public static bool IsLoggedIn => TokenManager.IsLoggedIn;
    }
}
