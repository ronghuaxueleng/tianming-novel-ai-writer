using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SystemInfoView : UserControl
    {
        public SystemInfoView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SystemInfoViewModel>();
        }
    }
}
