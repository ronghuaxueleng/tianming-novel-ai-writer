using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    public class TimeScheduleService
    {
        private readonly ThemeManager _themeManager;
        private readonly HolidayLibrary _holidayLibrary;
        private readonly Timer _timer;
        private volatile TimeBasedSettings _settings;
        private int _settingsVersion;
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
        private readonly System.Threading.Tasks.Task<TimeBasedSettings> _settingsTask;
        private volatile List<TimeSchedule>? _sortedSchedulesCache;

        public TimeScheduleService(ThemeManager themeManager, HolidayLibrary holidayLibrary)
        {
            _themeManager = themeManager;
            _holidayLibrary = holidayLibrary;
            _settings = new TimeBasedSettings();
            _settingsTask = LoadSettingsAsync();

            _timer = new Timer(60000);
            _timer.Elapsed += OnTimerElapsed;

            TM.App.Log("[TimeScheduleService] 初始化完成");
        }

        public async System.Threading.Tasks.Task InitializeAsync()
        {
            var loadVersion = System.Threading.Volatile.Read(ref _settingsVersion);
            var loaded = await _settingsTask.ConfigureAwait(false);
            if (loadVersion != System.Threading.Volatile.Read(ref _settingsVersion))
                return;
            _settings = loaded;
            _sortedSchedulesCache = null;
            if (_settings.Enabled && !_settings.TemporaryDisabled)
            {
                StartSchedule();
                TM.App.Log("[TimeScheduleService] 自动启动定时调度");
            }
        }

        public void StartSchedule()
        {
            if (!_timer.Enabled)
            {
                _timer.Start();
                TM.App.Log("[TimeScheduleService] 定时调度已启动");

                CheckAndSwitch();
            }
        }

        public void StopSchedule()
        {
            if (_timer.Enabled)
            {
                _timer.Stop();
                TM.App.Log("[TimeScheduleService] 定时调度已停止");
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            CheckAndSwitch();
        }

        private void CheckAndSwitch()
        {
            if (!_settings.Enabled || _settings.TemporaryDisabled)
                return;

            if (_settings.TemporaryDisabled && _settings.DisabledUntil.HasValue)
            {
                if (DateTime.Now >= _settings.DisabledUntil.Value)
                {
                    _settings.TemporaryDisabled = false;
                    _settings.DisabledUntil = null;
                    SaveSettingsAsync().SafeFireAndForget(ex => TM.App.Log($"[TimeSchedule] 保存失败: {ex.Message}"));
                }
                else
                {
                    return;
                }
            }

            if (_settings.ExcludeHolidays && IsHoliday(DateTime.Now))
            {
                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[TimeScheduleService] 今天是节假日，跳过切换");
                }
                return;
            }

            var targetTheme = CalculateCurrentTheme();

            if (IsHoliday(DateTime.Now) && _settings.HolidayThemeOverride != HolidayThemeOverride.NoChange)
            {
                targetTheme = _settings.HolidayThemeOverride switch
                {
                    HolidayThemeOverride.ForceLight => ThemeType.Light,
                    HolidayThemeOverride.ForceDark => ThemeType.Dark,
                    HolidayThemeOverride.Custom => _settings.HolidayTheme,
                    _ => targetTheme
                };

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log($"[TimeScheduleService] 节假日主题覆盖: {targetTheme}");
                }
            }

            if (targetTheme.HasValue && _themeManager.CurrentTheme != targetTheme.Value)
            {
                SwitchTheme(targetTheme.Value, "定时调度");
            }
        }

        public ThemeType? CalculateCurrentTheme()
        {
            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;
            var currentWeekday = ConvertDayOfWeek(now.DayOfWeek);

            switch (_settings.Mode)
            {
                case TimeScheduleMode.Simple:
                    if (_settings.DayStartTime < _settings.NightStartTime)
                    {
                        if (currentTime >= _settings.DayStartTime && currentTime < _settings.NightStartTime)
                            return _settings.DayTheme;
                        else
                            return _settings.NightTheme;
                    }
                    else
                    {
                        if (currentTime >= _settings.DayStartTime || currentTime < _settings.NightStartTime)
                            return _settings.DayTheme;
                        else
                            return _settings.NightTheme;
                    }

                case TimeScheduleMode.Flexible:
                    var sorted = _sortedSchedulesCache ??= _settings.Schedules
                        .OrderByDescending(s => s.Priority)
                        .ThenByDescending(s => s.StartTime)
                        .ToList();
                    foreach (var schedule in sorted)
                    {
                        if ((schedule.EnabledWeekdays & currentWeekday) != currentWeekday)
                            continue;

                        if (schedule.StartTime <= schedule.EndTime)
                        {
                            if (currentTime >= schedule.StartTime && currentTime < schedule.EndTime)
                                return schedule.TargetTheme;
                        }
                        else
                        {
                            if (currentTime >= schedule.StartTime || currentTime < schedule.EndTime)
                                return schedule.TargetTheme;
                        }
                    }
                    break;

                case TimeScheduleMode.Sunrise:
                    var (sunrise, sunset) = SunCalculator.CalculateSunTimes(now, _settings.Latitude, _settings.Longitude);

                    if (currentTime >= sunrise && currentTime < sunset)
                        return _settings.SunriseTheme;
                    else
                        return _settings.SunsetTheme;
            }

            return null;
        }

        private void SwitchTheme(ThemeType targetTheme, string scheduleName)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess())
            {
                SwitchThemeCore(targetTheme, scheduleName);
            }
            else
            {
                dispatcher.BeginInvoke(() => SwitchThemeCore(targetTheme, scheduleName));
            }
        }

        private void SwitchThemeCore(ThemeType targetTheme, string scheduleName)
        {
            try
            {
                _themeManager.SwitchTheme(targetTheme);

                _settings.LastSwitchTime = DateTime.Now;
                _settings.TotalSwitchCount++;

                if (_settings.RecordHistory)
                {
                    var record = new SwitchHistoryRecord
                    {
                        SwitchTime = DateTime.Now,
                        ScheduleName = scheduleName,
                        TargetTheme = targetTheme,
                        Success = true
                    };

                    _settings.History.Add(record);

                    if (_settings.History.Count > 100)
                    {
                        var removeCount = _settings.History.Count - 100;
                        _settings.History.RemoveRange(0, removeCount);
                    }
                }

                SaveSettingsAsync().SafeFireAndForget(ex => TM.App.Log($"[TimeSchedule] 保存失败: {ex.Message}"));

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log($"[TimeScheduleService] 主题切换完成: {targetTheme}, 触发: {scheduleName}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeScheduleService] 主题切换失败: {ex.Message}");

                if (_settings.RecordHistory)
                {
                    var record = new SwitchHistoryRecord
                    {
                        SwitchTime = DateTime.Now,
                        ScheduleName = scheduleName,
                        TargetTheme = targetTheme,
                        Success = false
                    };

                    _settings.History.Add(record);
                }
            }
        }

        public List<string> DetectConflicts()
        {
            var conflicts = new List<string>();

            if (_settings.Mode != TimeScheduleMode.Flexible)
                return conflicts;

            for (int i = 0; i < _settings.Schedules.Count; i++)
            {
                for (int j = i + 1; j < _settings.Schedules.Count; j++)
                {
                    var schedule1 = _settings.Schedules[i];
                    var schedule2 = _settings.Schedules[j];

                    if ((schedule1.EnabledWeekdays & schedule2.EnabledWeekdays) == Weekday.None)
                        continue;

                    if (IsTimeOverlap(schedule1.StartTime, schedule1.EndTime, schedule2.StartTime, schedule2.EndTime))
                    {
                        conflicts.Add($"时间段 {i + 1} 与时间段 {j + 1} 冲突");
                    }
                }
            }

            return conflicts;
        }

        private bool IsTimeOverlap(TimeSpan start1, TimeSpan end1, TimeSpan start2, TimeSpan end2)
        {
            if (start1 <= end1 && start2 <= end2)
            {
                return (start1 < end2 && end1 > start2);
            }

            return true;
        }

        private bool IsHoliday(DateTime date)
        {
            if (_settings.CustomHolidays.Any(h => h.Date == date.Date))
                return true;

            if (_settings.UseBuiltInHolidays)
            {
                return _holidayLibrary.IsHoliday(date);
            }

            return false;
        }

        private Weekday ConvertDayOfWeek(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => Weekday.Monday,
                DayOfWeek.Tuesday => Weekday.Tuesday,
                DayOfWeek.Wednesday => Weekday.Wednesday,
                DayOfWeek.Thursday => Weekday.Thursday,
                DayOfWeek.Friday => Weekday.Friday,
                DayOfWeek.Saturday => Weekday.Saturday,
                DayOfWeek.Sunday => Weekday.Sunday,
                _ => Weekday.None
            };
        }

        public TimeBasedSettings GetSettings()
        {
            return _settings;
        }

        public void UpdateSettings(TimeBasedSettings settings)
        {
            System.Threading.Interlocked.Increment(ref _settingsVersion);
            _settings = settings;
            _sortedSchedulesCache = null;
            SaveSettingsAsync().SafeFireAndForget(ex => TM.App.Log($"[TimeSchedule] 保存失败: {ex.Message}"));

            if (_settings.Enabled && !_settings.TemporaryDisabled)
            {
                StartSchedule();
            }
            else
            {
                StopSchedule();
            }

            TM.App.Log("[TimeScheduleService] 设置已更新");
        }

        private async System.Threading.Tasks.Task<TimeBasedSettings> LoadSettingsAsync()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/TimeBased", "settings.json");

                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                    var settings = JsonSerializer.Deserialize<TimeBasedSettings>(json);
                    if (settings != null)
                    {
                        TM.App.Log("[TimeScheduleService] 设置加载成功");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeScheduleService] 加载设置失败: {ex.Message}");
            }

            return TimeBasedSettings.CreateDefault();
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/TimeBased", "settings.json");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmpTsA = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpTsA))
                {
                    await JsonSerializer.SerializeAsync(stream, _settings, JsonHelper.Default).ConfigureAwait(false);
                }
                File.Move(tmpTsA, filePath, overwrite: true);

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[TimeScheduleService] 设置已异步保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeScheduleService] 异步保存设置失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}

