using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.SystemMonitor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SystemMonitorView : UserControl
    {
        public SystemMonitorView()
        {
            InitializeComponent();
        }
    }
}

