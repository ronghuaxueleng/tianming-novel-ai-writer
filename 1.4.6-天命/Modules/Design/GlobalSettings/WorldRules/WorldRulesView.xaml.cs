using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.GlobalSettings.WorldRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class WorldRulesView : UserControl
    {
        public WorldRulesView(WorldRulesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
