using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Services;

namespace TM.Framework.Notifications.SystemNotifications.NotificationTypes
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class NotificationTypesView : UserControl
    {
        private readonly NotificationTypesViewModel _viewModel;
        private NotificationTypeData? _selectedType;

        public NotificationTypesView()
        {
            InitializeComponent();
            _viewModel = ServiceLocator.Get<NotificationTypesViewModel>();
            DataContext = _viewModel;

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

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveSettings();
                GlobalToast.Success("保存成功", "通知类型配置已成功保存");
                App.Log("[NotificationTypesView] 配置已保存");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesView] 保存配置失败: {ex.Message}");
                StandardDialog.ShowError($"保存配置失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
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
                StandardDialog.ShowError($"重置配置失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void OnAddTypeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var typeName = StandardDialog.ShowInput(
                    "请输入新通知类型的名称：",
                    "",
                    "添加通知类型",
                    Window.GetWindow(this)
                );

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return;
                }

                if (_viewModel.Types.Any(t => t.Name == typeName))
                {
                    GlobalToast.Warning("类型已存在", $"通知类型「{typeName}」已存在");
                    return;
                }

                _viewModel.AddType(typeName, "🔔", $"用户自定义通知类型：{typeName}");

                GlobalToast.Success("添加成功", $"已添加通知类型「{typeName}」");
                App.Log($"[NotificationTypesView] 已添加类型: {typeName}");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesView] 添加类型失败: {ex.Message}");
                StandardDialog.ShowError($"添加类型失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void OnDeleteTypeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedType == null)
                {
                    GlobalToast.Warning("请先选择", "请先点击选择一个通知类型");
                    return;
                }

                var typeName = _selectedType.Name;
                _viewModel.DeleteType(_selectedType);
                _selectedType = null;

                GlobalToast.Success("删除成功", $"已删除通知类型「{typeName}」");
                App.Log($"[NotificationTypesView] 已删除类型: {typeName}");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesView] 删除类型失败: {ex.Message}");
                StandardDialog.ShowError($"删除类型失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void OnColorPickerClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is NotificationTypeData typeData)
                {
                    var currentColor = (Color)ColorConverter.ConvertFromString(typeData.Color);

                    var selectedColor = ColorPickerDialog.Show(currentColor, Window.GetWindow(this));

                    if (selectedColor.HasValue)
                    {
                        var hexColor = $"#{selectedColor.Value.R:X2}{selectedColor.Value.G:X2}{selectedColor.Value.B:X2}";
                        typeData.Color = hexColor;
                        App.Log($"[NotificationTypes] 类型 {typeData.Name} 颜色已更新为: {hexColor}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypes] 颜色选择失败: {ex.Message}");
                StandardDialog.ShowError($"颜色选择失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }
    }
}

