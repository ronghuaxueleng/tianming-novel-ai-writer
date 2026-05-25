using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.ThemeManagement.ThemeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ThemeDesignView : UserControl
    {
        public ThemeDesignView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeDesignViewModel>();
        }
    }
}

