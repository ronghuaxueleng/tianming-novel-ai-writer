using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Common.Controls.Menus
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ChapterManagementContextMenu : ContextMenu
    {
        public ChapterManagementContextMenu()
        {
            InitializeComponent();
        }
    }
}
