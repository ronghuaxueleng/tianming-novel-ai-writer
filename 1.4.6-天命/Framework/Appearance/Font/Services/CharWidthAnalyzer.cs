using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum WidthCheckResult
    {
        Pass,
        Warning,
        Fail
    }

    public class WidthCheckItem
    {
        public string Category { get; set; } = string.Empty;
        public WidthCheckResult Result { get; set; }
        public string Details { get; set; } = string.Empty;
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; }
        public double Variance { get; set; }
    }

    public class CharWidthReport
    {
        public string FontName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public List<WidthCheckItem> Items { get; set; } = new();
        public WidthCheckResult OverallResult { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    public class CharWidthAnalyzer
    {
        public CharWidthAnalyzer() { }

        public CharWidthReport AnalyzeFont(string fontName, double fontSize)
        {
            TM.App.Log($"[CharWidthAnalyzer] 开始分析字体: {fontName} ({fontSize}px)");

            var report = new CharWidthReport
            {
                FontName = fontName,
                FontSize = fontSize
            };

            try
            {
                report.Items.Add(CheckDigitWidths(fontName, fontSize));

                report.Items.Add(CheckLetterWidths(fontName, fontSize));

                report.Items.Add(CheckSpecialCharWidths(fontName, fontSize));

                report.Items.Add(CheckCJKWidths(fontName, fontSize));

                report.OverallResult = CalculateOverallResult(report.Items);
                report.Summary = GenerateSummary(report);

                TM.App.Log($"[CharWidthAnalyzer] 分析完成: {report.OverallResult}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharWidthAnalyzer] 分析失败: {ex.Message}");
                report.OverallResult = WidthCheckResult.Fail;
                report.Summary = $"分析失败: {ex.Message}";
            }

            return report;
        }

        private WidthCheckItem CheckDigitWidths(string fontName, double fontSize)
        {
            var digits = "0123456789";
            var widths = MeasureCharacterWidths(fontName, fontSize, digits);

            var minWidth = widths.Min();
            var maxWidth = widths.Max();
            var avgWidth = widths.Average();
            var variance = widths.Select(w => Math.Abs(w - avgWidth)).Average();
            var variancePercent = (variance / avgWidth) * 100;

            var result = variancePercent < 1 ? WidthCheckResult.Pass :
                         variancePercent < 5 ? WidthCheckResult.Warning :
                         WidthCheckResult.Fail;

            return new WidthCheckItem
            {
                Category = "数字 (0-9)",
                Result = result,
                Details = $"宽度范围: {minWidth:F2}~{maxWidth:F2}px, 差异率: {variancePercent:F2}%",
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                Variance = variancePercent
            };
        }

        private WidthCheckItem CheckLetterWidths(string fontName, double fontSize)
        {
            var letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var widths = MeasureCharacterWidths(fontName, fontSize, letters);

            var minWidth = widths.Min();
            var maxWidth = widths.Max();
            var avgWidth = widths.Average();
            var variance = widths.Select(w => Math.Abs(w - avgWidth)).Average();
            var variancePercent = (variance / avgWidth) * 100;

            var result = variancePercent < 5 ? WidthCheckResult.Pass :
                         variancePercent < 30 ? WidthCheckResult.Warning :
                         WidthCheckResult.Fail;

            return new WidthCheckItem
            {
                Category = "字母 (a-z, A-Z)",
                Result = result,
                Details = $"宽度范围: {minWidth:F2}~{maxWidth:F2}px, 差异率: {variancePercent:F2}%",
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                Variance = variancePercent
            };
        }

        private WidthCheckItem CheckSpecialCharWidths(string fontName, double fontSize)
        {
            var specialChars = "[]{}()<>";
            var widths = MeasureCharacterWidths(fontName, fontSize, specialChars);

            var minWidth = widths.Min();
            var maxWidth = widths.Max();
            var avgWidth = widths.Average();
            var variance = widths.Select(w => Math.Abs(w - avgWidth)).Average();
            var variancePercent = (variance / avgWidth) * 100;

            var result = variancePercent < 10 ? WidthCheckResult.Pass :
                         variancePercent < 25 ? WidthCheckResult.Warning :
                         WidthCheckResult.Fail;

            return new WidthCheckItem
            {
                Category = "括号符号 ([]{}()<>)",
                Result = result,
                Details = $"宽度范围: {minWidth:F2}~{maxWidth:F2}px, 差异率: {variancePercent:F2}%",
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                Variance = variancePercent
            };
        }

        private WidthCheckItem CheckCJKWidths(string fontName, double fontSize)
        {
            var cjkChars = "中文汉字测试";
            var widths = MeasureCharacterWidths(fontName, fontSize, cjkChars);

            if (widths.Count == 0 || widths.All(w => w == 0))
            {
                return new WidthCheckItem
                {
                    Category = "CJK字符",
                    Result = WidthCheckResult.Warning,
                    Details = "字体不支持CJK字符",
                    MinWidth = 0,
                    MaxWidth = 0,
                    Variance = 0
                };
            }

            var minWidth = widths.Min();
            var maxWidth = widths.Max();
            var avgWidth = widths.Average();
            var variance = widths.Select(w => Math.Abs(w - avgWidth)).Average();
            var variancePercent = (variance / avgWidth) * 100;

            var latinWidth = MeasureCharacterWidths(fontName, fontSize, "a").FirstOrDefault();
            var isDoubleWidth = avgWidth >= latinWidth * 1.8 && avgWidth <= latinWidth * 2.2;

            var result = isDoubleWidth && variancePercent < 5 ? WidthCheckResult.Pass :
                         isDoubleWidth || variancePercent < 10 ? WidthCheckResult.Warning :
                         WidthCheckResult.Fail;

            return new WidthCheckItem
            {
                Category = "CJK字符",
                Result = result,
                Details = $"宽度范围: {minWidth:F2}~{maxWidth:F2}px, 差异率: {variancePercent:F2}%, {(isDoubleWidth ? "双宽正常" : "宽度异常")}",
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                Variance = variancePercent
            };
        }

        private List<double> MeasureCharacterWidths(string fontName, double fontSize, string characters)
        {
            var widths = new List<double>();

            try
            {
                var typeface = new Typeface(new FontFamily(fontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                foreach (var ch in characters)
                {
                    var formattedText = new FormattedText(
                        ch.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.Black,
                        VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip
                    );

                    widths.Add(formattedText.Width);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CharWidthAnalyzer] 测量字符宽度失败: {ex.Message}");
            }

            return widths;
        }

        private WidthCheckResult CalculateOverallResult(List<WidthCheckItem> items)
        {
            if (items.Any(i => i.Result == WidthCheckResult.Fail))
            {
                return WidthCheckResult.Fail;
            }

            if (items.Any(i => i.Result == WidthCheckResult.Warning))
            {
                return WidthCheckResult.Warning;
            }

            return WidthCheckResult.Pass;
        }

        private string GenerateSummary(CharWidthReport report)
        {
            var passCount = report.Items.Count(i => i.Result == WidthCheckResult.Pass);
            var warnCount = report.Items.Count(i => i.Result == WidthCheckResult.Warning);
            var failCount = report.Items.Count(i => i.Result == WidthCheckResult.Fail);

            return report.OverallResult switch
            {
                WidthCheckResult.Pass => $"字符宽度一致性良好 ({passCount}项通过)",
                WidthCheckResult.Warning => $"字符宽度存在轻微问题 ({passCount}项通过, {warnCount}项警告)",
                WidthCheckResult.Fail => $"字符宽度不一致 ({failCount}项失败, {warnCount}项警告)",
                _ => "未知"
            };
        }
    }
}

