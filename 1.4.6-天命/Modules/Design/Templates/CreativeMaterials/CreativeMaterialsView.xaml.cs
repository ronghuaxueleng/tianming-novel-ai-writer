using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CreativeMaterialsView : UserControl
    {
        public CreativeMaterialsView(CreativeMaterialsViewModel viewModel)
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
                TM.App.Log($"[CreativeMaterialsView] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}
