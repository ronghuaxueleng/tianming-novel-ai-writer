using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Account.LoginHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LoginHistoryView : UserControl
    {
        public LoginHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LoginHistoryViewModel>();
        }
    }
}
