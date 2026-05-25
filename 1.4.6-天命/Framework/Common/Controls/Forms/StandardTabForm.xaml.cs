using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.Common.Controls.Forms
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class StandardTabForm : UserControl
    {
        private readonly StandardFormOptionsService _optionsService;

        public StandardTabForm()
        {
            InitializeComponent();

            _optionsService = ServiceLocator.Get<StandardFormOptionsService>();
            LoadOptions();
        }

        private void LoadOptions()
        {
            var options = _optionsService.GetOptions();

            PriorityOptions.Clear();
            foreach (var option in options.PriorityOptions)
            {
                PriorityOptions.Add(option);
            }

            ScopeOptions.Clear();
            foreach (var option in options.ScopeOptions)
            {
                ScopeOptions.Add(option);
            }

            NovelGenreOptions.Clear();
            foreach (var option in options.NovelGenreOptions)
            {
                NovelGenreOptions.Add(option);
            }

            TM.App.Log($"[StandardTabForm] 加载配置选项: {PriorityOptions.Count}个优先级, {ScopeOptions.Count}个范围, {NovelGenreOptions.Count}个风格");
        }

        #region 字段显示控制

        public static readonly DependencyProperty ShowNameFieldProperty =
            DependencyProperty.Register(
                nameof(ShowNameField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(true));

        public bool ShowNameField
        {
            get => (bool)GetValue(ShowNameFieldProperty);
            set => SetValue(ShowNameFieldProperty, value);
        }

        public static readonly DependencyProperty ShowCategoryFieldProperty =
            DependencyProperty.Register(
                nameof(ShowCategoryField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(true));

        public bool ShowCategoryField
        {
            get => (bool)GetValue(ShowCategoryFieldProperty);
            set => SetValue(ShowCategoryFieldProperty, value);
        }

        public static readonly DependencyProperty ShowPriorityFieldProperty =
            DependencyProperty.Register(
                nameof(ShowPriorityField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(false));

        public bool ShowPriorityField
        {
            get => (bool)GetValue(ShowPriorityFieldProperty);
            set => SetValue(ShowPriorityFieldProperty, value);
        }

        public static readonly DependencyProperty ShowScopeFieldProperty =
            DependencyProperty.Register(
                nameof(ShowScopeField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(false));

        public bool ShowScopeField
        {
            get => (bool)GetValue(ShowScopeFieldProperty);
            set => SetValue(ShowScopeFieldProperty, value);
        }

        public static readonly DependencyProperty ShowAIWeightFieldProperty =
            DependencyProperty.Register(
                nameof(ShowAIWeightField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(false));

        public bool ShowAIWeightField
        {
            get => (bool)GetValue(ShowAIWeightFieldProperty);
            set => SetValue(ShowAIWeightFieldProperty, value);
        }

        public static readonly DependencyProperty ShowGenresFieldProperty =
            DependencyProperty.Register(
                nameof(ShowGenresField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(false));

        public bool ShowGenresField
        {
            get => (bool)GetValue(ShowGenresFieldProperty);
            set => SetValue(ShowGenresFieldProperty, value);
        }

        public static readonly DependencyProperty ShowDescriptionFieldProperty =
            DependencyProperty.Register(
                nameof(ShowDescriptionField),
                typeof(bool),
                typeof(StandardTabForm),
                new PropertyMetadata(true));

        public bool ShowDescriptionField
        {
            get => (bool)GetValue(ShowDescriptionFieldProperty);
            set => SetValue(ShowDescriptionFieldProperty, value);
        }

        #endregion

        #region 字段标签

        public static readonly DependencyProperty NameFieldLabelProperty =
            DependencyProperty.Register(
                nameof(NameFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("名称"));

        public string NameFieldLabel
        {
            get => (string)GetValue(NameFieldLabelProperty);
            set => SetValue(NameFieldLabelProperty, value);
        }

        public static readonly DependencyProperty CategoryFieldLabelProperty =
            DependencyProperty.Register(
                nameof(CategoryFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("分类"));

        public string CategoryFieldLabel
        {
            get => (string)GetValue(CategoryFieldLabelProperty);
            set => SetValue(CategoryFieldLabelProperty, value);
        }

        public static readonly DependencyProperty PriorityFieldLabelProperty =
            DependencyProperty.Register(
                nameof(PriorityFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("优先级（冲突仲裁时生效）"));

        public string PriorityFieldLabel
        {
            get => (string)GetValue(PriorityFieldLabelProperty);
            set => SetValue(PriorityFieldLabelProperty, value);
        }

        public static readonly DependencyProperty ScopeFieldLabelProperty =
            DependencyProperty.Register(
                nameof(ScopeFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("作用范围"));

        public string ScopeFieldLabel
        {
            get => (string)GetValue(ScopeFieldLabelProperty);
            set => SetValue(ScopeFieldLabelProperty, value);
        }

        public static readonly DependencyProperty AIWeightFieldLabelProperty =
            DependencyProperty.Register(
                nameof(AIWeightFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("AI生成影响权重"));

        public string AIWeightFieldLabel
        {
            get => (string)GetValue(AIWeightFieldLabelProperty);
            set => SetValue(AIWeightFieldLabelProperty, value);
        }

        public static readonly DependencyProperty GenresFieldLabelProperty =
            DependencyProperty.Register(
                nameof(GenresFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("适用小说风格（多选）"));

        public string GenresFieldLabel
        {
            get => (string)GetValue(GenresFieldLabelProperty);
            set => SetValue(GenresFieldLabelProperty, value);
        }

        public static readonly DependencyProperty DescriptionFieldLabelProperty =
            DependencyProperty.Register(
                nameof(DescriptionFieldLabel),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata("描述"));

        public string DescriptionFieldLabel
        {
            get => (string)GetValue(DescriptionFieldLabelProperty);
            set => SetValue(DescriptionFieldLabelProperty, value);
        }

        #endregion

        #region 字段值（双向绑定）

        public static readonly DependencyProperty NameValueProperty =
            DependencyProperty.Register(
                nameof(NameValue),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string NameValue
        {
            get => (string)GetValue(NameValueProperty);
            set => SetValue(NameValueProperty, value);
        }

        public static readonly DependencyProperty PriorityValueProperty =
            DependencyProperty.Register(
                nameof(PriorityValue),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata("中", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string PriorityValue
        {
            get => (string)GetValue(PriorityValueProperty);
            set => SetValue(PriorityValueProperty, value);
        }

        public static readonly DependencyProperty ScopeValueProperty =
            DependencyProperty.Register(
                nameof(ScopeValue),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata("全局", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string ScopeValue
        {
            get => (string)GetValue(ScopeValueProperty);
            set => SetValue(ScopeValueProperty, value);
        }

        public static readonly DependencyProperty AIWeightValueProperty =
            DependencyProperty.Register(
                nameof(AIWeightValue),
                typeof(int),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(100, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int AIWeightValue
        {
            get => (int)GetValue(AIWeightValueProperty);
            set => SetValue(AIWeightValueProperty, value);
        }

        public static readonly DependencyProperty DescriptionValueProperty =
            DependencyProperty.Register(
                nameof(DescriptionValue),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string DescriptionValue
        {
            get => (string)GetValue(DescriptionValueProperty);
            set => SetValue(DescriptionValueProperty, value);
        }

        #endregion

        #region 分类/类型选择器配置

        public static readonly DependencyProperty CategoryTreeProperty =
            DependencyProperty.Register(
                nameof(CategoryTree),
                typeof(ObservableCollection<TreeNodeItem>),
                typeof(StandardTabForm),
                new PropertyMetadata(null));

        public ObservableCollection<TreeNodeItem> CategoryTree
        {
            get => (ObservableCollection<TreeNodeItem>)GetValue(CategoryTreeProperty);
            set => SetValue(CategoryTreeProperty, value);
        }

        public static readonly DependencyProperty CategoryPathProperty =
            DependencyProperty.Register(
                nameof(CategoryPath),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string CategoryPath
        {
            get => (string)GetValue(CategoryPathProperty);
            set => SetValue(CategoryPathProperty, value);
        }

        public static readonly DependencyProperty CategoryIconProperty =
            DependencyProperty.Register(
                nameof(CategoryIcon),
                typeof(string),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata("🏠", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string CategoryIcon
        {
            get => (string)GetValue(CategoryIconProperty);
            set => SetValue(CategoryIconProperty, value);
        }

        public static readonly DependencyProperty CurrentLevelProperty =
            DependencyProperty.Register(
                nameof(CurrentLevel),
                typeof(int),
                typeof(StandardTabForm),
                new PropertyMetadata(1));

        public int CurrentLevel
        {
            get => (int)GetValue(CurrentLevelProperty);
            set => SetValue(CurrentLevelProperty, value);
        }

        public static readonly DependencyProperty CurrentPathProperty =
            DependencyProperty.Register(
                nameof(CurrentPath),
                typeof(string),
                typeof(StandardTabForm),
                new PropertyMetadata(""));

        public string CurrentPath
        {
            get => (string)GetValue(CurrentPathProperty);
            set => SetValue(CurrentPathProperty, value);
        }

        public static readonly DependencyProperty CategoryMaxLevelProperty =
            DependencyProperty.Register(
                nameof(CategoryMaxLevel),
                typeof(int),
                typeof(StandardTabForm),
                new PropertyMetadata(5));

        public int CategoryMaxLevel
        {
            get => (int)GetValue(CategoryMaxLevelProperty);
            set => SetValue(CategoryMaxLevelProperty, value);
        }

        public static readonly DependencyProperty CategorySelectCommandProperty =
            DependencyProperty.Register(
                nameof(CategorySelectCommand),
                typeof(ICommand),
                typeof(StandardTabForm),
                new PropertyMetadata(null));

        public ICommand CategorySelectCommand
        {
            get => (ICommand)GetValue(CategorySelectCommandProperty);
            set => SetValue(CategorySelectCommandProperty, value);
        }

        public static readonly DependencyProperty IsTreeComboBoxOpenProperty =
            DependencyProperty.Register(
                nameof(IsTreeComboBoxOpen),
                typeof(bool),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsTreeComboBoxOpen
        {
            get => (bool)GetValue(IsTreeComboBoxOpenProperty);
            set => SetValue(IsTreeComboBoxOpenProperty, value);
        }

        #endregion

        #region NovelGenres CheckBox事件处理

        private void GenreCheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string genre)
            {
                if (NovelGenresValue != null && NovelGenresValue.Contains(genre))
                {
                    checkBox.IsChecked = true;
                }
            }
        }

        private void GenreCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string genre)
            {
                if (NovelGenresValue == null)
                {
                    NovelGenresValue = new ObservableCollection<string>();
                }

                if (!NovelGenresValue.Contains(genre))
                {
                    NovelGenresValue.Add(genre);
                    TM.App.Log($"[StandardTabForm] 添加风格: {genre}");
                }
            }
        }

        private void GenreCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string genre)
            {
                if (NovelGenresValue != null && NovelGenresValue.Contains(genre))
                {
                    NovelGenresValue.Remove(genre);
                    TM.App.Log($"[StandardTabForm] 移除风格: {genre}");
                }
            }
        }

        #endregion

        #region 动态选项集合

        public ObservableCollection<string> PriorityOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> ScopeOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> NovelGenreOptions { get; } = new ObservableCollection<string>();

        public static readonly DependencyProperty NovelGenresValueProperty =
            DependencyProperty.Register(
                nameof(NovelGenresValue),
                typeof(ObservableCollection<string>),
                typeof(StandardTabForm),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ObservableCollection<string> NovelGenresValue
        {
            get => (ObservableCollection<string>)GetValue(NovelGenresValueProperty);
            set => SetValue(NovelGenresValueProperty, value);
        }

        #endregion
    }
}

