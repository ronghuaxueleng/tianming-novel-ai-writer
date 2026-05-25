using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.AppInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AppInfoView : UserControl
    {
        public AppInfoView()
        {
            InitializeComponent();
        }
    }
}
