using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.SystemSettings.Proxy.ProxyChain
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ProxyChainView : UserControl
    {
        public ProxyChainView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProxyChainViewModel>();

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
