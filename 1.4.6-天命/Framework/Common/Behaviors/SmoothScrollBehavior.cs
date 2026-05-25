using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace TM.Framework.Common.Behaviors
{
    public static class SmoothScrollBehavior
    {
        #region 附加属性定义

        public static readonly DependencyProperty EnableSmoothScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableSmoothScroll",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(false, OnEnableSmoothScrollChanged));

        public static bool GetEnableSmoothScroll(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableSmoothScrollProperty);
        }

        public static void SetEnableSmoothScroll(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableSmoothScrollProperty, value);
        }

        public static readonly DependencyProperty ScrollIncrementProperty =
            DependencyProperty.RegisterAttached(
                "ScrollIncrement",
                typeof(double),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(48.0));

        public static double GetScrollIncrement(DependencyObject obj)
        {
            return (double)obj.GetValue(ScrollIncrementProperty);
        }

        public static void SetScrollIncrement(DependencyObject obj, double value)
        {
            obj.SetValue(ScrollIncrementProperty, value);
        }

        public static readonly DependencyProperty AnimationDurationProperty =
            DependencyProperty.RegisterAttached(
                "AnimationDuration",
                typeof(double),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(200.0));

        public static double GetAnimationDuration(DependencyObject obj)
        {
            return (double)obj.GetValue(AnimationDurationProperty);
        }

        public static void SetAnimationDuration(DependencyObject obj, double value)
        {
            obj.SetValue(AnimationDurationProperty, value);
        }

        #endregion

        #region 事件处理

        private static void OnEnableSmoothScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
                    scrollViewer.Loaded += OnScrollViewerLoaded;
                }
                else
                {
                    scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                    scrollViewer.Loaded -= OnScrollViewerLoaded;
                }
            }
        }

        private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.CanContentScroll = false;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && !e.Handled)
            {
                double increment = GetScrollIncrement(scrollViewer);
                double duration = GetAnimationDuration(scrollViewer);

                double delta = e.Delta > 0 ? -increment : increment;
                double targetOffset = scrollViewer.VerticalOffset + delta;

                targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, targetOffset));

                var animation = new DoubleAnimation
                {
                    From = scrollViewer.VerticalOffset,
                    To = targetOffset,
                    Duration = TimeSpan.FromMilliseconds(duration),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);

                Storyboard.SetTarget(animation, scrollViewer);
                Storyboard.SetTargetProperty(animation, new PropertyPath(ScrollViewerBehavior.VerticalOffsetProperty));

                storyboard.Begin();

                e.Handled = true;
            }
        }

        #endregion
    }

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollViewerBehavior),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static double GetVerticalOffset(DependencyObject obj)
        {
            return (double)obj.GetValue(VerticalOffsetProperty);
        }

        public static void SetVerticalOffset(DependencyObject obj, double value)
        {
            obj.SetValue(VerticalOffsetProperty, value);
        }

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
    }
}

