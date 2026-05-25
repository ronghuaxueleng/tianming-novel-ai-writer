using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.IntelligentGeneration.GenerationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class GenerationHistoryView : UserControl
    {
        public GenerationHistoryView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<GenerationHistoryViewModel>();
        }
    }
}
