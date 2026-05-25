using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.User.Account.PasswordSecurity.Services;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.PasswordSecurity
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class PasswordSecurityViewModel : INotifyPropertyChanged
    {
        private static readonly System.Collections.Generic.Dictionary<string, SolidColorBrush> _brushCache = new();

        private static SolidColorBrush GetCachedBrush(string colorHex)
        {
            if (!_brushCache.TryGetValue(colorHex, out var brush))
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                brush.Freeze();
                _brushCache[colorHex] = brush;
            }
            return brush;
        }

        private readonly AccountSecurityService _securityService;
        private readonly AccountLockoutService _lockoutService;
        private readonly ApiService _apiService;
        private bool _suppressTwoFactorToggle;

        public PasswordSecurityViewModel(AccountSecurityService securityService, AccountLockoutService lockoutService, ApiService apiService)
        {
            _securityService = securityService;
            _lockoutService = lockoutService;
            _apiService = apiService;

            ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync);
            GenerateQRCodeCommand = new RelayCommand(GenerateQRCode);
            VerifyCodeCommand = new AsyncRelayCommand(VerifyCodeAsync);
            CopySecretCommand = new RelayCommand(CopySecret);
            UnlockAccountCommand = new AsyncRelayCommand(_ => UnlockAccountAsync());

            AsyncSettingsLoader.RunOrDeferAsync(async () =>
            {
                var isTwoFactor = await _securityService.IsTwoFactorEnabledAsync().ConfigureAwait(false);
                var secret = isTwoFactor ? await _securityService.GetTwoFactorSecretAsync().ConfigureAwait(false) ?? string.Empty : string.Empty;
                return () =>
                {
                    _suppressTwoFactorToggle = true;
                    IsTwoFactorEnabled = isTwoFactor;
                    _suppressTwoFactorToggle = false;
                    if (isTwoFactor) TwoFactorSecret = secret;
                    _ = LoadLockoutStatusAsync();
                };
            }, "PasswordSecurity");
        }

        #region 属性

        private string _oldPassword = string.Empty;
        public string OldPassword
        {
            get => _oldPassword;
            set
            {
                _oldPassword = value;
                OnPropertyChanged();
            }
        }

        private string _newPassword = string.Empty;
        public string NewPassword
        {
            get => _newPassword;
            set
            {
                _newPassword = value;
                OnPropertyChanged();
                UpdatePasswordStrength();
            }
        }

        private string _confirmPassword = string.Empty;
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                _confirmPassword = value;
                OnPropertyChanged();
            }
        }

        private int _passwordStrength;
        public int PasswordStrength
        {
            get => _passwordStrength;
            set
            {
                _passwordStrength = value;
                OnPropertyChanged();
            }
        }

        private string _passwordStrengthText = string.Empty;
        public string PasswordStrengthText
        {
            get => _passwordStrengthText;
            set
            {
                _passwordStrengthText = value;
                OnPropertyChanged();
            }
        }

        private SolidColorBrush _passwordStrengthColor = GetCachedBrush("#9E9E9E");
        public SolidColorBrush PasswordStrengthColor
        {
            get => _passwordStrengthColor;
            set
            {
                _passwordStrengthColor = value;
                OnPropertyChanged();
            }
        }

        private bool _isTwoFactorEnabled;
        public bool IsTwoFactorEnabled
        {
            get => _isTwoFactorEnabled;
            set
            {
                if (_isTwoFactorEnabled != value)
                {
                    _isTwoFactorEnabled = value;
                    OnPropertyChanged();

                    if (!_suppressTwoFactorToggle)
                    {
                        if (value) EnableTwoFactor();
                        else DisableTwoFactor();
                    }
                }
            }
        }

        private string _twoFactorSecret = string.Empty;
        public string TwoFactorSecret
        {
            get => _twoFactorSecret;
            set
            {
                _twoFactorSecret = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedSecret));
            }
        }

        public string FormattedSecret => QRCodeGenerator.FormatSecret(TwoFactorSecret);

        private ImageSource? _qrCodeImage;
        public ImageSource? QRCodeImage
        {
            get => _qrCodeImage;
            set
            {
                _qrCodeImage = value;
                OnPropertyChanged();
            }
        }

        private string _verificationCode = string.Empty;
        public string VerificationCode
        {
            get => _verificationCode;
            set
            {
                _verificationCode = value;
                OnPropertyChanged();
            }
        }

        private int _failedLoginAttempts;
        public int FailedLoginAttempts
        {
            get => _failedLoginAttempts;
            set
            {
                _failedLoginAttempts = value;
                OnPropertyChanged();
            }
        }

        private bool _isAccountLocked;
        public bool IsAccountLocked
        {
            get => _isAccountLocked;
            set
            {
                _isAccountLocked = value;
                OnPropertyChanged();
            }
        }

        private string _lockoutTimeRemaining = string.Empty;
        public string LockoutTimeRemaining
        {
            get => _lockoutTimeRemaining;
            set
            {
                _lockoutTimeRemaining = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<LoginAttemptRecord> _recentAttempts = new ObservableCollection<LoginAttemptRecord>();
        public ObservableCollection<LoginAttemptRecord> RecentAttempts
        {
            get => _recentAttempts;
            set
            {
                _recentAttempts = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 命令

        public ICommand ChangePasswordCommand { get; }
        public ICommand GenerateQRCodeCommand { get; }
        public ICommand VerifyCodeCommand { get; }
        public ICommand CopySecretCommand { get; }
        public ICommand UnlockAccountCommand { get; }

        #endregion

        private async Task LoadLockoutStatusAsync()
        {
            await _lockoutService.SyncLockoutStatusFromServerAsync();

            IsAccountLocked = await _lockoutService.IsAccountLockedAsync();
            FailedLoginAttempts = await _lockoutService.GetFailedAttemptsAsync();
            LockoutTimeRemaining = await _lockoutService.GetLockoutTimeRemainingAsync();

            var attempts = await _lockoutService.GetRecentAttemptsAsync(5);
            RecentAttempts = new ObservableCollection<LoginAttemptRecord>(attempts);
        }

        private void UpdatePasswordStrength()
        {
            var score = PasswordStrengthValidator.GetStrengthScore(NewPassword);
            PasswordStrength = score;

            var strength = PasswordStrengthValidator.ValidateStrength(NewPassword);
            PasswordStrengthText = PasswordStrengthValidator.GetStrengthText(strength);

            var colorHex = PasswordStrengthValidator.GetStrengthColor(strength);
            PasswordStrengthColor = GetCachedBrush(colorHex);
        }

        private async Task ChangePasswordAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(OldPassword))
                {
                    GlobalToast.Warning("修改密码", "请输入旧密码");
                    return;
                }

                if (string.IsNullOrEmpty(NewPassword))
                {
                    GlobalToast.Warning("修改密码", "请输入新密码");
                    return;
                }

                if (NewPassword != ConfirmPassword)
                {
                    GlobalToast.Error("修改密码", "两次输入的密码不一致");
                    return;
                }

                var (isValid, message) = PasswordStrengthValidator.ValidatePassword(NewPassword);
                if (!isValid)
                {
                    GlobalToast.Error("修改密码", message);
                    return;
                }

                if (!await _securityService.HasPasswordAsync())
                {
                    await _securityService.SetInitialPasswordAsync(NewPassword).ConfigureAwait(true);
                    GlobalToast.Success("设置密码", "密码设置成功");
                    ClearPasswordFields();
                    return;
                }

                var localVerifyOk = await _securityService.VerifyPasswordAsync(OldPassword).ConfigureAwait(true);
                if (!localVerifyOk)
                {
                    GlobalToast.Error("修改密码", "旧密码验证失败");
                    return;
                }

                var apiResult = await _apiService.ChangePasswordAsync(OldPassword, NewPassword);
                if (!apiResult.Success)
                {
                    TM.App.Log($"[PasswordSecurity] 修改密码失败: {apiResult.Message}");
                    var errorMsg = apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR
                        ? "网络连接失败，请检查网络后重试"
                        : $"服务器同步失败：{apiResult.Message}";
                    GlobalToast.Error("修改密码", errorMsg);
                    return;
                }

                var success = await _securityService.ChangePasswordAsync(OldPassword, NewPassword).ConfigureAwait(true);
                if (success)
                {
                    GlobalToast.Success("修改密码", "密码修改成功");
                    ClearPasswordFields();
                }
                else
                {
                    GlobalToast.Error("修改密码", "新密码与历史密码重复");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] change err: {ex.Message}");
                GlobalToast.Error("修改密码", $"操作失败：{ex.Message}");
            }
        }

        private void EnableTwoFactor() => _ = EnableTwoFactorAsync();

        private async System.Threading.Tasks.Task EnableTwoFactorAsync()
        {
            try
            {
                var secret = await _securityService.EnableTwoFactorAuthAsync().ConfigureAwait(true);
                TwoFactorSecret = secret;
                GenerateQRCode();
                TM.App.Log("[PasswordSecurityViewModel] 双因素认证已启用");
                GlobalToast.Success("双因素认证", "已启用双因素认证，请扫描二维码");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] 启用双因素认证失败: {ex.Message}");
                GlobalToast.Error("双因素认证", $"启用失败：{ex.Message}");
                _suppressTwoFactorToggle = true;
                IsTwoFactorEnabled = false;
                _suppressTwoFactorToggle = false;
            }
        }

        private void DisableTwoFactor() => _ = DisableTwoFactorAsync();

        private async System.Threading.Tasks.Task DisableTwoFactorAsync()
        {
            try
            {
                await _securityService.DisableTwoFactorAuthAsync().ConfigureAwait(true);
                TwoFactorSecret = string.Empty;
                QRCodeImage = null;
                TM.App.Log("[PasswordSecurityViewModel] 双因素认证已禁用");
                GlobalToast.Info("双因素认证", "已禁用双因素认证");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] 禁用双因素认证失败: {ex.Message}");
                GlobalToast.Error("双因素认证", $"禁用失败：{ex.Message}");
            }
        }

        private void GenerateQRCode()
        {
            try
            {
                var uri = QRCodeGenerator.GenerateTOTPUri(TwoFactorSecret, "天命用户");
                QRCodeImage = QRCodeGenerator.GenerateQRCodeImage(uri);

                TM.App.Log("[PasswordSecurityViewModel] 二维码已生成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] 生成二维码失败: {ex.Message}");
                GlobalToast.Error("生成二维码", $"操作失败：{ex.Message}");
            }
        }

        private async Task VerifyCodeAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(VerificationCode))
                {
                    GlobalToast.Warning("验证码验证", "请输入验证码");
                    return;
                }

                var code = VerificationCode;
                var isValid = await _securityService.VerifyTOTPCodeAsync(code).ConfigureAwait(true);
                if (isValid)
                {
                    GlobalToast.Success("验证码验证", "验证成功");
                }
                else
                {
                    GlobalToast.Error("验证码验证", "验证码错误");
                }

                VerificationCode = string.Empty;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] 验证码验证失败: {ex.Message}");
                GlobalToast.Error("验证码验证", $"操作失败：{ex.Message}");
            }
        }

        private void CopySecret()
        {
            try
            {
                System.Windows.Clipboard.SetText(TwoFactorSecret);
                GlobalToast.Success("复制密钥", "密钥已复制到剪贴板");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] copy err: {ex.Message}");
                GlobalToast.Error("复制密钥", "复制失败");
            }
        }

        private async Task UnlockAccountAsync()
        {
            try
            {
                var success = await _lockoutService.UnlockAccountAsync();
                await LoadLockoutStatusAsync();

                if (success)
                {
                    GlobalToast.Success("解锁账户", "账户已成功解锁");
                    TM.App.Log("[PasswordSecurityViewModel] 账户已手动解锁");
                }
                else
                {
                    GlobalToast.Error("解锁账户", "服务器解锁失败，请检查网络后重试");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordSecurityViewModel] 解锁账户失败: {ex.Message}");
                GlobalToast.Error("解锁账户", $"操作失败：{ex.Message}");
            }
        }

        private void ClearPasswordFields()
        {
            OldPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordStrength = 0;
            PasswordStrengthText = string.Empty;
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

