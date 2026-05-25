using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ImageColorPickerView : UserControl
    {
        public ImageColorPickerView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ImageColorPickerViewModel>();
        }
    }
}
