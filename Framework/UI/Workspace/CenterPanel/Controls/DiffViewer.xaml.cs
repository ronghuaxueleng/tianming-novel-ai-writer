using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DiffViewer : UserControl
    {
        public DiffViewer()
        {
            InitializeComponent();
        }
    }
}
