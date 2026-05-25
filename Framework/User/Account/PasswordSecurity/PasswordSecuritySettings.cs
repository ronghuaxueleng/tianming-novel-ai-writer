using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TM.Framework.User.Account.PasswordSecurity.Services;

namespace TM.Framework.User.Account.PasswordSecurity
{
    public class PasswordSecuritySettings
    {

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

            System.Diagnostics.Debug.WriteLine($"[PasswordSecuritySettings] {key}: {ex.Message}");
        }

        private readonly string _passwordHashFile;
        private readonly string _passwordHistoryFile;
        private readonly string _twoFactorSecretFile;
        private readonly string _lockoutDataFile;
        private int? _strengthLevelCache;
        private volatile AccountLockoutData? _lockoutDataCache;
        private volatile TwoFactorAuthData? _twoFactorDataCache;

        public PasswordSecuritySettings()
        {
            var basePath = "Framework";
            var subPath = "User/Account/PasswordSecurity";
            _passwordHashFile = StoragePathHelper.GetFilePath(basePath, subPath, "password_hash.json");
            _passwordHistoryFile = StoragePathHelper.GetFilePath(basePath, subPath, "password_history.json");
            _twoFactorSecretFile = StoragePathHelper.GetFilePath(basePath, subPath, "2fa_secret.json");
            _lockoutDataFile = StoragePathHelper.GetFilePath(basePath, subPath, "lockout_data.json");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var data = await LoadPasswordDataAsync().ConfigureAwait(false);
                    _strengthLevelCache = data?.StrengthLevel > 0 ? data.StrengthLevel : (data != null ? 2 : 0);
                }
                catch { }
            });
        }

        #region 密码数据持久化

        public async System.Threading.Tasks.Task<PasswordData?> LoadPasswordDataAsync()
        {
            try
            {
                if (File.Exists(_passwordHashFile))
                {
                    var json = await File.ReadAllTextAsync(_passwordHashFile).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<PasswordData>(json);
                }
                return null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] async load err: {ex.Message}");
                return null;
            }
        }

        public async System.Threading.Tasks.Task SavePasswordDataAsync(PasswordData data)
        {
            try
            {
                var directory = Path.GetDirectoryName(_passwordHashFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = JsonHelper.Default;
                var tmpPhA = _passwordHashFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpPhA))
                {
                    await JsonSerializer.SerializeAsync(stream, data, options);
                }
                File.Move(tmpPhA, _passwordHashFile, overwrite: true);
                _strengthLevelCache = null;

                TM.App.Log("[PasswordSecuritySettings] async saved");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] async save err: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 密码历史持久化

        public async System.Threading.Tasks.Task<List<string>> LoadPasswordHistoryAsync()
        {
            try
            {
                if (File.Exists(_passwordHistoryFile))
                {
                    var json = await File.ReadAllTextAsync(_passwordHistoryFile).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
                return new List<string>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] async history load err: {ex.Message}");
                return new List<string>();
            }
        }

        public async System.Threading.Tasks.Task SavePasswordHistoryAsync(List<string> history)
        {
            var json = JsonSerializer.Serialize(history, JsonHelper.Default);
            var tmp = _passwordHistoryFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var target = _passwordHistoryFile;
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }

        #endregion

        #region 双因素认证持久化

        public async System.Threading.Tasks.Task SaveTwoFactorDataAsync(TwoFactorAuthData data)
        {
            _twoFactorDataCache = data;
            var json = JsonSerializer.Serialize(data, JsonHelper.Default);
            var tmp = _twoFactorSecretFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var target = _twoFactorSecretFile;
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }

        public async System.Threading.Tasks.Task<TwoFactorAuthData?> LoadTwoFactorDataAsync()
        {
            var cached = _twoFactorDataCache;
            if (cached != null) return cached;

            try
            {
                if (File.Exists(_twoFactorSecretFile))
                {
                    var json = await File.ReadAllTextAsync(_twoFactorSecretFile).ConfigureAwait(false);
                    var data = JsonSerializer.Deserialize<TwoFactorAuthData>(json);
                    _twoFactorDataCache = data;
                    return data;
                }
                return null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 异步加载双因素认证数据失败: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 账户锁定持久化

        public async System.Threading.Tasks.Task<AccountLockoutData> LoadLockoutDataAsync()
        {
            var cached = _lockoutDataCache;
            if (cached != null)
                return cached;

            try
            {
                AccountLockoutData data;
                if (File.Exists(_lockoutDataFile))
                {
                    var json = await File.ReadAllTextAsync(_lockoutDataFile).ConfigureAwait(false);
                    data = JsonSerializer.Deserialize<AccountLockoutData>(json) ?? new AccountLockoutData();
                }
                else
                {
                    data = new AccountLockoutData();
                }
                _lockoutDataCache = data;
                return data;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 异步加载账户锁定数据失败: {ex.Message}");
                return new AccountLockoutData();
            }
        }

        public async System.Threading.Tasks.Task SaveLockoutDataAsync(AccountLockoutData data)
        {
            _lockoutDataCache = data;
            var json = JsonSerializer.Serialize(data, JsonHelper.Default);
            var tmp = _lockoutDataFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var target = _lockoutDataFile;
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }

        #endregion

        #region 密码强度等级

        public int CurrentPasswordStrengthLevel
        {
            get
            {
                if (_strengthLevelCache.HasValue)
                    return _strengthLevelCache.Value;

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var data = await LoadPasswordDataAsync().ConfigureAwait(false);
                        _strengthLevelCache = data?.StrengthLevel > 0 ? data.StrengthLevel : (data != null ? 2 : 0);
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce(nameof(CurrentPasswordStrengthLevel), ex);
                    }
                });
                return 0;
            }
        }

        #endregion
    }
}

