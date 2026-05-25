using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TM.Framework.Common.Helpers.UI;

public class CircleProgress : UserControl
{
    private static readonly object _debugLogLock = new();
    private static readonly HashSet<string> _debugLoggedKeys = new();

    private static void DebugLogOnce(string key, Exception ex)
    {
        if (!TM.App.IsDebugMode)
        {
            return;
        }

        lock (_debugLogLock)
        {
            if (!_debugLoggedKeys.Add(key))
            {
                return;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[CircleProgress] {key}: {ex.Message}");
    }

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(CircleProgress),
            new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty StrokeColorProperty =
        DependencyProperty.Register(nameof(StrokeColor), typeof(string), typeof(CircleProgress),
            new PropertyMetadata("#22C55E", OnStrokeColorChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircleProgress),
            new PropertyMetadata(2.5, OnStrokeThicknessChanged));

    private Path _arcPath;

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string StrokeColor
    {
        get => (string)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public CircleProgress()
    {
        _arcPath = new Path
        {
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };

        Content = _arcPath;
        SizeChanged += (s, e) => UpdateArc();
        Loaded += (s, e) => UpdateArc();
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircleProgress cp) cp.UpdateArc();
    }

    private static void OnStrokeColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircleProgress cp) cp.UpdateArc();
    }

    private static void OnStrokeThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircleProgress cp) cp.UpdateArc();
    }

    private void UpdateArc()
    {
        if (_arcPath == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = (size - StrokeThickness) / 2;
        var center = size / 2;

        var progress = Math.Max(0, Math.Min(100, Progress));
        var angle = progress / 100.0 * 360;

        try
        {
            _arcPath.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(StrokeColor));
        }
        catch (Exception ex)
        {
            DebugLogOnce(nameof(UpdateArc), ex);
            _arcPath.Stroke = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }
        _arcPath.StrokeThickness = StrokeThickness;

        if (progress <= 0)
        {
            _arcPath.Data = null;
            return;
        }

        if (progress >= 100)
        {
            _arcPath.Data = new EllipseGeometry(new Point(center, center), radius, radius);
            return;
        }

        var startAngle = -90;
        var endAngle = startAngle + angle;

        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        var startX = center + radius * Math.Cos(startRad);
        var startY = center + radius * Math.Sin(startRad);
        var endX = center + radius * Math.Cos(endRad);
        var endY = center + radius * Math.Sin(endRad);

        var isLargeArc = angle > 180;

        var pathFigure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false
        };

        pathFigure.Segments.Add(new ArcSegment
        {
            Point = new Point(endX, endY),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);
        _arcPath.Data = pathGeometry;
    }
}
