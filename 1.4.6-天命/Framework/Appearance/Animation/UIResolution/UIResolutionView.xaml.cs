using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Animation.UIResolution
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class UIResolutionView : UserControl
    {
        public UIResolutionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<UIResolutionViewModel>();
        }
    }
}

