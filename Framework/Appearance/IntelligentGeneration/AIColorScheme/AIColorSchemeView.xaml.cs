using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.IntelligentGeneration.AIColorScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class AIColorSchemeView : UserControl
    {
        public AIColorSchemeView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AIColorSchemeViewModel>();
        }
    }
}
