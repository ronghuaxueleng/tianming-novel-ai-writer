using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TM.Framework.Common.Helpers.Security;
using TM.Framework.Common.Services;
using TM.Framework.User.Account.AccountBinding;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.Login
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LoginWindow : Window
    {
        public bool LoginSuccess { get; private set; }
        public string? LoggedInUsername { get; private set; }

        private readonly LoginService _loginService;
        private readonly AccountBindingService _accountBindingService;
        private readonly OAuthService _oAuthService;
        private readonly ApiService _apiService;
        private readonly AuthTokenManager _authTokenManager;

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

            System.Diagnostics.Debug.WriteLine($"[LoginWindow] {key}: {ex.Message}");
        }

        private bool _isPasswordVisible;
        private bool _isPasswordUpdating;

        private bool _rememberAccount;
        private bool _rememberPassword;

        public LoginWindow()
        {
            InitializeComponent();

            _loginService = ServiceLocator.Get<LoginService>();
            _accountBindingService = ServiceLocator.Get<AccountBindingService>();
            _oAuthService = ServiceLocator.Get<OAuthService>();
            _apiService = ServiceLocator.Get<ApiService>();
            _authTokenManager = ServiceLocator.Get<AuthTokenManager>();

            InitializeAccountList();
            LoadRememberedState();
            UpdateRememberOptionsButtonContent();
            LoadAppIcon();
            LoadThirdPartyIcons();
            UpdateRememberMenuHeaders();
            UpdatePasswordVisibilityUI();

            if (!_loginService.HasAnyAccount())
            {
                FirstTimeHintTextBlock.Visibility = Visibility.Visible;
            }

            TM.App.Log("[LoginWindow] 登录窗口已初始化");
        }

        private void UpdateRememberMenuHeaders()
        {
            if (RememberAccountMenuItem == null || RememberPasswordMenuItem == null)
            {
                return;
            }

            RememberAccountMenuItem.Header = _rememberAccount ? "不记住" : "记住账号";
            RememberPasswordMenuItem.Header = _rememberPassword ? "不记住" : "记住密码";

            var checkedBrush = TryFindResource("PrimaryColor") as Brush ?? Brushes.Black;
            var uncheckedBrush = TryFindResource("TextSecondary") as Brush ?? Brushes.Black;

            RememberAccountMenuItem.Foreground = _rememberAccount ? checkedBrush : uncheckedBrush;
            RememberPasswordMenuItem.Foreground = _rememberPassword ? checkedBrush : uncheckedBrush;
        }

        private void UpdatePasswordVisibilityUI()
        {
            if (VisiblePasswordTextBox == null || PasswordBox == null || TogglePasswordVisibilityIcon == null)
            {
                return;
            }

            if (_isPasswordVisible)
            {
                VisiblePasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                TogglePasswordVisibilityIcon.Text = "🙈";
            }
            else
            {
                VisiblePasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                TogglePasswordVisibilityIcon.Text = "👁";
            }
        }

        private void LoadThirdPartyIcons()
        {
            try
            {
                WeChatIcon.Source = AccountIconHelper.GetIcon("weixin.jpg");
                QQIcon.Source = AccountIconHelper.GetIcon("qq.png");
                GitHubIcon.Source = AccountIconHelper.GetIcon("Github.png");
                GoogleIcon.Source = AccountIconHelper.GetIcon("Google.png");
                MicrosoftIcon.Source = AccountIconHelper.GetIcon("Microsoft.png");
                BaiduIcon.Source = AccountIconHelper.GetIcon("Baidu.png");
                TM.App.Log("[LoginWindow] 第三方登录图标已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 加载第三方登录图标失败: {ex.Message}");
            }
        }

        private void SetPassword(string password)
        {
            try
            {
                _isPasswordUpdating = true;
                PasswordBox.Password = password;
                VisiblePasswordTextBox.Text = password;
            }
            finally
            {
                _isPasswordUpdating = false;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPasswordUpdating)
                return;

            try
            {
                _isPasswordUpdating = true;
                VisiblePasswordTextBox.Text = PasswordBox.Password;
            }
            finally
            {
                _isPasswordUpdating = false;
            }
        }

        private void VisiblePasswordTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isPasswordUpdating)
                return;

            try
            {
                _isPasswordUpdating = true;
                PasswordBox.Password = VisiblePasswordTextBox.Text;
            }
            finally
            {
                _isPasswordUpdating = false;
            }
        }

        private void TogglePasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;
            UpdatePasswordVisibilityUI();

            if (_isPasswordVisible)
            {
                VisiblePasswordTextBox?.Focus();
                if (VisiblePasswordTextBox != null)
                {
                    VisiblePasswordTextBox.CaretIndex = VisiblePasswordTextBox.Text?.Length ?? 0;
                }
            }
            else
            {
                PasswordBox?.Focus();
            }
        }

        private void LoadAppIcon()
        {
            try
            {
                var iconPath = StoragePathHelper.GetFrameworkPath("UI/Icons/app.ico");
                if (!File.Exists(iconPath))
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var decoder = new IconBitmapDecoder(new Uri(iconPath, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var target = 30;
                var best = decoder.Frames
                    .OrderBy(f => Math.Abs(f.PixelWidth - target))
                    .ThenByDescending(f => f.PixelWidth)
                    .FirstOrDefault();

                var source = best ?? decoder.Frames.FirstOrDefault();
                if (source == null)
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var brush = new ImageBrush(source)
                {
                    Stretch = Stretch.Uniform
                };
                if (brush.CanFreeze)
                    brush.Freeze();

                AppIconBorder.Background = brush;
                AppIconBorder.Visibility = Visibility.Visible;
                FallbackIconTextBlock.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 加载应用图标失败: {ex.Message}");
                AppIconBorder.Background = null;
                AppIconBorder.Visibility = Visibility.Collapsed;
                FallbackIconTextBlock.Visibility = Visibility.Visible;
            }
        }

        private const string ClearHistoryItem = "🗑️ 清除历史记录";

        private void InitializeAccountList()
        {
            try
            {
                UsernameComboBox.Items.Clear();

                var accounts = _loginService.GetAllAccounts()
                    .OrderByDescending(a => a.LastLoginTime ?? DateTime.MinValue)
                    .ThenBy(a => a.Username)
                    .ToList();

                foreach (var account in accounts)
                {
                    if (!string.IsNullOrWhiteSpace(account.Username))
                        UsernameComboBox.Items.Add(account.Username);
                }

                if (accounts.Count > 0)
                {
                    UsernameComboBox.Items.Add(ClearHistoryItem);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 加载历史账号失败: {ex.Message}");
            }
        }

        private void LoadRememberedState()
        {
            try
            {
                var remembered = _loginService.GetRememberedAccountInfo();

                _rememberAccount = remembered?.RememberAccount == true;
                _rememberPassword = remembered?.RememberPassword == true;
                UpdateRememberMenuHeaders();

                if (!string.IsNullOrWhiteSpace(remembered?.Username))
                {
                    UsernameComboBox.Text = remembered.Username;

                    if (_rememberPassword && !string.IsNullOrWhiteSpace(remembered.EncryptedPassword))
                    {
                        try
                        {
                            SetPassword(EncryptionHelper.DecryptApiKey(remembered.EncryptedPassword));
                        }
                        catch (Exception ex)
                        {
                            DebugLogOnce("DecryptRememberedPassword", ex);
                            SetPassword(string.Empty);
                        }
                    }

                    if (!string.IsNullOrEmpty(PasswordBox.Password))
                        LoginButton.Focus();
                    else
                        PasswordBox.Focus();
                }
                else
                {
                    UsernameComboBox.Focus();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 加载记住信息失败: {ex.Message}");
                UsernameComboBox.Focus();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LoginWindow] DragMove失败: {ex.Message}");
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            TM.App.Log("[LoginWindow] 用户点击关闭按钮，退出程序");
            Application.Current.Shutdown();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            TM.App.Log("[LoginWindow] 用户点击取消按钮，退出程序");
            Application.Current.Shutdown();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            _ = PerformLoginAsync();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = PerformLoginAsync();
            }
        }

        private void UsernameComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                ErrorTextBlock.Visibility = Visibility.Collapsed;

                var selectedItem = UsernameComboBox.SelectedItem?.ToString() ?? string.Empty;

                if (selectedItem == ClearHistoryItem)
                {
                    UsernameComboBox.SelectedItem = null;
                    UsernameComboBox.Text = string.Empty;

                    var result = StandardDialog.ShowConfirm("确定要清除所有历史账号记录吗？", "清除确认");
                    if (result)
                    {
                        _loginService.ClearAllAccounts();
                        _loginService.ClearRememberedAccount();
                        SetPassword(string.Empty);
                        InitializeAccountList();
                        GlobalToast.Success("已清除", "历史账号记录已清除");
                        TM.App.Log("[LoginWindow] 用户清除了历史账号记录");
                    }
                    return;
                }

                var username = UsernameComboBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username))
                {
                    SetPassword(string.Empty);
                    return;
                }

                var remembered = _loginService.GetRememberedAccountInfo();
                if (remembered != null && string.Equals(remembered.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    if (_rememberPassword && !string.IsNullOrWhiteSpace(remembered.EncryptedPassword))
                    {
                        try
                        {
                            SetPassword(EncryptionHelper.DecryptApiKey(remembered.EncryptedPassword));
                            return;
                        }
                        catch (Exception ex)
                        {
                            DebugLogOnce("DecryptRememberedPassword_OnUserChanged", ex);
                            SetPassword(string.Empty);
                        }
                    }
                }

                SetPassword(string.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 切换历史账号异常: {ex.Message}");
                SetPassword(string.Empty);
            }
        }

        private void RememberOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = RememberOptionsButton?.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.PlacementTarget = RememberOptionsButton;
            menu.IsOpen = true;
        }

        private void RememberOptionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender == RememberPasswordMenuItem)
            {
                if (_rememberPassword)
                {
                    _rememberPassword = false;
                }
                else
                {
                    _rememberPassword = true;
                    _rememberAccount = true;
                }
            }
            else if (sender == RememberAccountMenuItem)
            {
                if (_rememberAccount)
                {
                    _rememberAccount = false;
                    _rememberPassword = false;
                }
                else
                {
                    _rememberAccount = true;
                }
            }

            UpdateRememberMenuHeaders();
            UpdateRememberOptionsButtonContent();
        }

        private void UpdateRememberOptionsButtonContent()
        {
            if (RememberOptionsButton == null)
            {
                return;
            }

            if (_rememberPassword)
            {
                RememberOptionsButton.Content = "记住账号/密码 ▼";
            }
            else if (_rememberAccount)
            {
                RememberOptionsButton.Content = "记住账号 ▼";
            }
            else
            {
                RememberOptionsButton.Content = "不记住 ▼";
            }
        }

        private void MoreOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = MoreOptionsButton?.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.PlacementTarget = MoreOptionsButton;
            menu.IsOpen = true;
        }

        private void RegisterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var defaultUsername = UsernameComboBox.Text?.Trim() ?? string.Empty;
                var win = new RegisterWindow(defaultUsername)
                {
                    Owner = this
                };

                var result = win.ShowDialog();
                if (result != true)
                    return;

                if (string.IsNullOrWhiteSpace(win.RegisteredUsername) || string.IsNullOrWhiteSpace(win.RegisteredPassword))
                {
                    ShowError("注册失败：未返回账号信息");
                    return;
                }

                InitializeAccountList();
                UsernameComboBox.Text = win.RegisteredUsername.Trim();
                SetPassword(win.RegisteredPassword);

                StandardDialog.ShowInfo("账号创建成功，请直接登录。", "注册账号", this);
                PasswordBox.Focus();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 注册账号异常: {ex.Message}");
                ShowError($"注册失败: {ex.Message}");
            }
        }

        private void RenewAccountMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new AccountRenewDialog { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 打开账号续费异常: {ex.Message}");
                ShowError($"操作失败: {ex.Message}");
            }
        }

        private async Task PerformLoginAsync()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;

            var username = (UsernameComboBox.Text ?? string.Empty).Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("请输入账号");
                UsernameComboBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("请输入密码");
                PasswordBox.Focus();
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButton.Content = "登录中...";

            try
            {
                var loginResult = await _loginService.VerifyLoginAsync(username, password);

                if (loginResult.Success)
                {
                    LoggedInUsername = username;
                    LoginSuccess = true;

                    bool rememberAccount = _rememberAccount;
                    bool rememberPassword = _rememberPassword;

                    if (rememberAccount)
                    {
                        string? encrypted = null;
                        if (rememberPassword)
                        {
                            try
                            {
                                encrypted = EncryptionHelper.EncryptApiKey(password);
                            }
                            catch (Exception ex)
                            {
                                DebugLogOnce("EncryptRememberedPassword", ex);
                                encrypted = null;
                            }
                        }

                        _loginService.SaveRememberedAccount(username, true, rememberPassword, encrypted);
                    }
                    else
                    {
                        _loginService.ClearRememberedAccount();
                    }

                    TM.App.Log($"[LoginWindow] 登录成功: {username}");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    if (loginResult.ErrorCode == ApiErrorCodes.AUTH_DEVICE_KICKED)
                    {
                        ShowError("您的账号已在其他设备登录，当前会话已失效");
                    }
                    else if (loginResult.ErrorCode == ApiErrorCodes.SUBSCRIPTION_NONE)
                    {
                        StandardDialog.ShowWarning(
                            "账号未激活\n\n请先使用卡密激活后再登录。\n您可以点击「更多选项」→「账号续费」使用卡密激活。",
                            "登录失败", this);
                    }
                    else if (loginResult.ErrorCode == ApiErrorCodes.SUBSCRIPTION_EXPIRED)
                    {
                        StandardDialog.ShowWarning(
                            $"{loginResult.ErrorMessage}\n\n请点击「更多选项」→「账号续费」使用卡密续费。",
                            "登录失败", this);
                    }
                    else
                    {
                        ShowError(loginResult.ErrorMessage ?? "登录失败");
                    }
                    SetPassword(string.Empty);
                    PasswordBox.Focus();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 登录异常: {ex.Message}");
                ShowError($"登录失败: {ex.Message}");
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "登录";
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
            TM.App.Log($"[LoginWindow] 显示错误: {message}");
        }

        private void ThirdPartyLogin_Click(object sender, RoutedEventArgs e)
        {
            _ = ThirdPartyLogin_ClickAsync(sender, e);
        }

        private async Task ThirdPartyLogin_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button button || button.Tag is not string platform)
                    return;

                TM.App.Log($"[LoginWindow] 用户点击第三方登录: {platform}");

                var platformType = ParsePlatformType(platform);
                if (!_accountBindingService.IsBound(platformType))
                {
                    var platformName = GetPlatformDisplayName(platform);
                    StandardDialog.ShowInfo($"您尚未绑定{platformName}账号。\n\n请先登录，然后在【账号绑定】中完成绑定后再使用第三方登录。", "提示", this);
                    TM.App.Log($"[LoginWindow] 第三方登录被拒绝: {platform} 未绑定");
                    return;
                }

                WeChatLoginButton.IsEnabled = false;
                QQLoginButton.IsEnabled = false;
                GitHubLoginButton.IsEnabled = false;

                OAuthRequest request;

                if (!OAuthService.IsPlatformConfigured(platform))
                {
                    var platformName = GetPlatformDisplayName(platform);
                    StandardDialog.ShowWarning($"{platformName}登录尚未配置，请联系管理员。", "提示", this);
                    return;
                }

                GlobalToast.Info("OAuth授权", $"正在打开{GetPlatformDisplayName(platform)}授权页面...");
                var authResult = await _oAuthService.StartAuthorizationAsync(platform);

                if (!authResult.Success)
                {
                    ShowError(authResult.ErrorMessage ?? "授权失败");
                    return;
                }

                request = new OAuthRequest
                {
                    Platform = platform,
                    Code = authResult.Code,
                    State = authResult.State
                };

