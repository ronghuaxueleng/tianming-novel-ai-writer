using System;
using System.Reflection;
using System.ComponentModel;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemIntegrationViewModel : INotifyPropertyChanged
    {
        private readonly SystemIntegrationSettings _settings;

        public SystemIntegrationViewModel(SystemIntegrationSettings settings)
        {
            _settings = settings;
            App.Log("[SystemIntegrationViewModel] 初始化完成");
        }

        public bool EnableWindowsNotification
        {
            get => _settings.EnableWindowsNotification;
            set
            {
                if (_settings.EnableWindowsNotification != value)
                {
                    _settings.EnableWindowsNotification = value;
                    OnPropertyChanged(nameof(EnableWindowsNotification));
                }
            }
        }

        public bool NotificationSound
        {
            get => _settings.NotificationSound;
            set
            {
                if (_settings.NotificationSound != value)
                {
                    _settings.NotificationSound = value;
                    OnPropertyChanged(nameof(NotificationSound));
                }
            }
        }

        public string NotificationPriority
        {
            get => _settings.NotificationPriority;
            set
            {
                if (_settings.NotificationPriority != value)
                {
                    _settings.NotificationPriority = value;
                    OnPropertyChanged(nameof(NotificationPriority));
                }
            }
        }

        public bool ShowTrayIcon
        {
            get => _settings.ShowTrayIcon;
            set
            {
                if (_settings.ShowTrayIcon != value)
                {
                    _settings.ShowTrayIcon = value;
                    OnPropertyChanged(nameof(ShowTrayIcon));
                }
            }
        }

        public ClickBehavior SingleClickBehavior
        {
            get => _settings.SingleClickBehavior;
            set
            {
                if (_settings.SingleClickBehavior != value)
                {
                    _settings.SingleClickBehavior = value;
                    OnPropertyChanged(nameof(SingleClickBehavior));
                }
            }
        }

        public ClickBehavior DoubleClickBehavior
        {
            get => _settings.DoubleClickBehavior;
            set
            {
                if (_settings.DoubleClickBehavior != value)
                {
                    _settings.DoubleClickBehavior = value;
                    OnPropertyChanged(nameof(DoubleClickBehavior));
                }
            }
        }

        public bool CloseToTray
        {
            get => _settings.CloseToTray;
            set
            {
                if (_settings.CloseToTray != value)
                {
                    _settings.CloseToTray = value;
                    OnPropertyChanged(nameof(CloseToTray));
                }
            }
        }

        public bool AutoStartup
        {
            get => _settings.AutoStartup;
            set
            {
                if (_settings.AutoStartup != value)
                {
                    _settings.AutoStartup = value;
                    OnPropertyChanged(nameof(AutoStartup));
                }
            }
        }

        public StartupMode StartupMode
        {
            get => _settings.StartupMode;
            set
            {
                if (_settings.StartupMode != value)
                {
                    _settings.StartupMode = value;
                    OnPropertyChanged(nameof(StartupMode));
                }
            }
        }

        public int StartupDelay
        {
            get => _settings.StartupDelay;
            set
            {
                if (_settings.StartupDelay != value)
                {
                    _settings.StartupDelay = value;
                    OnPropertyChanged(nameof(StartupDelay));
                }
            }
        }

        public bool RegisterUrlProtocol
        {
            get => _settings.RegisterUrlProtocol;
            set
            {
                if (_settings.RegisterUrlProtocol != value)
                {
                    _settings.RegisterUrlProtocol = value;
                    OnPropertyChanged(nameof(RegisterUrlProtocol));
                }
            }
        }

        public bool AssociateFileType
        {
            get => _settings.AssociateFileType;
            set
            {
                if (_settings.AssociateFileType != value)
                {
                    _settings.AssociateFileType = value;
                    OnPropertyChanged(nameof(AssociateFileType));
                }
            }
        }

        public bool AddToContextMenu
        {
            get => _settings.AddToContextMenu;
            set
            {
                if (_settings.AddToContextMenu != value)
                {
                    _settings.AddToContextMenu = value;
                    OnPropertyChanged(nameof(AddToContextMenu));
                }
            }
        }

        public bool AddToSendToMenu
        {
            get => _settings.AddToSendToMenu;
            set
            {
                if (_settings.AddToSendToMenu != value)
                {
                    _settings.AddToSendToMenu = value;
                    OnPropertyChanged(nameof(AddToSendToMenu));
                }
            }
        }

        public void ResetToDefaults()
        {
            _settings.ResetToDefaults();

            OnPropertyChanged(nameof(EnableWindowsNotification));
            OnPropertyChanged(nameof(NotificationSound));
            OnPropertyChanged(nameof(NotificationPriority));
            OnPropertyChanged(nameof(ShowTrayIcon));
            OnPropertyChanged(nameof(SingleClickBehavior));
            OnPropertyChanged(nameof(DoubleClickBehavior));
            OnPropertyChanged(nameof(CloseToTray));
            OnPropertyChanged(nameof(AutoStartup));
            OnPropertyChanged(nameof(StartupMode));
            OnPropertyChanged(nameof(StartupDelay));
            OnPropertyChanged(nameof(RegisterUrlProtocol));
            OnPropertyChanged(nameof(AssociateFileType));
            OnPropertyChanged(nameof(AddToContextMenu));
            OnPropertyChanged(nameof(AddToSendToMenu));

            App.Log("[SystemIntegrationViewModel] 已重置为默认设置");
        }

        public void SaveAndApplySettings()
        {
            try
            {
                _settings.SaveSettings();
                App.Log("[SystemIntegrationViewModel] 保存设置成功");

                if (_settings.ShowTrayIcon)
                {
                    App.Log("[SystemIntegrationViewModel] 托盘图标: 启用");
                }

                if (_settings.AutoStartup)
                {
                    App.Log($"[SystemIntegrationViewModel] 开机自启: 启用 (模式: {_settings.StartupMode}, 延迟: {_settings.StartupDelay}s)");
                }

                if (_settings.RegisterUrlProtocol)
                {
                    App.Log("[SystemIntegrationViewModel] URL协议注册: 启用");
                }

                if (_settings.AssociateFileType)
                {
                    App.Log("[SystemIntegrationViewModel] 文件关联: 启用");
                }

                if (_settings.AddToContextMenu)
                {
                    App.Log("[SystemIntegrationViewModel] 右键菜单: 启用");
                }

                if (_settings.AddToSendToMenu)
                {
                    App.Log("[SystemIntegrationViewModel] 发送到菜单: 启用");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemIntegrationViewModel] 保存设置失败: {ex.Message}");
                throw;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

