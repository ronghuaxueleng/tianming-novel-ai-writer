using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Services;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.Login
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AccountRenewDialog : Window
    {
        private readonly SubscriptionService _subscriptionService;

        public AccountRenewDialog()
        {
            InitializeComponent();
            _subscriptionService = ServiceLocator.Get<SubscriptionService>();
            AccountTextBox.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RenewButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RenewButton_ClickAsync();
        }

        private async Task RenewButton_ClickAsync()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;

            var account = (AccountTextBox.Text ?? string.Empty).Trim();
            var cardKey = (CardKeyTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(account))
            {
                ShowError("请输入账号");
                AccountTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(cardKey))
            {
                ShowError("请输入卡密");
                CardKeyTextBox.Focus();
                return;
            }

            RenewButton.IsEnabled = false;
            RenewButton.Content = "续费中...";

            try
            {
                var result = await _subscriptionService.RenewAccountAsync(account, cardKey);

                if (result.Success)
                {
                    StandardDialog.ShowInfo("续费成功", result.Message ?? $"已为账号 {account} 增加会员时长", this);
                    TM.App.Log($"[AccountRenewDialog] 续费成功: {account}");
                    Close();
                }
                else
                {
                    ShowError(result.ErrorMessage ?? "续费失败");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountRenewDialog] 续费异常: {ex.Message}");
                ShowError($"续费失败: {ex.Message}");
            }
            finally
            {
                RenewButton.IsEnabled = true;
                RenewButton.Content = "续费";
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}
