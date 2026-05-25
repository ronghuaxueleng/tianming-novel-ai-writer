using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TM.Framework.Notifications.SystemNotifications.NotificationTypes
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class NotificationTypesView : UserControl
    {
        private readonly NotificationTypesViewModel _viewModel;
        private NotificationTypeData? _selectedType;

        public NotificationTypesView()
        {
            InitializeComponent();
            _viewModel = ServiceLocator.Get<NotificationTypesViewModel>();
            DataContext = _viewModel;

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };

            App.Log("[NotificationTypesView] 视图初始化完成");
        }

        private void OnTypeCardClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string typeId)
            {
                foreach (var type in _viewModel.Types)
                {
                    type.IsSelected = false;
                }

                var selectedType = _viewModel.Types.FirstOrDefault(t => t.Id == typeId);
                if (selectedType != null)
                {
                    selectedType.IsSelected = true;
                    _selectedType = selectedType;
                    App.Log($"[NotificationTypesView] 已选中类型: {selectedType.Name}");
                }
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.SaveSettingsAsync();
                GlobalToast.Success("保存成功", "通知类型配置已成功保存");
                App.Log("[NotificationTypesView] 配置已保存");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesView] 保存配置失败: {ex.Message}");
                StandardDialog.ShowError($"保存配置失败：{ex.Message}", "保存失败", Window.GetWindow(this));
            }
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.ResetToDefaults();
                _selectedType = null;

                GlobalToast.Success("重置成功", "已恢复为默认配置");
                App.Log("[NotificationTypesView] 已重置为默认配置");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesView] 重置配置失败: {ex.Message}");
                StandardDialog.ShowError($"重置配置失败：{ex.Message}", "重置失败", Window.GetWindow(this));
            }
        }

    }
}

