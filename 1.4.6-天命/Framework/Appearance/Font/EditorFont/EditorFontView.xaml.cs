using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Font.EditorFont
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class EditorFontView : UserControl
    {
        public EditorFontView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<EditorFontViewModel>();
        }
    }
}

