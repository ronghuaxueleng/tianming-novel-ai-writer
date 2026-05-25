using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Value
{
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return false;
            }

            return System.Convert.ToInt32(value) == int.Parse(parameter.ToString()!);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return Enum.ToObject(targetType, int.Parse(parameter.ToString()!));
            }

            return Binding.DoNothing;
        }
    }
}

