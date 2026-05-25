using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SystemFollowView : UserControl
    {
        public SystemFollowView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SystemFollowViewModel>();
        }
    }
}
