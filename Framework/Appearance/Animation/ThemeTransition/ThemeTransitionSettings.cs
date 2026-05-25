using System.Reflection;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TransitionEffect
    {
        None,

        Rotate,

        Blur,

        SlideLeft,

        SlideRight,

        SlideUp,

        SlideDown,

        FlipHorizontal,

        FlipVertical
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TransitionPreset
    {
        Fast,

        Smooth,

        Fancy,

        Simple,

        Dynamic,

        Cool,

        Custom
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ViewSwitchEffect
    {
        None,
        Fade,
        FadeScale,
        SlideUp,
        SlideDown,
        SlideLeft,
        SlideRight,
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ThemeTransitionSettings
    {
        [JsonPropertyName("Effect")] public TransitionEffect Effect { get; set; } = TransitionEffect.Rotate;
        [JsonPropertyName("CombinedEffects")] public List<TransitionEffect> CombinedEffects { get; set; } = new List<TransitionEffect>();
        [JsonPropertyName("EasingType")] public EasingFunctionType EasingType { get; set; } = EasingFunctionType.Linear;
        [JsonPropertyName("Duration")] public int Duration { get; set; } = 2000;
        [JsonPropertyName("TargetFPS")] public int TargetFPS { get; set; } = 60;
        [JsonPropertyName("IntensityMultiplier")] public double IntensityMultiplier { get; set; } = 1.0;
        [JsonPropertyName("Preset")] public TransitionPreset Preset { get; set; } = TransitionPreset.Fast;

        [JsonPropertyName("ViewSwitchEnabled")] public bool ViewSwitchEnabled { get; set; } = true;
        [JsonPropertyName("ViewSwitchOutMs")] public int ViewSwitchOutMs { get; set; } = 60;
        [JsonPropertyName("ViewSwitchInMs")] public int ViewSwitchInMs { get; set; } = 120;
        [JsonPropertyName("ViewSwitchEffect")] public ViewSwitchEffect ViewSwitchEffect { get; set; } = ViewSwitchEffect.Fade;

        public static ThemeTransitionSettings CreateDefault()
        {
            return new ThemeTransitionSettings
            {
                Effect = TransitionEffect.Rotate,
                CombinedEffects = new List<TransitionEffect> { TransitionEffect.Rotate },
                EasingType = EasingFunctionType.Linear,
                Duration = 2000,
                TargetFPS = 60,
                IntensityMultiplier = 1.0,
                Preset = TransitionPreset.Fast,
                ViewSwitchEnabled = true,
                ViewSwitchOutMs = 60,
                ViewSwitchInMs = 120,
                ViewSwitchEffect = ViewSwitchEffect.Fade
            };
        }

        public ThemeTransitionSettings Clone()
        {
            return new ThemeTransitionSettings
            {
                Effect = this.Effect,
                CombinedEffects = new List<TransitionEffect>(this.CombinedEffects),
                EasingType = this.EasingType,
                Duration = this.Duration,
                TargetFPS = this.TargetFPS,
                IntensityMultiplier = this.IntensityMultiplier,
                Preset = this.Preset,
                ViewSwitchEnabled = this.ViewSwitchEnabled,
                ViewSwitchOutMs = this.ViewSwitchOutMs,
                ViewSwitchInMs = this.ViewSwitchInMs,
                ViewSwitchEffect = this.ViewSwitchEffect
            };
        }
    }

    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ViewSwitchEffectItem
    {
        public ViewSwitchEffectItem(ViewSwitchEffect effect, ImageSource? icon, string displayName, string description)
        {
            Effect = effect;
            Icon = icon;
            DisplayName = displayName;
            Description = description;
        }
        public ViewSwitchEffect Effect { get; }
        public ImageSource? Icon { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }

    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class TransitionEffectItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public TransitionEffect Effect { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public ImageSource? Icon { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string FullDisplay => DisplayName;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

