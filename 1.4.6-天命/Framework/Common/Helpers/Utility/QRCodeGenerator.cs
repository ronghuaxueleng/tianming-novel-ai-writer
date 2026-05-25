using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace TM.Framework.Common.Helpers.Utility
{
    public static class QRCodeGenerator
    {
        public static string GenerateTOTPUri(string secret, string accountName, string issuer = "天命")
        {
            var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
            return uri;
        }

        public static ImageSource GenerateQRCodeImage(string text)
        {
            try
            {
                using var qrGenerator = new QRCoder.QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);

                var qrCodeBytes = qrCode.GetGraphic(20);

                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(qrCodeBytes);

                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[QRCodeGenerator] 生成二维码失败: {ex.Message}");

                return GeneratePlaceholderImage();
            }
        }

        private static ImageSource GeneratePlaceholderImage()
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 200, 200));
                context.DrawRectangle(null, new Pen(Brushes.Gray, 2), new System.Windows.Rect(10, 10, 180, 180));

                var formattedText = new FormattedText(
                    "二维码生成失败",
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"),
                    14,
                    Brushes.Gray,
                    96);

                context.DrawText(formattedText, new System.Windows.Point(50, 90));
            }

            var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            return bitmap;
        }

        public static string FormatSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret))
                return string.Empty;

            var result = string.Empty;
            for (int i = 0; i < secret.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    result += " ";
                result += secret[i];
            }
            return result;
        }
    }
}

