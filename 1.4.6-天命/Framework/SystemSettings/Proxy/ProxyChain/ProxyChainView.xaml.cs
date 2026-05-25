using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyChain
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProxyChainView : UserControl
    {
        public ProxyChainView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxyChainViewModel>();
        }
    }
}
