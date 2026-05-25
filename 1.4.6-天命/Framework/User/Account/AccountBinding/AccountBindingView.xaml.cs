using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AccountBindingView : UserControl
    {
        public AccountBindingView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AccountBindingViewModel>();
        }
    }
}
