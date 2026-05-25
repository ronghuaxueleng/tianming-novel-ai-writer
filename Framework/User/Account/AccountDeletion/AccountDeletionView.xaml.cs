using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Account.AccountDeletion
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class AccountDeletionView : UserControl
    {
        public AccountDeletionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AccountDeletionViewModel>();
        }
    }
}
