using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.PasswordSecurity.Services
{
    public class AccountLockoutService
    {
        private readonly PasswordSecuritySettings _securitySettings;
        private readonly ApiService _apiService;

        public AccountLockoutService(PasswordSecuritySettings securitySettings, ApiService apiService)
        {
            _securitySettings = securitySettings;
            _apiService = apiService;
            _ = Task.Run(async () => { try { await IsAccountLockedAsync().ConfigureAwait(false); } catch { } });
        }

        #region 锁定检查

        public async Task<bool> IsAccountLockedAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);

            if (data.IsPermanentlyLocked)
            {
                TM.App.Log("[ALS] locked");
                return true;
            }

            if (data.LockedUntil.HasValue)
            {
                if (DateTime.Now < data.LockedUntil.Value)
                {
                    TM.App.Log("[ALS] locked");
                    return true;
                }
                else
                {
                    data.LockedUntil = null;
                    settings.SaveLockoutDataAsync(data).SafeFireAndForget(ex => TM.App.Log($"[AccountLockoutService] 展期解锁保存失败: {ex.Message}"));
                    TM.App.Log("[ALS] unlock");
                }
            }

            return false;
        }

        public async Task<string> GetLockoutTimeRemainingAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);

            if (data.IsPermanentlyLocked)
                return "永久锁定";

            if (data.LockedUntil.HasValue && DateTime.Now < data.LockedUntil.Value)
            {
                var remaining = data.LockedUntil.Value - DateTime.Now;

                if (remaining.TotalHours >= 1)
                    return $"{remaining.Hours}小时{remaining.Minutes}分钟";
                else
                    return $"{remaining.Minutes}分钟{remaining.Seconds}秒";
            }

            return "未锁定";
        }

        #endregion

        #region 失败计数

        public async Task RecordFailedAttemptAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);
            data.FailedAttempts++;

            var attempt = new LoginAttemptRecord
            {
                Timestamp = DateTime.Now,
                IsSuccess = false,
                AttemptNumber = data.FailedAttempts
            };
            data.AttemptHistory.Add(attempt);

            ApplyLockoutPolicy(data);

            settings.SaveLockoutDataAsync(data).SafeFireAndForget(ex => TM.App.Log($"[AccountLockoutService] 记录失败保存失败: {ex.Message}"));

            TM.App.Log($"[AccountLockoutService] 登录失败 (第{data.FailedAttempts}次)");
        }

        public async Task ResetFailedAttemptsAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);

            if (data.FailedAttempts > 0)
            {
                TM.App.Log($"[AccountLockoutService] 重置失败计数（之前有{data.FailedAttempts}次失败）");
            }

            data.FailedAttempts = 0;
            data.LockedUntil = null;

            var attempt = new LoginAttemptRecord
            {
                Timestamp = DateTime.Now,
                IsSuccess = true,
                AttemptNumber = 0
            };
            data.AttemptHistory.Add(attempt);

            if (data.AttemptHistory.Count > 20)
            {
                data.AttemptHistory = data.AttemptHistory.Skip(data.AttemptHistory.Count - 20).ToList();
            }

            settings.SaveLockoutDataAsync(data).SafeFireAndForget(ex => TM.App.Log($"[AccountLockoutService] 重置失败计数保存失败: {ex.Message}"));
        }

        public async Task<int> GetFailedAttemptsAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);
            return data.FailedAttempts;
        }

        #endregion

        #region 锁定策略

        private void ApplyLockoutPolicy(AccountLockoutData data)
        {

            if (data.FailedAttempts >= 15)
            {
                data.IsPermanentlyLocked = true;
                data.LockedUntil = DateTime.MaxValue;
                TM.App.Log("[AccountLockoutService] [!] 账户已永久锁定（失败15次）");
            }
            else if (data.FailedAttempts >= 10)
            {
                data.LockedUntil = DateTime.Now.AddHours(1);
                TM.App.Log("[AccountLockoutService] [!] 账户锁定1小时（失败10次）");
            }
            else if (data.FailedAttempts >= 5)
            {
                data.LockedUntil = DateTime.Now.AddMinutes(15);
                TM.App.Log("[AccountLockoutService] [!] 账户锁定15分钟（失败5次）");
            }
        }

        public async Task<bool> UnlockAccountAsync()
        {
            try
            {
                var apiResult = await _apiService.UnlockAccountAsync().ConfigureAwait(false);
                if (!apiResult.Success)
                {
                    TM.App.Log($"[AccountLockoutService] 服务器解锁失败: {apiResult.Message}");
                    return false;
                }

                var settings = _securitySettings;
                var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);

                data.FailedAttempts = 0;
                data.LockedUntil = null;
                data.IsPermanentlyLocked = false;

                await settings.SaveLockoutDataAsync(data).ConfigureAwait(false);

                TM.App.Log("[AccountLockoutService] 账户已手动解锁（服务端+本地）");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountLockoutService] 解锁失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SyncLockoutStatusFromServerAsync()
        {
            try
            {
                var apiResult = await _apiService.GetLockoutStatusAsync().ConfigureAwait(false);
                if (apiResult.Success && apiResult.Data != null)
                {
                    var settings = _securitySettings;
                    var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);

                    data.FailedAttempts = apiResult.Data.FailedAttempts;
                    data.LockedUntil = apiResult.Data.LockedUntil;
                    data.IsPermanentlyLocked = apiResult.Data.IsPermanentlyLocked;

                    await settings.SaveLockoutDataAsync(data).ConfigureAwait(false);
                    TM.App.Log("[AccountLockoutService] 锁定状态已从服务器同步");
                    return true;
                }

                await _securitySettings.LoadLockoutDataAsync().ConfigureAwait(false);
                TM.App.Log($"[AccountLockoutService] 服务器同步失败: {apiResult.Message}");
                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountLockoutService] 同步锁定状态失败: {ex.Message}");
                try { await _securitySettings.LoadLockoutDataAsync().ConfigureAwait(false); } catch { }
                return false;
            }
        }

        #endregion

        #region 登录历史

        public async Task<List<LoginAttemptRecord>> GetRecentAttemptsAsync(int count = 5)
        {
            var settings = _securitySettings;
            var data = await settings.LoadLockoutDataAsync().ConfigureAwait(false);
            return data.AttemptHistory
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }

        #endregion

    }

    #region 数据模型

    public class AccountLockoutData
    {
        [System.Text.Json.Serialization.JsonPropertyName("FailedAttempts")] public int FailedAttempts { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("LockedUntil")] public DateTime? LockedUntil { get; set; } = null;
        [System.Text.Json.Serialization.JsonPropertyName("IsPermanentlyLocked")] public bool IsPermanentlyLocked { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("AttemptHistory")] public List<LoginAttemptRecord> AttemptHistory { get; set; } = new List<LoginAttemptRecord>();
    }

    public class LoginAttemptRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsSuccess")] public bool IsSuccess { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AttemptNumber")] public int AttemptNumber { get; set; }
    }

    #endregion
}

