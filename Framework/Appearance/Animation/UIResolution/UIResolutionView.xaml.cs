using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.Animation.UIResolution
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class UIResolutionView : UserControl
    {
        public UIResolutionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<UIResolutionViewModel>();
        }
    }
}

