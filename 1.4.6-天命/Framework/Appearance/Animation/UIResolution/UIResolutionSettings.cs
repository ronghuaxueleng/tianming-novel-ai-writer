using System;
using System.Text.Json.Serialization;

namespace TM.Framework.Appearance.Animation.UIResolution
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum PresetResolution
    {
        HD,

        FullHD,

        QHD,

        Custom
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum UIScaleLevel
    {
        Scale100 = 100,

        Scale125 = 125,

        Scale150 = 150,

        Scale200 = 200
    }

    public class UIResolutionSettings
    {

        [JsonPropertyName("WindowWidth")] public int WindowWidth { get; set; } = 1920;
        [JsonPropertyName("WindowHeight")] public int WindowHeight { get; set; } = 1080;
        [JsonPropertyName("UsePreset")] public bool UsePreset { get; set; } = true;
        [JsonPropertyName("Preset")] public PresetResolution Preset { get; set; } = PresetResolution.FullHD;
        [JsonPropertyName("ScalePercent")] public int ScalePercent { get; set; } = 100;
        [JsonPropertyName("MinWidth")] public int MinWidth { get; set; } = 800;
        [JsonPropertyName("MinHeight")] public int MinHeight { get; set; } = 600;
        [JsonPropertyName("MaxWidth")] public int MaxWidth { get; set; } = 0;
        [JsonPropertyName("MaxHeight")] public int MaxHeight { get; set; } = 0;

        public static UIResolutionSettings CreateDefault()
        {
            return new UIResolutionSettings
            {
                WindowWidth = 1920,
                WindowHeight = 1080,
                UsePreset = true,
                Preset = PresetResolution.FullHD,
                ScalePercent = 100,
                MinWidth = 800,
                MinHeight = 600,
                MaxWidth = 0,
                MaxHeight = 0
            };
        }

        public static (int width, int height) GetPresetResolution(PresetResolution preset)
        {
            return preset switch
            {
                PresetResolution.HD => (1280, 720),
                PresetResolution.FullHD => (1920, 1080),
                PresetResolution.QHD => (2560, 1440),
                PresetResolution.Custom => (1920, 1080),
                _ => (1920, 1080)
            };
        }

        public static string GetPresetDisplayName(PresetResolution preset)
        {
            return preset switch
            {
                PresetResolution.HD => "720p (1280×720)",
                PresetResolution.FullHD => "1080p (1920×1080)",
                PresetResolution.QHD => "1440p (2560×1440)",
                PresetResolution.Custom => "自定义",
                _ => "未知"
            };
        }

        public static string GetScaleDisplayName(UIScaleLevel scale)
        {
            return scale switch
            {
                UIScaleLevel.Scale100 => "100% (标准)",
                UIScaleLevel.Scale125 => "125% (稍大)",
                UIScaleLevel.Scale150 => "150% (较大)",
                UIScaleLevel.Scale200 => "200% (很大)",
                _ => "未知"
            };
        }

        public bool ValidateSize(int width, int height, int maxWidth, int maxHeight)
        {
            if (width < MinWidth || height < MinHeight)
                return false;

            if (maxWidth > 0 && width > maxWidth)
                return false;

            if (maxHeight > 0 && height > maxHeight)
                return false;

            return true;
        }

        public UIResolutionSettings Clone()
        {
            return new UIResolutionSettings
            {
                WindowWidth = this.WindowWidth,
                WindowHeight = this.WindowHeight,
                UsePreset = this.UsePreset,
                Preset = this.Preset,
                ScalePercent = this.ScalePercent,
                MinWidth = this.MinWidth,
                MinHeight = this.MinHeight,
                MaxWidth = this.MaxWidth,
                MaxHeight = this.MaxHeight
            };
        }
    }
}

