using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace TM.Framework.User.Security.PasswordProtection.AutoLock
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class AutoLockViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AppLockSettings _lockSettings;
        private bool _disposed;

        private bool _enableAutoLock;
        private int _autoLockMinutes;
        private string _remainingTime;
        private string _lastActivityTime;
        private readonly DispatcherTimer _updateTimer;
        private string _countdownColor = "PrimaryColor";
        private int _recentLockCount;
        private readonly EventHandler _activityTimeUpdatedHandler;

        public AutoLockViewModel(AppLockSettings lockSettings)
        {
            _lockSettings = lockSettings;
            _remainingTime = "--:--";
            _lastActivityTime = "暂无活动";

            SaveSettingsCommand = new RelayCommand(() => SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[AutoLockViewModel] {ex.Message}")));
            LockNowCommand = new RelayCommand(LockNow);
            ResetTimerCommand = new RelayCommand(ResetTimer);

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            _activityTimeUpdatedHandler = (s, e) => UpdateDisplayInfo();
            _lockSettings.ActivityTimeUpdated += _activityTimeUpdatedHandler;

            AsyncSettingsLoader.RunOrDeferAsync(async () =>
            {
                var config = await _lockSettings.LoadConfigAsync().ConfigureAwait(false);
                var lockCount = _lockSettings.GetLockHistoryCount(7);
                return () =>
                {
                    EnableAutoLock = config.EnableAutoLock;
                    AutoLockMinutes = config.AutoLockMinutes;
                    RecentLockCount = lockCount;
                    UpdateDisplayInfo();
                    if (!_disposed) _updateTimer.Start();
                };
            }, "AutoLock");

            TM.App.Log("[AutoLockViewModel] 初始化完成");
        }

        #region 属性

        public ObservableCollection<int> AvailableMinutes { get; } = new ObservableCollection<int>
        {
            1, 3, 5, 10, 15, 30
        };

        public bool EnableAutoLock
        {
            get => _enableAutoLock;
            set
            {
                if (_enableAutoLock != value)
                {
                    _enableAutoLock = value;
                    OnPropertyChanged(nameof(EnableAutoLock));
                    OnPropertyChanged(nameof(AutoLockOptionsEnabled));
                    UpdateDisplayInfo();
                }
            }
        }

        public int AutoLockMinutes
        {
            get => _autoLockMinutes;
            set
            {
                if (_autoLockMinutes != value)
                {
                    _autoLockMinutes = value;
                    OnPropertyChanged(nameof(AutoLockMinutes));
                    UpdateDisplayInfo();
                }
            }
        }

        public string RemainingTime
        {
            get => _remainingTime;
            set
            {
                if (_remainingTime != value)
                {
                    _remainingTime = value;
                    OnPropertyChanged(nameof(RemainingTime));
                }
            }
        }

        public string LastActivityTime
        {
            get => _lastActivityTime;
            set
            {
                if (_lastActivityTime != value)
                {
                    _lastActivityTime = value;
                    OnPropertyChanged(nameof(LastActivityTime));
                }
            }
        }

        public bool AutoLockOptionsEnabled => EnableAutoLock;

        public string CountdownColor
        {
            get => _countdownColor;
            set
            {
                if (_countdownColor != value)
                {
                    _countdownColor = value;
                    OnPropertyChanged(nameof(CountdownColor));
                }
            }
        }

        public int RecentLockCount
        {
            get => _recentLockCount;
            set
            {
                if (_recentLockCount != value)
                {
                    _recentLockCount = value;
                    OnPropertyChanged(nameof(RecentLockCount));
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SaveSettingsCommand { get; }
        public ICommand LockNowCommand { get; }
        public ICommand ResetTimerCommand { get; }

        #endregion

        #region 方法

        private async Task SaveSettings()
        {
            try
            {
                var enable = EnableAutoLock;
                var minutes = AutoLockMinutes;

                var ok = await System.Threading.Tasks.Task.Run(async () =>
                {
                    var config = await _lockSettings.LoadConfigAsync().ConfigureAwait(false);
                    config.EnableAutoLock = enable;
                    config.AutoLockMinutes = minutes;

                    if (enable)
                    {
                        config.LastActivityTime = DateTime.Now;
                    }

                    return _lockSettings.SaveConfig(config);
                });

                if (ok)
                {
                    UpdateDisplayInfo();
                    GlobalToast.Success("保存成功", "自动锁定设置已保存");
                    TM.App.Log("[AutoLockViewModel] 设置保存成功");
                }
                else
                {
                    GlobalToast.Error("保存失败", "无法保存自动锁定设置");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoLockViewModel] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
            }
        }

        private void LockNow()
        {
            try
            {
                TM.App.Log("[AutoLockViewModel] 用户触发立即锁定");
                _lockSettings.LockApp();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoLockViewModel] 立即锁定失败: {ex.Message}");
                GlobalToast.Error("锁定失败", $"锁定失败：{ex.Message}");
            }
        }

        private void ResetTimer()
        {
            try
            {
                _lockSettings.UpdateLastActivity();
                UpdateDisplayInfo();
                GlobalToast.Success("重置成功", "已重置自动锁定计时器");
                TM.App.Log("[AutoLockViewModel] 计时器已重置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoLockViewModel] 重置计时器失败: {ex.Message}");
                GlobalToast.Error("重置失败", $"重置失败：{ex.Message}");
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateDisplayInfo();
        }

        private void UpdateDisplayInfo()
        {
            try
            {
                var config = _lockSettings.LoadConfig();

                if (config.LastActivityTime.HasValue)
                {
                    LastActivityTime = config.LastActivityTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    LastActivityTime = "暂无活动";
                }

                if (EnableAutoLock && config.LastActivityTime.HasValue)
                {
                    var remaining = _lockSettings.GetTimeUntilAutoLock(config);

                    if (remaining.TotalSeconds > 0)
                    {
                        RemainingTime = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

                        if (remaining.TotalSeconds < 10)
                        {
                            CountdownColor = "DangerColor";
                        }
                        else if (remaining.TotalSeconds < 60)
                        {
                            CountdownColor = "WarningColor";
                        }
                        else
                        {
                            CountdownColor = "PrimaryColor";
                        }
                    }
                    else
                    {
                        RemainingTime = "00:00 (已超时)";
                        CountdownColor = "DangerColor";
                    }
                }
                else
                {
                    RemainingTime = "--:--";
                    CountdownColor = "TextTertiary";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoLockViewModel] 更新显示信息失败: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimer_Tick;
            _lockSettings.ActivityTimeUpdated -= _activityTimeUpdatedHandler;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

