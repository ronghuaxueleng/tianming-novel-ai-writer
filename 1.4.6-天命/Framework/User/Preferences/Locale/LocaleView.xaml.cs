using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Preferences.Locale
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LocaleView : UserControl
    {
        public LocaleView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<LocaleViewModel>();
        }
    }
}
