using System;
using System.Reflection;
using System.ComponentModel;

namespace TM.Framework.Notifications.SystemNotifications.NotificationStyle
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationStyleViewModel : INotifyPropertyChanged
    {
        private readonly NotificationStyleSettings _settings;

        public NotificationStyleViewModel(NotificationStyleSettings settings)
        {
            _settings = settings;
            App.Log("[NotificationStyleViewModel] 初始化完成");
        }

        public double CornerRadius
        {
            get => _settings.CornerRadius;
            set
            {
                if (Math.Abs(_settings.CornerRadius - value) > 0.01)
                {
                    _settings.CornerRadius = value;
                    OnPropertyChanged(nameof(CornerRadius));
                }
            }
        }

        public double ShadowIntensity
        {
            get => _settings.ShadowIntensity;
            set
            {
                if (Math.Abs(_settings.ShadowIntensity - value) > 0.01)
                {
                    _settings.ShadowIntensity = value;
                    OnPropertyChanged(nameof(ShadowIntensity));
                }
            }
        }

        public double BorderThickness
        {
            get => _settings.BorderThickness;
            set
            {
                if (Math.Abs(_settings.BorderThickness - value) > 0.01)
                {
                    _settings.BorderThickness = value;
                    OnPropertyChanged(nameof(BorderThickness));
                }
            }
        }

        public double BackgroundOpacity
        {
            get => _settings.BackgroundOpacity;
            set
            {
                if (Math.Abs(_settings.BackgroundOpacity - value) > 0.01)
                {
                    _settings.BackgroundOpacity = value;
                    OnPropertyChanged(nameof(BackgroundOpacity));
                }
            }
        }

        public AnimationType AnimationType
        {
            get => _settings.AnimationType;
            set
            {
                if (_settings.AnimationType != value)
                {
                    _settings.AnimationType = value;
                    OnPropertyChanged(nameof(AnimationType));
                }
            }
        }

        public int AnimationDuration
        {
            get => _settings.AnimationDuration;
            set
            {
                if (_settings.AnimationDuration != value)
                {
                    _settings.AnimationDuration = value;
                    OnPropertyChanged(nameof(AnimationDuration));
                }
            }
        }

        public EasingFunction EasingFunction
        {
            get => _settings.EasingFunction;
            set
            {
                if (_settings.EasingFunction != value)
                {
                    _settings.EasingFunction = value;
                    OnPropertyChanged(nameof(EasingFunction));
                }
            }
        }

        public ScreenPosition ScreenPosition
        {
            get => _settings.ScreenPosition;
            set
            {
                if (_settings.ScreenPosition != value)
                {
                    _settings.ScreenPosition = value;
                    OnPropertyChanged(nameof(ScreenPosition));
                }
            }
        }

        public double NotificationWidth
        {
            get => _settings.NotificationWidth;
            set
            {
                if (Math.Abs(_settings.NotificationWidth - value) > 0.01)
                {
                    _settings.NotificationWidth = value;
                    OnPropertyChanged(nameof(NotificationWidth));
                }
            }
        }

        public double NotificationHeight
        {
            get => _settings.NotificationHeight;
            set
            {
                if (Math.Abs(_settings.NotificationHeight - value) > 0.01)
                {
                    _settings.NotificationHeight = value;
                    OnPropertyChanged(nameof(NotificationHeight));
                }
            }
        }

        public double NotificationSpacing
        {
            get => _settings.NotificationSpacing;
            set
            {
                if (Math.Abs(_settings.NotificationSpacing - value) > 0.01)
                {
                    _settings.NotificationSpacing = value;
                    OnPropertyChanged(nameof(NotificationSpacing));
                }
            }
        }

        public StackDirection StackDirection
        {
            get => _settings.StackDirection;
            set
            {
                if (_settings.StackDirection != value)
                {
                    _settings.StackDirection = value;
                    OnPropertyChanged(nameof(StackDirection));
                }
            }
        }

        public void ResetToDefaults()
        {
            _settings.ResetToDefaults();

            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(ShadowIntensity));
            OnPropertyChanged(nameof(BorderThickness));
            OnPropertyChanged(nameof(BackgroundOpacity));
            OnPropertyChanged(nameof(AnimationType));
            OnPropertyChanged(nameof(AnimationDuration));
            OnPropertyChanged(nameof(EasingFunction));
            OnPropertyChanged(nameof(ScreenPosition));
            OnPropertyChanged(nameof(NotificationWidth));
            OnPropertyChanged(nameof(NotificationHeight));
            OnPropertyChanged(nameof(NotificationSpacing));
            OnPropertyChanged(nameof(StackDirection));

            App.Log("[NotificationStyleViewModel] 已重置为默认设置");
        }

        public void ApplyPreset(string presetName)
        {
            _settings.ApplyPreset(presetName);

            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(ShadowIntensity));
            OnPropertyChanged(nameof(BorderThickness));
            OnPropertyChanged(nameof(BackgroundOpacity));
            OnPropertyChanged(nameof(AnimationType));
            OnPropertyChanged(nameof(AnimationDuration));
            OnPropertyChanged(nameof(EasingFunction));
            OnPropertyChanged(nameof(ScreenPosition));
            OnPropertyChanged(nameof(NotificationWidth));
            OnPropertyChanged(nameof(NotificationHeight));
            OnPropertyChanged(nameof(NotificationSpacing));
            OnPropertyChanged(nameof(StackDirection));

            App.Log($"[NotificationStyleViewModel] 已应用预设: {presetName}");
        }

        public void SaveSettings()
        {
            try
            {
                _settings.SaveSettings();
                App.Log("[NotificationStyleViewModel] 保存设置成功");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationStyleViewModel] 保存设置失败: {ex.Message}");
                throw;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

