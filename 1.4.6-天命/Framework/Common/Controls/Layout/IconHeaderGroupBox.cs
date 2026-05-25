using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Common.Controls.Layout
{
    public class IconHeaderGroupBox : GroupBox
    {
        static IconHeaderGroupBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(IconHeaderGroupBox),
                new FrameworkPropertyMetadata(typeof(GroupBox)));

            PaddingProperty.OverrideMetadata(typeof(IconHeaderGroupBox),
                new FrameworkPropertyMetadata(new Thickness(5)));
        }

        #region Icon 依赖属性

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(string), typeof(IconHeaderGroupBox),
                new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

        #endregion

        #region Title 依赖属性

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(IconHeaderGroupBox),
                new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

        #endregion

        #region Count 依赖属性

        public int Count
        {
            get { return (int)GetValue(CountProperty); }
            set { SetValue(CountProperty, value); }
        }

        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(int), typeof(IconHeaderGroupBox),
                new PropertyMetadata(-1, OnHeaderPropertyChanged));

        #endregion

        #region IconSize 依赖属性

        public double IconSize
        {
            get { return (double)GetValue(IconSizeProperty); }
            set { SetValue(IconSizeProperty, value); }
        }

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register("IconSize", typeof(double), typeof(IconHeaderGroupBox),
                new PropertyMetadata(14.0, OnHeaderPropertyChanged));

        #endregion

        #region TitleFontSize 依赖属性

        public double TitleFontSize
        {
            get { return (double)GetValue(TitleFontSizeProperty); }
            set { SetValue(TitleFontSizeProperty, value); }
        }

        public static readonly DependencyProperty TitleFontSizeProperty =
            DependencyProperty.Register("TitleFontSize", typeof(double), typeof(IconHeaderGroupBox),
                new PropertyMetadata(14.0, OnHeaderPropertyChanged));

        #endregion

        #region TitleFontWeight 依赖属性

        public FontWeight TitleFontWeight
        {
            get { return (FontWeight)GetValue(TitleFontWeightProperty); }
            set { SetValue(TitleFontWeightProperty, value); }
        }

        public static readonly DependencyProperty TitleFontWeightProperty =
            DependencyProperty.Register("TitleFontWeight", typeof(FontWeight), typeof(IconHeaderGroupBox),
                new PropertyMetadata(FontWeights.Bold, OnHeaderPropertyChanged));

        #endregion

        #region RequiredMark 依赖属性

        public bool RequiredMark
        {
            get { return (bool)GetValue(RequiredMarkProperty); }
            set { SetValue(RequiredMarkProperty, value); }
        }

        public static readonly DependencyProperty RequiredMarkProperty =
            DependencyProperty.Register("RequiredMark", typeof(bool), typeof(IconHeaderGroupBox),
                new PropertyMetadata(false, OnHeaderPropertyChanged));

        #endregion

        #region ShowCount 依赖属性

        public bool ShowCount
        {
            get { return (bool)GetValue(ShowCountProperty); }
            set { SetValue(ShowCountProperty, value); }
        }

        public static readonly DependencyProperty ShowCountProperty =
            DependencyProperty.Register("ShowCount", typeof(bool), typeof(IconHeaderGroupBox),
                new PropertyMetadata(false, OnHeaderPropertyChanged));

        #endregion

        private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IconHeaderGroupBox control)
            {
                control.UpdateHeader();
            }
        }

        private void UpdateHeader()
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            if (!string.IsNullOrEmpty(Icon))
            {
                var iconBlock = new Emoji.Wpf.TextBlock
                {
                    Text = Icon,
                    FontSize = IconSize,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                stackPanel.Children.Add(iconBlock);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                var titleBlock = new TextBlock
                {
                    Text = Title,
                    FontSize = TitleFontSize,
                    FontWeight = TitleFontWeight
                };
                if (RequiredMark)
                    titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DangerColor");
                stackPanel.Children.Add(titleBlock);
            }

            if (RequiredMark)
            {
                var requiredBlock = new TextBlock
                {
                    Text = "（必选）",
                    FontSize = TitleFontSize,
                    FontWeight = TitleFontWeight,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                requiredBlock.SetResourceReference(TextBlock.ForegroundProperty, "DangerColor");
                stackPanel.Children.Add(requiredBlock);
            }

            if (ShowCount && Count >= 0)
            {
                var countBlock = new TextBlock
                {
                    Text = $"({Count})",
                    FontSize = 12,
                    Margin = new Thickness(5, 0, 0, 0)
                };

                countBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");

                stackPanel.Children.Add(countBlock);
            }

            Header = stackPanel;
        }

        public IconHeaderGroupBox()
        {
            Loaded += (s, e) => UpdateHeader();
        }
    }
}

