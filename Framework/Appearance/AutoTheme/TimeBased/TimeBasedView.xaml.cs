using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class TimeBasedView : UserControl
    {
        public TimeBasedView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<TimeBasedViewModel>();
        }
    }
}
