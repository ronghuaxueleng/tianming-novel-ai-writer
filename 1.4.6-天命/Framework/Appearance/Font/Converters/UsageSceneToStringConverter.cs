using System;
using System.Globalization;
using System.Windows.Data;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.Converters
{
    public class UsageSceneToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is UsageScene scene)
            {
                return scene switch
                {
                    UsageScene.Coding => "编码开发",
                    UsageScene.Reading => "文档阅读",
                    UsageScene.Presentation => "屏幕演示",
                    UsageScene.Terminal => "终端控制台",
                    UsageScene.Documentation => "技术文档",
                    _ => "未知场景"
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

