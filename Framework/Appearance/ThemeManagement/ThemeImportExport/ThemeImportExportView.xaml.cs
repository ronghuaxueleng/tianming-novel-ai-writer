using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.ThemeManagement.ThemeImportExport
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThemeImportExportView : UserControl
    {
        public ThemeImportExportView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeImportExportViewModel>();
        }
    }
}
