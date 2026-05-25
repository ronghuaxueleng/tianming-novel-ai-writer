using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DoNotDisturbView : UserControl
    {
        public DoNotDisturbView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<DoNotDisturbViewModel>();
            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
