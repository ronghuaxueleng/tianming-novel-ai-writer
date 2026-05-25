using System;
using SystemVisibility = System.Windows.Visibility;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Visibility
{
    public class InverseCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? SystemVisibility.Visible : SystemVisibility.Collapsed;
            }

            if (value is long longCount)
            {
                return longCount == 0 ? SystemVisibility.Visible : SystemVisibility.Collapsed;
            }

            if (value is double doubleCount)
            {
                return doubleCount == 0 ? SystemVisibility.Visible : SystemVisibility.Collapsed;
            }

            return SystemVisibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("InverseCountToVisibilityConverter does not support ConvertBack");
        }
    }
}

