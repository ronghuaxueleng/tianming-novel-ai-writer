using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class OneClickGenerateView : UserControl
    {
        public OneClickGenerateView()
        {
            InitializeComponent();
            DataContext = new OneClickGenerateViewModel();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IsVisibleChanged += OnIsVisibleChanged;
            RefreshCategories();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                RefreshCategories();
        }

        private void RefreshCategories()
        {
            if (DataContext is OneClickGenerateViewModel vm && vm.LoadCategoriesCommand.CanExecute(null))
                vm.LoadCategoriesCommand.Execute(null);
        }
    }
}
