using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.Common.Search
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SearchTextBox : UserControl
    {
        public SearchTextBox()
        {
            InitializeComponent();
        }

        #region SearchKeyword 依赖属性

        public string SearchKeyword
        {
            get { return (string)GetValue(SearchKeywordProperty); }
            set { SetValue(SearchKeywordProperty, value); }
        }

        public static readonly DependencyProperty SearchKeywordProperty =
            DependencyProperty.Register("SearchKeyword", typeof(string), typeof(SearchTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        #endregion

        #region PlaceholderText 依赖属性

        public string PlaceholderText
        {
            get { return (string)GetValue(PlaceholderTextProperty); }
            set { SetValue(PlaceholderTextProperty, value); }
        }

        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.Register("PlaceholderText", typeof(string), typeof(SearchTextBox),
                new PropertyMetadata("搜索..."));

        #endregion
    }
}

