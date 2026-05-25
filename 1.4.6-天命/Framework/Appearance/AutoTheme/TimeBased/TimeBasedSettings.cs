using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TimeScheduleMode
    {
        Simple,
        Flexible,
        Sunrise
    }

    [Flags]
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum Weekday
    {
        None = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 4,
        Thursday = 8,
        Friday = 16,
        Saturday = 32,
        Sunday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekends = Saturday | Sunday,
        All = Weekdays | Weekends
    }

    public class TimeSchedule
    {
        [JsonPropertyName("StartTime")] public TimeSpan StartTime { get; set; }
        [JsonPropertyName("EndTime")] public TimeSpan EndTime { get; set; }
        [JsonPropertyName("TargetTheme")] public ThemeType TargetTheme { get; set; }
        [JsonPropertyName("EnabledWeekdays")] public Weekday EnabledWeekdays { get; set; } = Weekday.All;
        [JsonPropertyName("UseTransition")] public bool UseTransition { get; set; } = true;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("Priority")] public int Priority { get; set; } = 5;
    }

    public class SwitchHistoryRecord
    {
        [JsonPropertyName("SwitchTime")] public DateTime SwitchTime { get; set; }
        [JsonPropertyName("ScheduleName")] public string ScheduleName { get; set; } = string.Empty;
        [JsonPropertyName("TargetTheme")] public ThemeType TargetTheme { get; set; }
        [JsonPropertyName("Success")] public bool Success { get; set; }
    }

    public class TimeBasedSettings
    {
        [JsonPropertyName("Enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("Mode")] public TimeScheduleMode Mode { get; set; } = TimeScheduleMode.Simple;
        [JsonPropertyName("DayStartTime")] public TimeSpan DayStartTime { get; set; } = new TimeSpan(7, 0, 0);
        [JsonPropertyName("DayTheme")] public ThemeType DayTheme { get; set; } = ThemeType.Light;
        [JsonPropertyName("NightStartTime")] public TimeSpan NightStartTime { get; set; } = new TimeSpan(19, 0, 0);
        [JsonPropertyName("NightTheme")] public ThemeType NightTheme { get; set; } = ThemeType.Dark;
        [JsonPropertyName("Schedules")] public List<TimeSchedule> Schedules { get; set; } = new();
        [JsonPropertyName("Latitude")] public double Latitude { get; set; } = 39.9;
        [JsonPropertyName("Longitude")] public double Longitude { get; set; } = 116.4;
        [JsonPropertyName("AutoLocation")] public bool AutoLocation { get; set; } = false;
        [JsonPropertyName("SunriseTheme")] public ThemeType SunriseTheme { get; set; } = ThemeType.Light;
        [JsonPropertyName("SunsetTheme")] public ThemeType SunsetTheme { get; set; } = ThemeType.Dark;
        [JsonPropertyName("ExcludeHolidays")] public bool ExcludeHolidays { get; set; } = false;
        [JsonPropertyName("UseBuiltInHolidays")] public bool UseBuiltInHolidays { get; set; } = false;
        [JsonPropertyName("CustomHolidays")] public List<DateTime> CustomHolidays { get; set; } = new();
        [JsonPropertyName("HolidayThemeOverride")] public HolidayThemeOverride HolidayThemeOverride { get; set; } = HolidayThemeOverride.NoChange;
        [JsonPropertyName("HolidayTheme")] public ThemeType HolidayTheme { get; set; } = ThemeType.Light;
        [JsonPropertyName("TemporaryDisabled")] public bool TemporaryDisabled { get; set; } = false;
        [JsonPropertyName("DisabledUntil")] public DateTime? DisabledUntil { get; set; }
        [JsonPropertyName("RecordHistory")] public bool RecordHistory { get; set; } = true;
        [JsonPropertyName("History")] public List<SwitchHistoryRecord> History { get; set; } = new();
        [JsonPropertyName("LastSwitchTime")] public DateTime? LastSwitchTime { get; set; }
        [JsonPropertyName("TotalSwitchCount")] public int TotalSwitchCount { get; set; }

        [JsonPropertyName("NextSwitchTime")] public DateTime? NextSwitchTime { get; set; }
        [JsonPropertyName("EnableVerboseLog")] public bool EnableVerboseLog { get; set; } = false;
        [JsonPropertyName("Priority")] public int Priority { get; set; } = 5;

        public static TimeBasedSettings CreateDefault()
        {
            return new TimeBasedSettings
            {
                Enabled = false,
                Mode = TimeScheduleMode.Simple,
                DayStartTime = new TimeSpan(7, 0, 0),
                DayTheme = ThemeType.Light,
                NightStartTime = new TimeSpan(19, 0, 0),
                NightTheme = ThemeType.Dark,
                Schedules = new List<TimeSchedule>(),
                Latitude = 39.9,
                Longitude = 116.4,
                AutoLocation = false,
                SunriseTheme = ThemeType.Light,
                SunsetTheme = ThemeType.Dark,
                ExcludeHolidays = false,
                UseBuiltInHolidays = false,
                CustomHolidays = new List<DateTime>(),
                HolidayThemeOverride = HolidayThemeOverride.NoChange,
                HolidayTheme = ThemeType.Light,
                TemporaryDisabled = false,
                RecordHistory = true,
                History = new List<SwitchHistoryRecord>(),
                EnableVerboseLog = false,
                Priority = 5
            };
        }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum HolidayThemeOverride
    {
        NoChange,
        ForceLight,
        ForceDark,
        Custom
    }
}

