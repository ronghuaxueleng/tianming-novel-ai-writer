using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThemeTransitionView : UserControl
    {
        public ThemeTransitionView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeTransitionViewModel>();

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
