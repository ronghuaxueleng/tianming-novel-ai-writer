using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DoNotDisturbViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DoNotDisturbSettings _settings;
        private ObservableCollection<string> _exceptionApps;
        private DispatcherTimer? _activeStateTimer;
        private bool _disposed;

        public DoNotDisturbViewModel(DoNotDisturbSettings settings)
        {
            _settings = settings;
            _exceptionApps = new ObservableCollection<string>(_settings.ExceptionApps);

            TimeOptions = Enumerable.Range(0, 24)
                .Select(h => new TimeSpan(h, 0, 0))
                .ToList();

            ToggleCommand = new RelayCommand(Toggle);
            QuickEnableCommand = new RelayCommand<string>(QuickEnable);

            _settings.PropertyChanged += OnSettingsPropertyChanged;

            _activeStateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _activeStateTimer.Tick += (_, _) => _settings.NotifyActiveStateChanged();
            _activeStateTimer.Start();

            TM.App.Log("[DoNotDisturb] 加载免打扰设置");
        }

        public bool IsEnabled
        {
            get => _settings.IsEnabled;
            set
            {
                if (_settings.IsEnabled == value) return;
                _settings.IsEnabled = value;
                TM.App.Log($"[DoNotDisturb] 手动开关: {(value ? "启用" : "关闭")}");
            }
        }

        public bool IsScheduleEnabled
        {
            get => _settings.IsScheduleEnabled;
            set
            {
                if (_settings.IsScheduleEnabled == value) return;
                _settings.IsScheduleEnabled = value;
                TM.App.Log($"[DoNotDisturb] 定时免打扰: {(value ? "启用" : "关闭")}");
            }
        }

        public TimeSpan StartTime
        {
            get => _settings.StartTime;
            set { _settings.StartTime = value; }
        }

        public TimeSpan EndTime
        {
            get => _settings.EndTime;
            set { _settings.EndTime = value; }
        }

        public bool AllowUrgentNotifications
        {
            get => _settings.AllowUrgentNotifications;
            set { _settings.AllowUrgentNotifications = value; }
        }

        public bool AutoEnableInFullscreen
        {
            get => _settings.AutoEnableInFullscreen;
            set { _settings.AutoEnableInFullscreen = value; }
        }

        public ObservableCollection<string> ExceptionApps
        {
            get => _exceptionApps;
            set { _exceptionApps = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<TimeSpan> TimeOptions { get; }

        public bool IsActive => _settings.IsCurrentlyActive;

        public string StatusText
        {
            get
            {
                if (!IsActive) return "免打扰已关闭";
                if (_settings.IsEnabled)
                {
                    var offAt = _settings.AutoOffAt;
                    if (offAt.HasValue)
                        return $"免打扰已启用（至 {offAt.Value:HH:mm}）";
                    return "免打扰已启用";
                }
                if (_settings.IsScheduleEnabled
                    && DoNotDisturbSettings.IsInScheduleRange(DateTime.Now.TimeOfDay, _settings.StartTime, _settings.EndTime))
                    return $"定时免打扰中（至 {_settings.EndTime:hh\\:mm}）";
                if (_settings.AutoEnableInFullscreen)
                    return "全屏应用免打扰中";
                return "免打扰已启用";
            }
        }

        public ICommand ToggleCommand { get; }
        public ICommand QuickEnableCommand { get; }

        private void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        private void QuickEnable(string? duration)
        {
            DateTime? offAt = duration switch
            {
                "1小时" => DateTime.Now.AddHours(1),
                "直到明天8:00" => GetNext8Am(),
                _ => null
            };

            _settings.EnableUntil(offAt);
            TM.App.Log($"[DoNotDisturb] 快捷启用: {duration}, 到期时间={offAt}");
        }

        private static DateTime GetNext8Am()
        {
            var now = DateTime.Now;
            var target = new DateTime(now.Year, now.Month, now.Day, 8, 0, 0);
            if (now >= target) target = target.AddDays(1);
            return target;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            if (string.IsNullOrEmpty(name))
            {
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(IsScheduleEnabled));
                OnPropertyChanged(nameof(StartTime));
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(AllowUrgentNotifications));
                OnPropertyChanged(nameof(AutoEnableInFullscreen));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(StatusText));
                return;
            }

            switch (name)
            {
                case nameof(DoNotDisturbSettings.IsEnabled):
                    OnPropertyChanged(nameof(IsEnabled));
                    break;
                case nameof(DoNotDisturbSettings.IsScheduleEnabled):
                    OnPropertyChanged(nameof(IsScheduleEnabled));
                    break;
                case nameof(DoNotDisturbSettings.StartTime):
                    OnPropertyChanged(nameof(StartTime));
                    break;
                case nameof(DoNotDisturbSettings.EndTime):
                    OnPropertyChanged(nameof(EndTime));
                    break;
                case nameof(DoNotDisturbSettings.AllowUrgentNotifications):
                    OnPropertyChanged(nameof(AllowUrgentNotifications));
                    break;
                case nameof(DoNotDisturbSettings.AutoEnableInFullscreen):
                    OnPropertyChanged(nameof(AutoEnableInFullscreen));
                    break;
                case nameof(DoNotDisturbSettings.IsCurrentlyActive):
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(StatusText));
                    break;
                case nameof(DoNotDisturbSettings.AutoOffAt):
                    OnPropertyChanged(nameof(StatusText));
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            if (_activeStateTimer != null)
            {
                _activeStateTimer.Stop();
                _activeStateTimer = null;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
