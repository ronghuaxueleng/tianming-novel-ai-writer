using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Generate.Elements.Chapter
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ChapterView : UserControl
    {
        public ChapterView(ChapterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
