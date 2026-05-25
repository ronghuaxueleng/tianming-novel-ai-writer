using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.LoginHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LoginHistoryView : UserControl
    {
        public LoginHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LoginHistoryViewModel>();
        }
    }
}
