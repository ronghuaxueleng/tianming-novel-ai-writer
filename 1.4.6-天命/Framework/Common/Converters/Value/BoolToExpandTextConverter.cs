using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Value
{
    public class BoolToExpandTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
            {
                return isExpanded ? "▼ 收起参数" : "▶ 编辑参数";
            }
            return "▶ 编辑参数";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
