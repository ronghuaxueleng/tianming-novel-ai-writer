using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.IntelligentGeneration.GenerationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class GenerationHistoryView : UserControl
    {
        public GenerationHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<GenerationHistoryViewModel>();
        }
    }
}
