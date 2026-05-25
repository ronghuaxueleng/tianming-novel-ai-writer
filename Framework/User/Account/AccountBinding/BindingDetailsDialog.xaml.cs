using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class BindingDetailsDialog : Window
    {
        private static readonly SolidColorBrush _noneBg = Freeze(Color.FromRgb(224, 224, 224));
        private static readonly SolidColorBrush _noneFg = Freeze(Color.FromRgb(97, 97, 97));
        private static readonly SolidColorBrush _syncingBg = Freeze(Color.FromRgb(255, 243, 224));
        private static readonly SolidColorBrush _syncingFg = Freeze(Color.FromRgb(230, 126, 34));
        private static readonly SolidColorBrush _syncedBg = Freeze(Color.FromRgb(232, 245, 233));
        private static readonly SolidColorBrush _syncedFg = Freeze(Color.FromRgb(46, 125, 50));
        private static readonly SolidColorBrush _failedBg = Freeze(Color.FromRgb(255, 235, 238));
        private static readonly SolidColorBrush _failedFg = Freeze(Color.FromRgb(211, 47, 47));
        private static readonly SolidColorBrush _outdatedBg = Freeze(Color.FromRgb(255, 243, 224));
        private static readonly SolidColorBrush _outdatedFg = Freeze(Color.FromRgb(245, 124, 0));
        private static SolidColorBrush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private readonly ThirdPartyBinding _binding;
        private readonly string _platformName;
        private readonly ImageSource? _platformIcon;

        public BindingDetailsDialog(ThirdPartyBinding binding, string platformName, ImageSource? platformIcon)
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
            PlatformIconImage.Source = _platformIcon;

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
                    SyncStatusBorder.Background = _noneBg;
                    SyncStatusText.Text = "未同步";
                    SyncStatusText.Foreground = _noneFg;
                    break;
                case SyncStatus.Syncing:
                    SyncStatusBorder.Background = _syncingBg;
                    SyncStatusText.Text = "同步中...";
                    SyncStatusText.Foreground = _syncingFg;
                    break;
                case SyncStatus.Synced:
                    SyncStatusBorder.Background = _syncedBg;
                    SyncStatusText.Text = "已同步";
                    SyncStatusText.Foreground = _syncedFg;
                    break;
                case SyncStatus.Failed:
                    SyncStatusBorder.Background = _failedBg;
                    SyncStatusText.Text = "同步失败";
                    SyncStatusText.Foreground = _failedFg;
                    break;
                case SyncStatus.Outdated:
                    SyncStatusBorder.Background = _outdatedBg;
                    SyncStatusText.Text = "需要更新";
                    SyncStatusText.Foreground = _outdatedFg;
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
                GlobalToast.Error("数据同步", $"同步失败：{ex.Message}");
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

