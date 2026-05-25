using System.Reflection;
using System.Windows;

namespace TM.Framework.Common.Controls.DataManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class FunctionalDetailField : DependencyObject
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(FunctionalDetailField),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty GroupKeyProperty =
            DependencyProperty.Register(
                nameof(GroupKey),
                typeof(string),
                typeof(FunctionalDetailField),
                new PropertyMetadata(string.Empty));

        public string GroupKey
        {
            get => (string)GetValue(GroupKeyProperty);
            set => SetValue(GroupKeyProperty, value);
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register(
                nameof(Content),
                typeof(UIElement),
                typeof(FunctionalDetailField),
                new PropertyMetadata(null));

        public UIElement? Content
        {
            get => (UIElement?)GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        public static readonly DependencyProperty MinWidthProperty =
            DependencyProperty.Register(
                nameof(MinWidth),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(160d));

        public double MinWidth
        {
            get => (double)GetValue(MinWidthProperty);
            set => SetValue(MinWidthProperty, value);
        }

        public static readonly DependencyProperty MaxWidthProperty =
            DependencyProperty.Register(
                nameof(MaxWidth),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(320d));

        public double MaxWidth
        {
            get => (double)GetValue(MaxWidthProperty);
            set => SetValue(MaxWidthProperty, value);
        }

        public static readonly DependencyProperty AllowGrowProperty =
            DependencyProperty.Register(
                nameof(AllowGrow),
                typeof(bool),
                typeof(FunctionalDetailField),
                new PropertyMetadata(true));

        public bool AllowGrow
        {
            get => (bool)GetValue(AllowGrowProperty);
            set => SetValue(AllowGrowProperty, value);
        }

        public static readonly DependencyProperty GrowWeightProperty =
            DependencyProperty.Register(
                nameof(GrowWeight),
                typeof(double),
                typeof(FunctionalDetailField),
                new PropertyMetadata(1d));

        public double GrowWeight
        {
            get => (double)GetValue(GrowWeightProperty);
            set => SetValue(GrowWeightProperty, value);
        }

        public static readonly DependencyProperty AllowMergeProperty =
            DependencyProperty.Register(
                nameof(AllowMerge),
                typeof(bool),
                typeof(FunctionalDetailField),
                new PropertyMetadata(true));

        public bool AllowMerge
        {
            get => (bool)GetValue(AllowMergeProperty);
            set => SetValue(AllowMergeProperty, value);
        }

        public static readonly DependencyProperty OrderProperty =
            DependencyProperty.Register(
                nameof(Order),
                typeof(int),
                typeof(FunctionalDetailField),
                new PropertyMetadata(0));

        public int Order
        {
            get => (int)GetValue(OrderProperty);
            set => SetValue(OrderProperty, value);
        }
    }
}
