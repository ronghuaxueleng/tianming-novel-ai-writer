using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Logging.LogRotation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LogRotationView : UserControl
    {
        public LogRotationView()
        {
            InitializeComponent();
        }
    }
}
