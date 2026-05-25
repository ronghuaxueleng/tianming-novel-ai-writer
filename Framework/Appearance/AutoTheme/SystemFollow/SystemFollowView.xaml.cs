using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SystemFollowView : UserControl
    {
        public SystemFollowView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SystemFollowViewModel>();
        }
    }
}
