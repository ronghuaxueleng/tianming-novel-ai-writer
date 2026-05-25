using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.IntelligentGeneration.AIColorScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AIColorSchemeView : UserControl
    {
        public AIColorSchemeView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<AIColorSchemeViewModel>();
        }
    }
}
