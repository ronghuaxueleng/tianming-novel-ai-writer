using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.Sound.VoiceBroadcast
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class VoiceBroadcastView : UserControl
    {
        public VoiceBroadcastView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<VoiceBroadcastViewModel>();
        }
    }
}

