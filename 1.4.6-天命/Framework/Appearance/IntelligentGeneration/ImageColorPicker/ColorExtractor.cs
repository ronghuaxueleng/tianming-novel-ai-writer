using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker
{
    public static class ColorExtractor
    {
        public static List<Color> ExtractPalette(BitmapImage bitmap, int numColors)
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

                var pixels = GetPixels(writeableBitmap);

                var filteredPixels = pixels
                    .Where(c =>
                    {
                        var brightness = (c.R + c.G + c.B) / 3.0;
                        return brightness > 20 && brightness < 235;
                    })
                    .ToList();

                if (!filteredPixels.Any())
                    filteredPixels = pixels;

                var quantizedPixels = QuantizeColors(filteredPixels, 30);

                var colorCounts = quantizedPixels
                    .GroupBy(c => $"{c.R},{c.G},{c.B}")
                    .Select(g => new { Color = g.First(), Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(numColors * 2)
                    .Select(x => x.Color)
                    .ToList();

                var palette = SelectDiverseColors(colorCounts, numColors);

                return palette;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ColorExtractor] 提取颜色失败: {ex.Message}");
                return new List<Color> { Color.FromRgb(100, 100, 100) };
            }
        }

        private static List<Color> GetPixels(WriteableBitmap bitmap)
        {
            var pixels = new List<Color>();
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixelData = new byte[height * stride];

            bitmap.CopyPixels(pixelData, stride, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = y * stride + x * 4;
                    var b = pixelData[index];
                    var g = pixelData[index + 1];
                    var r = pixelData[index + 2];

                    pixels.Add(Color.FromRgb(r, g, b));
                }
            }

            return pixels;
        }

        private static List<Color> QuantizeColors(List<Color> pixels, int step)
        {
            return pixels.Select(c => Color.FromRgb(
                (byte)((c.R / step) * step),
                (byte)((c.G / step) * step),
                (byte)((c.B / step) * step)
            )).ToList();
        }

        private static List<Color> SelectDiverseColors(List<Color> colors, int count)
        {
            if (colors.Count <= count)
                return colors;

            var selected = new List<Color> { colors[0] };

            foreach (var color in colors.Skip(1))
            {
                if (selected.Count >= count)
                    break;

                if (IsColorDiverse(color, selected, 60))
                {
                    selected.Add(color);
                }
            }

            return selected;
        }

        private static bool IsColorDiverse(Color color, List<Color> selected, int threshold)
        {
            foreach (var selColor in selected)
            {
                var distance = Math.Sqrt(
                    Math.Pow(color.R - selColor.R, 2) +
                    Math.Pow(color.G - selColor.G, 2) +
                    Math.Pow(color.B - selColor.B, 2)
                );

                if (distance < threshold)
                    return false;
            }

            return true;
        }
    }
}

