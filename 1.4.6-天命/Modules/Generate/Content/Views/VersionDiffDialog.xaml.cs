using System.Reflection;
using System.Windows;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Modules.Generate.Content.Views
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class VersionDiffDialog : Window
    {
        public VersionDiffDialog(PackageVersionDiff diff)
        {
            InitializeComponent();

            VersionCompareText.Text = $"当前版本 {diff.CurrentVersion} vs 历史版本 {diff.HistoryVersion}";
            DiffItemsControl.ItemsSource = diff.DiffItems;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
