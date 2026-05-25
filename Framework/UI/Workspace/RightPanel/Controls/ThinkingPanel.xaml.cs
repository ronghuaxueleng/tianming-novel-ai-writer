using System;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ThinkingPanel : UserControl
    {
        #region 依赖属性

        public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
            nameof(Summary), typeof(string), typeof(ThinkingPanel),
            new PropertyMetadata(null, OnSummaryChanged));

        public string? Summary
        {
            get => (string?)GetValue(SummaryProperty);
            set => SetValue(SummaryProperty, value);
        }

        public static readonly DependencyProperty IsProcessingProperty = DependencyProperty.Register(
            nameof(IsProcessing), typeof(bool), typeof(ThinkingPanel),
            new PropertyMetadata(false, OnIsProcessingChanged));

        public bool IsProcessing
        {
            get => (bool)GetValue(IsProcessingProperty);
            set => SetValue(IsProcessingProperty, value);
        }

        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(ThinkingPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
            nameof(Blocks), typeof(IEnumerable), typeof(ThinkingPanel),
            new PropertyMetadata(null));

        public IEnumerable? Blocks
        {
            get => (IEnumerable?)GetValue(BlocksProperty);
            set => SetValue(BlocksProperty, value);
        }

        public static readonly DependencyProperty AnalysisRawProperty = DependencyProperty.Register(
            nameof(AnalysisRaw), typeof(string), typeof(ThinkingPanel),
            new PropertyMetadata(string.Empty, OnAnalysisRawChanged));

        public static readonly DependencyProperty CurrentPhaseProperty = DependencyProperty.Register(
            nameof(CurrentPhase), typeof(string), typeof(ThinkingPanel),
            new PropertyMetadata(null, OnCurrentPhaseChanged));

        public string? CurrentPhase
        {
            get => (string?)GetValue(CurrentPhaseProperty);
            set => SetValue(CurrentPhaseProperty, value);
        }

        public string AnalysisRaw
        {
            get => (string)GetValue(AnalysisRawProperty);
            set => SetValue(AnalysisRawProperty, value);
        }

        private static void OnCurrentPhaseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ThinkingPanel self) return;
            if (self.IsProcessing)
            {
                var seconds = (DateTime.Now - self._thinkingStartTime).TotalSeconds;
                self.SetValue(EffectiveSummaryPropertyKey, FormatThinkingText(seconds, self.GetCurrentKind()));
            }
            else
            {
                self.SetValue(EffectiveSummaryPropertyKey, self.Summary);
            }
        }

        public static readonly DependencyProperty ToggleCommandProperty = DependencyProperty.Register(
            nameof(ToggleCommand), typeof(ICommand), typeof(ThinkingPanel),
            new PropertyMetadata(null));

        public ICommand? ToggleCommand
        {
            get => (ICommand?)GetValue(ToggleCommandProperty);
            set => SetValue(ToggleCommandProperty, value);
        }

        private static readonly DependencyPropertyKey EffectiveSummaryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EffectiveSummary), typeof(string), typeof(ThinkingPanel),
            new PropertyMetadata(null));

        public static readonly DependencyProperty EffectiveSummaryProperty = EffectiveSummaryPropertyKey.DependencyProperty;

        public string? EffectiveSummary => (string?)GetValue(EffectiveSummaryProperty);

        #endregion

        #region 实时计时

        private DispatcherTimer? _elapsedTimer;
        private DateTime _thinkingStartTime;

        private void StartElapsedTimer()
        {
            _thinkingStartTime = DateTime.Now;
            SetValue(EffectiveSummaryPropertyKey, FormatThinkingText(0.0, GetCurrentKind()));

            if (_elapsedTimer == null)
            {
                _elapsedTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _elapsedTimer.Tick += OnElapsedTick;
            }
            _elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            _elapsedTimer?.Stop();
            SetValue(EffectiveSummaryPropertyKey, Summary);
        }

        private void OnElapsedTick(object? sender, EventArgs e)
        {
            var seconds = (DateTime.Now - _thinkingStartTime).TotalSeconds;
            SetValue(EffectiveSummaryPropertyKey, FormatThinkingText(seconds, GetCurrentKind()));
        }

        private static string FormatThinkingText(double seconds, string kind)
        {
            return $"{kind} for {seconds:F1}s";
        }

        private string GetCurrentKind()
        {
            var phase = CurrentPhase;
            if (!string.IsNullOrEmpty(phase)) return phase;

            var summary = Summary?.Trim() ?? string.Empty;
            if (summary.StartsWith("Reasoning", StringComparison.OrdinalIgnoreCase)) return "Reasoning";
            if (summary.StartsWith("Analysis", StringComparison.OrdinalIgnoreCase)) return "Analysis";
            return "Thinking";
        }

        #endregion

        public ThinkingPanel()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopElapsedTimer();
        }

        private static void OnIsProcessingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ThinkingPanel self)
            {
                if ((bool)e.NewValue)
                {
                    self.StartElapsedTimer();
                }
                else
                {
                    self.StopElapsedTimer();
                }
            }
        }

        private static void OnSummaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ThinkingPanel self && !self.IsProcessing)
            {
                self.SetValue(EffectiveSummaryPropertyKey, e.NewValue);
            }
        }

        private static void OnAnalysisRawChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ThinkingPanel self)
            {
                self.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    self.ThinkingScrollViewer?.ScrollToEnd();
                }));
            }
        }
    }
}
