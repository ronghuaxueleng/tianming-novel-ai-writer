using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.Common.Controls.Forms
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CategoryManagementForm : UserControl
    {
        public CategoryManagementForm()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty MaxLevelProperty =
            DependencyProperty.Register(
                nameof(MaxLevel),
                typeof(int),
                typeof(CategoryManagementForm),
                new PropertyMetadata(5));

        public int MaxLevel
        {
            get => (int)GetValue(MaxLevelProperty);
            set => SetValue(MaxLevelProperty, value);
        }
    }
}

