using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Common.Controls
{
    public class BreathingLight : ContentControl
    {
        #region 全局同步时钟（屏幕刷新率同步）

        private static bool _isRenderingHooked = false;
        private static DateTime _startTime;
        private static event Action<double>? GlobalTick;
        private static int _activeCount = 0;

        static BreathingLight()
        {
            _startTime = DateTime.Now;
        }

        private static void EnsureRenderingHooked()
        {
            if (!_isRenderingHooked)
            {
                _isRenderingHooked = true;
                _startTime = DateTime.Now;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private static void UnhookRenderingIfNeeded()
        {
            if (_isRenderingHooked && _activeCount <= 0)
            {
                _isRenderingHooked = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.Now - _startTime).TotalSeconds;
            GlobalTick?.Invoke(elapsed);
        }

        #endregion

        #region 依赖属性

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(BreathingLight),
                new PropertyMetadata(false, OnIsActiveChanged));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(double), typeof(BreathingLight),
                new PropertyMetadata(2.8, OnDurationChanged));

        public double Duration
        {
            get => (double)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BreathingLight light)
            {
                light._duration = (double)e.NewValue;
            }
        }

        public static readonly DependencyProperty MinOpacityProperty =
            DependencyProperty.Register(nameof(MinOpacity), typeof(double), typeof(BreathingLight),
                new PropertyMetadata(0.3));

        public double MinOpacity
        {
            get => (double)GetValue(MinOpacityProperty);
            set => SetValue(MinOpacityProperty, value);
        }

        public static readonly DependencyProperty MaxOpacityProperty =
            DependencyProperty.Register(nameof(MaxOpacity), typeof(double), typeof(BreathingLight),
                new PropertyMetadata(1.0));

        public double MaxOpacity
        {
            get => (double)GetValue(MaxOpacityProperty);
            set => SetValue(MaxOpacityProperty, value);
        }

        public static readonly DependencyProperty EnableScaleProperty =
            DependencyProperty.Register(nameof(EnableScale), typeof(bool), typeof(BreathingLight),
                new PropertyMetadata(false));

        public bool EnableScale
        {
            get => (bool)GetValue(EnableScaleProperty);
            set => SetValue(EnableScaleProperty, value);
        }

        public static readonly DependencyProperty MinScaleProperty =
            DependencyProperty.Register(nameof(MinScale), typeof(double), typeof(BreathingLight),
                new PropertyMetadata(1.0));

        public double MinScale
        {
            get => (double)GetValue(MinScaleProperty);
            set => SetValue(MinScaleProperty, value);
        }

        public static readonly DependencyProperty MaxScaleProperty =
            DependencyProperty.Register(nameof(MaxScale), typeof(double), typeof(BreathingLight),
                new PropertyMetadata(1.2));

        public double MaxScale
        {
            get => (double)GetValue(MaxScaleProperty);
            set => SetValue(MaxScaleProperty, value);
        }

        #endregion

        private ScaleTransform? _scaleTransform;

        public BreathingLight()
        {
            RenderTransformOrigin = new Point(0.5, 0.5);
            _scaleTransform = new ScaleTransform(1, 1);
            RenderTransform = _scaleTransform;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsActive)
            {
                SubscribeToGlobalTick();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromGlobalTick();
        }

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BreathingLight light)
            {
                if ((bool)e.NewValue)
                {
                    light.SubscribeToGlobalTick();
                }
                else
                {
                    light.UnsubscribeFromGlobalTick();
                    light.ResetToDefault();
                }
            }
        }

        private bool _isSubscribed = false;
        private double _duration = 2.8;

        private void SubscribeToGlobalTick()
        {
            if (_isSubscribed) return;
            _isSubscribed = true;
            _activeCount++;
            EnsureRenderingHooked();
            GlobalTick += OnGlobalTick;
        }

        private void UnsubscribeFromGlobalTick()
        {
            if (!_isSubscribed) return;
            _isSubscribed = false;
            GlobalTick -= OnGlobalTick;
            _activeCount--;
            UnhookRenderingIfNeeded();
        }

        private void OnGlobalTick(double elapsedSeconds)
        {
            if (!IsActive) return;

            var phase = elapsedSeconds * (2 * Math.PI / _duration);
            var sineValue = (Math.Sin(phase) + 1) / 2;

            var opacity = MinOpacity + sineValue * (MaxOpacity - MinOpacity);
            Opacity = opacity;

            if (EnableScale && _scaleTransform != null)
            {
                var scale = MinScale + sineValue * (MaxScale - MinScale);
                _scaleTransform.ScaleX = scale;
                _scaleTransform.ScaleY = scale;
            }
        }

        private void ResetToDefault()
        {
            Opacity = MaxOpacity;
            if (_scaleTransform != null)
            {
                _scaleTransform.ScaleX = 1;
                _scaleTransform.ScaleY = 1;
            }
        }
    }
}
