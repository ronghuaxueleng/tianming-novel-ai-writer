using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SoundSchemeView : UserControl
    {
        public SoundSchemeView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SoundSchemeViewModel>();
        }
    }
}

