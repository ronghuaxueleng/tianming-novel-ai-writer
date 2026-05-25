using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Framework.Common.Converters
{
    public class IconKeyToImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string iconKey && !string.IsNullOrEmpty(iconKey))
            {
                return IconHelper.TryGet(iconKey);
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
