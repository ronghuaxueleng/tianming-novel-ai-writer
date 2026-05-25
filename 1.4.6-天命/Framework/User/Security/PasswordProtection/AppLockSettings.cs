using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using TM.Framework.User.Account.PasswordSecurity.Services;

namespace TM.Framework.User.Security.PasswordProtection
{
    public class AppLockSettings
    {

        private readonly string _configFile;
        private AppLockConfig? _cachedConfig;
        private bool _isCurrentlyLocked = false;

        public event EventHandler<bool>? LockStateChanged;

        public event EventHandler? ActivityTimeUpdated;

        public AppLockSettings()
        {
            _configFile = StoragePathHelper.GetFilePath("Framework", "User/Security/PasswordProtection", "app_lock_config.json");
            TM.App.Log("[AppLockSettings] init");
        }

        #region 配置管理

        public AppLockConfig LoadConfig()
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    _cachedConfig = JsonSerializer.Deserialize<AppLockConfig>(json) ?? new AppLockConfig();
                    TM.App.Log($"[AppLockSettings] 配置加载成功");
                }
                else
                {
                    _cachedConfig = new AppLockConfig();
                    SaveConfig(_cachedConfig);
                    TM.App.Log($"[AppLockSettings] 使用默认配置并保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppLockSettings] 配置加载失败: {ex.Message}");
                _cachedConfig = new AppLockConfig();
            }

