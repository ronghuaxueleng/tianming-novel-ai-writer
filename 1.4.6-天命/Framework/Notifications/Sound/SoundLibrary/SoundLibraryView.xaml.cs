using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Notifications.Sound.SoundLibrary
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SoundLibraryView : UserControl
    {
        public SoundLibraryView()
        {
            InitializeComponent();
            DataContext = new SoundLibraryViewModel();
        }
    }
}
