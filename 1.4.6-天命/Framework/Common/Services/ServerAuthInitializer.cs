using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Common.Services
{
    public static class ServerAuthInitializer
    {
        private static CancellationTokenSource? _heartbeatCts;
        private static int _heartbeatFailCount;
        private const int MaxHeartbeatFailCount = 10;
        private static volatile bool _isReturningToLogin;
        private static bool _expireWarningShown;

        public static event Action<string>? OnReturnToLoginRequired;

        public static void Initialize()
        {
            _heartbeatFailCount = 0;
            _isReturningToLogin = false;
            _expireWarningShown = false;

            ProtectionService.SV = ValidateWithServerAsync;
            ProtectionService.SH = HeartbeatAsync;
            ProtectionService.FA = CheckFeatureAuthAsync;

            ProtectionService.MSI();

            StartHeartbeatLoop();

            TM.App.Log("[SAI] init");
        }

        public static void Stop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = null;

            ProtectionService.SV = null;
            ProtectionService.SH = null;
            ProtectionService.FA = null;

            TM.App.Log("[SAI] stop");
        }

        private static async Task<ProtectionService.SVR> ValidateWithServerAsync()
        {
            var authResult = await ServiceLocator.Get<ServerAuthService>().ValidateTokenAsync();

            return new ProtectionService.SVR
            {
                IsValid = authResult.Success,
                Message = authResult.Message
            };
        }

        private static async Task<bool> HeartbeatAsync()
        {
            var result = await ServiceLocator.Get<ServerAuthService>().SendHeartbeatAsync();

            if (result.Success)
            {
                _heartbeatFailCount = 0;
                if (!result.SubscriptionValid)
                {
                    TM.App.Log("[SAI] state");
                    RequestReturnToLogin("订阅已到期，请续费后重新登录");
                }
                else if (!_expireWarningShown && result.SubscriptionExpireTime.HasValue)
                {
                    var expireUtc = DateTimeOffset.FromUnixTimeSeconds(result.SubscriptionExpireTime.Value).UtcDateTime;
                    var remaining = expireUtc - DateTime.UtcNow;
                    if (remaining.TotalHours <= 24)
                    {
                        _expireWarningShown = true;
                        TM.App.Log("[SAI] warn");
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                GlobalToast.Warning("订阅即将到期", $"您的订阅将在 {(remaining.TotalHours < 1 ? "不到1小时" : $"{(int)remaining.TotalHours} 小时")}后到期，请及时续费。");
                            });
                        }
                        catch { }
                    }
                }
            }
            else
            {
                _heartbeatFailCount++;
                TM.App.Log("[SAI] err");
                if (_heartbeatFailCount >= MaxHeartbeatFailCount)
                {
                    RequestReturnToLogin("网络连接丢失，请检查网络后重新登录");
                }
            }

            return result.Success;
        }

        private static async Task<bool> CheckFeatureAuthAsync(string featureId)
        {
            return await ServiceLocator.Get<ServerAuthService>().CheckFeatureAuthAsync(featureId);
        }

        private static void StartHeartbeatLoop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = new CancellationTokenSource();

            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
        }

        private static async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var authService = ServiceLocator.Get<ServerAuthService>();
                    await Task.Delay(TimeSpan.FromSeconds(authService.HeartbeatIntervalSeconds), ct);

                    if (!authService.IsLoggedIn)
                    {
                        TM.App.Log("[SAI] state");
                        RequestReturnToLogin("登录已过期，请重新登录");
                        break;
                    }

                    await HeartbeatAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SAI] loop err: {ex.Message}");
                    _heartbeatFailCount++;
                    if (_heartbeatFailCount >= MaxHeartbeatFailCount)
                    {
                        TM.App.Log("[SAI] err");
                        RequestReturnToLogin("网络连接丢失，请检查网络后重新登录");
                        break;
                    }
                    GlobalToast.Warning("网络不稳定", $"心跳连接失败（{_heartbeatFailCount}/{MaxHeartbeatFailCount}），请检查网络");
                }
            }
        }

        private static void RequestReturnToLogin(string message)
        {
            if (_isReturningToLogin) return;
            _isReturningToLogin = true;

            TM.App.Log("[SAI] req");
            OnReturnToLoginRequired?.Invoke(message);
        }
    }
}
