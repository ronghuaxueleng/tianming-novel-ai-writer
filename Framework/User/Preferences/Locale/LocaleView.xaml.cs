using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Preferences.Locale
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class LocaleView : UserControl
    {
        public LocaleView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LocaleViewModel>();
        }
    }
}
