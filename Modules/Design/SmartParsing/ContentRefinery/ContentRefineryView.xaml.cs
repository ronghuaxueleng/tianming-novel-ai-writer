using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.SmartParsing.ContentRefinery
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ContentRefineryView : UserControl
    {
        public ContentRefineryView(ContentRefineryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
