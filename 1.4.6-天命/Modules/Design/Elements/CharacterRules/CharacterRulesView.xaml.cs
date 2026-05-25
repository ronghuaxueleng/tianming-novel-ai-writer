using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Elements.CharacterRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CharacterRulesView : UserControl
    {
        public CharacterRulesView(CharacterRulesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
