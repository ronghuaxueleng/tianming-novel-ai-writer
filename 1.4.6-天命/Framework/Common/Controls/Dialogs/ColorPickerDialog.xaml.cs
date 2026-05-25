using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TM.Framework.Common.Controls.Dialogs
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ColorPickerDialog : Window
    {
        private double currentHue = 0;
        private double currentSaturation = 1;
        private double currentValue = 1;
        private bool isMouseDownOnColorCanvas = false;
        private bool isMouseDownOnHueCanvas = false;

        public Color SelectedColor { get; private set; }

        public ColorPickerDialog(Color initialColor)
        {
            InitializeComponent();

            SelectedColor = initialColor;
            ColorToHSV(initialColor, out currentHue, out currentSaturation, out currentValue);

            Loaded += (s, e) =>
            {
                DrawColorCanvas();
                DrawHueSlider();
                UpdatePreview();
            };
        }

        private void DrawColorCanvas()
        {
            ColorCanvas.Children.Clear();

            int width = (int)ColorCanvas.ActualWidth;
            int height = (int)ColorCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var saturationGradient = new LinearGradientBrush();
            saturationGradient.StartPoint = new Point(0, 0);
            saturationGradient.EndPoint = new Point(1, 0);

            Color hueColor = HSVToColor(currentHue, 1, 1);
            saturationGradient.GradientStops.Add(new GradientStop(Colors.White, 0));
            saturationGradient.GradientStops.Add(new GradientStop(hueColor, 1));

            var saturationRect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = saturationGradient
            };
            ColorCanvas.Children.Add(saturationRect);

            var valueGradient = new LinearGradientBrush();
            valueGradient.StartPoint = new Point(0, 0);
            valueGradient.EndPoint = new Point(0, 1);
            valueGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            valueGradient.GradientStops.Add(new GradientStop(Colors.Black, 1));

            var valueRect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = valueGradient
            };
            ColorCanvas.Children.Add(valueRect);
        }

        private void DrawHueSlider()
        {
            HueCanvas.Children.Clear();

            int width = (int)HueCanvas.ActualWidth;
            int height = (int)HueCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var hueGradient = new LinearGradientBrush();
            hueGradient.StartPoint = new Point(0, 0);
            hueGradient.EndPoint = new Point(0, 1);

            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 0), 0.0));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 0), 0.17));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 0), 0.33));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 255), 0.5));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 255), 0.67));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 255), 0.83));
            hueGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 0), 1.0));

            var hueRect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = hueGradient
            };
            HueCanvas.Children.Add(hueRect);
        }

        private void ColorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDownOnColorCanvas = true;
            ColorCanvas.CaptureMouse();
            UpdateColorFromCanvas(e.GetPosition(ColorCanvas));
        }

        private void ColorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDownOnColorCanvas)
            {
                UpdateColorFromCanvas(e.GetPosition(ColorCanvas));
            }
        }

        private void ColorCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isMouseDownOnColorCanvas = false;
            ColorCanvas.ReleaseMouseCapture();
        }

        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDownOnHueCanvas = true;
            HueCanvas.CaptureMouse();
            UpdateHueFromCanvas(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDownOnHueCanvas)
            {
                UpdateHueFromCanvas(e.GetPosition(HueCanvas));
            }
        }

        private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isMouseDownOnHueCanvas = false;
            HueCanvas.ReleaseMouseCapture();
        }

        private void UpdateColorFromCanvas(Point position)
        {
            double s = Math.Max(0, Math.Min(1, position.X / ColorCanvas.ActualWidth));
            double v = Math.Max(0, Math.Min(1, 1.0 - position.Y / ColorCanvas.ActualHeight));

            currentSaturation = s;
            currentValue = v;
            UpdatePreview();
        }

        private void UpdateHueFromCanvas(Point position)
        {
            double hue = Math.Max(0, Math.Min(360, position.Y / HueCanvas.ActualHeight * 360));
            currentHue = hue;
            DrawColorCanvas();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            SelectedColor = HSVToColor(currentHue, currentSaturation, currentValue);
            PreviewBorder.Background = new SolidColorBrush(SelectedColor);

            RValue.Text = SelectedColor.R.ToString();
            GValue.Text = SelectedColor.G.ToString();
            BValue.Text = SelectedColor.B.ToString();
        }

        private Color HSVToColor(double h, double s, double v)
        {
            int hi = (int)(h / 60) % 6;
            double f = h / 60 - hi;

            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            double r, g, b;

            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private void ColorToHSV(Color color, out double h, out double s, out double v)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            if (delta == 0)
                h = 0;
            else if (max == r)
                h = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                h = 60 * (((b - r) / delta) + 2);
            else
                h = 60 * (((r - g) / delta) + 4);

            if (h < 0) h += 360;

            s = max == 0 ? 0 : delta / max;

            v = max;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static Color? Show(Color initialColor, Window? owner = null)
        {
            var dialog = new ColorPickerDialog(initialColor);
            StandardDialog.EnsureOwnerAndTopmost(dialog, owner);

            if (dialog.ShowDialog() == true)
            {
                return dialog.SelectedColor;
            }

            return null;
        }
    }
}

