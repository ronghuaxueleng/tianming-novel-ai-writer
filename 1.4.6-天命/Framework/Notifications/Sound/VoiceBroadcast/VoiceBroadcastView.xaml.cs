using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.Sound.VoiceBroadcast
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class VoiceBroadcastView : UserControl
    {
        public VoiceBroadcastView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<VoiceBroadcastViewModel>();
        }
    }
}

