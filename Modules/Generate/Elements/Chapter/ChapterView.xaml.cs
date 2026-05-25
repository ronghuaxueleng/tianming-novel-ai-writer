using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Generate.Elements.Chapter
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ChapterView : UserControl
    {
        public ChapterView(ChapterViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
