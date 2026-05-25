using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker
{
    public class ImageAnalysisResult
    {
        public double AvgBrightness { get; set; }
        public double DarkRatio { get; set; }
        public double LightRatio { get; set; }
        public bool IsDark { get; set; }
        public string ThemeType { get; set; } = "light";
        public string TextColor { get; set; } = "#212529";
        public string Notes { get; set; } = string.Empty;
    }

    public static class ImageAnalyzer
    {
        public static ImageAnalysisResult Analyze(BitmapImage bitmap)
        {
            try
            {
                var writeableBitmap = new WriteableBitmap(
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    bitmap.DpiX,
                    bitmap.DpiY,
                    System.Windows.Media.PixelFormats.Pbgra32,
                    null
                );

                var stride = bitmap.PixelWidth * 4;
                var pixelData = new byte[bitmap.PixelHeight * stride];
                bitmap.CopyPixels(pixelData, stride, 0);
                writeableBitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
                    pixelData,
                    stride,
                    0
                );

                if (writeableBitmap.PixelWidth > 200 || writeableBitmap.PixelHeight > 200)
                {
                    var scale = 200.0 / Math.Max(writeableBitmap.PixelWidth, writeableBitmap.PixelHeight);

                    var scaledBitmap = new TransformedBitmap(writeableBitmap, new ScaleTransform(scale, scale));

                    var scaledWriteable = new WriteableBitmap(
                        scaledBitmap.PixelWidth,
                        scaledBitmap.PixelHeight,
                        scaledBitmap.DpiX,
                        scaledBitmap.DpiY,
                        System.Windows.Media.PixelFormats.Pbgra32,
                        null
                    );

                    var scaledStride = scaledBitmap.PixelWidth * 4;
                    var scaledPixelData = new byte[scaledBitmap.PixelHeight * scaledStride];
                    scaledBitmap.CopyPixels(scaledPixelData, scaledStride, 0);
                    scaledWriteable.WritePixels(
                        new System.Windows.Int32Rect(0, 0, scaledBitmap.PixelWidth, scaledBitmap.PixelHeight),
                        scaledPixelData,
                        scaledStride,
                        0
                    );

                    writeableBitmap = scaledWriteable;
                }

                var brightnessInfo = AnalyzeBrightness(writeableBitmap);

                return GenerateRecommendation(brightnessInfo);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ImageAnalyzer] 分析失败: {ex.Message}");
                return new ImageAnalysisResult
                {
                    AvgBrightness = 128,
                    IsDark = false,
                    ThemeType = "light",
                    TextColor = "#212529",
                    Notes = "图片分析失败，使用默认配置"
                };
            }
        }

        private static BrightnessInfo AnalyzeBrightness(WriteableBitmap bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixelData = new byte[height * stride];

            bitmap.CopyPixels(pixelData, stride, 0);

            var brightnessList = new System.Collections.Generic.List<double>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = y * stride + x * 4;
                    var b = pixelData[index];
                    var g = pixelData[index + 1];
                    var r = pixelData[index + 2];

                    var brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                    brightnessList.Add(brightness);
                }
            }

            var avgBrightness = brightnessList.Average();
            var darkPixels = brightnessList.Count(b => b < 85);
            var lightPixels = brightnessList.Count(b => b > 170);
            var totalPixels = brightnessList.Count;

            var darkRatio = (double)darkPixels / totalPixels;
            var lightRatio = (double)lightPixels / totalPixels;

            var isDark = avgBrightness < 128 || darkRatio > 0.5;

            return new BrightnessInfo
            {
                AvgBrightness = avgBrightness,
                DarkRatio = darkRatio,
                LightRatio = lightRatio,
                IsDark = isDark
            };
        }

        private static ImageAnalysisResult GenerateRecommendation(BrightnessInfo brightnessInfo)
        {
            var result = new ImageAnalysisResult
            {
                AvgBrightness = brightnessInfo.AvgBrightness,
                DarkRatio = brightnessInfo.DarkRatio,
                LightRatio = brightnessInfo.LightRatio,
                IsDark = brightnessInfo.IsDark
            };

            if (brightnessInfo.IsDark)
            {
                result.ThemeType = "dark";
                result.TextColor = "#ffffff";
                result.Notes = "检测到暗色图片，建议使用深色主题配色，文字使用亮色并添加阴影。";
            }
            else
            {
                result.ThemeType = "light";
                result.TextColor = "#212529";
                result.Notes = "检测到亮色图片，建议使用浅色主题配色，文字使用深色。";
            }

            if (brightnessInfo.AvgBrightness < 50)
            {
                result.Notes += "\n图片非常暗，建议大幅提高UI不透明度。";
            }
            else if (brightnessInfo.AvgBrightness > 200)
            {
                result.Notes += "\n图片非常亮，建议降低UI不透明度。";
            }

            return result;
        }

        private class BrightnessInfo
        {
            public double AvgBrightness { get; set; }
            public double DarkRatio { get; set; }
            public double LightRatio { get; set; }
            public bool IsDark { get; set; }
        }
    }
}

