using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Logging.LogRotation
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LogRotationView : UserControl
    {
        public LogRotationView()
        {
            InitializeComponent();

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
