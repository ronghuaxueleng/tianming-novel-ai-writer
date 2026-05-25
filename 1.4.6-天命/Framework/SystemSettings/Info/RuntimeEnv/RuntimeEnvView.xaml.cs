using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.RuntimeEnv
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class RuntimeEnvView : UserControl
    {
        public RuntimeEnvView()
        {
            InitializeComponent();
        }
    }
}
