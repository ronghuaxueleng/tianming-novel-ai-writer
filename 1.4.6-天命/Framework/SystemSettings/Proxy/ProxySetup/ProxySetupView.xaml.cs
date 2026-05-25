using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxySetup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProxySetupView : UserControl
    {
        private ProxySetupViewModel? ViewModel => DataContext as ProxySetupViewModel;

        public ProxySetupView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxySetupViewModel>();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && ViewModel != null)
            {
                ViewModel.Password = passwordBox.Password;
            }
        }
    }
}
