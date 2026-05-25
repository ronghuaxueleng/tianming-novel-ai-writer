using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Elements.PlotRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class PlotRulesView : UserControl
    {
        public PlotRulesView(PlotRulesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
