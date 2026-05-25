using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Elements.FactionRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class FactionRulesView : UserControl
    {
        public FactionRulesView(FactionRulesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
