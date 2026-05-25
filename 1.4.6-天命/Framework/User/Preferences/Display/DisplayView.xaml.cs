using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Preferences.Display
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DisplayView : UserControl
    {
        public DisplayView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<DisplayViewModel>();
        }
    }
}
