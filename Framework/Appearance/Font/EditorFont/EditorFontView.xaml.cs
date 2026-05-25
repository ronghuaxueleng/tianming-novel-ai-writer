using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.Font.EditorFont
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class EditorFontView : UserControl
    {
        public EditorFontView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<EditorFontViewModel>();
        }
    }
}

