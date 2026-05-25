using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.DiagnosticInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DiagnosticInfoView : UserControl
    {
        public DiagnosticInfoView()
        {
            InitializeComponent();
        }
    }
}
