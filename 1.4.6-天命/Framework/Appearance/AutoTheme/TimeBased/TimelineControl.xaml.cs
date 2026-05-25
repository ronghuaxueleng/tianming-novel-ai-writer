using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TimelineControl : UserControl
    {
        private const double CanvasWidth = 720;
        private const double CanvasHeight = 60;
        private const double PixelsPerHour = 30;

        private DispatcherTimer? _updateTimer;

        public static readonly DependencyProperty SchedulesProperty =
            DependencyProperty.Register(nameof(Schedules), typeof(List<TimeScheduleItem>), typeof(TimelineControl),
                new PropertyMetadata(null, OnSchedulesChanged));

        public List<TimeScheduleItem>? Schedules
        {
            get => (List<TimeScheduleItem>?)GetValue(SchedulesProperty);
            set => SetValue(SchedulesProperty, value);
        }

        public TimelineControl()
        {
            InitializeComponent();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _updateTimer.Tick += (s, e) => UpdateCurrentTimeIndicator();
            _updateTimer.Start();

            Loaded += (s, e) => 
            {
                DrawTimeScale();
                UpdateCurrentTimeIndicator();
                DrawSchedules();
            };

            Unloaded += (s, e) => _updateTimer?.Stop();
        }

        private static void OnSchedulesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl control)
            {
                control.DrawSchedules();
            }
        }

        private void DrawTimeScale()
        {
            var borderColor = TryFindResource("BorderBrush") as Color? ?? Colors.Gray;
            var textColor = TryFindResource("TextSecondary") as Color? ?? Colors.Gray;

            for (int hour = 0; hour <= 24; hour += 3)
            {
                double x = hour * PixelsPerHour;

                var tickLine = new Line
                {
                    X1 = x,
                    Y1 = 25,
                    X2 = x,
                    Y2 = 35,
                    Stroke = new SolidColorBrush(borderColor),
                    StrokeThickness = 1
                };
                TimelineCanvas.Children.Add(tickLine);

                var label = new TextBlock
                {
                    Text = $"{hour:00}:00",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(textColor)
                };
                Canvas.SetLeft(label, x - 15);
                Canvas.SetTop(label, 40);
                TimelineCanvas.Children.Add(label);
            }
        }

        private void DrawSchedules()
        {
            var toRemove = TimelineCanvas.Children.OfType<Rectangle>().ToList();
            foreach (var rect in toRemove)
            {
                TimelineCanvas.Children.Remove(rect);
            }

            if (Schedules == null || !Schedules.Any()) return;

            DrawConflictAreas();

            foreach (var schedule in Schedules)
            {
                DrawScheduleBlock(schedule);
            }
        }

        private void DrawScheduleBlock(TimeScheduleItem schedule)
        {
            double startX = schedule.StartTime.TotalHours * PixelsPerHour;
            double endX = schedule.EndTime.TotalHours * PixelsPerHour;

            if (schedule.StartTime > schedule.EndTime)
            {
                DrawSingleBlock(startX, CanvasWidth, schedule.TargetTheme);
                DrawSingleBlock(0, endX, schedule.TargetTheme);
            }
            else
            {
                DrawSingleBlock(startX, endX, schedule.TargetTheme);
            }
        }

        private void DrawSingleBlock(double startX, double endX, ThemeType theme)
        {
            var primaryColor = TryFindResource("PrimaryColor") as Color? ?? Colors.DodgerBlue;

            var rect = new Rectangle
            {
                Width = endX - startX,
                Height = 20,
                Fill = new SolidColorBrush(primaryColor),
                Opacity = 0.6,
                RadiusX = 4,
                RadiusY = 4
            };

            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, 20);
            TimelineCanvas.Children.Add(rect);

            if (endX - startX > 30)
            {
                var label = new TextBlock
                {
                    Text = theme.ToString(),
                    FontSize = 10,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(label, startX + 5);
                Canvas.SetTop(label, 23);
                TimelineCanvas.Children.Add(label);
            }
        }

        private void DrawConflictAreas()
        {
            if (Schedules == null || Schedules.Count < 2) return;

            var now = DateTime.Now;
            var currentWeekday = ConvertDayOfWeek(now.DayOfWeek);

            for (int i = 0; i < Schedules.Count; i++)
            {
                for (int j = i + 1; j < Schedules.Count; j++)
                {
                    var s1 = Schedules[i];
                    var s2 = Schedules[j];

                    if ((s1.EnabledWeekdays & currentWeekday) == 0 || (s2.EnabledWeekdays & currentWeekday) == 0)
                        continue;

                    if (IsOverlapping(s1, s2))
                    {
                        DrawConflictBlock(s1, s2);
                    }
                }
            }
        }

        private bool IsOverlapping(TimeScheduleItem s1, TimeScheduleItem s2)
        {
            bool s1CrossMidnight = s1.StartTime > s1.EndTime;
            bool s2CrossMidnight = s2.StartTime > s2.EndTime;

            if (!s1CrossMidnight && !s2CrossMidnight)
            {
                return s1.StartTime < s2.EndTime && s2.StartTime < s1.EndTime;
            }
            else
            {
                return true;
            }
        }

        private void DrawConflictBlock(TimeScheduleItem s1, TimeScheduleItem s2)
        {
            double overlapStart = Math.Max(s1.StartTime.TotalHours, s2.StartTime.TotalHours);
            double overlapEnd = Math.Min(s1.EndTime.TotalHours, s2.EndTime.TotalHours);

            if (overlapEnd > overlapStart)
            {
                var dangerColor = TryFindResource("DangerColor") as Color? ?? Colors.Red;

                var rect = new Rectangle
                {
                    Width = (overlapEnd - overlapStart) * PixelsPerHour,
                    Height = 20,
                    Fill = new SolidColorBrush(dangerColor),
                    Opacity = 0.3
                };

                Canvas.SetLeft(rect, overlapStart * PixelsPerHour);
                Canvas.SetTop(rect, 20);
                TimelineCanvas.Children.Add(rect);
            }
        }

        private void UpdateCurrentTimeIndicator()
        {
            var now = DateTime.Now.TimeOfDay;
            double x = now.TotalHours * PixelsPerHour;

            if (CurrentTimeIndicator.RenderTransform is TranslateTransform transform)
            {
                transform.X = x;
            }
        }

        private Weekday ConvertDayOfWeek(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => Weekday.Monday,
                DayOfWeek.Tuesday => Weekday.Tuesday,
                DayOfWeek.Wednesday => Weekday.Wednesday,
                DayOfWeek.Thursday => Weekday.Thursday,
                DayOfWeek.Friday => Weekday.Friday,
                DayOfWeek.Saturday => Weekday.Saturday,
                DayOfWeek.Sunday => Weekday.Sunday,
                _ => Weekday.None
            };
        }
    }
}

