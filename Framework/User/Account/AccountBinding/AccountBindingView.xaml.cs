using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class AccountBindingView : UserControl
    {
        public AccountBindingView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AccountBindingViewModel>();
        }
    }
}
