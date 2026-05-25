using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.NotificationManagement.NotificationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class NotificationHistoryView : UserControl
    {
        public NotificationHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<NotificationHistoryViewModel>();
        }
    }
}