#pragma warning disable CS0618
                var result = await _apiService.OAuthLoginAsync(platform, request);
#pragma warning restore CS0618

                if (result.Success && result.Data != null)
                {
                    var loginResult = new LoginResult
                    {
                        AccessToken = result.Data.AccessToken,
                        RefreshToken = result.Data.RefreshToken,
                        SessionKey = result.Data.SessionKey,
                        ExpiresAt = result.Data.ExpiresAt,
                        User = result.Data.User
                    };
                    _authTokenManager.SaveTokens(loginResult);

                    LoggedInUsername = result.Data.User.Username;
                    LoginSuccess = true;

                    TM.App.Log($"[LoginWindow] 第三方登录成功: {platform} - {LoggedInUsername}");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError(result.Message ?? $"{platform}登录失败");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginWindow] 第三方登录异常: {ex.Message}");
                ShowError($"登录失败: {ex.Message}");
            }
            finally
            {
                WeChatLoginButton.IsEnabled = true;
                QQLoginButton.IsEnabled = true;
                GitHubLoginButton.IsEnabled = true;
            }
        }

        private static PlatformType ParsePlatformType(string platform)
        {
            return platform.ToLower() switch
            {
                "wechat" => PlatformType.WeChat,
                "qq" => PlatformType.QQ,
                "github" => PlatformType.GitHub,
                "google" => PlatformType.Google,
                "microsoft" => PlatformType.Microsoft,
                "baidu" => PlatformType.Baidu,
                _ => PlatformType.WeChat
            };
        }

        private static string GetPlatformDisplayName(string platform)
        {
            return platform.ToLower() switch
            {
                "wechat" => "微信",
                "qq" => "QQ",
                "github" => "GitHub",
                "google" => "Google",
                "microsoft" => "Microsoft",
                "baidu" => "百度",
                _ => platform
            };
        }
    }
}
