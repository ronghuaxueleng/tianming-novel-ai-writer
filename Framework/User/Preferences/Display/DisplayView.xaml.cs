using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Preferences.Display
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DisplayView : UserControl
    {
        public DisplayView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<DisplayViewModel>();
        }
    }
}
