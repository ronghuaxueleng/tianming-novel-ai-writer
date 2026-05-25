using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProjectSelector : UserControl
    {
        public ProjectSelector()
        {
            InitializeComponent();
        }

        private void OnProjectButtonClick(object sender, RoutedEventArgs e)
        {
            ProjectPopup.IsOpen = !ProjectPopup.IsOpen;
        }
    }
}
