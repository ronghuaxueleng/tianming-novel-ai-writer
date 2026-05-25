using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Services;
using TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services;
using TM.Services.Framework.SystemIntegration;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SystemIntegrationView : UserControl
    {
        private readonly SystemIntegrationViewModel _viewModel;

        public SystemIntegrationView()
        {
            InitializeComponent();
            _viewModel = ServiceLocator.Get<SystemIntegrationViewModel>();
            DataContext = _viewModel;

            App.Log("[SystemIntegrationView] 视图初始化完成");
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.ResetToDefaults();
                GlobalToast.Success("重置成功", "已恢复为默认配置");
                App.Log("[SystemIntegrationView] 已重置为默认设置");
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegrationView] 重置失败: {ex.Message}");
                StandardDialog.ShowError($"重置失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveAndApplySettings();

                ApplySystemIntegration();

                GlobalToast.Success("保存成功", "系统集成设置已成功保存并应用");
                App.Log("[SystemIntegrationView] 设置已保存");
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegrationView] 保存失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败：\n\n{ex.Message}", "错误", Window.GetWindow(this));
            }
        }

        private void ApplySystemIntegration()
        {
            try
            {
                ApplyWindowsNotification();

                ApplyAutoStartup();

                ApplyTrayIconSettings();

                ApplyUrlProtocol();

                ApplyFileTypeAssociation();

                ApplyContextMenu();

                ApplySendToMenu();

                App.Log("[SystemIntegration] 系统集成设置已应用");
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用系统集成设置失败: {ex.Message}");
                throw;
            }
        }

        private void ApplyWindowsNotification()
        {
            try
            {
                if (_viewModel.EnableWindowsNotification)
                {
                    WindowsNotificationService.Enable();
                    App.Log("[SystemIntegration] Windows原生通知已启用");
                }
                else
                {
                    WindowsNotificationService.Disable();
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用Windows通知设置失败: {ex.Message}");
            }
        }

        private void ApplyAutoStartup()
        {
            try
            {
                var success = AutoStartupService.SetAutoStartup(
                    _viewModel.AutoStartup,
                    ((int)_viewModel.StartupMode).ToString(),
                    _viewModel.StartupDelay
                );

                if (success)
                {
                    var status = _viewModel.AutoStartup ? "已启用" : "已禁用";
                    App.Log($"[SystemIntegration] 开机自启动{status}");
                }
                else
                {
                    App.Log("[SystemIntegration] 开机自启动设置失败（可能需要管理员权限）");
                    GlobalToast.Warning("权限提示", "设置开机自启动可能需要管理员权限");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用开机自启动设置失败: {ex.Message}");
            }
        }

        private void ApplyTrayIconSettings()
        {
            try
            {
                var trayService = ServiceLocator.Get<TrayIconService>();

                var status = _viewModel.ShowTrayIcon ? "已启用" : "已禁用";
                App.Log($"[SystemIntegration] 托盘图标{status}");
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用托盘图标设置失败: {ex.Message}");
            }
        }

        private void ApplyUrlProtocol()
        {
            try
            {
                bool success;
                if (_viewModel.RegisterUrlProtocol)
                {
                    success = UrlProtocolService.RegisterUrlProtocol();

                    if (success)
                    {
                        App.Log("[SystemIntegration] URL协议已注册");
                    }
                    else
                    {
                        App.Log("[SystemIntegration] URL协议注册失败（可能需要管理员权限）");
                        GlobalToast.Warning("权限提示", "注册URL协议需要管理员权限，请以管理员身份运行程序");
                    }
                }
                else
                {
                    success = UrlProtocolService.UnregisterUrlProtocol();

                    if (success)
                    {
                        App.Log("[SystemIntegration] URL协议已取消注册");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用URL协议设置失败: {ex.Message}");
            }
        }

        private void ApplyFileTypeAssociation()
        {
            try
            {
                bool success;
                if (_viewModel.AssociateFileType)
                {
                    success = FileTypeAssociationService.AssociateFileType();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 文件类型已关联");

                        FileTypeAssociationService.RefreshIconCache();
                    }
                    else
                    {
                        App.Log("[SystemIntegration] 文件类型关联失败（可能需要管理员权限）");
                        GlobalToast.Warning("权限提示", "关联文件类型需要管理员权限，请以管理员身份运行程序");
                    }
                }
                else
                {
                    success = FileTypeAssociationService.UnassociateFileType();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 文件类型关联已取消");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用文件类型关联设置失败: {ex.Message}");
            }
        }

        private void ApplyContextMenu()
        {
            try
            {
                bool success;
                if (_viewModel.AddToContextMenu)
                {
                    success = TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.ContextMenuService.AddToContextMenu();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 右键菜单已添加");
                    }
                    else
                    {
                        App.Log("[SystemIntegration] 右键菜单添加失败（可能需要管理员权限）");
                        GlobalToast.Warning("权限提示", "添加右键菜单需要管理员权限，请以管理员身份运行程序");
                    }
                }
                else
                {
                    success = TM.Framework.Notifications.SystemNotifications.SystemIntegration.Services.ContextMenuService.RemoveFromContextMenu();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 右键菜单已移除");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用右键菜单设置失败: {ex.Message}");
            }
        }

        private void ApplySendToMenu()
        {
            try
            {
                bool success;
                if (_viewModel.AddToSendToMenu)
                {
                    success = SendToMenuService.AddToSendToMenu();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 发送到菜单已添加");
                    }
                    else
                    {
                        App.Log("[SystemIntegration] 发送到菜单添加失败");
                        GlobalToast.Warning("添加失败", "无法添加到发送到菜单");
                    }
                }
                else
                {
                    success = SendToMenuService.RemoveFromSendToMenu();

                    if (success)
                    {
                        App.Log("[SystemIntegration] 发送到菜单已移除");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegration] 应用发送到菜单设置失败: {ex.Message}");
            }
        }
    }
}

