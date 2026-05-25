using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Common.Controls.Feedback
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class BusyOverlay : UserControl
    {
        private static readonly SolidColorBrush _defaultOverlayBg = FreezeBrush(Color.FromArgb(0xAA, 0, 0, 0));
        private static SolidColorBrush FreezeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public BusyOverlay()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(
                nameof(IsBusy),
                typeof(bool),
                typeof(BusyOverlay),
                new PropertyMetadata(false));

        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        public static readonly DependencyProperty OverlayBackgroundProperty =
            DependencyProperty.Register(
                nameof(OverlayBackground),
                typeof(Brush),
                typeof(BusyOverlay),
                new PropertyMetadata(_defaultOverlayBg));

        public Brush OverlayBackground
        {
            get => (Brush)GetValue(OverlayBackgroundProperty);
            set => SetValue(OverlayBackgroundProperty, value);
        }

        public static readonly DependencyProperty OverlayCornerRadiusProperty =
            DependencyProperty.Register(
                nameof(OverlayCornerRadius),
                typeof(CornerRadius),
                typeof(BusyOverlay),
                new PropertyMetadata(new CornerRadius(0)));

        public CornerRadius OverlayCornerRadius
        {
            get => (CornerRadius)GetValue(OverlayCornerRadiusProperty);
            set => SetValue(OverlayCornerRadiusProperty, value);
        }

        public static readonly DependencyProperty OverlayContentProperty =
            DependencyProperty.Register(
                nameof(OverlayContent),
                typeof(object),
                typeof(BusyOverlay),
                new PropertyMetadata(null));

        public object? OverlayContent
        {
            get => GetValue(OverlayContentProperty);
            set => SetValue(OverlayContentProperty, value);
        }

        public static readonly DependencyProperty OverlayContentTemplateProperty =
            DependencyProperty.Register(
                nameof(OverlayContentTemplate),
                typeof(DataTemplate),
                typeof(BusyOverlay),
                new PropertyMetadata(null));

        public DataTemplate? OverlayContentTemplate
        {
            get => (DataTemplate?)GetValue(OverlayContentTemplateProperty);
            set => SetValue(OverlayContentTemplateProperty, value);
        }
    }
}

