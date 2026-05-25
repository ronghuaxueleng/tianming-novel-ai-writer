using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TimeBasedView : UserControl
    {
        public TimeBasedView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<TimeBasedViewModel>();
        }
    }
}
