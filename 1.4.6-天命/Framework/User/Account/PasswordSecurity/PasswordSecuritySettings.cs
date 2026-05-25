using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;
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

        public PasswordSecuritySettings()
        {
            var basePath = "Framework";
            var subPath = "User/Account/PasswordSecurity";
            _passwordHashFile = StoragePathHelper.GetFilePath(basePath, subPath, "password_hash.json");
            _passwordHistoryFile = StoragePathHelper.GetFilePath(basePath, subPath, "password_history.json");
            _twoFactorSecretFile = StoragePathHelper.GetFilePath(basePath, subPath, "2fa_secret.json");
            _lockoutDataFile = StoragePathHelper.GetFilePath(basePath, subPath, "lockout_data.json");
        }

        #region 密码数据持久化

        public PasswordData? LoadPasswordData()
        {
            try
            {
                if (File.Exists(_passwordHashFile))
                {
                    var json = File.ReadAllText(_passwordHashFile);
                    return JsonSerializer.Deserialize<PasswordData>(json);
                }
                return null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] load err: {ex.Message}");
                return null;
            }
        }

        public void SavePasswordData(PasswordData data)
        {
            try
            {
                var directory = Path.GetDirectoryName(_passwordHashFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(data, options);
                var tmpPh = _passwordHashFile + ".tmp";
                File.WriteAllText(tmpPh, json);
                File.Move(tmpPh, _passwordHashFile, overwrite: true);

                TM.App.Log("[PasswordSecuritySettings] saved");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] save err: {ex.Message}");
                throw;
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
                var json = JsonSerializer.Serialize(data, options);
                var tmpPhA = _passwordHashFile + ".tmp";
                await File.WriteAllTextAsync(tmpPhA, json);
                File.Move(tmpPhA, _passwordHashFile, overwrite: true);

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

        public List<string> LoadPasswordHistory()
        {
            try
            {
                if (File.Exists(_passwordHistoryFile))
                {
                    var json = File.ReadAllText(_passwordHistoryFile);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
                return new List<string>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] history load err: {ex.Message}");
                return new List<string>();
            }
        }

        public void SavePasswordHistory(List<string> history)
        {
            try
            {
                var directory = Path.GetDirectoryName(_passwordHistoryFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(history, options);
                var tmpHist = _passwordHistoryFile + ".tmp";
                File.WriteAllText(tmpHist, json);
                File.Move(tmpHist, _passwordHistoryFile, overwrite: true);

                TM.App.Log("[PasswordSecuritySettings] history saved");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] history save err: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 双因素认证持久化

        public TwoFactorAuthData? LoadTwoFactorData()
        {
            try
            {
                if (File.Exists(_twoFactorSecretFile))
                {
                    var json = File.ReadAllText(_twoFactorSecretFile);
                    return JsonSerializer.Deserialize<TwoFactorAuthData>(json);
                }
                return null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 加载双因素认证数据失败: {ex.Message}");
                return null;
            }
        }

        public void SaveTwoFactorData(TwoFactorAuthData data)
        {
            try
            {
                var directory = Path.GetDirectoryName(_twoFactorSecretFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(data, options);
                var tmpTf = _twoFactorSecretFile + ".tmp";
                File.WriteAllText(tmpTf, json);
                File.Move(tmpTf, _twoFactorSecretFile, overwrite: true);

                TM.App.Log("[PasswordSecuritySettings] 双因素认证数据已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 保存双因素认证数据失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 账户锁定持久化

        public AccountLockoutData LoadLockoutData()
        {
            try
            {
                if (File.Exists(_lockoutDataFile))
                {
                    var json = File.ReadAllText(_lockoutDataFile);
                    return JsonSerializer.Deserialize<AccountLockoutData>(json) ?? new AccountLockoutData();
                }
                return new AccountLockoutData();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 加载账户锁定数据失败: {ex.Message}");
                return new AccountLockoutData();
            }
        }

        public void SaveLockoutData(AccountLockoutData data)
        {
            try
            {
                var directory = Path.GetDirectoryName(_lockoutDataFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(data, options);
                var tmpLk = _lockoutDataFile + ".tmp";
                File.WriteAllText(tmpLk, json);
                File.Move(tmpLk, _lockoutDataFile, overwrite: true);

                TM.App.Log("[PasswordSecuritySettings] 账户锁定数据已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecuritySettings] 保存账户锁定数据失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 密码强度等级

        public int CurrentPasswordStrengthLevel
        {
            get
            {
                try
                {
                    var passwordData = LoadPasswordData();
                    if (passwordData != null)
                    {
                        return 2;
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(CurrentPasswordStrengthLevel), ex);
                    return 0;
                }
            }
        }

        #endregion
    }
}

