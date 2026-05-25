using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyTest
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ProxyTestView : UserControl
    {
        public ProxyTestView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxyTestViewModel>();
        }
    }
}
