using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.SystemNotifications.NotificationStyle
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class NotificationStyleView : UserControl
    {
        private readonly NotificationStyleViewModel _viewModel;

        public NotificationStyleView()
        {
            InitializeComponent();
            _viewModel = ServiceLocator.Get<NotificationStyleViewModel>();
            DataContext = _viewModel;

            AttachPreviewUpdates();
            UpdatePreview();

            App.Log("[NotificationStyleView] 视图初始化完成");
        }

        private void OnApplyMinimalStyleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.ApplyPreset("fancy");
                GlobalToast.Success("预设已应用", "已应用「华丽风格」样式预设");
                App.Log("[NotificationStyleView] 应用预设: fancy");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationStyleView] 应用华丽预设失败: {ex.Message}");
            }
        }

        private void AttachPreviewUpdates()
        {
            _viewModel.PropertyChanged += (s, e) => UpdatePreview();
        }

        private void UpdatePreview()
        {
            PreviewCard.CornerRadius = new CornerRadius(_viewModel.CornerRadius);
            PreviewCard.BorderThickness = new Thickness(_viewModel.BorderThickness);
            PreviewCard.Opacity = _viewModel.BackgroundOpacity / 100.0;
            PreviewCard.Width = _viewModel.NotificationWidth;
            PreviewCard.Height = _viewModel.NotificationHeight;

            if (PreviewShadow != null)
            {
                PreviewShadow.BlurRadius = _viewModel.ShadowIntensity;
            }
        }

        private void OnApplyPreset(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string presetName)
            {
                _viewModel.ApplyPreset(presetName);
                GlobalToast.Success("预设已应用", $"已应用「{presetName}」样式预设");
                App.Log($"[NotificationStyleView] 应用预设: {presetName}");
            }
        }

        private void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            OnTestNotificationClick(sender, e);
        }

        private void OnTestNotificationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ToastNotification.ShowInfo("测试通知", "这是通知样式的测试效果！");

                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    Application.Current?.Dispatcher.BeginInvoke(() => 
                        ToastNotification.ShowSuccess("操作成功", "配置已应用"));

                    await System.Threading.Tasks.Task.Delay(500);
                    Application.Current?.Dispatcher.BeginInvoke(() => 
                        ToastNotification.ShowWarning("注意事项", "请注意查看配置参数"));

                    await System.Threading.Tasks.Task.Delay(500);
                    Application.Current?.Dispatcher.BeginInvoke(() => 
                        ToastNotification.ShowError("发生错误", "这是错误提示示例"));
                });

                App.Log("[NotificationStyle] 测试通知效果");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationStyle] 测试失败: {ex.Message}");
            }
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.ResetToDefaults();
                GlobalToast.Success("重置成功", "已恢复为默认样式");
                App.Log("[NotificationStyleView] 已重置为默认设置");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationStyleView] 重置失败: {ex.Message}");
                StandardDialog.ShowError($"重置失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveSettings();
                GlobalToast.Success("保存成功", "通知样式设置已成功保存");
                App.Log("[NotificationStyleView] 设置已保存");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationStyleView] 保存失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }
    }
}

