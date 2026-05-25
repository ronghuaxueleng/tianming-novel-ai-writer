using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Appearance.ThemeManagement.ThemeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ColorPickerControl : UserControl
    {
        private static readonly SolidColorBrush _defaultWhite = FreezeB(Colors.White);
        private static SolidColorBrush FreezeB(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(ColorPickerControl), new PropertyMetadata("颜色"));

        public static readonly DependencyProperty SelectedBrushProperty =
            DependencyProperty.Register(nameof(SelectedBrush), typeof(SolidColorBrush), typeof(ColorPickerControl),
                new FrameworkPropertyMetadata(_defaultWhite, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public SolidColorBrush SelectedBrush
        {
            get => (SolidColorBrush)GetValue(SelectedBrushProperty);
            set => SetValue(SelectedBrushProperty, value);
        }

        public ColorPickerControl()
        {
            InitializeComponent();
        }

        private void SelectColor_Click(object sender, RoutedEventArgs e)
        {
            var newColor = ColorPickerDialog.Show(SelectedBrush.Color, Window.GetWindow(this));

            if (newColor.HasValue)
            {
                var b = new SolidColorBrush(newColor.Value);
                b.Freeze();
                SelectedBrush = b;
            }
        }
    }
}

