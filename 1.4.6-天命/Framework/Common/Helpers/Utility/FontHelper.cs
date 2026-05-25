using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Common.Helpers.Utility
{
    public static class FontHelper
    {
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[FontHelper] {key}: {ex.Message}");
        }

        public static List<FontFamily> GetSystemFonts()
        {
            try
            {
                return Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontHelper] 获取系统字体失败: {ex.Message}");
                return new List<FontFamily>();
            }
        }

        public static FontFamily? FindFontByName(string fontName)
        {
            try
            {
                return Fonts.SystemFontFamilies.FirstOrDefault(f => 
                    f.Source.Equals(fontName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(FindFontByName), ex);
                return null;
            }
        }

        public static bool FontExists(string fontName)
        {
            return FindFontByName(fontName) != null;
        }

        public static FontFamily? LoadFontFromFile(string fontPath)
        {
            try
            {
                var uri = new Uri(fontPath);
                return new FontFamily(uri, "./#");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontHelper] 加载字体文件失败: {ex.Message}");
                return null;
            }
        }

        public static string GetFontInfo(FontFamily fontFamily)
        {
            try
            {
                var typefaces = fontFamily.FamilyTypefaces;
                var typefaceCount = typefaces.Count;

                return $"字体: {fontFamily.Source}\n字体样式数: {typefaceCount}";
            }
            catch (Exception ex)
            {
                return $"获取字体信息失败: {ex.Message}";
            }
        }

        public static void ApplyFontToGlobalResources(string fontFamilyName, double fontSize, FontWeight fontWeight)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var fontFamily = new FontFamily(fontFamilyName);

                app.Resources["GlobalFontFamily"] = fontFamily;
                app.Resources["GlobalFontSize"] = fontSize;
                app.Resources["GlobalFontWeight"] = fontWeight;

                TM.App.Log($"[FontHelper] 应用全局字体: {fontFamilyName}, {fontSize}px");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontHelper] 应用全局字体失败: {ex.Message}");
            }
        }

        public static List<string> GetRecommendedChineseFonts()
        {
            var recommendedFonts = new List<string>
            {
                "Microsoft YaHei",
                "Microsoft YaHei UI",
                "SimSun",
                "SimHei",
                "NSimSun",
                "FangSong",
                "KaiTi",
                "黑体",
                "宋体",
                "楷体",
                "仿宋"
            };

            return recommendedFonts.Where(FontExists).ToList();
        }

        public static List<string> GetRecommendedEnglishFonts()
        {
            var recommendedFonts = new List<string>
            {
                "Arial",
                "Calibri",
                "Segoe UI",
                "Tahoma",
                "Verdana",
                "Times New Roman",
                "Georgia",
                "Trebuchet MS"
            };

            return recommendedFonts.Where(FontExists).ToList();
        }

        public static List<string> GetRecommendedMonospaceFonts()
        {
            var recommendedFonts = new List<string>
            {
                "Consolas",
                "Courier New",
                "Lucida Console",
                "Cascadia Code",
                "Cascadia Mono",
                "Fira Code",
                "JetBrains Mono",
                "Source Code Pro"
            };

            return recommendedFonts.Where(FontExists).ToList();
        }

        public static Typeface GetTypeface(FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight)
        {
            return new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
        }

        public static double MeasureTextWidth(string text, FontFamily fontFamily, double fontSize, FontWeight fontWeight)
        {
            try
            {
                var typeface = GetTypeface(fontFamily, FontStyles.Normal, fontWeight);
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    1.0);

                return formattedText.Width;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(MeasureTextWidth), ex);
                return 0;
            }
        }
    }
}

