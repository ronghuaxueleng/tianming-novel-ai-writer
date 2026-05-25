using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Timers;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    public class TimeScheduleService
    {
        private readonly ThemeManager _themeManager;
        private readonly HolidayLibrary _holidayLibrary;
        private readonly Timer _timer;
        private TimeBasedSettings _settings;

        public TimeScheduleService(ThemeManager themeManager, HolidayLibrary holidayLibrary)
        {
            _themeManager = themeManager;
            _holidayLibrary = holidayLibrary;
            _settings = LoadSettings();

            _timer = new Timer(60000);
            _timer.Elapsed += OnTimerElapsed;

            TM.App.Log("[TimeScheduleService] 初始化完成");
        }

        public void Initialize()
        {
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
                    SaveSettings();
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
                    foreach (var schedule in _settings.Schedules.OrderByDescending(s => s.Priority).ThenByDescending(s => s.StartTime))
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

                SaveSettings();

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
            _settings = settings;
            SaveSettings();

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

        private TimeBasedSettings LoadSettings()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/TimeBased", "settings.json");

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
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

        private void SaveSettings()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/TimeBased", "settings.json");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpTs = filePath + ".tmp";
                File.WriteAllText(tmpTs, json);
                File.Move(tmpTs, filePath, overwrite: true);

                if (_settings.EnableVerboseLog)
                {
                    TM.App.Log("[TimeScheduleService] 设置已保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeScheduleService] 保存设置失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var filePath = StoragePathHelper.GetFilePath("Framework", "Appearance/AutoTheme/TimeBased", "settings.json");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpTsA = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpTsA, json).ConfigureAwait(false);
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
        }
    }
}

