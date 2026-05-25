using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SoundSchemeView : UserControl
    {
        public SoundSchemeView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<SoundSchemeViewModel>();
        }
    }
}

