using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum PerformanceRating
    {
        Excellent,
        Good,
        Fair,
        Poor
    }

    public class PerformanceReport
    {
        public string FontName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public double AverageRenderTime { get; set; }
        public double MinRenderTime { get; set; }
        public double MaxRenderTime { get; set; }
        public int TestIterations { get; set; }
        public PerformanceRating Rating { get; set; }
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, double> DetailedTimings { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    public class FontPerformanceAnalyzer
    {
        public FontPerformanceAnalyzer() { }

        public PerformanceReport AnalyzePerformance(string fontName, double fontSize, int iterations = 20, double pixelsPerDip = 0, CancellationToken ct = default)
        {
            TM.App.Log($"[FontPerformance] 开始性能测试: {fontName} ({fontSize}px), {iterations}次迭代");

            var report = new PerformanceReport
            {
                FontName = fontName,
                FontSize = fontSize,
                TestIterations = iterations
            };

            try
            {
                if (pixelsPerDip <= 0)
                    pixelsPerDip = 1.0;

                var typeface = new Typeface(new FontFamily(fontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                var shortTextTime = MeasureRenderTime(typeface, fontSize, "Hello World!", iterations, pixelsPerDip, ct);
                report.DetailedTimings["短文本(12字符)"] = shortTextTime;

                ct.ThrowIfCancellationRequested();
                var mediumTextTime = MeasureRenderTime(typeface, fontSize, "The quick brown fox jumps over the lazy dog. 0123456789", iterations, pixelsPerDip, ct);
                report.DetailedTimings["中长文本(60字符)"] = mediumTextTime;

                ct.ThrowIfCancellationRequested();
                var codeText = "public class Example { private int _value = 42; }";
                var codeTime = MeasureRenderTime(typeface, fontSize, codeText, iterations, pixelsPerDip, ct);
                report.DetailedTimings["代码片段(50字符)"] = codeTime;

                ct.ThrowIfCancellationRequested();
                var cjkTime = MeasureRenderTime(typeface, fontSize, "中文测试文本字符渲染性能", iterations, pixelsPerDip, ct);
                report.DetailedTimings["CJK字符(12字符)"] = cjkTime;

                var allTimings = report.DetailedTimings.Values.ToList();
                report.AverageRenderTime = allTimings.Average();
                report.MinRenderTime = allTimings.Min();
                report.MaxRenderTime = allTimings.Max();

                report.Rating = CalculateRating(report.AverageRenderTime);
                report.Summary = GenerateSummary(report);

                TM.App.Log($"[FontPerformance] 测试完成: 平均{report.AverageRenderTime:F2}ms, 评级{report.Rating}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPerformance] 测试失败: {ex.Message}");
                report.Rating = PerformanceRating.Poor;
                report.Summary = $"测试失败：{ex.Message}";
            }

            return report;
        }

        private double MeasureRenderTime(Typeface typeface, double fontSize, string text, int iterations, double pixelsPerDip, CancellationToken ct)
        {
            var stopwatch = new Stopwatch();

            for (int i = 0; i < 10; i++)
            {
                ct.ThrowIfCancellationRequested();
                CreateFormattedText(text, typeface, fontSize, pixelsPerDip);
            }

            stopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                CreateFormattedText(text, typeface, fontSize, pixelsPerDip);
            }
            stopwatch.Stop();

            return stopwatch.Elapsed.TotalMilliseconds / iterations;
        }

        private FormattedText CreateFormattedText(string text, Typeface typeface, double fontSize, double pixelsPerDip)
        {
            return new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                pixelsPerDip
            );
        }

        private PerformanceRating CalculateRating(double averageTime)
        {
            if (averageTime < 0.01) return PerformanceRating.Excellent;
            if (averageTime < 0.02) return PerformanceRating.Good;
            if (averageTime < 0.05) return PerformanceRating.Fair;
            return PerformanceRating.Poor;
        }

        private string GenerateSummary(PerformanceReport report)
        {
            var ratingText = report.Rating switch
            {
                PerformanceRating.Excellent => "优秀",
                PerformanceRating.Good => "良好",
                PerformanceRating.Fair => "一般",
                PerformanceRating.Poor => "较差",
                _ => "未知"
            };

            return $"渲染性能{ratingText}，平均耗时 {report.AverageRenderTime:F3}ms";
        }
    }
}

