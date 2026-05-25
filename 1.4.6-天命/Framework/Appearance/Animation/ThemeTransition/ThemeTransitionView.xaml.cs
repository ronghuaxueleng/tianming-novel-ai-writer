using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ThemeTransitionView : UserControl
    {
        public ThemeTransitionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeTransitionViewModel>();
        }
    }
}
