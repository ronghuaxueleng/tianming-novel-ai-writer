using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class VolumeDesignView : UserControl
    {
        public VolumeDesignView(VolumeDesignViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
