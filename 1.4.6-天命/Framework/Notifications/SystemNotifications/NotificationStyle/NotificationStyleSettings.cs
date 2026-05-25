using System;
using System.Reflection;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace TM.Framework.Notifications.SystemNotifications.NotificationStyle
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum AnimationType
    {
        FadeInOut,
        SlideIn,
        Bounce,
        Scale
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ScreenPosition
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft,
        Center
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum StackDirection
    {
        Up,
        Down
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum EasingFunction
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationStyleData
    {
        [System.Text.Json.Serialization.JsonPropertyName("CornerRadius")] public double CornerRadius { get; set; } = 8;
        [System.Text.Json.Serialization.JsonPropertyName("ShadowIntensity")] public double ShadowIntensity { get; set; } = 12;
        [System.Text.Json.Serialization.JsonPropertyName("BorderThickness")] public double BorderThickness { get; set; } = 1;
        [System.Text.Json.Serialization.JsonPropertyName("BackgroundOpacity")] public double BackgroundOpacity { get; set; } = 95;
        [System.Text.Json.Serialization.JsonPropertyName("AnimationType")] public AnimationType AnimationType { get; set; } = AnimationType.FadeInOut;
        [System.Text.Json.Serialization.JsonPropertyName("AnimationDuration")] public int AnimationDuration { get; set; } = 300;
        [System.Text.Json.Serialization.JsonPropertyName("EasingFunction")] public EasingFunction EasingFunction { get; set; } = EasingFunction.Linear;
        [System.Text.Json.Serialization.JsonPropertyName("ScreenPosition")] public ScreenPosition ScreenPosition { get; set; } = ScreenPosition.BottomRight;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationWidth")] public double NotificationWidth { get; set; } = 300;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationHeight")] public double NotificationHeight { get; set; } = 80;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationSpacing")] public double NotificationSpacing { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("StackDirection")] public StackDirection StackDirection { get; set; } = StackDirection.Down;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationStyleSettings : BaseSettings<NotificationStyleSettings, NotificationStyleData>
    {
        public NotificationStyleSettings(Common.Services.Factories.IStoragePathHelper storagePathHelper, 
            Common.Services.Factories.IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/SystemNotifications/NotificationStyle", "notification_style.json");

        protected override NotificationStyleData CreateDefaultData() => _objectFactory.Create<NotificationStyleData>();

        public double CornerRadius { get => Data.CornerRadius; set { Data.CornerRadius = value; OnPropertyChanged(); } }
        public double ShadowIntensity { get => Data.ShadowIntensity; set { Data.ShadowIntensity = value; OnPropertyChanged(); } }
        public double BorderThickness { get => Data.BorderThickness; set { Data.BorderThickness = value; OnPropertyChanged(); } }
        public double BackgroundOpacity { get => Data.BackgroundOpacity; set { Data.BackgroundOpacity = value; OnPropertyChanged(); } }
        public AnimationType AnimationType { get => Data.AnimationType; set { Data.AnimationType = value; OnPropertyChanged(); } }
        public int AnimationDuration { get => Data.AnimationDuration; set { Data.AnimationDuration = value; OnPropertyChanged(); } }
        public EasingFunction EasingFunction { get => Data.EasingFunction; set { Data.EasingFunction = value; OnPropertyChanged(); } }
        public ScreenPosition ScreenPosition { get => Data.ScreenPosition; set { Data.ScreenPosition = value; OnPropertyChanged(); } }
        public double NotificationWidth { get => Data.NotificationWidth; set { Data.NotificationWidth = value; OnPropertyChanged(); } }
        public double NotificationHeight { get => Data.NotificationHeight; set { Data.NotificationHeight = value; OnPropertyChanged(); } }
        public double NotificationSpacing { get => Data.NotificationSpacing; set { Data.NotificationSpacing = value; OnPropertyChanged(); } }
        public StackDirection StackDirection { get => Data.StackDirection; set { Data.StackDirection = value; OnPropertyChanged(); } }

        public void ApplyPreset(string presetName)
        {
            switch (presetName.ToLower())
            {
                case "simple":
                    CornerRadius = 4;
                    ShadowIntensity = 5;
                    BorderThickness = 1;
                    BackgroundOpacity = 100;
                    AnimationType = AnimationType.FadeInOut;
                    AnimationDuration = 200;
                    break;

                case "standard":
                    ResetToDefaults();
                    break;

                case "fancy":
                    CornerRadius = 16;
                    ShadowIntensity = 25;
                    BorderThickness = 0;
                    BackgroundOpacity = 90;
                    AnimationType = AnimationType.Bounce;
                    AnimationDuration = 500;
                    break;
            }
            SaveData();
            TM.App.Log($"[NotificationStyleSettings] 应用预设: {presetName}");
        }

        public void LoadSettings() => LoadData();
        public void SaveSettings() => SaveData();
    }
}

