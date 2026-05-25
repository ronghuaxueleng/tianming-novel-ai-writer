using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DoNotDisturbView : UserControl
    {
        public DoNotDisturbView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<DoNotDisturbViewModel>();
        }
    }
}
