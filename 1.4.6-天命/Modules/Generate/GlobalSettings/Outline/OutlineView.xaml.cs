using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Generate.GlobalSettings.Outline
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class OutlineView : UserControl
    {
        public OutlineView(OutlineViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
