using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Value
{
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2)
                return 0.0;

            if (values[0] is not double percentage)
                return 0.0;

            if (values[1] is not double containerWidth)
                return 0.0;

            return percentage * containerWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

