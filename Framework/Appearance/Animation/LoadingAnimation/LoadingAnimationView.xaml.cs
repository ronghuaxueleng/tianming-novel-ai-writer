using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LoadingAnimationView : UserControl
    {
        public LoadingAnimationView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LoadingAnimationViewModel>();
        }
    }
}
