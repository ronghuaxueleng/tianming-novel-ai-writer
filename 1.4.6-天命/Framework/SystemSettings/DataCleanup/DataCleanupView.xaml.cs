using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.DataCleanup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DataCleanupView : UserControl
    {
        public DataCleanupView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<DataCleanupViewModel>();
        }
    }
}
