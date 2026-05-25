using System;
using SystemVisibility = System.Windows.Visibility;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Visibility
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool IsInverted { get; set; } = false;

        public bool UseHidden { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool boolValue)
                return SystemVisibility.Collapsed;

            bool shouldShow = IsInverted ? !boolValue : boolValue;

            if (shouldShow)
                return SystemVisibility.Visible;

            return UseHidden ? SystemVisibility.Hidden : SystemVisibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SystemVisibility visibility)
                return false;

            bool isVisible = visibility == SystemVisibility.Visible;
            return IsInverted ? !isVisible : isVisible;
        }
    }
}

