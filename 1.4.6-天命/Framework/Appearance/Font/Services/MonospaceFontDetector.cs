using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    public class MonospaceFontDetector
    {
        private readonly Dictionary<string, bool> _cache = new();

        private static readonly char[] TestChars = { 'i', 'l', 'I', '1', 'm', 'W', 'M', '0', 'O' };

        public MonospaceFontDetector() { }

        public bool IsMonospace(string fontFamilyName)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName))
                return false;

            if (_cache.TryGetValue(fontFamilyName, out var cached))
                return cached;

            if (IsKnownMonospaceFont(fontFamilyName))
            {
                _cache[fontFamilyName] = true;
                return true;
            }

            var result = MeasureCharacterWidths(fontFamilyName);
            _cache[fontFamilyName] = result;
            return result;
        }

        private bool IsKnownMonospaceFont(string fontName)
        {
            var lowerName = fontName.ToLowerInvariant();

            var knownMonospace = new[]
            {
                "consolas", "courier", "courier new", "monaco", "menlo",
                "fira code", "fira mono", "jetbrains mono", "cascadia code", "cascadia mono",
                "source code pro", "inconsolata", "dejavu sans mono", "ubuntu mono",
                "roboto mono", "sf mono", "droid sans mono", "liberation mono",
                "noto mono", "hack", "anonymous pro", "meslo", "input mono"
            };

            return knownMonospace.Any(mono => lowerName.Contains(mono));
        }

        private bool MeasureCharacterWidths(string fontFamilyName)
        {
            try
            {
                var fontFamily = new FontFamily(fontFamilyName);
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                var widths = new List<double>();

                foreach (var ch in TestChars)
                {
                    var formattedText = new FormattedText(
                        ch.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        12.0,
                        Brushes.Black,
                        VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);

                    widths.Add(formattedText.Width);
                }

                if (widths.Count < 2)
                    return false;

                var avgWidth = widths.Average();
                var maxDeviation = widths.Max(w => Math.Abs(w - avgWidth));
                var tolerance = avgWidth * 0.05;

                var isMonospace = maxDeviation <= tolerance;

                TM.App.Log($"[MonospaceFontDetector] {fontFamilyName}: 平均宽度={avgWidth:F2}, 最大偏差={maxDeviation:F2}, 等宽={isMonospace}");

                return isMonospace;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MonospaceFontDetector] 检测失败: {fontFamilyName}, 错误:{ex.Message}");
                return false;
            }
        }

        public Dictionary<char, double> GetCharacterWidths(string fontFamilyName, double fontSize = 12.0)
        {
            var result = new Dictionary<char, double>();

            try
            {
                var fontFamily = new FontFamily(fontFamilyName);
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                foreach (var ch in TestChars)
                {
                    var formattedText = new FormattedText(
                        ch.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.Black,
                        VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);

                    result[ch] = formattedText.Width;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MonospaceFontDetector] 获取字符宽度失败: {fontFamilyName}, 错误:{ex.Message}");
            }

            return result;
        }

        public void ClearCache()
        {
            _cache.Clear();
            TM.App.Log("[MonospaceFontDetector] 缓存已清除");
        }
    }
}

