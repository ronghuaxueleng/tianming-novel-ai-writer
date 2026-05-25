using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TM.Framework.Common.Converters.Value;

public class BoolToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush TrueBrush;
    private static readonly SolidColorBrush FalseBrush;
    private static readonly SolidColorBrush DefaultBrush;

    static BoolToColorConverter()
    {
        TrueBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        TrueBrush.Freeze();
        FalseBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        FalseBrush.Freeze();
        DefaultBrush = new SolidColorBrush(Colors.Gray);
        DefaultBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? TrueBrush : FalseBrush;

        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
