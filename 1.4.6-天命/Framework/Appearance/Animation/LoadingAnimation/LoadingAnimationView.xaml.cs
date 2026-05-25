using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LoadingAnimationView : UserControl
    {
        public LoadingAnimationView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LoadingAnimationViewModel>();
        }
    }
}
