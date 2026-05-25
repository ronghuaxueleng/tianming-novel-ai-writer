using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.Common.Controls.Layout
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SingleTabContainer : UserControl
    {
        public SingleTabContainer()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty PageContentProperty =
            DependencyProperty.Register(
                nameof(PageContent),
                typeof(object),
                typeof(SingleTabContainer),
                new PropertyMetadata(null));

        public object PageContent
        {
            get => GetValue(PageContentProperty);
            set => SetValue(PageContentProperty, value);
        }

        public static readonly DependencyProperty ContentPaddingProperty =
            DependencyProperty.Register(
                nameof(ContentPadding),
                typeof(Thickness),
                typeof(SingleTabContainer),
                new PropertyMetadata(new Thickness(5)));

        public Thickness ContentPadding
        {
            get => (Thickness)GetValue(ContentPaddingProperty);
            set => SetValue(ContentPaddingProperty, value);
        }
    }
}

