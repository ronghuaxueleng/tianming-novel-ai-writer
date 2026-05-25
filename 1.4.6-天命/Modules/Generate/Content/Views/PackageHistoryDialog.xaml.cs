using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Modules.Generate.Content.Views
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class PackageHistoryDialog : Window
    {
        private readonly IPackageHistoryService _historyService;
        private List<PackageHistoryEntry> _historyEntries = new();

        public PackageHistoryDialog()
        {
            InitializeComponent();
            _historyService = ServiceLocator.Get<IPackageHistoryService>();

            RetainCountComboBox.SelectedIndex = _historyService.RetainCount - 1;

            LoadHistory();
        }

        private void LoadHistory()
        {
            _historyEntries = _historyService.GetAllHistory();
            HistoryListBox.ItemsSource = _historyEntries;

            if (_historyEntries.Count == 0)
            {
                GlobalToast.Info("暂无历史", "还没有打包历史记录");
            }
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

        private void ViewDiff_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int version)
            {
                var diff = _historyService.GetVersionDiff(version);

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
                        LoadHistory();
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
                GlobalToast.Error("恢复失败", ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
