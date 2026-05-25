using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;
using TM.Framework.User.Security.PasswordProtection;
using TM.Framework.User.Account.PasswordSecurity;
using TM.Framework.User.Account.PasswordSecurity.Services;

namespace TM.Framework.User.Security.PasswordProtection.PasswordLock
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class PasswordLockViewModel : INotifyPropertyChanged
    {
        private bool _enablePasswordLock;
        private bool _lockOnStartup;
        private bool _lockOnSwitch;
        private bool _hasPassword;
        private int _weekLockCount;
        private int _weekFailureCount;
        private string _passwordStrength = "未设置";
        private string _passwordStrengthColor = "TextTertiary";

        private readonly AppLockSettings _lockSettings;
        private readonly PasswordSecuritySettings _passwordSettings;

        public PasswordLockViewModel(AppLockSettings lockSettings, PasswordSecuritySettings passwordSettings)
        {
            _lockSettings = lockSettings;
            _passwordSettings = passwordSettings;
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            TestLockCommand = new RelayCommand(TestLock, () => EnablePasswordLock && HasPassword);
            SetPasswordCommand = new RelayCommand(SetPassword);
            QuickSetupCommand = new RelayCommand<string>(QuickSetup);
            SetEmergencyCodeCommand = new RelayCommand(SetEmergencyCode);

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var config = _lockSettings.LoadConfig();
                var weekLock = _lockSettings.GetLockHistoryCount(7);
                var weekFail = _lockSettings.GetUnlockFailureCount(7);
                return () =>
                {
                    EnablePasswordLock = config.EnablePasswordLock;
                    LockOnStartup = config.LockOnStartup;
                    LockOnSwitch = config.LockOnSwitch;
                    HasPassword = _lockSettings.HasPasswordSet();
                    WeekLockCount = weekLock;
                    WeekFailureCount = weekFail;
                    CheckPasswordStrength();
                };
            }, "PasswordLock");

            TM.App.Log("[PasswordLockViewModel] 初始化完成");
        }

        #region 属性

        public bool EnablePasswordLock
        {
            get => _enablePasswordLock;
            set
            {
                if (_enablePasswordLock != value)
                {
                    _enablePasswordLock = value;
                    OnPropertyChanged(nameof(EnablePasswordLock));
                    OnPropertyChanged(nameof(LockOptionsEnabled));
                }
            }
        }

        public bool LockOnStartup
        {
            get => _lockOnStartup;
            set
            {
                if (_lockOnStartup != value)
                {
                    _lockOnStartup = value;
                    OnPropertyChanged(nameof(LockOnStartup));
                }
            }
        }

        public bool LockOnSwitch
        {
            get => _lockOnSwitch;
            set
            {
                if (_lockOnSwitch != value)
                {
                    _lockOnSwitch = value;
                    OnPropertyChanged(nameof(LockOnSwitch));
                }
            }
        }

        public bool HasPassword
        {
            get => _hasPassword;
            set
            {
                if (_hasPassword != value)
                {
                    _hasPassword = value;
                    OnPropertyChanged(nameof(HasPassword));
                    OnPropertyChanged(nameof(PasswordStatusText));
                    OnPropertyChanged(nameof(PasswordStatusColor));
                }
            }
        }

        public string PasswordStatusText => HasPassword ? "✅ 已设置密码" : "❌ 未设置密码";

        public string PasswordStatusColor => HasPassword ? "SuccessColor" : "DangerColor";

        public bool LockOptionsEnabled => EnablePasswordLock && HasPassword;

        public int WeekLockCount
        {
            get => _weekLockCount;
            set
            {
                if (_weekLockCount != value)
                {
                    _weekLockCount = value;
                    OnPropertyChanged(nameof(WeekLockCount));
                }
            }
        }

        public int WeekFailureCount
        {
            get => _weekFailureCount;
            set
            {
                if (_weekFailureCount != value)
                {
                    _weekFailureCount = value;
                    OnPropertyChanged(nameof(WeekFailureCount));
                }
            }
        }

        public string PasswordStrength
        {
            get => _passwordStrength;
            set
            {
                if (_passwordStrength != value)
                {
                    _passwordStrength = value;
                    OnPropertyChanged(nameof(PasswordStrength));
                }
            }
        }

        public string PasswordStrengthColor
        {
            get => _passwordStrengthColor;
            set
            {
                if (_passwordStrengthColor != value)
                {
                    _passwordStrengthColor = value;
                    OnPropertyChanged(nameof(PasswordStrengthColor));
                }
            }
        }

        public bool HasEmergencyCode => _lockSettings.HasEmergencyCode();

        #endregion

        #region 命令

        public ICommand SaveSettingsCommand { get; }
        public ICommand TestLockCommand { get; }
        public ICommand SetPasswordCommand { get; }
        public ICommand QuickSetupCommand { get; }
        public ICommand SetEmergencyCodeCommand { get; }

        #endregion

        #region 方法

        private void SaveSettings()
        {
            try
            {
                if (EnablePasswordLock && !HasPassword)
                {
                    var result = StandardDialog.ShowConfirm(
                        "您还未设置密码，是否现在设置？\n\n设置密码后才能启用密码锁定功能。",
                        "未设置密码"
                    );

                    if (result)
                    {
                        SetPassword();
                    }
                    return;
                }

                var config = _lockSettings.LoadConfig();
                config.EnablePasswordLock = EnablePasswordLock;
                config.LockOnStartup = LockOnStartup;
                config.LockOnSwitch = LockOnSwitch;

                if (_lockSettings.SaveConfig(config))
                {
                    GlobalToast.Success("保存成功", "密码锁定设置已保存");
                    TM.App.Log("[PasswordLockViewModel] 设置保存成功");
                }
                else
                {
                    GlobalToast.Error("保存失败", "无法保存密码锁定设置");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存设置时出错: {ex.Message}");
            }
        }

        private void TestLock()
        {
            try
            {
                TM.App.Log("[PasswordLockViewModel] 用户触发测试锁定");
                _lockSettings.LockApp();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] 测试锁定失败: {ex.Message}");
                GlobalToast.Error("测试失败", $"触发锁定时出错: {ex.Message}");
            }
        }

        private void SetPassword()
        {
            try
            {
                TM.App.Log("[PasswordLockViewModel] setup prompt");
                StandardDialog.ShowInfo(
                    "请导航到以下路径设置密码：\n\n" +
                    "用户设置 → 账户管理 → 密码安全\n\n" +
                    "在密码安全页面中，您可以设置或修改应用密码。",
                    "设置密码"
                );
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] 提示失败: {ex.Message}");
                GlobalToast.Error("错误", $"无法显示提示: {ex.Message}");
            }
        }

        private void QuickSetup(string? preset)
        {
            try
            {
                if (!HasPassword)
                {
                    GlobalToast.Warning("未设置密码", "请先设置密码后再使用快捷设置");
                    return;
                }

                switch (preset)
                {
                    case "high_security":
                        EnablePasswordLock = true;
                        LockOnStartup = true;
                        LockOnSwitch = true;
                        var config = _lockSettings.LoadConfig();
                        config.EnableAutoLock = true;
                        config.AutoLockMinutes = 5;
                        _lockSettings.SaveConfig(config);
                        GlobalToast.Success("已应用", "高安全配置：启动锁定 + 切换锁定 + 5分钟自动锁定");
                        break;

                    case "balanced":
                        EnablePasswordLock = true;
                        LockOnStartup = true;
                        LockOnSwitch = false;
                        config = _lockSettings.LoadConfig();
                        config.EnableAutoLock = true;
                        config.AutoLockMinutes = 15;
                        _lockSettings.SaveConfig(config);
                        GlobalToast.Success("已应用", "平衡配置：启动锁定 + 15分钟自动锁定");
                        break;

                    case "convenient":
                        EnablePasswordLock = true;
                        LockOnStartup = true;
                        LockOnSwitch = false;
                        config = _lockSettings.LoadConfig();
                        config.EnableAutoLock = false;
                        _lockSettings.SaveConfig(config);
                        GlobalToast.Success("已应用", "便利配置：仅启动锁定");
                        break;
                }

                SaveSettings();
                TM.App.Log($"[PasswordLockViewModel] 应用快捷设置: {preset}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] 快捷设置失败: {ex.Message}");
                GlobalToast.Error("设置失败", $"应用快捷设置时出错: {ex.Message}");
            }
        }

        private void SetEmergencyCode()
        {
            try
            {
                var code = StandardDialog.ShowInput(
                    "请输入紧急解锁码（6-20位字符）：\n\n" +
                    "注意：紧急解锁码使用后会自动失效，需要重新设置。\n" +
                    "请妥善保管，不要与应用密码相同。",
                    "设置紧急解锁码"
                );

                if (string.IsNullOrWhiteSpace(code))
                {
                    return;
                }

                if (code.Length < 6 || code.Length > 20)
                {
                    GlobalToast.Warning("长度不符", "紧急解锁码长度必须在6-20位之间");
                    return;
                }

                if (_lockSettings.SetEmergencyCode(code))
                {
                    GlobalToast.Success("设置成功", "紧急解锁码已设置");
                    OnPropertyChanged(nameof(HasEmergencyCode));
                }
                else
                {
                    GlobalToast.Error("设置失败", "无法设置紧急解锁码");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] 设置紧急解锁码失败: {ex.Message}");
                GlobalToast.Error("设置失败", $"设置紧急解锁码时出错: {ex.Message}");
            }
        }

        private void CheckPasswordStrength()
        {
            try
            {
                if (!HasPassword)
                {
                    PasswordStrength = "未设置";
                    PasswordStrengthColor = "TextTertiary";
                    return;
                }

                var strengthLevel = _passwordSettings.CurrentPasswordStrengthLevel;

                switch (strengthLevel)
                {
                    case 0:
                    case 1:
                        PasswordStrength = "弱";
                        PasswordStrengthColor = "DangerColor";
                        break;
                    case 2:
                        PasswordStrength = "中";
                        PasswordStrengthColor = "WarningColor";
                        break;
                    case 3:
                        PasswordStrength = "强";
                        PasswordStrengthColor = "SuccessColor";
                        break;
                    case 4:
                    case 5:
                        PasswordStrength = "很强";
                        PasswordStrengthColor = "SuccessColor";
                        break;
                    default:
                        PasswordStrength = "中";
                        PasswordStrengthColor = "WarningColor";
                        break;
                }

                TM.App.Log($"[PasswordLockViewModel] strength: {PasswordStrength} ({strengthLevel})");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockViewModel] check err: {ex.Message}");
                PasswordStrength = "中";
                PasswordStrengthColor = "WarningColor";
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

