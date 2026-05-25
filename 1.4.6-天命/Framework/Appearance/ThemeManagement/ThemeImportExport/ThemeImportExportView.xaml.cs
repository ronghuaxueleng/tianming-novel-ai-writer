using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.ThemeManagement.ThemeImportExport
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ThemeImportExportView : UserControl
    {
        public ThemeImportExportView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ThemeImportExportViewModel>();
        }
    }
}
