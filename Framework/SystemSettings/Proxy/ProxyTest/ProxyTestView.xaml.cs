using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Proxy.ProxyTest
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ProxyTestView : UserControl
    {
        public ProxyTestView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxyTestViewModel>();
        }
    }
}
