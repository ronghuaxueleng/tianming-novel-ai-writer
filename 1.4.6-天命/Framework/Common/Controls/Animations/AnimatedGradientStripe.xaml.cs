using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TM.Framework.Common.Controls.Animations;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class AnimatedGradientStripe : UserControl
{
    private LinearGradientBrush? _stripeBrush;
    private TranslateTransform? _transform;

    public AnimatedGradientStripe()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region Dependency Properties

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(Orientation.Vertical, OnVisualPropertyChanged));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(
            nameof(Thickness),
            typeof(double),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(3d, OnThicknessChanged));

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty BaseBrushProperty =
        DependencyProperty.Register(
            nameof(BaseBrush),
            typeof(SolidColorBrush),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x7D, 0xE2)), OnVisualPropertyChanged));

    public SolidColorBrush BaseBrush
    {
        get => (SolidColorBrush)GetValue(BaseBrushProperty);
        set => SetValue(BaseBrushProperty, value);
    }

    public static readonly DependencyProperty PulseBrushProperty =
        DependencyProperty.Register(
            nameof(PulseBrush),
            typeof(SolidColorBrush),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x8C, 0xF8, 0xBC)), OnVisualPropertyChanged));

    public SolidColorBrush PulseBrush
    {
        get => (SolidColorBrush)GetValue(PulseBrushProperty);
        set => SetValue(PulseBrushProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(
            nameof(AnimationDuration),
            typeof(TimeSpan),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(TimeSpan.FromSeconds(1.6), OnVisualPropertyChanged));

    public TimeSpan AnimationDuration
    {
        get => (TimeSpan)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public static readonly DependencyProperty StripeCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(StripeCornerRadius),
            typeof(CornerRadius),
            typeof(AnimatedGradientStripe),
            new PropertyMetadata(new CornerRadius(0), OnCornerRadiusChanged));

    public CornerRadius StripeCornerRadius
    {
        get => (CornerRadius)GetValue(StripeCornerRadiusProperty);
        set => SetValue(StripeCornerRadiusProperty, value);
    }

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyThickness();
        RefreshVisual();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedGradientStripe stripe && stripe.IsLoaded)
        {
            stripe.RefreshVisual();
        }
    }

    private static void OnThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedGradientStripe stripe && stripe.IsLoaded)
        {
            stripe.ApplyThickness();
        }
    }

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedGradientStripe stripe && stripe.IsLoaded)
        {
            stripe.StripeBorder.CornerRadius = stripe.StripeCornerRadius;
        }
    }

    private void ApplyThickness()
    {
        if (Orientation == Orientation.Vertical)
        {
            Width = Thickness;
            Height = double.NaN;
        }
        else
        {
            Height = Thickness;
            Width = double.NaN;
        }
    }

    private void RefreshVisual()
    {
        ApplyThickness();
        EnsureBrush();

        if (_stripeBrush == null)
        {
            return;
        }

        UpdateGradientStops();

        if (IsActive)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void EnsureBrush()
    {
        StopAnimation();

        _transform = new TranslateTransform();

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = Orientation == Orientation.Vertical ? new Point(0, 0) : new Point(0, 0),
            EndPoint = Orientation == Orientation.Vertical ? new Point(0, 1) : new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            RelativeTransform = _transform
        };

        var baseColor = GetBaseColor();
        var pulseColor = GetPulseColor();
        var brighterPulseColor = GetBrighterPulseColor();

        brush.GradientStops.Add(new GradientStop(baseColor, 0));
        brush.GradientStops.Add(new GradientStop(baseColor, 0.35));
        brush.GradientStops.Add(new GradientStop(pulseColor, 0.45));
        brush.GradientStops.Add(new GradientStop(brighterPulseColor, 0.5));
        brush.GradientStops.Add(new GradientStop(pulseColor, 0.55));
        brush.GradientStops.Add(new GradientStop(baseColor, 0.65));
        brush.GradientStops.Add(new GradientStop(baseColor, 1));

        _stripeBrush = brush;
        StripeBorder.Background = brush;
    }

    private void UpdateGradientStops()
    {
        if (_stripeBrush == null)
        {
            return;
        }

        var baseColor = GetBaseColor();
        var pulseColor = GetPulseColor();
        var brighterPulseColor = GetBrighterPulseColor();

        if (_stripeBrush.GradientStops.Count >= 7)
        {
            _stripeBrush.GradientStops[0].Color = baseColor;
            _stripeBrush.GradientStops[1].Color = baseColor;
            _stripeBrush.GradientStops[2].Color = pulseColor;
            _stripeBrush.GradientStops[3].Color = brighterPulseColor;
            _stripeBrush.GradientStops[4].Color = pulseColor;
            _stripeBrush.GradientStops[5].Color = baseColor;
            _stripeBrush.GradientStops[6].Color = baseColor;
        }

        _stripeBrush.StartPoint = Orientation == Orientation.Vertical ? new Point(0, 0) : new Point(0, 0);
        _stripeBrush.EndPoint = Orientation == Orientation.Vertical ? new Point(0, 1) : new Point(1, 0);
    }

    private void StartAnimation()
    {
        if (_transform == null)
        {
            return;
        }

        var duration = AnimationDuration <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1.6)
            : AnimationDuration;

        var property = Orientation == Orientation.Vertical
            ? TranslateTransform.YProperty
            : TranslateTransform.XProperty;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(duration),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _transform.BeginAnimation(property, animation);
    }

    private void StopAnimation()
    {
        if (_transform == null)
        {
            return;
        }

        var property = Orientation == Orientation.Vertical
            ? TranslateTransform.YProperty
            : TranslateTransform.XProperty;

        _transform.BeginAnimation(property, null);
        _transform.X = 0;
        _transform.Y = 0;
    }

    private Color GetBaseColor()
    {
        return (BaseBrush ?? new SolidColorBrush(Colors.Transparent)).Color;
    }

    private Color GetPulseColor()
    {
        return (PulseBrush ?? new SolidColorBrush(GetBaseColor())).Color;
    }

    private Color GetBrighterPulseColor()
    {
        var pulseColor = GetPulseColor();

        byte r = (byte)Math.Min(255, pulseColor.R + (255 - pulseColor.R) * 0.4);
        byte g = (byte)Math.Min(255, pulseColor.G + (255 - pulseColor.G) * 0.4);
        byte b = (byte)Math.Min(255, pulseColor.B + (255 - pulseColor.B) * 0.4);

        return Color.FromArgb(pulseColor.A, r, g, b);
    }
}

