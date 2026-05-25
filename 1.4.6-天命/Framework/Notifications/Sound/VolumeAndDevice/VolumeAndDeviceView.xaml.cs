using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class VolumeAndDeviceView : UserControl
    {
        public VolumeAndDeviceView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<VolumeAndDeviceViewModel>();
        }
    }
}

