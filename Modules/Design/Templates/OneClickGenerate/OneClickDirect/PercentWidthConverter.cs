using System;
using System.Globalization;
using System.Windows.Data;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public class PercentWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return 0.0;

            double val;
            double totalWidth;

            try
            {
                val = System.Convert.ToDouble(values[0], culture);
                totalWidth = System.Convert.ToDouble(values[1], culture);
            }
            catch
            {
                return 0.0;
            }

            if (totalWidth <= 0)
                return 0.0;

            var percent = Math.Clamp(val / 100.0, 0, 1);
            return percent * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
