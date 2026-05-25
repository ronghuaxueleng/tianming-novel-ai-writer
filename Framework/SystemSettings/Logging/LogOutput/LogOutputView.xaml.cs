using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Logging.LogOutput
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LogOutputView : UserControl
    {
        public LogOutputView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LogOutputViewModel>();
        }
    }
}
