using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.AccountDeletion
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AccountDeletionView : UserControl
    {
        public AccountDeletionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AccountDeletionViewModel>();
        }
    }
}
