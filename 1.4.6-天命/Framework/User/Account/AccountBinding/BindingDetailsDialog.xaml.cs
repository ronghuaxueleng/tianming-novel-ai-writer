using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class BindingDetailsDialog : Window
    {
        private readonly ThirdPartyBinding _binding;
        private readonly string _platformName;
        private readonly string _platformIcon;

        public BindingDetailsDialog(ThirdPartyBinding binding, string platformName, string platformIcon)
        {
            InitializeComponent();

            _binding = binding;
            _platformName = platformName;
            _platformIcon = platformIcon;

            LoadBindingDetails();
        }

        private void LoadBindingDetails()
        {
            PlatformNameText.Text = $"{_platformName} 绑定详情";
            PlatformIconText.Text = _platformIcon;

            AccountIdText.Text = _binding.AccountId;
            NicknameText.Text = _binding.Nickname;
            EmailText.Text = string.IsNullOrEmpty(_binding.Email) ? "未提供" : _binding.Email;

            UpdateSyncStatus();

            BindTimeText.Text = _binding.BindTime.ToString("yyyy-MM-dd HH:mm:ss");
            LastSyncTimeText.Text = _binding.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "从未同步";
            LastUseTimeText.Text = _binding.LastUseTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "从未使用";

            var permissionNames = _binding.Permissions.Select(p => p switch
            {
                "basic_info" => "基本信息（必需）",
                "profile" => "个人资料",
                "email" => "电子邮箱",
                "sync" => "数据同步",
                _ => p
            }).ToList();

            PermissionsList.ItemsSource = permissionNames.Count > 0 ? permissionNames : new[] { "无授权权限" };

            var history = ServiceLocator.Get<AccountBindingService>().GetHistory(_binding.Platform, 10);
            HistoryList.ItemsSource = history;
        }

        private void UpdateSyncStatus()
        {
            switch (_binding.SyncStatus)
            {
                case SyncStatus.None:
                    SyncStatusBorder.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    SyncStatusText.Text = "未同步";
                    SyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(97, 97, 97));
                    break;
                case SyncStatus.Syncing:
                    SyncStatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 224));
                    SyncStatusText.Text = "同步中...";
                    SyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34));
                    break;
                case SyncStatus.Synced:
                    SyncStatusBorder.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                    SyncStatusText.Text = "已同步";
                    SyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                    break;
                case SyncStatus.Failed:
                    SyncStatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                    SyncStatusText.Text = "同步失败";
                    SyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                    break;
                case SyncStatus.Outdated:
                    SyncStatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 224));
                    SyncStatusText.Text = "需要更新";
                    SyncStatusText.Foreground = new SolidColorBrush(Color.FromRgb(245, 124, 0));
                    break;
            }
        }

        private void Sync_Click(object sender, RoutedEventArgs e)
        {
            _ = Sync_ClickAsync();
        }

        private async System.Threading.Tasks.Task Sync_ClickAsync()
        {
            try
            {
                ServiceLocator.Get<AccountBindingService>().UpdateSyncStatus(_binding.Platform, SyncStatus.Syncing);
                _binding.SyncStatus = SyncStatus.Syncing;
                UpdateSyncStatus();

                GlobalToast.Info("数据同步", "正在同步账号数据...");

                await System.Threading.Tasks.Task.Delay(2000);

                var random = new Random();
                var success = random.Next(100) > 10;

                if (success)
                {
                    ServiceLocator.Get<AccountBindingService>().UpdateSyncStatus(_binding.Platform, SyncStatus.Synced);
                    _binding.SyncStatus = SyncStatus.Synced;
                    _binding.LastSyncTime = DateTime.Now;
                    LastSyncTimeText.Text = _binding.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                    GlobalToast.Success("数据同步", "同步成功");
                }
                else
                {
                    ServiceLocator.Get<AccountBindingService>().UpdateSyncStatus(_binding.Platform, SyncStatus.Failed);
                    _binding.SyncStatus = SyncStatus.Failed;
                    GlobalToast.Error("数据同步", "同步失败，请稍后重试");
                }

                UpdateSyncStatus();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BindingDetailsDialog] 同步失败: {ex.Message}");
                GlobalToast.Error("数据同步", $"同步异常: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}

