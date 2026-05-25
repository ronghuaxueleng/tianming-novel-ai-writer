using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Data;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class ComboBoxHelper
    {
        #region TreeMode - 树形模式

        public static readonly DependencyProperty TreeModeProperty =
            DependencyProperty.RegisterAttached(
                "TreeMode",
                typeof(bool),
                typeof(ComboBoxHelper),
                new PropertyMetadata(false));

        public static bool GetTreeMode(DependencyObject obj)
        {
            return (bool)obj.GetValue(TreeModeProperty);
        }

        public static void SetTreeMode(DependencyObject obj, bool value)
        {
            obj.SetValue(TreeModeProperty, value);
        }

        #endregion

        #region FilterEmptyStringItems - 过滤空白字符串项（防脏）

        public static readonly DependencyProperty FilterEmptyStringItemsProperty =
            DependencyProperty.RegisterAttached(
                "FilterEmptyStringItems",
                typeof(bool),
                typeof(ComboBoxHelper),
                new PropertyMetadata(false, OnFilterEmptyStringItemsChanged));

        public static bool GetFilterEmptyStringItems(DependencyObject obj)
        {
            return (bool)obj.GetValue(FilterEmptyStringItemsProperty);
        }

        public static void SetFilterEmptyStringItems(DependencyObject obj, bool value)
        {
            obj.SetValue(FilterEmptyStringItemsProperty, value);
        }

        private static readonly DependencyProperty ItemsSourceWatcherProperty =
            DependencyProperty.RegisterAttached(
                "ItemsSourceWatcher",
                typeof(IDisposable),
                typeof(ComboBoxHelper),
                new PropertyMetadata(null));

        private static IDisposable? GetItemsSourceWatcher(DependencyObject obj)
        {
            return (IDisposable?)obj.GetValue(ItemsSourceWatcherProperty);
        }

        private static void SetItemsSourceWatcher(DependencyObject obj, IDisposable? value)
        {
            obj.SetValue(ItemsSourceWatcherProperty, value);
        }

        private static readonly DependencyProperty ItemsSourceCollectionWatcherProperty =
            DependencyProperty.RegisterAttached(
                "ItemsSourceCollectionWatcher",
                typeof(IDisposable),
                typeof(ComboBoxHelper),
                new PropertyMetadata(null));

        private static IDisposable? GetItemsSourceCollectionWatcher(DependencyObject obj)
        {
            return (IDisposable?)obj.GetValue(ItemsSourceCollectionWatcherProperty);
        }

        private static void SetItemsSourceCollectionWatcher(DependencyObject obj, IDisposable? value)
        {
            obj.SetValue(ItemsSourceCollectionWatcherProperty, value);
        }

        private static readonly DependencyProperty OriginalViewFilterProperty =
            DependencyProperty.RegisterAttached(
                "OriginalViewFilter",
                typeof(Predicate<object>),
                typeof(ComboBoxHelper),
                new PropertyMetadata(null));

        private static Predicate<object>? GetOriginalViewFilter(DependencyObject obj)
        {
            return (Predicate<object>?)obj.GetValue(OriginalViewFilterProperty);
        }

        private static void SetOriginalViewFilter(DependencyObject obj, Predicate<object>? value)
        {
            obj.SetValue(OriginalViewFilterProperty, value);
        }

        private static void OnFilterEmptyStringItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox comboBox)
                return;

            var enabled = (bool)e.NewValue;
            if (!enabled)
            {
                RemoveEmptyStringFilter(comboBox);
                return;
            }

            comboBox.Loaded -= ComboBoxOnLoadedForEmptyStringFilter;
            comboBox.Loaded += ComboBoxOnLoadedForEmptyStringFilter;
            comboBox.Unloaded -= ComboBoxOnUnloadedForEmptyStringFilter;
            comboBox.Unloaded += ComboBoxOnUnloadedForEmptyStringFilter;

            if (comboBox.IsLoaded)
            {
                AttachEmptyStringFilter(comboBox);
            }
        }

        private static void ComboBoxOnLoadedForEmptyStringFilter(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && GetFilterEmptyStringItems(comboBox))
            {
                AttachEmptyStringFilter(comboBox);
            }
        }

        private static void ComboBoxOnUnloadedForEmptyStringFilter(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                RemoveEmptyStringFilter(comboBox);
            }
        }

        private static void AttachEmptyStringFilter(ComboBox comboBox)
        {
            GetItemsSourceWatcher(comboBox)?.Dispose();
            SetItemsSourceWatcher(comboBox, null);

            var descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl));
            if (descriptor != null)
            {
                EventHandler handler = (_, _) => ApplyEmptyStringFilter(comboBox);
                descriptor.AddValueChanged(comboBox, handler);
                SetItemsSourceWatcher(comboBox, new DelegateDisposable(() => descriptor.RemoveValueChanged(comboBox, handler)));
            }

            ApplyEmptyStringFilter(comboBox);
        }

        private static void RemoveEmptyStringFilter(ComboBox comboBox)
        {
            GetItemsSourceWatcher(comboBox)?.Dispose();
            SetItemsSourceWatcher(comboBox, null);
            GetItemsSourceCollectionWatcher(comboBox)?.Dispose();
            SetItemsSourceCollectionWatcher(comboBox, null);

            if (comboBox.ItemsSource == null)
                return;

            var view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
            if (view == null)
                return;

            var original = GetOriginalViewFilter(comboBox);
            if (original != null)
            {
                view.Filter = original;
            }

            SetOriginalViewFilter(comboBox, null);
            if (view is IEditableCollectionView editable)
            {
                editable.NewItemPlaceholderPosition = NewItemPlaceholderPosition.None;
            }

            view.Refresh();
        }

        private static void ApplyEmptyStringFilter(ComboBox comboBox)
        {
            GetItemsSourceCollectionWatcher(comboBox)?.Dispose();
            SetItemsSourceCollectionWatcher(comboBox, null);

            if (comboBox.ItemsSource == null)
                return;

            var view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
            if (view == null)
                return;

            if (view is IEditableCollectionView editable)
            {
                editable.NewItemPlaceholderPosition = NewItemPlaceholderPosition.None;
            }

            var original = GetOriginalViewFilter(comboBox);
            if (original == null)
            {
                if (view.Filter is Predicate<object> existing)
                {
                    SetOriginalViewFilter(comboBox, existing);
                    original = existing;
                }
                else
                {
                    original = _ => true;
                    SetOriginalViewFilter(comboBox, original);
                }
            }

            view.Filter = item =>
            {
                if (!original(item)) return false;

                if (item == null) return false;

                if (item is string s)
                    return !string.IsNullOrWhiteSpace(s);

                return true;
            };

            if (comboBox.ItemsSource is INotifyCollectionChanged incc)
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => view.Refresh();
                incc.CollectionChanged += handler;
                SetItemsSourceCollectionWatcher(comboBox, new DelegateDisposable(() => incc.CollectionChanged -= handler));
            }

            view.Refresh();
        }

        private sealed class DelegateDisposable : IDisposable
        {
            private readonly Action _dispose;
            private bool _isDisposed;

            public DelegateDisposable(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _dispose();
            }
        }

        #endregion

        #region MaxLevel - 最大层级限制

        public static readonly DependencyProperty MaxLevelProperty =
            DependencyProperty.RegisterAttached(
                "MaxLevel",
                typeof(int),
                typeof(ComboBoxHelper),
                new PropertyMetadata(5));

        public static int GetMaxLevel(DependencyObject obj)
        {
            return (int)obj.GetValue(MaxLevelProperty);
        }

        public static void SetMaxLevel(DependencyObject obj, int value)
        {
            obj.SetValue(MaxLevelProperty, value);
        }

        #endregion

        #region SelectedPath - 选中路径

        public static readonly DependencyProperty SelectedPathProperty =
            DependencyProperty.RegisterAttached(
                "SelectedPath",
                typeof(string),
                typeof(ComboBoxHelper),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedPathChanged));

        public static string GetSelectedPath(DependencyObject obj)
        {
            return (string)obj.GetValue(SelectedPathProperty);
        }

        public static void SetSelectedPath(DependencyObject obj, string value)
        {
            obj.SetValue(SelectedPathProperty, value);
        }

        private static void OnSelectedPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBox comboBox)
            {
                var newPath = e.NewValue as string;
                SetDisplayText(comboBox, newPath ?? string.Empty);
                TM.App.Log($"[ComboBoxHelper] SelectedPath变化: '{newPath}' -> DisplayText已更新");
            }
        }

        #endregion

        #region DisplayText - 显示文本（内部使用）

        public static readonly DependencyProperty DisplayTextProperty =
            DependencyProperty.RegisterAttached(
                "DisplayText",
                typeof(string),
                typeof(ComboBoxHelper),
                new FrameworkPropertyMetadata(string.Empty));

        public static string GetDisplayText(DependencyObject obj)
        {
            return (string)obj.GetValue(DisplayTextProperty);
        }

        public static void SetDisplayText(DependencyObject obj, string value)
        {
            obj.SetValue(DisplayTextProperty, value);
        }

        #endregion

        #region DisplayIcon - 显示图标（内部使用）

        public static readonly DependencyProperty DisplayIconProperty =
            DependencyProperty.RegisterAttached(
                "DisplayIcon",
                typeof(string),
                typeof(ComboBoxHelper),
                new FrameworkPropertyMetadata(string.Empty));

        public static string GetDisplayIcon(DependencyObject obj)
        {
            return (string)obj.GetValue(DisplayIconProperty);
        }

        public static void SetDisplayIcon(DependencyObject obj, string value)
        {
            obj.SetValue(DisplayIconProperty, value);
        }

        #endregion

        #region AutoDetectTreeMode - 自动检测树形模式

        public static readonly DependencyProperty AutoDetectTreeModeProperty =
            DependencyProperty.RegisterAttached(
                "AutoDetectTreeMode",
                typeof(bool),
                typeof(ComboBoxHelper),
                new PropertyMetadata(true, OnAutoDetectTreeModeChanged));

        public static bool GetAutoDetectTreeMode(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoDetectTreeModeProperty);
        }

        public static void SetAutoDetectTreeMode(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoDetectTreeModeProperty, value);
        }

        private static void OnAutoDetectTreeModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBox comboBox && (bool)e.NewValue)
            {
                comboBox.Loaded += (s, args) =>
                {
                    DetectAndSetTreeMode(comboBox);
                };
            }
        }

        private static void DetectAndSetTreeMode(ComboBox comboBox)
        {
            if (comboBox.ItemsSource != null)
            {
                var enumerator = comboBox.ItemsSource.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    var firstItem = enumerator.Current;
                    if (firstItem is TM.Framework.Common.Controls.TreeNodeItem)
                    {
                        SetTreeMode(comboBox, true);
                        TM.App.Log($"[ComboBoxHelper] 自动检测到树形数据，启用TreeMode");
                    }
                }
            }
        }

        #endregion

        #region NodeDoubleClickCommand - 节点双击命令

        public static readonly DependencyProperty NodeDoubleClickCommandProperty =
            DependencyProperty.RegisterAttached(
                "NodeDoubleClickCommand",
                typeof(System.Windows.Input.ICommand),
                typeof(ComboBoxHelper),
                new PropertyMetadata(null));

        public static System.Windows.Input.ICommand GetNodeDoubleClickCommand(DependencyObject obj)
        {
            return (System.Windows.Input.ICommand)obj.GetValue(NodeDoubleClickCommandProperty);
        }

        public static void SetNodeDoubleClickCommand(DependencyObject obj, System.Windows.Input.ICommand value)
        {
            obj.SetValue(NodeDoubleClickCommandProperty, value);
        }

        #endregion
    }
}

