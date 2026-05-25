using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TM.Framework.Appearance.Animation.LoadingAnimation;

namespace TM.Framework.Common.Controls.Feedback
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class LoadingIndicator : UserControl
    {
        public LoadingIndicator()
        {
            InitializeComponent();
            Loaded += LoadingIndicator_Loaded;
        }

        private void LoadingIndicator_Loaded(object sender, RoutedEventArgs e)
        {
            ShowAnimation(LoadingAnimationType.Spinner);
        }

        public void ShowAnimation(LoadingAnimationType animationType)
        {
            SpinnerEllipse.Visibility = Visibility.Collapsed;
            DotsPanel.Visibility = Visibility.Collapsed;
            PulseEllipse.Visibility = Visibility.Collapsed;
            RingEllipse.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Collapsed;

            (Resources["SpinnerAnimation"] as Storyboard)?.Stop();
            (Resources["DotsAnimation"] as Storyboard)?.Stop();
            (Resources["PulseAnimation"] as Storyboard)?.Stop();

            switch (animationType)
            {
                case LoadingAnimationType.Spinner:
                    SpinnerEllipse.Visibility = Visibility.Visible;
                    (Resources["SpinnerAnimation"] as Storyboard)?.Begin();
                    break;

                case LoadingAnimationType.Dots:
                    DotsPanel.Visibility = Visibility.Visible;
                    (Resources["DotsAnimation"] as Storyboard)?.Begin();
                    break;

                case LoadingAnimationType.Pulse:
                    PulseEllipse.Visibility = Visibility.Visible;
                    (Resources["PulseAnimation"] as Storyboard)?.Begin();
                    break;

                case LoadingAnimationType.Ring:
                    RingEllipse.Visibility = Visibility.Visible;
                    break;

                default:
                    PlaceholderText.Visibility = Visibility.Visible;
                    PlaceholderText.Text = $"{animationType} 动画开发中...";
                    break;
            }
        }
    }
}

