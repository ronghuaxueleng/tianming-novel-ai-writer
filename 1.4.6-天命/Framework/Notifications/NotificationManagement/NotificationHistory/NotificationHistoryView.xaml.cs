using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.NotificationManagement.NotificationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class NotificationHistoryView : UserControl
    {
        public NotificationHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<NotificationHistoryViewModel>();
        }
    }
}
