using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class GenerationParamsPanel : UserControl
    {
        public GenerationParamsPanel()
        {
            InitializeComponent();
            DataContext = new GenerationParamsViewModel();
        }
    }
}
