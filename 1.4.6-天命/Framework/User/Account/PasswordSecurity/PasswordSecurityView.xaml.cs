using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.PasswordSecurity
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class PasswordSecurityView : UserControl
    {
        private PasswordSecurityViewModel ViewModel => (PasswordSecurityViewModel)DataContext;

        public PasswordSecurityView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<PasswordSecurityViewModel>();
        }

        private void OldPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.OldPassword = passwordBox.Password;
            }
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.NewPassword = passwordBox.Password;
            }
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.ConfirmPassword = passwordBox.Password;
            }
        }
    }
}
