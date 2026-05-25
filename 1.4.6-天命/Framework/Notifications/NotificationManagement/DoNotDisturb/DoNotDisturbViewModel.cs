using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DoNotDisturbViewModel : INotifyPropertyChanged
    {
        private readonly DoNotDisturbSettings _settings;
        private ObservableCollection<string> _exceptionApps;

        public DoNotDisturbViewModel(DoNotDisturbSettings settings)
        {
            _settings = settings;
            _exceptionApps = new ObservableCollection<string>(_settings.ExceptionApps);

            ToggleCommand = new RelayCommand(Toggle);
            QuickEnableCommand = new RelayCommand<string>(QuickEnable);
            SaveCommand = new RelayCommand(Save);

            TM.App.Log("[DoNotDisturb] 加载免打扰设置");
        }

        public bool IsEnabled
        {
            get => _settings.IsEnabled;
            set 
            { 
                _settings.IsEnabled = value; 
                OnPropertyChanged();
                OnStatusChanged();
            }
        }

        public TimeSpan StartTime
        {
            get => _settings.StartTime;
            set { _settings.StartTime = value; OnPropertyChanged(); }
        }

        public TimeSpan EndTime
        {
            get => _settings.EndTime;
            set { _settings.EndTime = value; OnPropertyChanged(); }
        }

        public bool AllowUrgentNotifications
        {
            get => _settings.AllowUrgentNotifications;
            set { _settings.AllowUrgentNotifications = value; OnPropertyChanged(); }
        }

        public bool AutoEnableInFullscreen
        {
            get => _settings.AutoEnableInFullscreen;
            set { _settings.AutoEnableInFullscreen = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> ExceptionApps
        {
            get => _exceptionApps;
            set { _exceptionApps = value; OnPropertyChanged(); }
        }

        public string StatusText => IsEnabled ? "免打扰已启用" : "免打扰已关闭";
        public string StatusColor => IsEnabled ? "#4CAF50" : "#9E9E9E";

        public ICommand ToggleCommand { get; }
        public ICommand QuickEnableCommand { get; }
        public ICommand SaveCommand { get; }

        private void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        private void QuickEnable(string? duration)
        {
            IsEnabled = true;
            GlobalToast.Success("免打扰", $"已启用免打扰模式：{duration}");
            TM.App.Log($"[DoNotDisturb] 快捷启用: {duration}");
        }

        private void Save()
        {
            _settings.ExceptionApps = _exceptionApps.ToList();
            _ = _settings.SaveDataAsync();
            GlobalToast.Success("保存设置", "免打扰设置已保存");
            TM.App.Log("[DoNotDisturb] 保存设置");
        }

        private void OnStatusChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));

            var status = IsEnabled ? "启用" : "关闭";
            GlobalToast.Info("免打扰", $"免打扰模式已{status}");
            TM.App.Log($"[DoNotDisturb] 状态变更: {status}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
