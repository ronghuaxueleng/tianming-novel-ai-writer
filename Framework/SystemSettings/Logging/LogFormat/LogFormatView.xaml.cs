using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Logging.LogFormat
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LogFormatView : UserControl
    {
        public LogFormatView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LogFormatViewModel>();
        }
    }
}
