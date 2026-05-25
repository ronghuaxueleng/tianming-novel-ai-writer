using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ShortStoryBlueprintView : UserControl
    {
        public ShortStoryBlueprintView(ShortStoryBlueprintViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                DataContext = viewModel;
                IsVisibleChanged += (_, e) =>
                {
                    if ((bool)e.NewValue)
                        viewModel.RefreshBookOptions();
                };
            }
            catch (System.Exception ex)
            {
                TM.App.Log($"[ShortStoryBlueprintView] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}
