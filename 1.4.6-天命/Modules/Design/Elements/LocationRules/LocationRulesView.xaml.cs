using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Elements.LocationRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LocationRulesView : UserControl
    {
        public LocationRulesView(LocationRulesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
