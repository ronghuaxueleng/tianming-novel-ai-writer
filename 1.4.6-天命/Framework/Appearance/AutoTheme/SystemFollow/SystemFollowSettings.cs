using System;
using System.Collections.Generic;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum SystemThemeMapping
    {
        Light,
        Dark,
        Custom
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum HighContrastBehavior
    {
        Ignore,
        UseLight,
        UseDark,
        Custom
    }

    public class ExclusionPeriod
    {
        [System.Text.Json.Serialization.JsonPropertyName("StartTime")] public TimeSpan StartTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndTime")] public TimeSpan EndTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Days")] public DayOfWeek Days { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    }

    public class SceneRule
    {
        [System.Text.Json.Serialization.JsonPropertyName("SceneName")] public string SceneName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("StartTime")] public TimeSpan StartTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndTime")] public TimeSpan EndTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DisableSwitching")] public bool DisableSwitching { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    }

    public class SystemFollowSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("Enabled")] public bool Enabled { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AutoStart")] public bool AutoStart { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("LightThemeMapping")] public ThemeType LightThemeMapping { get; set; } = ThemeType.Light;
        [System.Text.Json.Serialization.JsonPropertyName("DarkThemeMapping")] public ThemeType DarkThemeMapping { get; set; } = ThemeType.Dark;
        [System.Text.Json.Serialization.JsonPropertyName("HighContrastMapping")] public HighContrastBehavior HighContrastMapping { get; set; } = HighContrastBehavior.Ignore;
        [System.Text.Json.Serialization.JsonPropertyName("HighContrastCustomTheme")] public ThemeType HighContrastCustomTheme { get; set; } = ThemeType.Dark;
        [System.Text.Json.Serialization.JsonPropertyName("DelaySeconds")] public int DelaySeconds { get; set; } = 3;
        [System.Text.Json.Serialization.JsonPropertyName("ShowNotification")] public bool ShowNotification { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("EnableAccentColor")] public bool EnableAccentColor { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("OnlyWhenNotManual")] public bool OnlyWhenNotManual { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("ExclusionPeriods")] public List<ExclusionPeriod> ExclusionPeriods { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("MinSwitchInterval")] public int MinSwitchInterval { get; set; } = 30;
        [System.Text.Json.Serialization.JsonPropertyName("DebounceDelay")] public int DebounceDelay { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("EnableSmartDelay")] public bool EnableSmartDelay { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("EnableSceneDetection")] public bool EnableSceneDetection { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("SceneRules")] public List<SceneRule> SceneRules { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Priority")] public int Priority { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("LastSwitchTime")] public DateTime? LastSwitchTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("TotalSwitchCount")] public int TotalSwitchCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LastDetectedTheme")] public string LastDetectedTheme { get; set; } = "未知";
        [System.Text.Json.Serialization.JsonPropertyName("EnableVerboseLog")] public bool EnableVerboseLog { get; set; } = false;

        public static SystemFollowSettings CreateDefault()
        {
            return new SystemFollowSettings
            {
                Enabled = false,
                AutoStart = true,
                LightThemeMapping = ThemeType.Light,
                DarkThemeMapping = ThemeType.Dark,
                HighContrastMapping = HighContrastBehavior.Ignore,
                DelaySeconds = 3,
                ShowNotification = true,
                EnableAccentColor = false,
                OnlyWhenNotManual = false,
                ExclusionPeriods = new List<ExclusionPeriod>(),
                EnableVerboseLog = false
            };
        }
    }
}

