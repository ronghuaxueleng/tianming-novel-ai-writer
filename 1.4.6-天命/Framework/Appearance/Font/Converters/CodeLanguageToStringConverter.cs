using System;
using System.Globalization;
using System.Windows.Data;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.Converters
{
    public class CodeLanguageToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CodeLanguage language)
            {
                return language switch
                {
                    CodeLanguage.CSharp => "C#",
                    CodeLanguage.Python => "Python",
                    CodeLanguage.JavaScript => "JavaScript",
                    CodeLanguage.TypeScript => "TypeScript",
                    CodeLanguage.Java => "Java",
                    CodeLanguage.Cpp => "C++",
                    CodeLanguage.Go => "Go",
                    CodeLanguage.Rust => "Rust",
                    CodeLanguage.JSON => "JSON",
                    CodeLanguage.XML => "XML",
                    CodeLanguage.HTML => "HTML",
                    CodeLanguage.CSS => "CSS",
                    CodeLanguage.SQL => "SQL",
                    CodeLanguage.Markdown => "Markdown",
                    _ => language.ToString()
                };
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

