using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.ThemeManagement.ThemeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThemeDesignView : UserControl
    {
        public ThemeDesignView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeDesignViewModel>();
        }
    }
}

