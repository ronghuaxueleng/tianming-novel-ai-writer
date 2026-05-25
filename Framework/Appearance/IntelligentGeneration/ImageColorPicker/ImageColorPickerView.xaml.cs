using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ImageColorPickerView : UserControl
    {
        public ImageColorPickerView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ImageColorPickerViewModel>();
        }
    }
}
