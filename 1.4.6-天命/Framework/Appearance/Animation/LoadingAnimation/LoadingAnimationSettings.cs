using System;
using System.Text.Json.Serialization;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum LoadingAnimationType
    {
        Spinner,

        Dots,

        Bars,

        Pulse,

        Ring,

        Wave,

        Progress,

        Skeleton,

        Custom1,

        Custom2
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum LoadingPosition
    {
        Center,

        Top,

        Bottom,

        TopRight,

        BottomRight
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum OverlayMode
    {
        None,

        Transparent,

        Blur,

        Full
    }

    public class LoadingAnimationSettings
    {

        [JsonPropertyName("AnimationType")] public LoadingAnimationType AnimationType { get; set; } = LoadingAnimationType.Spinner;
        [JsonPropertyName("Position")] public LoadingPosition Position { get; set; } = LoadingPosition.Center;
        [JsonPropertyName("Overlay")] public OverlayMode Overlay { get; set; } = OverlayMode.Transparent;
        [JsonPropertyName("AnimationSpeed")] public int AnimationSpeed { get; set; } = 100;
        [JsonPropertyName("Size")] public int Size { get; set; } = 48;
        [JsonPropertyName("PrimaryColor")] public string PrimaryColor { get; set; } = "#3B82F6";
        [JsonPropertyName("SecondaryColor")] public string SecondaryColor { get; set; } = "#60A5FA";
        [JsonPropertyName("Opacity")] public double Opacity { get; set; } = 0.9;
        [JsonPropertyName("ShowText")] public bool ShowText { get; set; } = true;
        [JsonPropertyName("LoadingText")] public string LoadingText { get; set; } = "加载中...";
        [JsonPropertyName("TextSize")] public int TextSize { get; set; } = 14;
        [JsonPropertyName("TextColor")] public string TextColor { get; set; } = "#FFFFFF";
        [JsonPropertyName("ShowPercentage")] public bool ShowPercentage { get; set; } = false;
        [JsonPropertyName("OverlayOpacity")] public double OverlayOpacity { get; set; } = 0.5;
        [JsonPropertyName("OverlayColor")] public string OverlayColor { get; set; } = "#000000";
        [JsonPropertyName("BlurRadius")] public int BlurRadius { get; set; } = 8;
        [JsonPropertyName("MinDisplayTime")] public int MinDisplayTime { get; set; } = 300;
        [JsonPropertyName("DelayTime")] public int DelayTime { get; set; } = 200;
        [JsonPropertyName("CancelOnClick")] public bool CancelOnClick { get; set; } = false;
        [JsonPropertyName("EnableSound")] public bool EnableSound { get; set; } = false;
        [JsonPropertyName("SoundPath")] public string SoundPath { get; set; } = "";

        public static LoadingAnimationSettings CreateDefault()
        {
            return new LoadingAnimationSettings
            {
                AnimationType = LoadingAnimationType.Spinner,
                Position = LoadingPosition.Center,
                Overlay = OverlayMode.Transparent,
                AnimationSpeed = 100,
                Size = 48,
                PrimaryColor = "#3B82F6",
                SecondaryColor = "#60A5FA",
                Opacity = 0.9,
                ShowText = true,
                LoadingText = "加载中...",
                TextSize = 14,
                TextColor = "#FFFFFF",
                ShowPercentage = false,
                OverlayOpacity = 0.5,
                OverlayColor = "#000000",
                BlurRadius = 8,
                MinDisplayTime = 300,
                DelayTime = 200,
                CancelOnClick = false,
                EnableSound = false,
                SoundPath = ""
            };
        }

        public LoadingAnimationSettings Clone()
        {
            return new LoadingAnimationSettings
            {
                AnimationType = this.AnimationType,
                Position = this.Position,
                Overlay = this.Overlay,
                AnimationSpeed = this.AnimationSpeed,
                Size = this.Size,
                PrimaryColor = this.PrimaryColor,
                SecondaryColor = this.SecondaryColor,
                Opacity = this.Opacity,
                ShowText = this.ShowText,
                LoadingText = this.LoadingText,
                TextSize = this.TextSize,
                TextColor = this.TextColor,
                ShowPercentage = this.ShowPercentage,
                OverlayOpacity = this.OverlayOpacity,
                OverlayColor = this.OverlayColor,
                BlurRadius = this.BlurRadius,
                MinDisplayTime = this.MinDisplayTime,
                DelayTime = this.DelayTime,
                CancelOnClick = this.CancelOnClick,
                EnableSound = this.EnableSound,
                SoundPath = this.SoundPath
            };
        }
    }
}

