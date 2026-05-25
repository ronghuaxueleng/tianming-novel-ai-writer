using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class EditorTabBar : UserControl
    {
        private EditorTabManager? _tabManager;

        public EditorTabBar()
        {
            InitializeComponent();
        }

        public void BindTabManager(EditorTabManager manager)
        {
            _tabManager = manager;
            DataContext = manager;
        }

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is EditorTab tab)
            {
                _tabManager?.ActivateTab(tab);
            }
        }

        private void OnCloseTab(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EditorTab tab)
            {
                _tabManager?.CloseTab(tab);
            }
            e.Handled = true;
        }

        private void OnCloseAll(object sender, RoutedEventArgs e)
        {
            _tabManager?.CloseAllTabs();
        }
    }
}
