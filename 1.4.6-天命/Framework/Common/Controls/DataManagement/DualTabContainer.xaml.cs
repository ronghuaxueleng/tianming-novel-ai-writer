using System.Collections.ObjectModel;
using System.Reflection;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace TM.Framework.Common.Controls.DataManagement
{
    [ContentProperty(nameof(Content))]
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class TabItemData : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isEnabled = true;

        public string Header { get; set; } = "Tab";

        public string Icon { get; set; } = "📋";

        public object? Content { get; set; }

        public object? ExternalDataContext { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [ContentProperty(nameof(Tabs))]
    public partial class DualTabContainer : UserControl
    {
        private bool _isUpdatingSelection;

        public DualTabContainer()
        {
            InitializeComponent();

            Tabs = new ObservableCollection<TabItemData>();

            Tabs.CollectionChanged += Tabs_CollectionChanged;

            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateExternalDataContext();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateExternalDataContext();
        }

        private void UpdateExternalDataContext()
        {
            var externalDC = this.DataContext;
            foreach (var tab in TabItems)
            {
                tab.ExternalDataContext = externalDC;
            }
        }

        public ObservableCollection<TabItemData> Tabs { get; set; }

        private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            TabItems = Tabs;
        }

        public static readonly DependencyProperty TabItemsProperty =
            DependencyProperty.Register(
                nameof(TabItems),
                typeof(ObservableCollection<TabItemData>),
                typeof(DualTabContainer),
                new PropertyMetadata(null, OnTabItemsChanged));

        public ObservableCollection<TabItemData> TabItems
        {
            get => (ObservableCollection<TabItemData>)GetValue(TabItemsProperty);
            set => SetValue(TabItemsProperty, value);
        }

        private static void OnTabItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var container = (DualTabContainer)d;

            if (e.OldValue is ObservableCollection<TabItemData> oldItems)
            {
                foreach (var item in oldItems)
                {
                    item.PropertyChanged -= container.TabItem_PropertyChanged;
                }
            }

            if (e.NewValue is ObservableCollection<TabItemData> newItems && newItems.Count > 0)
            {
                if (!newItems.Any(item => item.IsSelected))
                {
                    newItems[0].IsSelected = true;
                }

                foreach (var item in newItems)
                {
                    item.PropertyChanged += container.TabItem_PropertyChanged;
                }
            }
        }

        private void TabItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabItemData.IsSelected) && !_isUpdatingSelection)
            {
                var selectedItem = sender as TabItemData;
                if (selectedItem?.IsSelected == true)
                {
                    _isUpdatingSelection = true;

                    foreach (var item in TabItems)
                    {
                        if (item != selectedItem)
                        {
                            item.IsSelected = false;
                        }
                    }

                    _isUpdatingSelection = false;
                }
            }
        }

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(
                nameof(SelectedIndex),
                typeof(int),
                typeof(DualTabContainer),
                new PropertyMetadata(0));

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public static readonly DependencyProperty ContentPaddingProperty =
            DependencyProperty.Register(
                nameof(ContentPadding),
                typeof(Thickness),
                typeof(DualTabContainer),
                new PropertyMetadata(new Thickness(5)));

        public Thickness ContentPadding
        {
            get => (Thickness)GetValue(ContentPaddingProperty);
            set => SetValue(ContentPaddingProperty, value);
        }
    }
}
