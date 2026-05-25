using System;
using SystemVisibility = System.Windows.Visibility;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Visibility
{
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? SystemVisibility.Collapsed : SystemVisibility.Visible;
            }
            return SystemVisibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SystemVisibility visibility)
            {
                return visibility != SystemVisibility.Visible;
            }
            return false;
        }
    }
}

