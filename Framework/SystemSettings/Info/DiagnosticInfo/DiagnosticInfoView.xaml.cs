using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.DiagnosticInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DiagnosticInfoView : UserControl
    {
        public DiagnosticInfoView()
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
