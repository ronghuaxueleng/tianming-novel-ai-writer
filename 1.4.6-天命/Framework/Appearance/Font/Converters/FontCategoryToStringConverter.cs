using System;
using System.Globalization;
using System.Windows.Data;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.Converters
{
    public class FontCategoryToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FontCategory category)
            {
                return FontCategoryService.GetCategoryDisplayName(category);
            }
            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

