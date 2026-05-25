using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Services.Modules.ProjectData.Models.Generate.Content;

namespace TM.Modules.Generate.Content
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ContentView : UserControl
    {
        public ContentView(ContentViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnStatusLabelClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ModuleCardInfo card)
            {
                if (DataContext is ContentViewModel vm)
                {
                    vm.ToggleModuleEnabledCommand.Execute(card);
                }
            }
            e.Handled = true;
        }
    }
}