            return _cachedConfig;
        }

        public bool SaveConfig(AppLockConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonHelper.CnDefault);

                var directory = Path.GetDirectoryName(_configFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tmpAls = _configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmpAls, json);
                File.Move(tmpAls, _configFile, overwrite: true);
                _cachedConfig = config;

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppLockSettings] 配置保存失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 锁定状态管理

        public bool IsLocked => _isCurrentlyLocked;

        public void LockApp(string? reason = null)
        {
            if (!_isCurrentlyLocked)
            {
                _isCurrentlyLocked = true;
                TM.App.Log($"[AppLockSettings] 应用已锁定{(reason != null ? $" - {reason}" : "")}");
                RecordLockEvent(reason ?? "手动锁定", true);
                LockStateChanged?.Invoke(this, true);
            }
        }

        public void UnlockApp()
        {
            if (_isCurrentlyLocked)
            {
                _isCurrentlyLocked = false;
                UpdateLastActivity();
                ResetFailedAttempts();
                TM.App.Log($"[AppLockSettings] 应用已解锁");
                RecordLockEvent("解锁成功", true);
                LockStateChanged?.Invoke(this, false);
            }
        }

        public bool ShouldLockOnStartup()
        {
            var config = LoadConfig();
            var shouldLock = config.EnablePasswordLock && config.LockOnStartup && HasPasswordSet();

            if (shouldLock)
            {
                TM.App.Log($"[AppLockSettings] 需要启动时锁定");
                RecordLockEvent("启动锁定", true, "应用启动时自动锁定");
            }

            return shouldLock;
        }

        public bool ShouldLockOnSwitch()
        {
            var config = LoadConfig();
            return config.EnablePasswordLock && config.LockOnSwitch && HasPasswordSet();
        }

        public bool HasPasswordSet()
        {
            return ServiceLocator.Get<AccountSecurityService>().HasPassword();
        }

        #endregion

        #region 自动锁定管理

        public void UpdateLastActivity()
        {
            var config = LoadConfig();
            config.LastActivityTime = DateTime.Now;
            _ = Task.Run(() => SaveConfig(config));
            ActivityTimeUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool ShouldAutoLock()
        {
            var config = LoadConfig();

            if (!config.EnableAutoLock || !config.EnablePasswordLock || !HasPasswordSet())
            {
                return false;
            }

            if (config.LastActivityTime == null)
            {
                return false;
            }

            var elapsed = DateTime.Now - config.LastActivityTime.Value;
            var threshold = TimeSpan.FromMinutes(config.AutoLockMinutes);

            return elapsed >= threshold;
        }

        public TimeSpan GetTimeUntilAutoLock()
        {
            var config = LoadConfig();

            if (config.LastActivityTime == null)
            {
                return TimeSpan.FromMinutes(config.AutoLockMinutes);
            }

            var elapsed = DateTime.Now - config.LastActivityTime.Value;
            var threshold = TimeSpan.FromMinutes(config.AutoLockMinutes);
            var remaining = threshold - elapsed;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        #endregion

        #region 失败处理

        public void IncrementFailedAttempts()
        {
            var config = LoadConfig();
            config.FailedAttempts++;

            TimeSpan lockoutDuration = TimeSpan.Zero;

            if (config.FailedAttempts >= 10)
            {
                lockoutDuration = TimeSpan.FromMinutes(30);
            }
            else if (config.FailedAttempts >= 5)
            {
                lockoutDuration = TimeSpan.FromMinutes(5);
            }
            else if (config.FailedAttempts >= 3)
            {
                lockoutDuration = TimeSpan.FromSeconds(30);
            }

            if (lockoutDuration > TimeSpan.Zero)
            {
                config.LockoutUntil = DateTime.Now.Add(lockoutDuration);
                TM.App.Log($"[AppLockSettings] 失败{config.FailedAttempts}次，锁定{lockoutDuration.TotalSeconds}秒");
            }

            SaveConfig(config);

            RecordLockEvent("解锁失败", false, $"连续失败{config.FailedAttempts}次");
        }

        public void ResetFailedAttempts()
        {
            var config = LoadConfig();
            config.FailedAttempts = 0;
            config.LockoutUntil = null;
            SaveConfig(config);
            TM.App.Log($"[AppLockSettings] 失败次数已重置");
        }

        public TimeSpan GetLockoutRemaining()
        {
            var config = LoadConfig();

            if (config.LockoutUntil == null || DateTime.Now >= config.LockoutUntil.Value)
            {
                return TimeSpan.Zero;
            }

            return config.LockoutUntil.Value - DateTime.Now;
        }

        public bool IsLockedOut()
        {
            var config = LoadConfig();

            if (config.LockoutUntil == null)
            {
                return false;
            }

            if (DateTime.Now >= config.LockoutUntil.Value)
            {
                config.LockoutUntil = null;
                SaveConfig(config);
                return false;
            }

            return true;
        }

        public int GetFailedAttempts()
        {
            var config = LoadConfig();
            return config.FailedAttempts;
        }

        #endregion

        #region EventLog

        public void RecordLockEvent(string eventType, bool success, string? details = null)
        {
            var message = $"[AppLock] {eventType} - {(success ? "ok" : "fail")}";
            if (!string.IsNullOrEmpty(details))
            {
                message += $": {details}";
            }
            TM.App.Log(message);
        }

        public int GetLockHistoryCount(int days)
        {
            return 0;
        }

        public int GetUnlockFailureCount(int days)
        {
            return 0;
        }

        #endregion

        #region 紧急解锁码

        public bool SetEmergencyCode(string code)
        {
            try
            {
                var config = LoadConfig();

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(code));
                    config.EmergencyCodeHash = Convert.ToBase64String(hash);
                }

                SaveConfig(config);
                TM.App.Log("[AppLockSettings] 紧急解锁码已设置");
                RecordLockEvent("设置紧急解锁码", true);
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppLockSettings] 设置紧急解锁码失败: {ex.Message}");
                return false;
            }
        }

        public bool VerifyEmergencyCode(string code)
        {
            try
            {
                var config = LoadConfig();

                if (string.IsNullOrEmpty(config.EmergencyCodeHash))
                {
                    return false;
                }

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(code));
                    var codeHash = Convert.ToBase64String(hash);

                    if (codeHash == config.EmergencyCodeHash)
                    {
                        config.EmergencyCodeHash = null;
                        SaveConfig(config);

                        TM.App.Log("[AppLockSettings] 紧急解锁码验证成功");
                        RecordLockEvent("使用紧急解锁码", true);
                        return true;
                    }
                }

                RecordLockEvent("使用紧急解锁码", false);
                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AppLockSettings] 验证紧急解锁码失败: {ex.Message}");
                return false;
            }
        }

        public bool HasEmergencyCode()
        {
            var config = LoadConfig();
            return !string.IsNullOrEmpty(config.EmergencyCodeHash);
        }

        #endregion
    }

    public class AppLockConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("ConfigVersion")] public int ConfigVersion { get; set; } = 1;
        [System.Text.Json.Serialization.JsonPropertyName("EnablePasswordLock")] public bool EnablePasswordLock { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("LockOnStartup")] public bool LockOnStartup { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("LockOnSwitch")] public bool LockOnSwitch { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("EnableAutoLock")] public bool EnableAutoLock { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("AutoLockMinutes")] public int AutoLockMinutes { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("LastActivityTime")] public DateTime? LastActivityTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("FailedAttempts")] public int FailedAttempts { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("LockoutUntil")] public DateTime? LockoutUntil { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EmergencyCode")] public string? EmergencyCode { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EmergencyCodeHash")] public string? EmergencyCodeHash { get; set; }
    }
}

