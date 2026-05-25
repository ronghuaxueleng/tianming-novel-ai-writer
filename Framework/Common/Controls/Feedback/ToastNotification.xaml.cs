using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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
    public sealed class ToastItem
    {
        public string Title { get; }
        public string Message { get; }
        public Visibility MessageVisibility { get; }
        public ImageSource? Icon { get; }
        public Brush Background { get; }
        public Brush MessageForeground { get; }
        public CornerRadius CornerRadius { get; }
        public Thickness BorderThick { get; }
        public double ShadowBlur { get; }
        public double MinItemHeight { get; }
        public double MaxItemHeight { get; }
        public Thickness ItemMargin { get; }
        public string DedupeKey { get; }
        public ICommand CloseCommand { get; }

        internal bool IsRemoving;
        private DispatcherTimer? _timer;
        private readonly Action<ToastItem> _removeAction;

        public ToastItem(string title, string message, ImageSource? icon, Brush background,
                         Brush messageForeground, CornerRadius cornerRadius, Thickness borderThick,
                         double shadowBlur, double minHeight, double maxHeight, Thickness itemMargin,
                         string dedupeKey, int duration, Action<ToastItem> removeAction)
        {
            Title = title;
            Message = message;
            MessageVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            Icon = icon;
            Background = background;
            MessageForeground = messageForeground;
            CornerRadius = cornerRadius;
            BorderThick = borderThick;
            ShadowBlur = shadowBlur;
            MinItemHeight = minHeight;
            MaxItemHeight = maxHeight;
            ItemMargin = itemMargin;
            DedupeKey = dedupeKey;
            _removeAction = removeAction;
            CloseCommand = new RelayCloseCommand(() => _removeAction(this));

            if (duration > 0)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) };
                _timer.Tick += (_, _) => { _timer?.Stop(); _removeAction(this); };
                _timer.Start();
            }
        }

        public void StopTimer() => _timer?.Stop();

        private sealed class RelayCloseCommand : ICommand
        {
            private readonly Action _execute;
            public RelayCloseCommand(Action execute) => _execute = execute;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute();
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ToastNotification : Window
    {
        private static ToastNotification? _instance;
        private static readonly ObservableCollection<ToastItem> _items = new();
        private static readonly HashSet<string> _activeKeys = new();
        private static readonly object _lock = new();
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static readonly QuadraticEase _fadeEaseIn = CreateFrozenEase(EasingMode.EaseIn);
        private static readonly QuadraticEase _fadeEaseOut = CreateFrozenEase(EasingMode.EaseOut);
        private static readonly QuadraticEase _fadeEaseInOut = CreateFrozenEase(EasingMode.EaseInOut);
        private static QuadraticEase CreateFrozenEase(EasingMode mode)
        {
            var ease = new QuadraticEase { EasingMode = mode };
            ease.Freeze();
            return ease;
        }

        private static readonly Color _successBgColor = (Color)ColorConverter.ConvertFromString("#10B981");
        private static readonly Color _warningBgColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
        private static readonly Color _errorBgColor = (Color)ColorConverter.ConvertFromString("#EF4444");
        private static readonly Color _infoBgColor = (Color)ColorConverter.ConvertFromString("#3B82F6");
        private static readonly SolidColorBrush _messageForegroundBrush;
        private static readonly ConcurrentDictionary<string, Color> _colorCache = new();
        private static readonly ConcurrentDictionary<(Color, double), SolidColorBrush> _frozenBrushCache = new();
        private static readonly BounceEase _bounceEaseOut;

        private static readonly ConcurrentDictionary<long, DoubleAnimation> _fadeInTemplateCache = new();
        private static readonly DoubleAnimation _fadeOutTemplate;

        private static TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings? _cachedStyleSettings;
        private static TM.Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypeSettings? _cachedTypeSettings;

        static ToastNotification()
        {
            var b = new SolidColorBrush(Colors.White) { Opacity = 0.9 };
            b.Freeze();
            _messageForegroundBrush = b;

            var bounce = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2 };
            bounce.Freeze();
            _bounceEaseOut = bounce;

            var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fo.Freeze();
            _fadeOutTemplate = fo;
        }

        private static DoubleAnimation GetOrCreateFadeInTemplate(int duration, IEasingFunction? ease)
        {
            long key = (long)duration << 32 | (uint)(ease?.GetHashCode() ?? 0);
            return _fadeInTemplateCache.GetOrAdd(key, _ =>
            {
                var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease };
                a.Freeze();
                return a;
            });
        }

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode) return;
            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key)) return;
            }
            System.Diagnostics.Debug.WriteLine($"[ToastNotification] {key}: {ex.Message}");
        }

        private ToastNotification()
        {
            InitializeComponent();
            DataContext = _items;

            _items.CollectionChanged += (_, _) =>
            {
                if (_items.Count == 0)
                    Hide();
                else
                    Dispatcher.InvokeAsync(PositionContainer, DispatcherPriority.Background);
            };
        }

        private void PositionContainer()
        {
            if (!IsLoaded) return;

            UpdateLayout();

            var workArea = SystemParameters.WorkArea;
            var settings = GetStyleSettings();
            double edgeMargin = 10;
            double topMargin = 10;

            switch (settings.ScreenPosition)
            {
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopRight:
                    Left = workArea.Right - ActualWidth - edgeMargin;
                    Top = workArea.Top + topMargin;
                    break;
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.TopLeft:
                    Left = workArea.Left + edgeMargin;
                    Top = workArea.Top + topMargin;
                    break;
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomRight:
                    Left = workArea.Right - ActualWidth - edgeMargin;
                    Top = workArea.Bottom - ActualHeight - topMargin;
                    break;
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomLeft:
                    Left = workArea.Left + edgeMargin;
                    Top = workArea.Bottom - ActualHeight - topMargin;
                    break;
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.Center:
                    Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
                    double firstH = ActualHeight;
                    if (_items.Count > 0)
                    {
                        var fc = ToastList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                        if (fc != null) firstH = fc.ActualHeight;
                    }
                    Top = workArea.Top + (workArea.Height - firstH) / 2;
                    break;
            }
        }

        private static ToastNotification EnsureInstance()
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new ToastNotification();
                var settings = GetStyleSettings();
                _instance.Width = settings.NotificationWidth;
                _instance.Loaded += (_, _) => _instance.PositionContainer();
                _instance.SizeChanged += (_, _) => _instance.PositionContainer();
            }
            return _instance;
        }

        private void PlayItemFadeIn(ToastItem item)
        {
            var container = ToastList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (container == null) return;

            var settings = GetStyleSettings();
            int duration = settings.AnimationDuration;
            IEasingFunction? easingFunc = ResolveEasingFunction(settings.EasingFunction);

            switch (settings.AnimationType)
            {
                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.FadeInOut:
                    container.BeginAnimation(OpacityProperty, GetOrCreateFadeInTemplate(duration, easingFunc));
                    break;

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.SlideIn:
                    {
                        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = easingFunc };
                        fadeAnim.Freeze();
                        container.BeginAnimation(OpacityProperty, fadeAnim);

                        var tt = new TranslateTransform(50, 0);
                        container.RenderTransform = tt;
                        var slideAnim = new DoubleAnimation(50, 0, TimeSpan.FromMilliseconds(duration)) { EasingFunction = easingFunc };
                        slideAnim.Freeze();
                        tt.BeginAnimation(TranslateTransform.XProperty, slideAnim);
                        break;
                    }

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.Bounce:
                    container.BeginAnimation(OpacityProperty, GetOrCreateFadeInTemplate(duration, _bounceEaseOut));
                    break;

                case TM.Framework.Notifications.SystemNotifications.NotificationStyle.AnimationType.Scale:
                    {
                        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = easingFunc };
                        fadeAnim.Freeze();
                        container.BeginAnimation(OpacityProperty, fadeAnim);

                        var st = new ScaleTransform(0.8, 0.8);
                        container.RenderTransform = st;
                        container.RenderTransformOrigin = new Point(0.5, 0.5);

                        var scaleX = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = easingFunc };
                        scaleX.Freeze();
                        var scaleY = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = easingFunc };
                        scaleY.Freeze();
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                        break;
                    }
            }
        }

        private static IEasingFunction? ResolveEasingFunction(
            TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction ef)
        {
            return ef switch
            {
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.Linear => null,
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseIn => _fadeEaseIn,
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseOut => _fadeEaseOut,
                TM.Framework.Notifications.SystemNotifications.NotificationStyle.EasingFunction.EaseInOut => _fadeEaseInOut,
                _ => _fadeEaseOut
            };
        }

        private static Dictionary<ToastItem, double> CaptureItemPositions(ToastItem excludeItem)
        {
            var positions = new Dictionary<ToastItem, double>();
            if (_instance?.ToastList == null) return positions;

            foreach (var item in _items)
            {
                if (item == excludeItem) continue;
                var c = _instance.ToastList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                if (c != null)
                {
                    var pt = c.TranslatePoint(new Point(0, 0), _instance.ToastList);
                    positions[item] = pt.Y;
                }
            }
            return positions;
        }

        private static void AnimateRepositionAfterRemoval(Dictionary<ToastItem, double> oldPositions)
        {
            if (_instance?.ToastList == null || oldPositions.Count == 0) return;

            _instance.UpdateLayout();

            foreach (var kvp in oldPositions)
            {
                var c = _instance.ToastList.ItemContainerGenerator.ContainerFromItem(kvp.Key) as ContentPresenter;
                if (c == null) continue;

                double newY = c.TranslatePoint(new Point(0, 0), _instance.ToastList).Y;
                double delta = kvp.Value - newY;
                if (Math.Abs(delta) < 1) continue;

                var tt = new TranslateTransform(0, delta);
                c.RenderTransform = tt;

                var anim = new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = _fadeEaseOut };
                anim.Freeze();
                tt.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        private static void RemoveItem(ToastItem item)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (item.IsRemoving) return;
                item.IsRemoving = true;
                item.StopTimer();

                var oldPositions = CaptureItemPositions(item);

                if (_instance?.ToastList != null)
                {
                    var container = _instance.ToastList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                    if (container != null)
                    {
                        var fadeOut = _fadeOutTemplate.Clone();
                        fadeOut.Completed += (_, _) =>
                        {
                            FinalRemove(item);
                            AnimateRepositionAfterRemoval(oldPositions);
                        };
                        container.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                        return;
                    }
                }

                FinalRemove(item);
                AnimateRepositionAfterRemoval(oldPositions);
            });
        }

        private static void FinalRemove(ToastItem item)
        {
            lock (_lock) { _activeKeys.Remove(item.DedupeKey); }
            _items.Remove(item);
        }

        public static void ClearAll()
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    foreach (var item in _items)
                    {
                        item.StopTimer();
                    }
                    _activeKeys.Clear();
                    _items.Clear();
                }
                _instance?.Hide();
            }, DispatcherPriority.Background);
        }

        private static (ImageSource? icon, SolidColorBrush bg) ResolveTypeStyle(ToastType type, double opacity)
        {
            try
            {
                _cachedTypeSettings ??= ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypeSettings>();
                var types = _cachedTypeSettings.LoadSettings();
                string typeId = type switch
                {
                    ToastType.Success => "success",
                    ToastType.Warning => "warning",
                    ToastType.Error => "error",
                    _ => "info"
                };
                var typeData = types.FirstOrDefault(t => t.Id == typeId);
                if (typeData != null && typeData.IsEnabled)
                {
                    var icon = TM.Framework.Common.Helpers.IconHelper.TryGet(typeData.Icon);
                    var color = _colorCache.GetOrAdd(typeData.Color, s => (Color)ColorConverter.ConvertFromString(s));
                    return (icon, GetOrCreateFrozenBrush(color, opacity));
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ToastNotification] 无法加载类型配置: {ex.Message}，使用默认样式");
            }

            return type switch
            {
                ToastType.Success => (TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.CheckCircle"), GetOrCreateFrozenBrush(_successBgColor, opacity)),
                ToastType.Warning => (TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Warning"), GetOrCreateFrozenBrush(_warningBgColor, opacity)),
                ToastType.Error => (TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Forbidden"), GetOrCreateFrozenBrush(_errorBgColor, opacity)),
                _ => (TM.Framework.Common.Helpers.IconHelper.TryGet("Icon.Info"), GetOrCreateFrozenBrush(_infoBgColor, opacity)),
            };
        }

        private static SolidColorBrush GetOrCreateFrozenBrush(Color color, double opacity)
        {
            return _frozenBrushCache.GetOrAdd((color, opacity), key =>
            {
                var b = new SolidColorBrush(key.Item1) { Opacity = key.Item2 };
                b.Freeze();
                return b;
            });
        }

        private static TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings GetStyleSettings()
        {
            return _cachedStyleSettings ??= ServiceLocator.Get<TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();
        }

        public static void PreWarm()
        {
            try { EnsureInstance(); }
            catch { }
        }

        public static void Show(string title, string message = "", ToastType type = ToastType.Info, int duration = 3000, bool isHighPriority = false)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var dndSettings = ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings>();
                bool isBlocked = dndSettings.IsCurrentlyActive;

                string typeStr = type switch
                {
                    ToastType.Success => "成功",
                    ToastType.Warning => "警告",
                    ToastType.Error => "错误",
                    ToastType.Info => "信息",
                    _ => "信息"
                };
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var historySettings = ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistorySettings>();
                        historySettings.AddRecord(title, message, typeStr, isBlocked);
                    }
                    catch (Exception ex) { App.Log($"[ToastNotification] 历史记录写入失败: {ex.Message}"); }
                });

                if (isBlocked)
                {
                    App.Log($"[ToastNotification] 免打扰已拦截通知: {title}");
                    return;
                }

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await ServiceLocator.Get<TM.Services.Framework.Notification.NotificationSoundService>().PlayNotificationSound(type, isHighPriority);
                    }
                    catch (Exception ex) { App.Log($"[ToastNotification] 音效播放失败: {ex.Message}"); }
                });

                string dedupeKey = $"{type}|{title}|{message}";
                lock (_lock)
                {
                    if (_items.Count >= 5) return;
                    if (!_activeKeys.Add(dedupeKey)) return;
                }

                var settings = GetStyleSettings();
                double opacity = settings.BackgroundOpacity / 100.0;
                double spacing = settings.NotificationSpacing;
                var (icon, bg) = ResolveTypeStyle(type, opacity);

                var item = new ToastItem(
                    title, message, icon, bg, _messageForegroundBrush,
                    new CornerRadius(settings.CornerRadius),
                    new Thickness(settings.BorderThickness),
                    settings.ShadowIntensity,
                    settings.NotificationHeight,
                    Math.Max(settings.NotificationHeight * 3, 200),
                    new Thickness(8, spacing / 2, 8, spacing / 2),
                    dedupeKey, duration, RemoveItem);

                bool isBottom = settings.ScreenPosition == TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomRight
                             || settings.ScreenPosition == TM.Framework.Notifications.SystemNotifications.NotificationStyle.ScreenPosition.BottomLeft;
                if (isBottom)
                    _items.Insert(0, item);
                else
                    _items.Add(item);

                var container = EnsureInstance();
                if (!container.IsVisible)
                    container.Show();

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _instance?.PlayItemFadeIn(item);
                }, DispatcherPriority.Background);

            }, DispatcherPriority.Background);
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

