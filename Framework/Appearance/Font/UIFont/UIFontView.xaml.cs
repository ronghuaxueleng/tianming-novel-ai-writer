using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.Font.UIFont
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class UIFontView : UserControl
    {
        public UIFontView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<UIFontViewModel>();
        }
    }
}

