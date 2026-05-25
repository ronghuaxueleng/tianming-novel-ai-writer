using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DoNotDisturbData
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsScheduleEnabled")] public bool IsScheduleEnabled { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("StartTime")] public TimeSpan StartTime { get; set; } = new TimeSpan(22, 0, 0);
        [System.Text.Json.Serialization.JsonPropertyName("EndTime")] public TimeSpan EndTime { get; set; } = new TimeSpan(8, 0, 0);
        [System.Text.Json.Serialization.JsonPropertyName("AllowUrgentNotifications")] public bool AllowUrgentNotifications { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("AutoEnableInFullscreen")] public bool AutoEnableInFullscreen { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExceptionApps")] public List<string> ExceptionApps { get; set; } = new();
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DoNotDisturbSettings : BaseSettings<DoNotDisturbSettings, DoNotDisturbData>
    {
        private System.Windows.Threading.DispatcherTimer? _autoOffTimer;
        private System.Windows.Threading.DispatcherTimer? _activeStateTimer;
        private DateTime? _autoOffAt;
        private readonly object _syncLoadLock = new();
        private bool _syncLoadAttempted;
        private bool _lastActiveState;

        public DoNotDisturbSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory)
        {
            StartActiveStateTimer();
        }

        public DateTime? AutoOffAt => _autoOffAt;

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/NotificationManagement/DoNotDisturb", "dnd_settings.json");

        protected override DoNotDisturbData CreateDefaultData() => _objectFactory.Create<DoNotDisturbData>();

        public bool IsEnabled
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.IsEnabled;
            }
            set
            {
                EnsureDataLoadedForBlocking();
                if (Data.IsEnabled == value) return;
                Data.IsEnabled = value;
                if (value) ToastNotification.ClearAll();
                if (!value) StopAutoOffTimer();
                _ = SaveDataAsync();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentlyActive));
            }
        }

        public void EnableUntil(DateTime? offAt)
        {
            StopAutoOffTimer();

            if (offAt.HasValue && offAt.Value <= DateTime.Now)
            {
                IsEnabled = false;
                return;
            }

            IsEnabled = true;

            if (offAt.HasValue)
            {
                _autoOffAt = offAt;
                ToastNotification.ClearAll();
                var span = offAt.Value - DateTime.Now;
                _autoOffTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = span
                };
                _autoOffTimer.Tick += (_, _) =>
                {
                    StopAutoOffTimer();
                    IsEnabled = false;
                    TM.App.Log("[DoNotDisturb] 快捷免打扰自动到期关闭");
                };
                _autoOffTimer.Start();
                OnPropertyChanged(nameof(AutoOffAt));
            }
        }

        private void StopAutoOffTimer()
        {
            if (_autoOffTimer != null)
            {
                _autoOffTimer.Stop();
                _autoOffTimer = null;
            }
            if (_autoOffAt.HasValue)
            {
                _autoOffAt = null;
                OnPropertyChanged(nameof(AutoOffAt));
            }
        }

        public bool IsScheduleEnabled
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.IsScheduleEnabled;
            }
            set
            {
                EnsureDataLoadedForBlocking();
                if (Data.IsScheduleEnabled == value) return;
                Data.IsScheduleEnabled = value;
                _ = SaveDataAsync();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentlyActive));
            }
        }

        public TimeSpan StartTime
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.StartTime;
            }
            set
            {
                EnsureDataLoadedForBlocking();
                if (Data.StartTime == value) return;
                Data.StartTime = value;
                _ = SaveDataAsync();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentlyActive));
            }
        }

        public TimeSpan EndTime
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.EndTime;
            }
            set
            {
                EnsureDataLoadedForBlocking();
                if (Data.EndTime == value) return;
                Data.EndTime = value;
                _ = SaveDataAsync();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentlyActive));
            }
        }

        public bool AllowUrgentNotifications
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.AllowUrgentNotifications;
            }
            set { EnsureDataLoadedForBlocking(); Data.AllowUrgentNotifications = value; _ = SaveDataAsync(); OnPropertyChanged(); }
        }

        public bool AutoEnableInFullscreen
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.AutoEnableInFullscreen;
            }
            set
            {
                EnsureDataLoadedForBlocking();
                if (Data.AutoEnableInFullscreen == value) return;
                Data.AutoEnableInFullscreen = value;
                _ = SaveDataAsync();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentlyActive));
            }
        }

        public List<string> ExceptionApps
        {
            get
            {
                EnsureDataLoadedForBlocking();
                return Data.ExceptionApps;
            }
            set { EnsureDataLoadedForBlocking(); Data.ExceptionApps = value; _ = SaveDataAsync(); OnPropertyChanged(); }
        }

        public bool IsCurrentlyActive
        {
            get
            {
                EnsureDataLoadedForBlocking();
                if (IsEnabled) return true;
                if (IsScheduleEnabled && IsInScheduleRange(DateTime.Now.TimeOfDay, StartTime, EndTime)) return true;
                if (AutoEnableInFullscreen && IsFullscreenAppActive()) return true;
                return false;
            }
        }

        public bool ShouldBlock(bool isHighPriority = false)
        {
            EnsureDataLoadedForBlocking();

            if (isHighPriority && AllowUrgentNotifications)
                return false;

            return IsCurrentlyActive;
        }

        public void NotifyActiveStateChanged()
        {
            var active = IsCurrentlyActive;
            if (active) ToastNotification.ClearAll();
            _lastActiveState = active;
            OnPropertyChanged(nameof(IsCurrentlyActive));
        }

        private void StartActiveStateTimer()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.InvokeAsync(() =>
            {
                if (_activeStateTimer != null) return;

                _lastActiveState = IsCurrentlyActive;
                if (_lastActiveState) ToastNotification.ClearAll();

                _activeStateTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background,
                    dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                _activeStateTimer.Tick += (_, _) =>
                {
                    var active = IsCurrentlyActive;
                    if (active) ToastNotification.ClearAll();
                    if (active != _lastActiveState)
                    {
                        _lastActiveState = active;
                        OnPropertyChanged(nameof(IsCurrentlyActive));
                    }
                };
                _activeStateTimer.Start();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void EnsureDataLoadedForBlocking()
        {
            if (_syncLoadAttempted) return;

            lock (_syncLoadLock)
            {
                if (_syncLoadAttempted) return;
                _syncLoadAttempted = true;

                try
                {
                    if (File.Exists(FilePath))
                    {
                        var json = File.ReadAllText(FilePath);
                        var loadedData = JsonSerializer.Deserialize<DoNotDisturbData>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (loadedData != null)
                        {
                            SetData(loadedData);
                            OnDataLoaded();
                            OnPropertyChanged(null);
                            TM.App.Log("[DoNotDisturb] 免打扰设置已同步加载");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[DoNotDisturb] 免打扰设置同步加载失败: {ex.Message}");
                }
            }
        }

        internal static bool IsInScheduleRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start == end) return false;
            if (start < end) return now >= start && now < end;
            return now >= start || now < end;
        }

        private static bool IsFullscreenAppActive()
        {
            try
            {
                if (SHQueryUserNotificationState(out var state) == 0)
                {
                    return state == QueryUserNotificationState.QUNS_BUSY
                        || state == QueryUserNotificationState.QUNS_RUNNING_D3D_FULL_SCREEN
                        || state == QueryUserNotificationState.QUNS_PRESENTATION_MODE;
                }
            }
            catch
            {
            }
            return false;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

        private enum QueryUserNotificationState
        {
            QUNS_NOT_PRESENT = 1,
            QUNS_BUSY = 2,
            QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4,
            QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6,
            QUNS_APP = 7
        }
    }
}
