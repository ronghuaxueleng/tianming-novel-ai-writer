using System;
using SystemVisibility = System.Windows.Visibility;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Visibility
{
    public class InverseNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? SystemVisibility.Visible : SystemVisibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

