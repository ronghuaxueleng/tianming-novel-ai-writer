using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Font.UIFont
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class UIFontView : UserControl
    {
        public UIFontView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<UIFontViewModel>();
        }
    }
}

