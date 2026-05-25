using System;
using System.Globalization;
using System.Windows.Data;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.Converters
{
    public class PerformanceRatingToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PerformanceRating rating)
            {
                return rating switch
                {
                    PerformanceRating.Excellent => "优秀",
                    PerformanceRating.Good => "良好",
                    PerformanceRating.Fair => "一般",
                    PerformanceRating.Poor => "较差",
                    _ => "未知"
                };
            }
            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

