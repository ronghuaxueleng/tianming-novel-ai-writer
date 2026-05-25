using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class VolumeAndDeviceView : UserControl
    {
        public VolumeAndDeviceView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<VolumeAndDeviceViewModel>();
            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
