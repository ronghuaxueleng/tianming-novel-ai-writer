using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class BindingDialog : Window
    {
        public string AccountId => AccountIdTextBox.Text;
        public string Nickname => NicknameTextBox.Text;

        public BindingDialog(string platformName)
        {
            InitializeComponent();
            PlatformNameText.Text = $"请输入{platformName}账号信息";
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(Nickname))
            {
                GlobalToast.Warning("绑定账号", "请输入完整的账号信息");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

