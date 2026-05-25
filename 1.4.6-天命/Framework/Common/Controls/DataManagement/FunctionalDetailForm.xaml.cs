using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TM;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.Common.Controls.DataManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class FunctionalDetailForm : UserControl
    {
        private bool _layoutPending;

        public FunctionalDetailForm()
        {
            InitializeComponent();

            Fields = new ObservableCollection<FunctionalDetailField>();
            Fields.CollectionChanged += FieldsCollectionChanged;

            Loaded += FunctionalDetailForm_Loaded;
            Unloaded += FunctionalDetailForm_Unloaded;
        }

        #region Header

        public static readonly DependencyProperty HeaderIconProperty =
            DependencyProperty.Register(
                nameof(HeaderIcon),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("📘"));

        public string HeaderIcon
        {
            get => (string)GetValue(HeaderIconProperty);
            set => SetValue(HeaderIconProperty, value);
        }

        public static readonly DependencyProperty HeaderTitleProperty =
            DependencyProperty.Register(
                nameof(HeaderTitle),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("基础信息"));

        public string HeaderTitle
        {
            get => (string)GetValue(HeaderTitleProperty);
            set => SetValue(HeaderTitleProperty, value);
        }

        #endregion

        #region Name Field

        public static readonly DependencyProperty NameFieldLabelProperty =
            DependencyProperty.Register(
                nameof(NameFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("名称"));

        public string NameFieldLabel
        {
            get => (string)GetValue(NameFieldLabelProperty);
            set => SetValue(NameFieldLabelProperty, value);
        }

        public static readonly DependencyProperty NameValueProperty =
            DependencyProperty.Register(
                nameof(NameValue),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string NameValue
        {
            get => (string)GetValue(NameValueProperty);
            set => SetValue(NameValueProperty, value);
        }

        public static readonly DependencyProperty IsNameReadOnlyProperty =
            DependencyProperty.Register(
                nameof(IsNameReadOnly),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(false));

        public bool IsNameReadOnly
        {
            get => (bool)GetValue(IsNameReadOnlyProperty);
            set => SetValue(IsNameReadOnlyProperty, value);
        }

        public static readonly DependencyProperty NameMinWidthProperty =
            DependencyProperty.Register(
                nameof(NameMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(212d, LayoutPropertyChanged));

        public double NameMinWidth
        {
            get => (double)GetValue(NameMinWidthProperty);
            set => SetValue(NameMinWidthProperty, value);
        }

        public static readonly DependencyProperty NameMaxWidthProperty =
            DependencyProperty.Register(
                nameof(NameMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(212d, LayoutPropertyChanged));

        public double NameMaxWidth
        {
            get => (double)GetValue(NameMaxWidthProperty);
            set => SetValue(NameMaxWidthProperty, value);
        }

        public static readonly DependencyProperty NameAllowGrowProperty =
            DependencyProperty.Register(
                nameof(NameAllowGrow),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(false, LayoutPropertyChanged));

        public bool NameAllowGrow
        {
            get => (bool)GetValue(NameAllowGrowProperty);
            set => SetValue(NameAllowGrowProperty, value);
        }

        public static readonly DependencyProperty NameGrowWeightProperty =
            DependencyProperty.Register(
                nameof(NameGrowWeight),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(0d, LayoutPropertyChanged));

        public double NameGrowWeight
        {
            get => (double)GetValue(NameGrowWeightProperty);
            set => SetValue(NameGrowWeightProperty, value);
        }

        public static readonly DependencyProperty NameFieldContentProperty =
            DependencyProperty.Register(
                nameof(NameFieldContent),
                typeof(object),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null, LayoutPropertyChanged));

        public object? NameFieldContent
        {
            get => GetValue(NameFieldContentProperty);
            set => SetValue(NameFieldContentProperty, value);
        }

        #endregion

        #region Occupy Field

        public static readonly DependencyProperty ShowOccupyFieldProperty =
            DependencyProperty.Register(
                nameof(ShowOccupyField),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowOccupyField
        {
            get => (bool)GetValue(ShowOccupyFieldProperty);
            set => SetValue(ShowOccupyFieldProperty, value);
        }

        public static readonly DependencyProperty OccupyFieldLabelProperty =
            DependencyProperty.Register(
                nameof(OccupyFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("占位"));

        public string OccupyFieldLabel
        {
            get => (string)GetValue(OccupyFieldLabelProperty);
            set => SetValue(OccupyFieldLabelProperty, value);
        }

        public static readonly DependencyProperty OccupyValueProperty =
            DependencyProperty.Register(
                nameof(OccupyValue),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string OccupyValue
        {
            get => (string)GetValue(OccupyValueProperty);
            set => SetValue(OccupyValueProperty, value);
        }

        public static readonly DependencyProperty OccupyMinWidthProperty =
            DependencyProperty.Register(
                nameof(OccupyMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double OccupyMinWidth
        {
            get => (double)GetValue(OccupyMinWidthProperty);
            set => SetValue(OccupyMinWidthProperty, value);
        }

        public static readonly DependencyProperty OccupyMaxWidthProperty =
            DependencyProperty.Register(
                nameof(OccupyMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double OccupyMaxWidth
        {
            get => (double)GetValue(OccupyMaxWidthProperty);
            set => SetValue(OccupyMaxWidthProperty, value);
        }

        #endregion

        public static readonly DependencyProperty ShowBasicFieldsProperty =
            DependencyProperty.Register(
                nameof(ShowBasicFields),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowBasicFields
        {
            get => (bool)GetValue(ShowBasicFieldsProperty);
            set => SetValue(ShowBasicFieldsProperty, value);
        }

        #region Icon Field

        public static readonly DependencyProperty ShowIconFieldProperty =
            DependencyProperty.Register(
                nameof(ShowIconField),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowIconField
        {
            get => (bool)GetValue(ShowIconFieldProperty);
            set => SetValue(ShowIconFieldProperty, value);
        }

        public static readonly DependencyProperty IconFieldLabelProperty =
            DependencyProperty.Register(
                nameof(IconFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("图标"));

        public string IconFieldLabel
        {
            get => (string)GetValue(IconFieldLabelProperty);
            set => SetValue(IconFieldLabelProperty, value);
        }

        public static readonly DependencyProperty IconValueProperty =
            DependencyProperty.Register(
                nameof(IconValue),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string IconValue
        {
            get => (string)GetValue(IconValueProperty);
            set => SetValue(IconValueProperty, value);
        }

        public static readonly DependencyProperty IconMinWidthProperty =
            DependencyProperty.Register(
                nameof(IconMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double IconMinWidth
        {
            get => (double)GetValue(IconMinWidthProperty);
            set => SetValue(IconMinWidthProperty, value);
        }

        public static readonly DependencyProperty IconMaxWidthProperty =
            DependencyProperty.Register(
                nameof(IconMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double IconMaxWidth
        {
            get => (double)GetValue(IconMaxWidthProperty);
            set => SetValue(IconMaxWidthProperty, value);
        }

        public static readonly DependencyProperty IconAllowGrowProperty =
            DependencyProperty.Register(
                nameof(IconAllowGrow),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(false, LayoutPropertyChanged));

        public bool IconAllowGrow
        {
            get => (bool)GetValue(IconAllowGrowProperty);
            set => SetValue(IconAllowGrowProperty, value);
        }

        public static readonly DependencyProperty IconGrowWeightProperty =
            DependencyProperty.Register(
                nameof(IconGrowWeight),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(0.5d, LayoutPropertyChanged));

        public double IconGrowWeight
        {
            get => (double)GetValue(IconGrowWeightProperty);
            set => SetValue(IconGrowWeightProperty, value);
        }

        #endregion

        #region Status Field

        public static readonly DependencyProperty ShowStatusFieldProperty =
            DependencyProperty.Register(
                nameof(ShowStatusField),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowStatusField
        {
            get => (bool)GetValue(ShowStatusFieldProperty);
            set => SetValue(ShowStatusFieldProperty, value);
        }

        public static readonly DependencyProperty StatusFieldLabelProperty =
            DependencyProperty.Register(
                nameof(StatusFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("启用状态"));

        public string StatusFieldLabel
        {
            get => (string)GetValue(StatusFieldLabelProperty);
            set => SetValue(StatusFieldLabelProperty, value);
        }

        public static readonly DependencyProperty StatusValueProperty =
            DependencyProperty.Register(
                nameof(StatusValue),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata("已禁用", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string StatusValue
        {
            get => (string)GetValue(StatusValueProperty);
            set => SetValue(StatusValueProperty, value);
        }

        public static readonly DependencyProperty StatusMinWidthProperty =
            DependencyProperty.Register(
                nameof(StatusMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double StatusMinWidth
        {
            get => (double)GetValue(StatusMinWidthProperty);
            set => SetValue(StatusMinWidthProperty, value);
        }

        public static readonly DependencyProperty StatusMaxWidthProperty =
            DependencyProperty.Register(
                nameof(StatusMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double StatusMaxWidth
        {
            get => (double)GetValue(StatusMaxWidthProperty);
            set => SetValue(StatusMaxWidthProperty, value);
        }

        public static readonly DependencyProperty StatusAllowGrowProperty =
            DependencyProperty.Register(
                nameof(StatusAllowGrow),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(false, LayoutPropertyChanged));

        public bool StatusAllowGrow
        {
            get => (bool)GetValue(StatusAllowGrowProperty);
            set => SetValue(StatusAllowGrowProperty, value);
        }

        public static readonly DependencyProperty StatusGrowWeightProperty =
            DependencyProperty.Register(
                nameof(StatusGrowWeight),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(0.5d, LayoutPropertyChanged));

        public double StatusGrowWeight
        {
            get => (double)GetValue(StatusGrowWeightProperty);
            set => SetValue(StatusGrowWeightProperty, value);
        }

        #endregion

        #region Category Field

        public static readonly DependencyProperty ShowCategoryFieldProperty =
            DependencyProperty.Register(
                nameof(ShowCategoryField),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowCategoryField
        {
            get => (bool)GetValue(ShowCategoryFieldProperty);
            set => SetValue(ShowCategoryFieldProperty, value);
        }

        public static readonly DependencyProperty CategoryMinWidthProperty =
            DependencyProperty.Register(
                nameof(CategoryMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(324d, LayoutPropertyChanged));

        public double CategoryMinWidth
        {
            get => (double)GetValue(CategoryMinWidthProperty);
            set => SetValue(CategoryMinWidthProperty, value);
        }

        public static readonly DependencyProperty CategoryMaxWidthProperty =
            DependencyProperty.Register(
                nameof(CategoryMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(324d, LayoutPropertyChanged));

        public double CategoryMaxWidth
        {
            get => (double)GetValue(CategoryMaxWidthProperty);
            set => SetValue(CategoryMaxWidthProperty, value);
        }

        public static readonly DependencyProperty CategoryAllowGrowProperty =
            DependencyProperty.Register(
                nameof(CategoryAllowGrow),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(false, LayoutPropertyChanged));

        public bool CategoryAllowGrow
        {
            get => (bool)GetValue(CategoryAllowGrowProperty);
            set => SetValue(CategoryAllowGrowProperty, value);
        }

        public static readonly DependencyProperty CategoryGrowWeightProperty =
            DependencyProperty.Register(
                nameof(CategoryGrowWeight),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(1d, LayoutPropertyChanged));

        public double CategoryGrowWeight
        {
            get => (double)GetValue(CategoryGrowWeightProperty);
            set => SetValue(CategoryGrowWeightProperty, value);
        }

        public static readonly DependencyProperty CategoryFieldContentProperty =
            DependencyProperty.Register(
                nameof(CategoryFieldContent),
                typeof(object),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null, LayoutPropertyChanged));

        public object? CategoryFieldContent
        {
            get => GetValue(CategoryFieldContentProperty);
            set => SetValue(CategoryFieldContentProperty, value);
        }

        public static readonly DependencyProperty CategoryFieldLabelProperty =
            DependencyProperty.Register(
                nameof(CategoryFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("所属分类"));

        public string CategoryFieldLabel
        {
            get => (string)GetValue(CategoryFieldLabelProperty);
            set => SetValue(CategoryFieldLabelProperty, value);
        }

        public static readonly DependencyProperty CategoryItemsSourceProperty =
            DependencyProperty.Register(
                nameof(CategoryItemsSource),
                typeof(IEnumerable),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null));

        public IEnumerable? CategoryItemsSource
        {
            get => (IEnumerable?)GetValue(CategoryItemsSourceProperty);
            set => SetValue(CategoryItemsSourceProperty, value);
        }

        public static readonly DependencyProperty CategorySelectedItemProperty =
            DependencyProperty.Register(
                nameof(CategorySelectedItem),
                typeof(object),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object? CategorySelectedItem
        {
            get => GetValue(CategorySelectedItemProperty);
            set => SetValue(CategorySelectedItemProperty, value);
        }

        public static readonly DependencyProperty IsCategoryEditableProperty =
            DependencyProperty.Register(
                nameof(IsCategoryEditable),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true));

        public bool IsCategoryEditable
        {
            get => (bool)GetValue(IsCategoryEditableProperty);
            set => SetValue(IsCategoryEditableProperty, value);
        }

        public static readonly DependencyProperty CategorySelectedPathProperty =
            DependencyProperty.Register(
                nameof(CategorySelectedPath),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string CategorySelectedPath
        {
            get => (string)GetValue(CategorySelectedPathProperty);
            set => SetValue(CategorySelectedPathProperty, value);
        }

        public static readonly DependencyProperty CategoryIsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(CategoryIsDropDownOpen),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool CategoryIsDropDownOpen
        {
            get => (bool)GetValue(CategoryIsDropDownOpenProperty);
            set => SetValue(CategoryIsDropDownOpenProperty, value);
        }

        public static readonly DependencyProperty CategoryDisplayIconProperty =
            DependencyProperty.Register(
                nameof(CategoryDisplayIcon),
                typeof(string),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string CategoryDisplayIcon
        {
            get => (string)GetValue(CategoryDisplayIconProperty);
            set => SetValue(CategoryDisplayIconProperty, value);
        }

        public static readonly DependencyProperty CategoryNodeSelectCommandProperty =
            DependencyProperty.Register(
                nameof(CategoryNodeSelectCommand),
                typeof(ICommand),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null));

        public ICommand? CategoryNodeSelectCommand
        {
            get => (ICommand?)GetValue(CategoryNodeSelectCommandProperty);
            set => SetValue(CategoryNodeSelectCommandProperty, value);
        }

        public static readonly DependencyProperty CategoryMaxLevelProperty =
            DependencyProperty.Register(
                nameof(CategoryMaxLevel),
                typeof(int),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(5));

        public int CategoryMaxLevel
        {
            get => (int)GetValue(CategoryMaxLevelProperty);
            set => SetValue(CategoryMaxLevelProperty, value);
        }

        #endregion

        #region Type Field

        public static readonly DependencyProperty ShowTypeFieldProperty =
            DependencyProperty.Register(
                nameof(ShowTypeField),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool ShowTypeField
        {
            get => (bool)GetValue(ShowTypeFieldProperty);
            set => SetValue(ShowTypeFieldProperty, value);
        }

        public static readonly DependencyProperty TypeMinWidthProperty =
            DependencyProperty.Register(
                nameof(TypeMinWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double TypeMinWidth
        {
            get => (double)GetValue(TypeMinWidthProperty);
            set => SetValue(TypeMinWidthProperty, value);
        }

        public static readonly DependencyProperty TypeMaxWidthProperty =
            DependencyProperty.Register(
                nameof(TypeMaxWidth),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(120d, LayoutPropertyChanged));

        public double TypeMaxWidth
        {
            get => (double)GetValue(TypeMaxWidthProperty);
            set => SetValue(TypeMaxWidthProperty, value);
        }

        public static readonly DependencyProperty TypeAllowGrowProperty =
            DependencyProperty.Register(
                nameof(TypeAllowGrow),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true, LayoutPropertyChanged));

        public bool TypeAllowGrow
        {
            get => (bool)GetValue(TypeAllowGrowProperty);
            set => SetValue(TypeAllowGrowProperty, value);
        }

        public static readonly DependencyProperty TypeGrowWeightProperty =
            DependencyProperty.Register(
                nameof(TypeGrowWeight),
                typeof(double),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(1d, LayoutPropertyChanged));

        public double TypeGrowWeight
        {
            get => (double)GetValue(TypeGrowWeightProperty);
            set => SetValue(TypeGrowWeightProperty, value);
        }

        public static readonly DependencyProperty TypeFieldContentProperty =
            DependencyProperty.Register(
                nameof(TypeFieldContent),
                typeof(object),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null, LayoutPropertyChanged));

        public object? TypeFieldContent
        {
            get => GetValue(TypeFieldContentProperty);
            set => SetValue(TypeFieldContentProperty, value);
        }

        public static readonly DependencyProperty TypeFieldLabelProperty =
            DependencyProperty.Register(
                nameof(TypeFieldLabel),
                typeof(string),
                typeof(FunctionalDetailForm),
                new PropertyMetadata("类型"));

        public string TypeFieldLabel
        {
            get => (string)GetValue(TypeFieldLabelProperty);
            set => SetValue(TypeFieldLabelProperty, value);
        }

        public static readonly DependencyProperty TypeItemsSourceProperty =
            DependencyProperty.Register(
                nameof(TypeItemsSource),
                typeof(IEnumerable),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null));

        public IEnumerable? TypeItemsSource
        {
            get => (IEnumerable?)GetValue(TypeItemsSourceProperty);
            set => SetValue(TypeItemsSourceProperty, value);
        }

        public static readonly DependencyProperty TypeSelectedItemProperty =
            DependencyProperty.Register(
                nameof(TypeSelectedItem),
                typeof(object),
                typeof(FunctionalDetailForm),
                new FrameworkPropertyMetadata(
                    null, 
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnTypeSelectedItemChanged));

        public object? TypeSelectedItem
        {
            get => GetValue(TypeSelectedItemProperty);
            set => SetValue(TypeSelectedItemProperty, value);
        }

        private static void OnTypeSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FunctionalDetailForm form)
            {
                form.RebuildFieldRows();
            }
        }

        public static readonly DependencyProperty IsTypeEditableProperty =
            DependencyProperty.Register(
                nameof(IsTypeEditable),
                typeof(bool),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(true));

        public bool IsTypeEditable
        {
            get => (bool)GetValue(IsTypeEditableProperty);
            set => SetValue(IsTypeEditableProperty, value);
        }

        #endregion

        #region Additional Content

        public static readonly DependencyProperty AdditionalContentProperty =
            DependencyProperty.Register(
                nameof(AdditionalContent),
                typeof(object),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null));

        public object? AdditionalContent
        {
            get => GetValue(AdditionalContentProperty);
            set => SetValue(AdditionalContentProperty, value);
        }

        #endregion
        #region Custom Fields

        public static readonly DependencyProperty FieldsProperty =
            DependencyProperty.Register(
                nameof(Fields),
                typeof(ObservableCollection<FunctionalDetailField>),
                typeof(FunctionalDetailForm),
                new PropertyMetadata(null, OnFieldsPropertyChanged));

        public ObservableCollection<FunctionalDetailField>? Fields
        {
            get => (ObservableCollection<FunctionalDetailField>?)GetValue(FieldsProperty);
            set => SetValue(FieldsProperty, value);
        }

        private static void OnFieldsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FunctionalDetailForm form)
            {
                form.AttachFields(e.OldValue as ObservableCollection<FunctionalDetailField>, e.NewValue as ObservableCollection<FunctionalDetailField>);
                form.ScheduleLayoutRefresh();
            }
        }

        private void AttachFields(ObservableCollection<FunctionalDetailField>? oldCollection, ObservableCollection<FunctionalDetailField>? newCollection)
        {
            if (oldCollection != null)
            {
                oldCollection.CollectionChanged -= FieldsCollectionChanged;
            }

            if (newCollection != null)
            {
                newCollection.CollectionChanged += FieldsCollectionChanged;
            }

            ScheduleLayoutRefresh();
        }

        private void FieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleLayoutRefresh();
        }

        #endregion

        #region Layout Logic

        private const double FieldSpacing = 12d;
        private double _lastKnownAvailableWidth;

        private readonly List<RowLayoutContext> _rowContexts = new();

        private void FunctionalDetailForm_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleLayoutRefresh();
        }

        private void FunctionalDetailForm_Unloaded(object sender, RoutedEventArgs e)
        {
            ClearRowContexts();
        }

        private static void LayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FunctionalDetailForm form)
            {
                form.ScheduleLayoutRefresh();
            }
        }

        private void ScheduleLayoutRefresh()
        {
            if (_layoutPending)
            {
                return;
            }

            if (!IsLoaded)
            {
                Loaded -= DeferredLoadedHandler;
                Loaded += DeferredLoadedHandler;
                return;
            }

            _layoutPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _layoutPending = false;
                RefreshLayout();
            }));
        }

        private void DeferredLoadedHandler(object sender, RoutedEventArgs e)
        {
            Loaded -= DeferredLoadedHandler;
            ScheduleLayoutRefresh();
        }

        private void RefreshLayout()
        {
            try
            {
                RebuildFieldRows();
                UpdateAllRowLayouts();
            }
            catch (Exception ex)
            {
                App.Log($"[FunctionalDetailForm] 布局刷新失败: {ex}");
                throw;
            }
        }

        private void RebuildFieldRows()
        {
            ClearRowContexts();
            FieldRowsPanel.Children.Clear();

            var fields = BuildEffectiveFieldList();
            if (fields.Count == 0)
            {
                return;
            }

            var availableWidth = GetAvailableRowWidth();
            var groupContexts = GroupFields(fields);

            _lastKnownAvailableWidth = availableWidth;

            foreach (var group in groupContexts.OrderBy(g => g.Order))
            {
                var rowSegments = SplitFieldsIntoRows(group, availableWidth).ToList();
                foreach (var segment in rowSegments)
                {
                    var (rowGrid, rowContext) = BuildRowGrid(segment);
                    FieldRowsPanel.Children.Add(rowGrid);
                    _rowContexts.Add(rowContext);
                }
            }

            ApplyRowSpacing();
        }

        private List<FunctionalDetailField> BuildEffectiveFieldList()
        {
            var result = new List<FunctionalDetailField>();

            if (Fields is { Count: > 0 })
            {
                foreach (var field in Fields)
                {
                    if (field?.Content is UIElement)
                    {
                        result.Add(field);
                    }
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            return ShowBasicFields ? BuildBasicFields() : new List<FunctionalDetailField>();
        }

        private List<FunctionalDetailField> BuildBasicFields()
        {
            var fields = new List<FunctionalDetailField>();

            var basicTotalWidth = GetBasicTotalWidth();
            var basicNameWidth = GetBasicNameWidth(basicTotalWidth);

            var nameField = new FunctionalDetailField
            {
                Label = NameFieldLabel,
                GroupKey = "BasicRow1",
                Order = 0,
                MinWidth = basicNameWidth,
                MaxWidth = basicNameWidth,
                AllowGrow = NameAllowGrow,
                GrowWeight = NameGrowWeight,
                Content = CreateBasicNameContent()
            };
            fields.Add(nameField);

            if (ShowOccupyField)
            {
                var occupyField = new FunctionalDetailField
                {
                    Label = OccupyFieldLabel,
                    GroupKey = "BasicRow1",
                    Order = 1,
                    MinWidth = OccupyMinWidth,
                    MaxWidth = OccupyMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    Content = CreateBasicOccupyContent()
                };
                fields.Add(occupyField);
            }

            if (ShowIconField)
            {
                var iconField = new FunctionalDetailField
                {
                    Label = IconFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 0,
                    MinWidth = IconMinWidth,
                    MaxWidth = IconMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicIconContent()
                };
                fields.Add(iconField);
            }

            if (ShowStatusField)
            {
                var statusField = new FunctionalDetailField
                {
                    Label = StatusFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 1,
                    MinWidth = StatusMinWidth,
                    MaxWidth = StatusMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicStatusContent()
                };
                fields.Add(statusField);
            }

            if (ShowTypeField)
            {
                var typeField = new FunctionalDetailField
                {
                    Label = TypeFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 2,
                    MinWidth = TypeMinWidth,
                    MaxWidth = TypeMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicTypeContent()
                };
                fields.Add(typeField);
            }

            if (ShowCategoryField)
            {
                var categoryField = new FunctionalDetailField
                {
                    Label = CategoryFieldLabel,
                    GroupKey = "BasicCategory",
                    MinWidth = basicTotalWidth,
                    MaxWidth = basicTotalWidth,
                    AllowGrow = CategoryAllowGrow,
                    GrowWeight = CategoryGrowWeight,
                    AllowMerge = false,
                    Order = 100,
                    Content = CreateBasicCategoryContent()
                };
                fields.Add(categoryField);
            }

            return fields;
        }

        private double GetBasicTotalWidth()
        {
            return IconMinWidth + StatusMinWidth + TypeMinWidth + (FieldSpacing * 2d);
        }

        private double GetBasicNameWidth(double totalWidth)
        {
            var nameWidth = totalWidth - OccupyMinWidth - FieldSpacing;
            return Math.Max(0d, nameWidth);
        }

        private FrameworkElement CreateBasicNameContent()
        {
            if (NameFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            TM.Framework.Common.Helpers.UI.TextInputContextMenuHelper.SetEnableStandardEditMenu(textBox, true);

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(NameValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            textBox.SetBinding(TextBox.IsReadOnlyProperty, new System.Windows.Data.Binding(nameof(IsNameReadOnly))
            {
                Source = this
            });

            return textBox;
        }

        private FrameworkElement CreateBasicOccupyContent()
        {
            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsReadOnly = true
            };

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(OccupyValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return textBox;
        }

        private FrameworkElement CreateBasicStatusContent()
        {
            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("StandardComboBoxStyle") is Style comboStyle)
            {
                comboBox.Style = comboStyle;
            }

            comboBox.Items.Add("已禁用");
            comboBox.Items.Add("已启用");

            comboBox.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(StatusValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return comboBox;
        }

        private FrameworkElement CreateBasicIconContent()
        {
            try
            {
                var emojiPicker = new TM.Framework.Common.Controls.EmojiPicker
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                emojiPicker.SetBinding(TM.Framework.Common.Controls.EmojiPicker.SelectedEmojiProperty,
                    new System.Windows.Data.Binding(nameof(IconValue))
                    {
                        Source = this,
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    });

                return emojiPicker;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FunctionalDetailForm] EmojiPicker创建失败: {ex.Message}");
            }

            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            TM.Framework.Common.Helpers.UI.TextInputContextMenuHelper.SetEnableStandardEditMenu(textBox, true);

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(IconValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return textBox;
        }

        private FrameworkElement CreateBasicCategoryContent()
        {
            if (CategoryFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("TreeComboBoxStyle") is Style treeComboStyle)
            {
                comboBox.Style = treeComboStyle;
            }

            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(CategoryItemsSource))
            {
                Source = this
            });

            comboBox.SetBinding(ComboBox.IsDropDownOpenProperty, new System.Windows.Data.Binding(nameof(CategoryIsDropDownOpen))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.SelectedPathProperty,
                new System.Windows.Data.Binding(nameof(CategorySelectedPath))
                {
                    Source = this,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.DisplayIconProperty,
                new System.Windows.Data.Binding(nameof(CategoryDisplayIcon))
                {
                    Source = this,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.MaxLevelProperty,
                new System.Windows.Data.Binding(nameof(CategoryMaxLevel))
                {
                    Source = this
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.NodeDoubleClickCommandProperty,
                new System.Windows.Data.Binding(nameof(CategoryNodeSelectCommand))
                {
                    Source = this
                });

            return comboBox;
        }

        private FrameworkElement CreateBasicTypeContent()
        {
            if (TypeFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("StandardComboBoxStyle") is Style comboStyle)
            {
                comboBox.Style = comboStyle;
            }

            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(TypeItemsSource))
            {
                Source = this
            });

            comboBox.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(TypeSelectedItem))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            comboBox.SetBinding(ComboBox.IsEditableProperty, new System.Windows.Data.Binding(nameof(IsTypeEditable))
            {
                Source = this
            });

            return comboBox;
        }

        private IList<GroupContext> GroupFields(IReadOnlyList<FunctionalDetailField> fields)
        {
            var groups = new List<GroupContext>();
            var groupMap = new Dictionary<string, GroupContext>();
            int sequence = 0;

            foreach (var field in fields)
            {
                if (field.Content is not UIElement)
                {
                    continue;
                }

                string key;
                if (!field.AllowMerge || string.IsNullOrWhiteSpace(field.GroupKey))
                {
                    key = $"__single_{ShortIdGenerator.NewGuid()}";
                }
                else
                {
                    key = field.GroupKey!;
                }

                if (!groupMap.TryGetValue(key, out var group))
                {
                    group = new GroupContext(key, field.Order, sequence);
                    groupMap[key] = group;
                    groups.Add(group);
                }

                group.Fields.Add(new FieldWrapper(field, sequence));
                sequence++;
            }

        return groups;
        }

        private double GetAvailableRowWidth()
        {
            double width = FieldRowsPanel.ActualWidth;
            if (double.IsNaN(width) || width <= 0)
            {
                width = FieldRowsPanel.RenderSize.Width;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = ActualWidth;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = RenderSize.Width;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = 800d;
            }

            return Math.Max(200d, width - 1d);
        }

        private IEnumerable<IReadOnlyList<FieldWrapper>> SplitFieldsIntoRows(GroupContext group, double maxRowWidth)
        {
            var rows = new List<List<FieldWrapper>>();
            var currentRow = new List<FieldWrapper>();
            double currentWidth = 0;

            bool isForcedSingleRow = group.Key == "BasicRow1" || group.Key == "BasicRow2";

            foreach (var fieldWrapper in group.Fields)
            {
                var field = fieldWrapper.Field;
                double fieldWidth = Math.Max(1d, field.MinWidth);
                double spacing = currentRow.Count > 0 ? FieldSpacing : 0;

                if (!isForcedSingleRow && currentRow.Count > 0 && currentWidth + spacing + fieldWidth > maxRowWidth)
                {
                    rows.Add(currentRow);
                    currentRow = new List<FieldWrapper>();
                    currentWidth = 0;
                    spacing = 0;
                }

                currentRow.Add(fieldWrapper);
                currentWidth += spacing + fieldWidth;
            }

            if (currentRow.Count > 0)
            {
                rows.Add(currentRow);
            }

            return rows;
        }

        private (Grid Grid, RowLayoutContext Context) BuildRowGrid(IReadOnlyList<FieldWrapper> fields)
        {
            var rowGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };

            var rowContext = new RowLayoutContext(rowGrid);
            int columnIndex = 0;

            for (int i = 0; i < fields.Count; i++)
            {
                var fieldWrapper = fields[i];
                var field = fieldWrapper.Field;

                var column = new ColumnDefinition
                {
                    Width = new GridLength(field.MinWidth),
                    MinWidth = Math.Max(0, field.MinWidth),
                    MaxWidth = field.MaxWidth > 0 ? Math.Max(field.MinWidth, field.MaxWidth) : double.PositiveInfinity
                };

                rowGrid.ColumnDefinitions.Add(column);
                rowContext.FieldColumns.Add(new FieldColumnContext(column, field));

                var container = CreateFieldContainer(field);
                Grid.SetColumn(container, columnIndex);
                rowGrid.Children.Add(container);

                columnIndex++;

                if (i < fields.Count - 1)
                {
                    var spacingColumn = new ColumnDefinition
                    {
                        Width = new GridLength(FieldSpacing),
                        MinWidth = FieldSpacing,
                        MaxWidth = FieldSpacing
                    };
                    rowGrid.ColumnDefinitions.Add(spacingColumn);
                    rowContext.SpacingColumns.Add(spacingColumn);
                    columnIndex++;
                }
            }

            rowGrid.SizeChanged += RowGrid_SizeChanged;
            rowGrid.Tag = rowContext;

            return (rowGrid, rowContext);
        }

        private void ApplyRowSpacing()
        {
            int count = FieldRowsPanel.Children.Count;
            for (int i = 0; i < count; i++)
            {
                if (FieldRowsPanel.Children[i] is FrameworkElement fe)
                {
                    fe.Margin = new Thickness(0, 0, 0, i == count - 1 ? 0 : 10);
                }
            }
        }

        private FrameworkElement CreateFieldContainer(FunctionalDetailField field)
        {
            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            bool hasLabel = !string.IsNullOrWhiteSpace(field.Label);

            if (hasLabel)
            {
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = field.Label,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                container.Children.Add(label);
            }
            else
            {
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (field.Content is UIElement element)
            {
                EnsureDetached(element);

                if (element is FrameworkElement fe)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                var presenter = new ContentPresenter
                {
                    Content = element,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                Grid.SetRow(presenter, hasLabel ? 1 : 0);
                container.Children.Add(presenter);
            }

            return container;
        }

        private void UpdateAllRowLayouts()
        {
            foreach (var context in _rowContexts)
            {
                UpdateRowLayout(context);
            }
        }

        private void RowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is RowLayoutContext context)
            {
                UpdateRowLayout(context);

                var availableWidth = GetAvailableRowWidth();
                if (Math.Abs(availableWidth - _lastKnownAvailableWidth) > 20)
                {
                    _lastKnownAvailableWidth = availableWidth;
                    ScheduleLayoutRefresh();
                }
            }
        }

        private void UpdateRowLayout(RowLayoutContext context)
        {
            if (context.FieldColumns.Count == 0)
            {
                return;
            }

            double available = context.Grid.ActualWidth - context.Grid.Margin.Left - context.Grid.Margin.Right;
            if (available <= 0)
            {
                return;
            }

            double spacingWidth = FieldSpacing * Math.Max(0, context.FieldColumns.Count - 1);
            available -= spacingWidth;
            if (available <= 0)
            {
                return;
            }

            var minWidths = context.FieldColumns.Select(c => c.Field.MinWidth).ToArray();
            var maxWidths = context.FieldColumns.Select(c => c.Field.MaxWidth > 0 ? Math.Max(c.Field.MinWidth, c.Field.MaxWidth) : double.PositiveInfinity).ToArray();
            var growWeights = context.FieldColumns.Select(c => c.Field.AllowGrow ? Math.Max(0, c.Field.GrowWeight) : 0).ToArray();

            double minTotal = minWidths.Sum();
            var targetWidths = (double[])minWidths.Clone();

            if (available > minTotal)
            {
                double leftover = available - minTotal;
                var adjustable = new List<int>();
                for (int i = 0; i < growWeights.Length; i++)
                {
                    if (growWeights[i] > 0 && maxWidths[i] > minWidths[i])
                    {
                        adjustable.Add(i);
                    }
                }

                while (leftover > 0.1 && adjustable.Count > 0)
                {
                    double weightSum = adjustable.Sum(i => growWeights[i]);
                    if (weightSum <= 0)
                    {
                        break;
                    }

                    double consumed = 0;
                    foreach (var index in adjustable.ToList())
                    {
                        double capacity = maxWidths[index] - targetWidths[index];
                        if (capacity <= 0)
                        {
                            adjustable.Remove(index);
                            continue;
                        }

                        double share = leftover * (growWeights[index] / weightSum);
                        double addition = double.IsInfinity(maxWidths[index])
                            ? share
                            : Math.Min(share, capacity);

                        targetWidths[index] += addition;
                        consumed += addition;

                        if (!double.IsInfinity(maxWidths[index]) && targetWidths[index] >= maxWidths[index] - 0.1)
                        {
                            adjustable.Remove(index);
                        }
                    }

                    if (consumed <= 0.1)
                    {
                        break;
                    }

                    leftover -= consumed;
                }
            }

            for (int i = 0; i < context.FieldColumns.Count; i++)
            {
                double width = Math.Max(minWidths[i], targetWidths[i]);
                width = Math.Min(width, maxWidths[i]);

                var column = context.FieldColumns[i].Column;
                column.MinWidth = minWidths[i];
                column.MaxWidth = double.IsInfinity(maxWidths[i]) ? double.PositiveInfinity : maxWidths[i];
                column.Width = new GridLength(width, GridUnitType.Pixel);
            }
        }

        private void ClearRowContexts()
        {
            foreach (var context in _rowContexts)
            {
                context.Grid.SizeChanged -= RowGrid_SizeChanged;
                context.Grid.Tag = null;
            }

            _rowContexts.Clear();
        }

        private static void EnsureDetached(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            if (VisualTreeHelper.GetParent(element) is DependencyObject parent)
            {
                switch (parent)
                {
                    case Panel panel:
                        panel.Children.Remove(element);
                        break;
                    case ContentPresenter presenter:
                        presenter.Content = null;
                        break;
                    case ContentControl control:
                        control.Content = null;
                        break;
                    case Decorator decorator:
                        decorator.Child = null;
                        break;
                }
            }
        }

        private sealed class RowLayoutContext
        {
            public RowLayoutContext(Grid grid)
            {
                Grid = grid;
            }

            public Grid Grid { get; }
            public List<FieldColumnContext> FieldColumns { get; } = new();
            public List<ColumnDefinition> SpacingColumns { get; } = new();
        }

        private sealed class FieldColumnContext
        {
            public FieldColumnContext(ColumnDefinition column, FunctionalDetailField field)
            {
                Column = column;
                Field = field;
            }

            public ColumnDefinition Column { get; }
            public FunctionalDetailField Field { get; }
        }

        private sealed class GroupContext
        {
            public GroupContext(string key, int order, int sequence)
            {
                Key = key;
                Order = order == 0 ? sequence : order;
            }

            public string Key { get; }
            public int Order { get; }
            public List<FieldWrapper> Fields { get; } = new();
        }

        private sealed class FieldWrapper
        {
            public FieldWrapper(FunctionalDetailField field, int sequence)
            {
                Field = field;
                Sequence = sequence;
            }

            public FunctionalDetailField Field { get; }
            public int Sequence { get; }
        }

        #endregion
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class FunctionalDetailField : DependencyObject
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(FunctionalDetailField),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty GroupKeyProperty =
            DependencyProperty.Register(
                nameof(GroupKey),
                typeof(string),
                typeof(FunctionalDetailField),
                new PropertyMetadata(string.Empty));

        public string GroupKey
        {
            get => (string)GetValue(GroupKeyProperty);
            set => SetValue(GroupKeyProperty, value);
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register(
                nameof(Content),
                typeof(UIElement),
                typeof(FunctionalDetailField),
                new PropertyMetadata(null));

        public UIElement? Content
        {
            get => (UIElement?)GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        public static readonly DependencyProperty MinWidthProperty =
            DependencyProperty.Register(
                nameof(MinWidth),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(160d));

        public double MinWidth
        {
            get => (double)GetValue(MinWidthProperty);
            set => SetValue(MinWidthProperty, value);
        }

        public static readonly DependencyProperty MaxWidthProperty =
            DependencyProperty.Register(
                nameof(MaxWidth),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(320d));

        public double MaxWidth
        {
            get => (double)GetValue(MaxWidthProperty);
            set => SetValue(MaxWidthProperty, value);
        }

        public static readonly DependencyProperty AllowGrowProperty =
            DependencyProperty.Register(
                nameof(AllowGrow),
                typeof(bool),
                typeof(FunctionalDetailField),
                new PropertyMetadata(true));

        public bool AllowGrow
        {
            get => (bool)GetValue(AllowGrowProperty);
            set => SetValue(AllowGrowProperty, value);
        }

        public static readonly DependencyProperty GrowWeightProperty =
            DependencyProperty.Register(
                nameof(GrowWeight),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(1d));

        public double GrowWeight
        {
            get => (double)GetValue(GrowWeightProperty);
            set => SetValue(GrowWeightProperty, value);
        }

        public static readonly DependencyProperty AllowMergeProperty =
            DependencyProperty.Register(
                nameof(AllowMerge),
                typeof(bool),
                typeof(FunctionalDetailField),
                new PropertyMetadata(true));

        public bool AllowMerge
        {
            get => (bool)GetValue(AllowMergeProperty);
            set => SetValue(AllowMergeProperty, value);
        }

        public static readonly DependencyProperty OrderProperty =
            DependencyProperty.Register(
                nameof(Order),
                typeof(int),
                typeof(FunctionalDetailField),
                new PropertyMetadata(0));

        public int Order
        {
            get => (int)GetValue(OrderProperty);
            set => SetValue(OrderProperty, value);
        }
    }
}

