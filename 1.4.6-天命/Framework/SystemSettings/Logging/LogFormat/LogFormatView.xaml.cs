using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Logging.LogFormat
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LogFormatView : UserControl
    {
        public LogFormatView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LogFormatViewModel>();
        }
    }
}
