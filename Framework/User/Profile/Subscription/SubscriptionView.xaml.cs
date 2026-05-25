using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.User.Profile.Subscription
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SubscriptionView : UserControl
    {
        public SubscriptionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SubscriptionViewModel>();
            Loaded += SubscriptionView_Loaded;
        }

        private void SubscriptionView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SubscriptionViewModel vm)
            {
                _ = vm.RefreshAsync();
            }
        }
    }
}
