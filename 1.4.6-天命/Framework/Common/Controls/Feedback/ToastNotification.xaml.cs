using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TM.Framework.Common.Services;

namespace TM.Framework.Common.Controls.Feedback
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ToastNotification : Window
    {
        private static readonly List<ToastNotification> _activeToasts = new();
        private static readonly object _lock = new();
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();
        private DispatcherTimer? _autoCloseTimer;

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ToastNotification] {key}: {ex.Message}");
        }

        private ToastNotification(string title, string message, ToastType type, int duration)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

            ApplyStyleSettings();

            SetTypeStyle(type);

            Loaded += (s, e) =>
            {
                PositionToast();
                PlayFadeInAnimation();
            };

            Closed += (s, e) => RemoveFromActiveList();

            if (duration > 0)
            {
                _autoCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(duration)
                };
                _autoCloseTimer.Tick += (s, e) =>
                {
                    _autoCloseTimer?.Stop();
                    CloseToast();
                };
                _autoCloseTimer.Start();
            }
        }

        private void SetTypeStyle(ToastType type)
        {
            double opacity = 0.95;

            try
            {
                var settings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();
                opacity = settings.BackgroundOpacity / 100.0;
            }
            catch (Exception ex)
            {
                DebugLogOnce("SetTypeStyle_Opacity", ex);
            }

            try
            {
                var typeSettings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypeSettings>();
                var types = typeSettings.LoadSettings();

                string typeId = type switch
                {
                    ToastType.Success => "success",
                    ToastType.Warning => "warning",
                    ToastType.Error => "error",
                    ToastType.Info => "info",
                    _ => "info"
                };

                var typeData = types.FirstOrDefault(t => t.Id == typeId);

                if (typeData != null && typeData.IsEnabled)
                {
                    IconText.Text = typeData.Icon;
                    ToastBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeData.Color));
                    ToastBorder.Background.Opacity = opacity;
                    TitleText.Foreground = Brushes.White;
                    MessageText.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ToastNotification] 无法加载类型配置: {ex.Message}，使用默认样式");
            }

            switch (type)
            {
                case ToastType.Success:
                    IconText.Text = "✅";
                    ToastBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    ToastBorder.Background.Opacity = opacity;
                    TitleText.Foreground = Brushes.White;
                    MessageText.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
                    break;

                case ToastType.Warning:
                    IconText.Text = "⚠️";
                    ToastBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    ToastBorder.Background.Opacity = opacity;
                    TitleText.Foreground = Brushes.White;
                    MessageText.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
                    break;

                case ToastType.Error:
                    IconText.Text = "❌";
                    ToastBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    ToastBorder.Background.Opacity = opacity;
                    TitleText.Foreground = Brushes.White;
                    MessageText.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
                    break;

                case ToastType.Info:
                    IconText.Text = "ℹ️";
                    ToastBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                    ToastBorder.Background.Opacity = opacity;
                    TitleText.Foreground = Brushes.White;
                    MessageText.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
                    break;
            }
        }

        private void ApplyStyleSettings()
        {
            try
            {
                var settings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();

                ToastBorder.CornerRadius = new CornerRadius(settings.CornerRadius);
                ToastBorder.BorderThickness = new Thickness(settings.BorderThickness);

                if (ToastBorder.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = settings.ShadowIntensity;
                }

                Width = settings.NotificationWidth;
                ToastBorder.MinHeight = settings.NotificationHeight;
                ToastBorder.MaxHeight = Math.Max(settings.NotificationHeight * 3, 200);

            }
            catch (Exception ex)
            {
                DebugLogOnce("ApplyStyleSettings", ex);
            }
        }

        private void PositionToast()
        {
            var workArea = SystemParameters.WorkArea;
            var settings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();

            lock (_lock)
            {
                int count = _activeToasts.Count;
                double spacing = settings.NotificationSpacing;
                double edgeMargin = 10;
                double topMargin = 10;
                double offset = topMargin;

                for (int i = 0; i < count; i++)
                {
                    var toast = _activeToasts[i];
                    if (toast != null && toast.IsLoaded)
                    {
                        offset += toast.ActualHeight + spacing;
                    }
                }

                switch (settings.ScreenPosition)
                {
                    case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopRight:
                        Left = workArea.Right - ActualWidth - edgeMargin;
                        Top = workArea.Top + offset;
                        break;
                    case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopLeft:
                        Left = workArea.Left + edgeMargin;
                        Top = workArea.Top + offset;
                        break;
                    case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomRight:
                        Left = workArea.Right - ActualWidth - edgeMargin;
                        Top = workArea.Bottom - offset - ActualHeight;
                        break;
                    case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomLeft:
                        Left = workArea.Left + edgeMargin;
                        Top = workArea.Bottom - offset - ActualHeight;
                        break;
                    case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.Center:
                        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
                        Top = workArea.Top + (workArea.Height / 2) + (count * (ActualHeight + spacing)) - (ActualHeight / 2);
                        break;
                }

                _activeToasts.Add(this);
            }
        }

        private void PlayFadeInAnimation()
        {
            var settings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();
            int duration = settings.AnimationDuration;

            IEasingFunction? easingFunc = settings.EasingFunction switch
            {
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.Linear => null,
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseIn => new QuadraticEase { EasingMode = EasingMode.EaseIn },
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseOut => new QuadraticEase { EasingMode = EasingMode.EaseOut },
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseInOut => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                _ => new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            switch (settings.AnimationType)
            {
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.FadeInOut:
                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    BeginAnimation(OpacityProperty, fadeIn);
                    break;

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.SlideIn:
                    var fadeInSlide = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    var slideIn = new DoubleAnimation
                    {
                        From = Left + 50,
                        To = Left,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    BeginAnimation(OpacityProperty, fadeInSlide);
                    BeginAnimation(LeftProperty, slideIn);
                    break;

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.Bounce:
                    var fadeInBounce = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2 }
                    };
                    BeginAnimation(OpacityProperty, fadeInBounce);
                    break;

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.Scale:
                    var fadeInScale = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    BeginAnimation(OpacityProperty, fadeInScale);

                    var scaleTransform = new System.Windows.Media.ScaleTransform(0.8, 0.8);
                    RenderTransform = scaleTransform;
                    RenderTransformOrigin = new Point(0.5, 0.5);

                    var scaleX = new DoubleAnimation
                    {
                        From = 0.8,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    var scaleY = new DoubleAnimation
                    {
                        From = 0.8,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(duration),
                        EasingFunction = easingFunc
                    };
                    scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                    scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
                    break;
            }
        }

        private void PlayFadeOutAnimation(Action onComplete)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            fadeOut.Completed += (s, e) => onComplete?.Invoke();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private void CloseToast()
        {
            PlayFadeOutAnimation(Close);
        }

        private void RemoveFromActiveList()
        {
            lock (_lock)
            {
                _activeToasts.Remove(this);
                RepositionToasts();
            }
        }

        private static void RepositionToasts()
        {
            var workArea = SystemParameters.WorkArea;
            var settings = ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();

            double edgeMargin = 10;
            double topMargin = 10;
            double spacing = settings.NotificationSpacing;
            double offset = topMargin;

            foreach (var toast in _activeToasts.ToList())
            {
                if (toast != null && toast.IsLoaded)
                {
                    double newTop;
                    double newLeft;

                    switch (settings.ScreenPosition)
                    {
                        case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopRight:
                            newLeft = workArea.Right - toast.ActualWidth - edgeMargin;
                            newTop = workArea.Top + offset;
                            toast.BeginAnimation(LeftProperty, new DoubleAnimation
                            {
                                To = newLeft,
                                Duration = TimeSpan.FromMilliseconds(200),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                            break;

                        case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopLeft:
                            newLeft = workArea.Left + edgeMargin;
                            newTop = workArea.Top + offset;
                            toast.BeginAnimation(LeftProperty, new DoubleAnimation
                            {
                                To = newLeft,
                                Duration = TimeSpan.FromMilliseconds(200),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                            break;

                        case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomRight:
                            newLeft = workArea.Right - toast.ActualWidth - edgeMargin;
                            newTop = workArea.Bottom - offset - toast.ActualHeight;
                            toast.BeginAnimation(LeftProperty, new DoubleAnimation
                            {
                                To = newLeft,
                                Duration = TimeSpan.FromMilliseconds(200),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                            break;

                        case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomLeft:
                            newLeft = workArea.Left + edgeMargin;
                            newTop = workArea.Bottom - offset - toast.ActualHeight;
                            toast.BeginAnimation(LeftProperty, new DoubleAnimation
                            {
                                To = newLeft,
                                Duration = TimeSpan.FromMilliseconds(200),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                            break;

                        case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.Center:
                            newLeft = workArea.Left + (workArea.Width - toast.ActualWidth) / 2;
                            newTop = workArea.Top + (workArea.Height / 2) + offset;
                            toast.BeginAnimation(LeftProperty, new DoubleAnimation
                            {
                                To = newLeft,
                                Duration = TimeSpan.FromMilliseconds(200),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                            break;

                        default:
                            newTop = workArea.Top + offset;
                            break;
                    }

                    var moveAnimation = new DoubleAnimation
                    {
                        To = newTop,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    toast.BeginAnimation(TopProperty, moveAnimation);
                    offset += toast.ActualHeight + spacing;
                }
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            CloseToast();
        }

        public static void Show(string title, string message = "", ToastType type = ToastType.Info, int duration = 3000, bool isHighPriority = false)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var dndSettings = ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings>();
                bool isBlocked = dndSettings.ShouldBlock(isHighPriority);

                var historySettings = ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistorySettings>();
                string typeStr = type switch
                {
                    ToastType.Success => "成功",
                    ToastType.Warning => "警告",
                    ToastType.Error => "错误",
                    ToastType.Info => "信息",
                    _ => "信息"
                };
                historySettings.AddRecord(title, message, typeStr, isBlocked);

                if (isBlocked)
                {
                    App.Log($"[ToastNotification] 免打扰已拦截通知: {title}");
                    return;
                }

                System.Threading.Tasks.Task.Run(async () =>
                {
                    await ServiceLocator.Get<TM.Services.Framework.Notification.NotificationSoundService>().PlayNotificationSound(type, isHighPriority);
                });

                var toast = new ToastNotification(title, message, type, duration);
                toast.Show();
            });
        }

        public static void ShowSuccess(string title, string message = "", int duration = 3000)
        {
            Show(title, message, ToastType.Success, duration);
        }

        public static void ShowWarning(string title, string message = "", int duration = 3000)
        {
            Show(title, message, ToastType.Warning, duration);
        }

        public static void ShowError(string title, string message = "", int duration = 3000)
        {
            Show(title, message, ToastType.Error, duration);
        }

        public static void ShowInfo(string title, string message = "", int duration = 3000)
        {
            Show(title, message, ToastType.Info, duration);
        }
    }
}

