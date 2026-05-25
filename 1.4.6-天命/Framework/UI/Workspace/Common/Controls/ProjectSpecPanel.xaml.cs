using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProjectSpecPanel : UserControl
    {
        public ProjectSpecPanel()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProjectSpecPanelViewModel>();
        }
    }
}
