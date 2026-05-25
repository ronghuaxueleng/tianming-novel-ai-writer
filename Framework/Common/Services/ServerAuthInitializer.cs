using System;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public static class ServerAuthInitializer
    {
        private static CancellationTokenSource? _heartbeatCts;
        private static int _heartbeatFailCount;
        private const int MaxHeartbeatFailCount = 10;
        private const int HeartbeatWarningCount = 7;
        private const int HeartbeatToastCount = 3;
        private static readonly int[] BackoffSeconds = new[] { 1, 3, 9, 27 };
        private static volatile bool _isReturningToLogin;
        private static bool _expireWarningShown;
        private static bool _heartbeatWarningShown;
        private static bool _heartbeatToastShown;
        private static readonly Random _jitterRand = new();
        private static readonly System.Collections.Generic.HashSet<int> _shownAnnouncementIds = new();
        private static string? _lastFallbackAnnouncement;

        public static event Action<string>? OnReturnToLoginRequired;

        public static void Initialize()
        {
            _heartbeatFailCount = 0;
            _isReturningToLogin = false;
            _expireWarningShown = false;
            _heartbeatWarningShown = false;
            _heartbeatToastShown = false;
            _shownAnnouncementIds.Clear();
            _lastFallbackAnnouncement = null;

            ProtectionService.SV = ValidateWithServerAsync;
            ProtectionService.SH = HeartbeatAsync;
            ProtectionService.FA = CheckFeatureAuthAsync;

            ProtectionService.MSI();

            ServerAuthService.OnFallbackSwitched -= OnFallbackSwitchedHandler;
            ServerAuthService.OnFallbackSwitched += OnFallbackSwitchedHandler;

            StartHeartbeatLoop();

            TM.App.Log("[SAI] init");
        }

        public static void Stop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;

            ProtectionService.SV = null;
            ProtectionService.SH = null;
            ProtectionService.FA = null;

            ServerAuthService.OnFallbackSwitched -= OnFallbackSwitchedHandler;

            TM.App.Log("[SAI] stop");
        }

        private static void OnFallbackSwitchedHandler()
        {
            _heartbeatFailCount = 0;
            _heartbeatToastShown = false;
            _heartbeatWarningShown = false;
            TM.App.Log("[SAI] fallback 切换→重置心跳计数");
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
                _heartbeatWarningShown = false;
                _heartbeatToastShown = false;

                if (result.RecoveredFromSuspended)
                {
                    TM.App.Log("[SAI] 服务端心跳恢复了 Suspended 会话（软吊销自动激活）");
                }

                if (result.Announcements.Count > 0)
                {
                    foreach (var ann in result.Announcements)
                    {
                        if (_shownAnnouncementIds.Contains(ann.Id))
                            continue;
                        _shownAnnouncementIds.Add(ann.Id);
                        var text = string.IsNullOrWhiteSpace(ann.Content)
                            ? ann.Title
                            : $"{ann.Title}：{ann.Content}";
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                GlobalToast.Info("系统公告", text);
                            });
                        }
                        catch { }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(result.Announcement))
                {
                    if (!_shownAnnouncementIds.Contains(-1) ||
                        !string.Equals(_lastFallbackAnnouncement, result.Announcement, StringComparison.Ordinal))
                    {
                        _shownAnnouncementIds.Add(-1);
                        _lastFallbackAnnouncement = result.Announcement;
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                GlobalToast.Info("系统公告", result.Announcement);
                            });
                        }
                        catch { }
                    }
                }

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
                        var remainText = remaining.TotalHours < 1 ? "不到1小时" : $"{(int)remaining.TotalHours}小时";
                        TM.App.Log($"[SAI] 订阅即将到期 - 剩余 {remainText}");
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
                TM.App.Log($"[SAI] 心跳失败（连续第{_heartbeatFailCount}次）");
                TryShowToastWarning();
                TryShowEarlyWarning();
                if (_heartbeatFailCount >= MaxHeartbeatFailCount)
                {
                    RequestReturnToLogin("网络连接丢失，请检查网络后重新登录");
                }
            }

            return result.Success;
        }

        private static async Task<bool?> CheckFeatureAuthAsync(string featureId)
        {
            return await ServiceLocator.Get<ServerAuthService>().CheckFeatureAuthAsync(featureId);
        }

        private static void StartHeartbeatLoop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
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
                    var delaySeconds = ComputeNextDelaySeconds(authService.HeartbeatIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);

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
                    TryShowToastWarning();
                    TryShowEarlyWarning();
                    if (_heartbeatFailCount >= MaxHeartbeatFailCount)
                    {
                        TM.App.Log($"[SAI] 心跳连续失败达上限({MaxHeartbeatFailCount}次)，强制返回登录");
                        RequestReturnToLogin("网络连接丢失，请检查网络后重新登录");
                        break;
                    }
                }
            }
        }

        private static int ComputeNextDelaySeconds(int normalIntervalSeconds)
        {
            if (_heartbeatFailCount <= 0) return normalIntervalSeconds;
            if (_heartbeatFailCount > BackoffSeconds.Length) return normalIntervalSeconds;

            var baseSeconds = BackoffSeconds[_heartbeatFailCount - 1];
            int jitterRange;
            lock (_jitterRand)
            {
                jitterRange = _jitterRand.Next(0, baseSeconds + 1);
            }
            var withJitter = (baseSeconds / 2) + jitterRange;
            return Math.Max(1, withJitter);
        }

        private static void TryShowToastWarning()
        {
            if (_heartbeatToastShown) return;
            if (_heartbeatFailCount < HeartbeatToastCount) return;
            if (_heartbeatFailCount >= HeartbeatWarningCount) return;

            _heartbeatToastShown = true;
            TM.App.Log($"[SAI] 心跳预警 Toast（{_heartbeatFailCount}/{MaxHeartbeatFailCount}）");

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    GlobalToast.Warning("网络不稳定", $"心跳连接失败 {_heartbeatFailCount} 次，正在自动重试");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SAI] 预警 Toast 显示失败: {ex.Message}");
                }
            });
        }

        private static void TryShowEarlyWarning()
        {
            if (_heartbeatWarningShown) return;
            if (_heartbeatFailCount < HeartbeatWarningCount) return;
            if (_heartbeatFailCount >= MaxHeartbeatFailCount) return;

            _heartbeatWarningShown = true;
            TM.App.Log($"[SAI] 心跳预警弹窗（{_heartbeatFailCount}/{MaxHeartbeatFailCount}）");

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    StandardDialog.ShowWarning(
                        $"网络心跳已连续失败 {_heartbeatFailCount} 次，连续失败 {MaxHeartbeatFailCount} 次后将自动退出登录。\n请尽快保存当前工作，并检查网络连接，避免数据丢失。",
                        "网络连接异常");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SAI] 预警弹窗显示失败: {ex.Message}");
                }
            });
        }

        private static void RequestReturnToLogin(string message)
        {
            if (_isReturningToLogin) return;
            _isReturningToLogin = true;

            try { _heartbeatCts?.Cancel(); } catch { }

            TM.App.Log("[SAI] req");
            OnReturnToLoginRequired?.Invoke(message);
        }
    }
}
