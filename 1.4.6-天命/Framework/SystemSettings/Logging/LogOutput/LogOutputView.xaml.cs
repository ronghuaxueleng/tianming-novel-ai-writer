using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Logging.LogOutput
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LogOutputView : UserControl
    {
        public LogOutputView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LogOutputViewModel>();
        }
    }
}
