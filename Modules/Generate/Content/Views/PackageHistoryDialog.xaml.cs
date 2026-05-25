using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Modules.Generate.Content.Views
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class PackageHistoryDialog : Window
    {
        private readonly IPackageHistoryService _historyService;
        private List<PackageHistoryEntry> _historyEntries = new();

        public PackageHistoryDialog()
        {
            InitializeComponent();
            _historyService = ServiceLocator.Get<IPackageHistoryService>();

            RetainCountComboBox.SelectedIndex = _historyService.RetainCount - 1;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Loaded -= OnLoaded;
                await LoadHistoryAsync();
            }
            catch (Exception ex) { TM.App.Log($"[PackageHistory] 加载历史失败: {ex.Message}"); }
        }

        private async Task LoadHistoryAsync()
        {
            _historyEntries = await _historyService.GetAllHistoryAsync().ConfigureAwait(true);
            HistoryListBox.ItemsSource = _historyEntries;
        }

        private void RetainCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_historyService != null)
            {
                _historyService.RetainCount = RetainCountComboBox.SelectedIndex + 1;
            }
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private async void ViewDiff_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is int version)
                {
                    var diff = await _historyService.GetVersionDiffAsync(version);

                    if (diff.DiffItems.Count == 0)
                    {
                        GlobalToast.Info("无差异", "当前版本与历史版本没有差异");
                        return;
                    }

                    var diffDialog = new VersionDiffDialog(diff);
                    diffDialog.Owner = this;
                    diffDialog.ShowDialog();
                }
            }
            catch (Exception ex) { TM.App.Log($"[PackageHistory] 查看差异失败: {ex.Message}"); }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            _ = Restore_ClickAsync(sender, e);
        }

        private async Task Restore_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is int version)
                {
                    if (!StandardDialog.ShowConfirm($"确定要恢复到版本 {version} 吗？\n当前版本将被保存到历史。", "确认恢复"))
                        return;

                    var success = await _historyService.RestoreVersionAsync(version);

                    if (success)
                    {
                        GlobalToast.Success("恢复成功", $"已恢复到版本 {version}");
                        await LoadHistoryAsync();
                    }
                    else
                    {
                        GlobalToast.Error("恢复失败", "无法恢复到指定版本");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryDialog] 恢复版本失败: {ex.Message}");
                GlobalToast.Error("恢复失败", $"恢复失败：{ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
