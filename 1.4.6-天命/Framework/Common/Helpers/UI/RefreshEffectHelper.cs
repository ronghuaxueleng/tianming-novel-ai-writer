using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class RefreshEffectHelper
    {
        #region EnableRefreshEffect 附加属性（挂在 ContextMenu 上）

        public static readonly DependencyProperty EnableRefreshEffectProperty =
            DependencyProperty.RegisterAttached(
                "EnableRefreshEffect",
                typeof(bool),
                typeof(RefreshEffectHelper),
                new PropertyMetadata(false, OnEnableRefreshEffectChanged));

        public static void SetEnableRefreshEffect(DependencyObject element, bool value)
            => element.SetValue(EnableRefreshEffectProperty, value);

        public static bool GetEnableRefreshEffect(DependencyObject element)
            => (bool)element.GetValue(EnableRefreshEffectProperty);

        private static void OnEnableRefreshEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ContextMenu menu)
            {
                return;
            }

            if (e.NewValue is bool enabled && enabled)
            {
                menu.Opened += OnContextMenuOpened;
            }
            else
            {
                menu.Opened -= OnContextMenuOpened;
            }
        }

        #endregion

        #region 内部：标记已挂接刷新效果的菜单项

        private static readonly DependencyProperty IsRefreshEffectAttachedProperty =
            DependencyProperty.RegisterAttached(
                "IsRefreshEffectAttached",
                typeof(bool),
                typeof(RefreshEffectHelper),
                new PropertyMetadata(false));

        private static void SetIsRefreshEffectAttached(DependencyObject element, bool value)
            => element.SetValue(IsRefreshEffectAttachedProperty, value);

        private static bool GetIsRefreshEffectAttached(DependencyObject element)
            => (bool)element.GetValue(IsRefreshEffectAttachedProperty);

        #endregion

        private static void OnContextMenuOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
            {
                return;
            }

            AttachRefreshEffectToMenu(menu);
        }

        private static void AttachRefreshEffectToMenu(ContextMenu menu)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    AttachRefreshEffectToMenuItemRecursive(menuItem);
                }
            }
        }

        private static void AttachRefreshEffectToMenuItemRecursive(MenuItem menuItem)
        {
            if (!GetIsRefreshEffectAttached(menuItem) && IsRefreshHeader(menuItem.Header))
            {
                SetIsRefreshEffectAttached(menuItem, true);
                menuItem.Click += OnRefreshMenuItemClick;
            }

            if (menuItem.HasItems)
            {
                foreach (var child in menuItem.Items)
                {
                    if (child is MenuItem childItem)
                    {
                        AttachRefreshEffectToMenuItemRecursive(childItem);
                    }
                }
            }
        }

        private static bool IsRefreshHeader(object? header)
        {
            if (header is string s)
            {
                return s.Contains("刷新", StringComparison.Ordinal);
            }

            return false;
        }

        private static void OnRefreshMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            var menu = FindParentContextMenu(menuItem);
            if (menu?.PlacementTarget is not FrameworkElement target)
            {
                return;
            }

            PlayRefreshAnimation(target);
        }

        private static ContextMenu? FindParentContextMenu(DependencyObject? source)
        {
            while (source != null && source is not ContextMenu)
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source) ??
                         System.Windows.LogicalTreeHelper.GetParent(source);
            }

            return source as ContextMenu;
        }

        private static void PlayRefreshAnimation(FrameworkElement target)
        {
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.7,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(1)
            };

            target.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }
}
