using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Logging.LogLevel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LogLevelView : UserControl
    {
        public LogLevelView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LogLevelViewModel>();
        }
    }
}
