using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Framework.Common.Converters.Value
{
    public class BoolToTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "是";

        public string FalseText { get; set; } = "否";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueText : FalseText;
            }

            return FalseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

