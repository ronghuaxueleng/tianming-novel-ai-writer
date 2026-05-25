using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Common.Controls.Statistics
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TokenStatistics : UserControl
    {
        public static readonly DependencyProperty PromptTokensProperty =
            DependencyProperty.Register(
                nameof(PromptTokens),
                typeof(int),
                typeof(TokenStatistics),
                new PropertyMetadata(0, OnTokensChanged));

        public static readonly DependencyProperty CompletionTokensProperty =
            DependencyProperty.Register(
                nameof(CompletionTokens),
                typeof(int),
                typeof(TokenStatistics),
                new PropertyMetadata(0, OnTokensChanged));

        public static readonly DependencyProperty TotalTokensProperty =
            DependencyProperty.Register(
                nameof(TotalTokens),
                typeof(int),
                typeof(TokenStatistics),
                new PropertyMetadata(0, OnTokensChanged));

        public static readonly DependencyProperty EstimatedCostProperty =
            DependencyProperty.Register(
                nameof(EstimatedCost),
                typeof(decimal),
                typeof(TokenStatistics),
                new PropertyMetadata(0m, OnCostChanged));

        public static readonly DependencyProperty WarningThresholdProperty =
            DependencyProperty.Register(
                nameof(WarningThreshold),
                typeof(int),
                typeof(TokenStatistics),
                new PropertyMetadata(8000, OnTokensChanged));

        public int PromptTokens
        {
            get => (int)GetValue(PromptTokensProperty);
            set => SetValue(PromptTokensProperty, value);
        }

        public int CompletionTokens
        {
            get => (int)GetValue(CompletionTokensProperty);
            set => SetValue(CompletionTokensProperty, value);
        }

        public int TotalTokens
        {
            get => (int)GetValue(TotalTokensProperty);
            set => SetValue(TotalTokensProperty, value);
        }

        public decimal EstimatedCost
        {
            get => (decimal)GetValue(EstimatedCostProperty);
            set => SetValue(EstimatedCostProperty, value);
        }

        public int WarningThreshold
        {
            get => (int)GetValue(WarningThresholdProperty);
            set => SetValue(WarningThresholdProperty, value);
        }

        public TokenStatistics()
        {
            InitializeComponent();
        }

        private static void OnTokensChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TokenStatistics control)
            {
                control.UpdateDisplay();
            }
        }

        private static void OnCostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TokenStatistics control)
            {
                control.UpdateCostDisplay();
            }
        }

        private void UpdateDisplay()
        {
            PromptTokensValue.Text = PromptTokens.ToString("N0");
            CompletionTokensValue.Text = CompletionTokens.ToString("N0");
            TotalTokensValue.Text = TotalTokens.ToString("N0");

            if (TotalTokens > WarningThreshold)
            {
                TotalTokensLabel.Foreground = new SolidColorBrush(Colors.Red);
                TotalTokensValue.Foreground = new SolidColorBrush(Colors.Red);
            }
            else
            {
                TotalTokensLabel.Foreground = (Brush)FindResource("TextPrimary");
                TotalTokensValue.Foreground = (Brush)FindResource("TextPrimary");
            }

            if (TotalTokens == 0 && PromptTokens == 0 && CompletionTokens == 0)
            {
                Visibility = Visibility.Collapsed;
            }
            else
            {
                Visibility = Visibility.Visible;
            }
        }

        private void UpdateCostDisplay()
        {
            CostValue.Text = EstimatedCost.ToString("F4");

            if (EstimatedCost > 1.0m)
            {
                CostLabel.Foreground = new SolidColorBrush(Colors.OrangeRed);
            }
            else
            {
                CostLabel.Foreground = (Brush)FindResource("InfoColor");
            }
        }

        public void SetTokens(int promptTokens, int completionTokens, int totalTokens, decimal estimatedCost)
        {
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            TotalTokens = totalTokens;
            EstimatedCost = estimatedCost;
        }
    }
}

