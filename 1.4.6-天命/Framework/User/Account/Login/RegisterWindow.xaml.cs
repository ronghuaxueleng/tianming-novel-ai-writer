using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.Login
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class RegisterWindow : Window
    {
        public string? RegisteredUsername { get; private set; }
        public string? RegisteredPassword { get; private set; }

        private readonly LoginService _loginService;

        public RegisterWindow(string? defaultUsername = null)
        {
            InitializeComponent();
            _loginService = ServiceLocator.Get<LoginService>();

            Loaded += (_, _) =>
            {
                UsernameTextBox.Text = defaultUsername ?? string.Empty;
                UsernameTextBox.Focus();
                UsernameTextBox.SelectAll();
            };
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
                    TM.App.Log($"[RegisterWindow] DragMove失败: {ex.Message}");
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RegisterButton_ClickAsync();
        }

        private async Task RegisterButton_ClickAsync()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;

            var username = (UsernameTextBox.Text ?? string.Empty).Trim();
            var password = PasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;
            var inviteCode = (InviteCodeTextBox.Text ?? string.Empty).Trim();
            var licenseKey = (LicenseKeyTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("请输入账号");
                UsernameTextBox.Focus();
                return;
            }

            if (username.Length < 3)
            {
                ShowError("账号至少3个字符");
                UsernameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("请输入密码");
                PasswordBox.Focus();
                return;
            }

            if (password.Length < 6)
            {
                ShowError("密码至少6位");
                PasswordBox.Focus();
                return;
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ShowError("两次输入的密码不一致");
                ConfirmPasswordBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                ShowError("请输入卡密");
                LicenseKeyTextBox.Focus();
                return;
            }

            RegisterButton.IsEnabled = false;
            RegisterButton.Content = "注册中...";

            try
            {
                var result = await _loginService.CreateAccountAsync(
                    username, 
                    password,
                    string.IsNullOrWhiteSpace(licenseKey) ? null : licenseKey);

                if (!result.Success)
                {
                    ShowError(result.ErrorMessage ?? "注册失败");
                    return;
                }

                RegisteredUsername = username;
                RegisteredPassword = password;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RegisterWindow] 注册异常: {ex.Message}");
                ShowError($"注册失败: {ex.Message}");
            }
            finally
            {
                RegisterButton.IsEnabled = true;
                RegisterButton.Content = "注册";
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}
