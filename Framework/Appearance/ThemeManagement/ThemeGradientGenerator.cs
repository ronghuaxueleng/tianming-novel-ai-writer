using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.ThemeManagement
{
    public static class ThemeGradientGenerator
    {
        private static long _cachedColorKey;
        private static Dictionary<string, Brush>? _cachedBrushes;
        private static long ComputeColorKey(Color c1, Color c2, Color c3, Color c4, Color c5,
            Color c6, Color c7, Color c8, Color c9, Color c10, Color c11)
        {
            unchecked
            {
                long h = 17;
                h = h * 31 + c1.GetHashCode(); h = h * 31 + c2.GetHashCode();
                h = h * 31 + c3.GetHashCode(); h = h * 31 + c4.GetHashCode();
                h = h * 31 + c5.GetHashCode(); h = h * 31 + c6.GetHashCode();
                h = h * 31 + c7.GetHashCode(); h = h * 31 + c8.GetHashCode();
                h = h * 31 + c9.GetHashCode(); h = h * 31 + c10.GetHashCode();
                h = h * 31 + c11.GetHashCode();
                return h;
            }
        }

        public static void InjectGradients(ResourceDictionary themeDict)
        {
            try
            {
                var unifiedBg = GetColor(themeDict, "UnifiedBackground");
                var contentBg = GetColor(themeDict, "ContentBackground");
                var surface = GetColor(themeDict, "Surface");
                var primary = GetColor(themeDict, "PrimaryColor");
                var primaryHover = GetColor(themeDict, "PrimaryHover");
                var primaryActive = GetColor(themeDict, "PrimaryActive");
                var danger = GetColor(themeDict, "DangerColor");
                var dangerHover = GetColor(themeDict, "DangerHover");
                var success = GetColor(themeDict, "SuccessColor");
                var warning = GetColor(themeDict, "WarningColor");
                var info = GetColor(themeDict, "InfoColor");

                var colorKey = ComputeColorKey(unifiedBg, contentBg, surface, primary, primaryHover,
                    primaryActive, danger, dangerHover, success, warning, info);
                if (colorKey == _cachedColorKey && _cachedBrushes != null)
                {
                    foreach (var kv in _cachedBrushes)
                        themeDict[kv.Key] = kv.Value;
                    return;
                }
                var brushMap = new Dictionary<string, Brush>();

                double lum = GetLuminance(unifiedBg);
                static double Lerp(double a, double b, double t) => a + (b - a) * t;
                var primary_30p = RotateHue(primary, 30);
                var primary_30n = RotateHue(primary, -30);
                double bgMix = Lerp(0.20, 0.12, lum);
                double bgStart = bgMix * 1.20;
                double bgMiddle = bgMix * 0.75;
                double bgEnd = bgMix * 1.25;

                brushMap["GradientBackground"] = Create3StopGradient(
                    BlendColors(unifiedBg, primary_30p, bgStart),
                    BlendColors(unifiedBg, primary, bgMiddle),
                    BlendColors(unifiedBg, primary_30n, bgEnd));

                double surfaceMix = Lerp(0.05, 0.02, lum);
                var surfaceShifted = BlendColors(contentBg, primary, surfaceMix);
                brushMap["GradientSurface"] = CreateVerticalGradient(contentBg, surfaceShifted);

                byte surfaceAlpha = (byte)Math.Round(Lerp(110, 77, lum));
                byte contentAlpha = (byte)Math.Round(Lerp(80, 51, lum));
                byte panelAlpha = (byte)Math.Round(Lerp(52, 26, lum));
                byte dialogAlpha = (byte)Math.Round(Lerp(250, 245, lum));

                byte tintedR = (byte)(surface.R * 0.95 + primary.R * 0.05);
                byte tintedG = (byte)(surface.G * 0.95 + primary.G * 0.05);
                byte tintedB = (byte)(surface.B * 0.95 + primary.B * 0.05);

                double dialogMix = Lerp(0.03, 0.08, lum);
                byte dialogR = (byte)(surface.R * (1 - dialogMix) + primary.R * dialogMix);
                byte dialogG = (byte)(surface.G * (1 - dialogMix) + primary.G * dialogMix);
                byte dialogB = (byte)(surface.B * (1 - dialogMix) + primary.B * dialogMix);

                var surfaceGlassBrush = new SolidColorBrush(Color.FromArgb(surfaceAlpha, tintedR, tintedG, tintedB));
                var contentGlassBrush = new SolidColorBrush(Color.FromArgb(contentAlpha, tintedR, tintedG, tintedB));
                var panelGlassBrush = new SolidColorBrush(Color.FromArgb(panelAlpha, tintedR, tintedG, tintedB));
                var dialogGlassBrush = new SolidColorBrush(Color.FromArgb(dialogAlpha, dialogR, dialogG, dialogB));

                surfaceGlassBrush.Freeze();
                contentGlassBrush.Freeze();
                panelGlassBrush.Freeze();
                dialogGlassBrush.Freeze();

                brushMap["SurfaceGlass"] = surfaceGlassBrush;
                brushMap["ContentBackgroundGlass"] = contentGlassBrush;
                brushMap["PanelGlass"] = panelGlassBrush;
                brushMap["DialogGlass"] = dialogGlassBrush;

                byte borderAlpha = (byte)Math.Round(Lerp(60, 40, lum));
                var glassBorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, primary.R, primary.G, primary.B));
                glassBorderBrush.Freeze();
                brushMap["GlassBorderBrush"] = glassBorderBrush;

                brushMap["GradientPrimary"] = CreateHorizontalGradient(primary, primaryActive);

                brushMap["GradientPrimaryVertical"] = CreateVerticalGradient(primary, primaryActive);

                brushMap["GradientPrimaryDiagonal"] = CreateDiagonalGradient(primaryHover, primaryActive);

                var accentEnd = BlendColors(primary, info, 0.5);
                brushMap["GradientAccent"] = CreateHorizontalGradient(primary, accentEnd);

                brushMap["GradientDanger"] = CreateHorizontalGradient(danger, dangerHover);

                var successEnd = ShiftColor(success, -15);
                brushMap["GradientSuccess"] = CreateHorizontalGradient(success, successEnd);

                var warningEnd = ShiftColor(warning, -15);
                brushMap["GradientWarning"] = CreateHorizontalGradient(warning, warningEnd);

                brushMap["GradientHeader"] = CreateHorizontalGradient(primary, primaryHover);

                var cardHoverStart = BlendColors(surface, primary, 0.03);
                var cardHoverEnd = BlendColors(surface, primary, 0.08);
                brushMap["GradientCardHover"] = CreateVerticalGradient(cardHoverStart, cardHoverEnd);

                var sidebarTop = ShiftColor(unifiedBg, -8);
                var sidebarBottom = ShiftColor(unifiedBg, 5);
                brushMap["GradientSidebar"] = CreateVerticalGradient(sidebarTop, sidebarBottom);

                double overlayMix = Lerp(0.25, 0.15, lum);
                double baseFactor = Lerp(0.50, 0.20, lum);
                byte overlR = (byte)Math.Clamp(unifiedBg.R * baseFactor * (1 - overlayMix) + primary.R * overlayMix, 0, 255);
                byte overlG = (byte)Math.Clamp(unifiedBg.G * baseFactor * (1 - overlayMix) + primary.G * overlayMix, 0, 255);
                byte overlB = (byte)Math.Clamp(unifiedBg.B * baseFactor * (1 - overlayMix) + primary.B * overlayMix, 0, 255);
                byte overlAlpha = (byte)Math.Round(Lerp(178, 148, lum));
                var busyOverlayBrush = new SolidColorBrush(Color.FromArgb(overlAlpha, overlR, overlG, overlB));
                busyOverlayBrush.Freeze();
                brushMap["BusyOverlayBackground"] = busyOverlayBrush;

                foreach (var kv in brushMap)
                    themeDict[kv.Key] = kv.Value;
                _cachedBrushes = brushMap;
                _cachedColorKey = colorKey;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeGradient] 渐变色注入失败（不影响主题加载）: {ex.Message}");
            }
        }

        private static LinearGradientBrush CreateHorizontalGradient(Color start, Color end)
        {
            var brush = new LinearGradientBrush(start, end, new Point(0, 0.5), new Point(1, 0.5));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateVerticalGradient(Color start, Color end)
        {
            var brush = new LinearGradientBrush(start, end, new Point(0.5, 0), new Point(0.5, 1));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateDiagonalGradient(Color start, Color end)
        {
            var brush = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush Create3StopGradient(
            Color color0, Color color1, Color color2)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(color0, 0.00));
            brush.GradientStops.Add(new GradientStop(color1, 0.50));
            brush.GradientStops.Add(new GradientStop(color2, 1.00));
            brush.Freeze();
            return brush;
        }

        private static Color RotateHue(Color c, double degrees)
        {
            RgbToHsl(c, out double h, out double s, out double l);
            h = ((h + degrees) % 360 + 360) % 360;
            return HslToRgb(h, s, l);
        }

        private static void RgbToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            l = (max + min) / 2.0;
            if (delta == 0) { h = 0; s = 0; return; }
            s = delta / (1 - Math.Abs(2 * l - 1));
            if (max == r) h = 60 * (((g - b) / delta % 6 + 6) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            double chroma = (1 - Math.Abs(2 * l - 1)) * s;
            double x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - chroma / 2;
            double r, g, b;
            if (h < 60) { r = chroma; g = x; b = 0; }
            else if (h < 120) { r = x; g = chroma; b = 0; }
            else if (h < 180) { r = 0; g = chroma; b = x; }
            else if (h < 240) { r = 0; g = x; b = chroma; }
            else if (h < 300) { r = x; g = 0; b = chroma; }
            else { r = chroma; g = 0; b = x; }
            return Color.FromArgb(255,
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255));
        }

        private static double GetLuminance(Color c)
            => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        private static Color GetColor(ResourceDictionary dict, string key)
        {
            if (dict[key] is SolidColorBrush brush)
                return brush.Color;
            return Colors.Transparent;
        }

        private static Color BlendColors(Color a, Color b, double ratio)
        {
            ratio = Math.Clamp(ratio, 0.0, 1.0);
            return Color.FromArgb(
                a.A,
                (byte)(a.R + (b.R - a.R) * ratio),
                (byte)(a.G + (b.G - a.G) * ratio),
                (byte)(a.B + (b.B - a.B) * ratio));
        }

        private static Color ShiftColor(Color c, int delta)
        {
            return Color.FromArgb(
                c.A,
                (byte)Math.Clamp(c.R + delta, 0, 255),
                (byte)Math.Clamp(c.G + delta, 0, 255),
                (byte)Math.Clamp(c.B + delta, 0, 255));
        }
    }
}
