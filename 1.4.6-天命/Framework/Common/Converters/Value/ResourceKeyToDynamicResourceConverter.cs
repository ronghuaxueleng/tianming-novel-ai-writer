using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TM.Framework.Common.Converters.Value
{
    public class ResourceKeyToDynamicResourceConverter : IValueConverter
    {
        private static readonly SolidColorBrush DefaultGrayBrush;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Color, SolidColorBrush> ColorBrushCache = new();
        static ResourceKeyToDynamicResourceConverter()
        {
            DefaultGrayBrush = new SolidColorBrush(Colors.Gray);
            DefaultGrayBrush.Freeze();
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string resourceKey && !string.IsNullOrEmpty(resourceKey))
            {
                try
                {
                    var resource = Application.Current.TryFindResource(resourceKey);

                    if (resource is SolidColorBrush brush)
                    {
                        return brush;
                    }
                    else if (resource is Color color)
                    {
                        return ColorBrushCache.GetOrAdd(color, c =>
                        {
                            var b = new SolidColorBrush(c);
                            b.Freeze();
                            return b;
                        });
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ResourceKeyToDynamicResourceConverter] 转换资源键失败: {resourceKey}, 错误: {ex.Message}");
                }
            }

            return DefaultGrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ResourceKeyToDynamicResourceConverter does not support ConvertBack");
        }
    }
}
