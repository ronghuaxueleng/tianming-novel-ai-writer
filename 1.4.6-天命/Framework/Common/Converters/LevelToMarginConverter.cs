using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TM.Framework.Common.Converters
{
    public class LevelToMarginConverter : IValueConverter
    {
        public double IndentPerLevel { get; set; } = 20.0;

        public double BaseLeftMargin { get; set; } = 0.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                double leftMargin = BaseLeftMargin + (level - 1) * IndentPerLevel;
                return new Thickness(leftMargin, 2, 0, 2);
            }

            return new Thickness(0, 2, 0, 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

