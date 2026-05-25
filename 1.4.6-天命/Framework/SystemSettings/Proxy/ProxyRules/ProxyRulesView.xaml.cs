using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProxyRulesView : UserControl
    {
        public ProxyRulesView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxyRulesViewModel>();
        }
    }
}
