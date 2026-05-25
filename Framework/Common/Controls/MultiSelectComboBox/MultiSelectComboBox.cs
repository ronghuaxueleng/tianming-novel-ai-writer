using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    [TemplatePart(Name = PartItemsHost, Type = typeof(ItemsControl))]
    [TemplatePart(Name = PartToggleButton, Type = typeof(ToggleButton))]
    [TemplatePart(Name = PartPopup, Type = typeof(Popup))]
    public class MultiSelectComboBox : Control
    {
        private const string PartItemsHost = "PART_ItemsHost";
        private const string PartToggleButton = "PART_ToggleButton";
        private const string PartPopup = "PART_Popup";

        static MultiSelectComboBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MultiSelectComboBox),
                new FrameworkPropertyMetadata(typeof(MultiSelectComboBox)));
        }

        private readonly ObservableCollection<ItemWrapper> _wrappers = new();
        private bool _suppressSelectedItemsChanged;
        private ItemsControl? _itemsHost;

        public ObservableCollection<ItemWrapper> Wrappers => _wrappers;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _itemsHost = GetTemplateChild(PartItemsHost) as ItemsControl;
            if (_itemsHost != null)
            {
                _itemsHost.ItemsSource = _wrappers;
            }
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(MultiSelectComboBox),
                new FrameworkPropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MultiSelectComboBox c)
            {
                if (e.OldValue is INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= c.OnItemsSourceCollectionChanged;
                if (e.NewValue is INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += c.OnItemsSourceCollectionChanged;
                c.RebuildWrappers();
            }
        }

        private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildWrappers();
        }

        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.Register(
                nameof(SelectedItems),
                typeof(IList),
                typeof(MultiSelectComboBox),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemsChanged));

        public IList? SelectedItems
        {
            get => (IList?)GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MultiSelectComboBox c)
            {
                if (e.OldValue is INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= c.OnExternalSelectedItemsChanged;
                if (e.NewValue is INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += c.OnExternalSelectedItemsChanged;
                c.SyncWrappersFromSelectedItems();
            }
        }

        private void OnExternalSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSelectedItemsChanged) return;
            SyncWrappersFromSelectedItems();
        }

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register(
                nameof(DisplayMemberPath),
                typeof(string),
                typeof(MultiSelectComboBox),
                new FrameworkPropertyMetadata(string.Empty, OnDisplayDependencyChanged));

        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(
                nameof(ItemTemplate),
                typeof(DataTemplate),
                typeof(MultiSelectComboBox),
                new PropertyMetadata(null));

        public DataTemplate? ItemTemplate
        {
            get => (DataTemplate?)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(MultiSelectComboBox),
                new PropertyMetadata("请选择"));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public static readonly DependencyProperty MaxSelectionCountProperty =
            DependencyProperty.Register(
                nameof(MaxSelectionCount),
                typeof(int),
                typeof(MultiSelectComboBox),
                new PropertyMetadata(0));

        public int MaxSelectionCount
        {
            get => (int)GetValue(MaxSelectionCountProperty);
            set => SetValue(MaxSelectionCountProperty, value);
        }

        public static readonly DependencyProperty DisplaySeparatorProperty =
            DependencyProperty.Register(
                nameof(DisplaySeparator),
                typeof(string),
                typeof(MultiSelectComboBox),
                new PropertyMetadata("、", OnDisplayDependencyChanged));

        public string DisplaySeparator
        {
            get => (string)GetValue(DisplaySeparatorProperty);
            set => SetValue(DisplaySeparatorProperty, value);
        }

        private static readonly DependencyPropertyKey DisplayTextPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(DisplayText),
                typeof(string),
                typeof(MultiSelectComboBox),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

        public string DisplayText
        {
            get => (string)GetValue(DisplayTextProperty);
            private set => SetValue(DisplayTextPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HasSelectionPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(HasSelection),
                typeof(bool),
                typeof(MultiSelectComboBox),
                new PropertyMetadata(false));

        public static readonly DependencyProperty HasSelectionProperty = HasSelectionPropertyKey.DependencyProperty;

        public bool HasSelection
        {
            get => (bool)GetValue(HasSelectionProperty);
            private set => SetValue(HasSelectionPropertyKey, value);
        }

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(IsDropDownOpen),
                typeof(bool),
                typeof(MultiSelectComboBox),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsDropDownOpen
        {
            get => (bool)GetValue(IsDropDownOpenProperty);
            set => SetValue(IsDropDownOpenProperty, value);
        }

        private static void OnDisplayDependencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MultiSelectComboBox c) c.UpdateDisplay();
        }

        private void RebuildWrappers()
        {
            foreach (var w in _wrappers) w.PropertyChanged -= OnWrapperPropertyChanged;
            _wrappers.Clear();

            if (ItemsSource == null)
            {
                UpdateDisplay();
                return;
            }

            var selected = SelectedItems;
            foreach (var item in ItemsSource)
            {
                var isChecked = selected != null && selected.Cast<object?>().Any(s => Equals(s, item));
                var wrapper = new ItemWrapper(item, isChecked);
                wrapper.PropertyChanged += OnWrapperPropertyChanged;
                _wrappers.Add(wrapper);
            }

            UpdateDisplay();
        }

        private void SyncWrappersFromSelectedItems()
        {
            if (_wrappers.Count == 0) return;
            var selected = SelectedItems;
            foreach (var wrapper in _wrappers)
            {
                var shouldCheck = selected != null && selected.Cast<object?>().Any(s => Equals(s, wrapper.Item));
                if (wrapper.IsChecked != shouldCheck)
                {
                    _suppressSelectedItemsChanged = true;
                    try { wrapper.IsChecked = shouldCheck; }
                    finally { _suppressSelectedItemsChanged = false; }
                }
            }
            UpdateDisplay();
        }

        private void OnWrapperPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ItemWrapper.IsChecked)) return;
            if (sender is not ItemWrapper wrapper) return;

            var sel = SelectedItems;
            if (sel == null)
            {
                UpdateDisplay();
                return;
            }

            if (wrapper.IsChecked && MaxSelectionCount > 0)
            {
                var alreadyIn = sel.Cast<object?>().Any(s => Equals(s, wrapper.Item));
                if (!alreadyIn && sel.Count >= MaxSelectionCount)
                {
                    _suppressSelectedItemsChanged = true;
                    try { wrapper.IsChecked = false; }
                    finally { _suppressSelectedItemsChanged = false; }
                    UpdateDisplay();
                    GlobalToast.Warning("已达上限", $"最多只能选择 {MaxSelectionCount} 项，请先取消其他项再选择");
                    return;
                }
            }

            _suppressSelectedItemsChanged = true;
            try
            {
                if (wrapper.IsChecked)
                {
                    if (!sel.Cast<object?>().Any(s => Equals(s, wrapper.Item)))
                        sel.Add(wrapper.Item);
                }
                else
                {
                    var existing = sel.Cast<object?>().FirstOrDefault(s => Equals(s, wrapper.Item));
                    if (existing != null) sel.Remove(existing);
                }
            }
            finally { _suppressSelectedItemsChanged = false; }

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var checkedItems = _wrappers.Where(w => w.IsChecked).ToList();
            HasSelection = checkedItems.Count > 0;
            if (checkedItems.Count == 0)
            {
                DisplayText = string.Empty;
                return;
            }
            var sep = DisplaySeparator ?? "、";
            DisplayText = string.Join(sep, checkedItems.Select(w => GetItemText(w.Item)));
        }

        private string GetItemText(object? item)
        {
            if (item == null) return string.Empty;
            if (string.IsNullOrEmpty(DisplayMemberPath)) return item.ToString() ?? string.Empty;
            try
            {
                var prop = item.GetType().GetProperty(DisplayMemberPath);
                if (prop == null) return item.ToString() ?? string.Empty;
                return prop.GetValue(item)?.ToString() ?? string.Empty;
            }
            catch
            {
                return item.ToString() ?? string.Empty;
            }
        }

        public class ItemWrapper : INotifyPropertyChanged
        {
            public object? Item { get; }

            private bool _isChecked;
            public bool IsChecked
            {
                get => _isChecked;
                set
                {
                    if (_isChecked != value)
                    {
                        _isChecked = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                    }
                }
            }

            public ItemWrapper(object? item, bool isChecked)
            {
                Item = item;
                _isChecked = isChecked;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
