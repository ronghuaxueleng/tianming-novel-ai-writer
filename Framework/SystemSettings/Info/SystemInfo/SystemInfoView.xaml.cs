using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SystemInfoView : UserControl
    {
        public SystemInfoView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SystemInfoViewModel>();
        }
    }
}
