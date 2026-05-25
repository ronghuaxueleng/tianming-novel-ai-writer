using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontRecommendation
    {
        public string FontName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    public class FontThemeMatchService
    {
        private readonly Dictionary<ThemeType, List<string>> _themeToFontMap;
        private readonly FontCategoryService _categoryService;
        private readonly ThemeManager _themeManager;

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

            System.Diagnostics.Debug.WriteLine($"[FontThemeMatchService] {key}: {ex.Message}");
        }

        public FontThemeMatchService(FontCategoryService categoryService, ThemeManager themeManager)
        {
            _categoryService = categoryService;
            _themeManager = themeManager;
            _themeToFontMap = new Dictionary<ThemeType, List<string>>
            {
                {
                    ThemeType.Light,
                    new List<string>
                    {
                        "Segoe UI",
                        "Microsoft YaHei UI",
                        "微软雅黑",
                        "Calibri",
                        "Arial",
                        "Noto Sans"
                    }
                },
                {
                    ThemeType.Dark,
                    new List<string>
                    {
                        "SF Pro Display",
                        "苹方",
                        "PingFang SC",
                        "Roboto",
                        "Helvetica Neue",
                        "Microsoft YaHei UI"
                    }
                },
                {
                    ThemeType.Green,
                    new List<string>
                    {
                        "Microsoft YaHei UI",
                        "Segoe UI",
                        "Calibri"
                    }
                },
                {
                    ThemeType.Business,
                    new List<string>
                    {
                        "Calibri",
                        "Arial",
                        "Microsoft YaHei UI"
                    }
                }
            };
        }

        public List<FontRecommendation> GetRecommendations(ThemeType themeType, List<string> availableFonts)
        {
            var recommendations = new List<FontRecommendation>();

            if (!_themeToFontMap.TryGetValue(themeType, out var recommendedFonts))
            {
                recommendedFonts = _themeToFontMap[ThemeType.Light];
            }

            foreach (var fontName in recommendedFonts)
            {
                if (availableFonts.Contains(fontName, StringComparer.OrdinalIgnoreCase))
                {
                    var recommendation = new FontRecommendation
                    {
                        FontName = fontName,
                        Reason = GetRecommendationReason(fontName, themeType),
                        Score = CalculateScore(fontName, themeType)
                    };
                    recommendations.Add(recommendation);
                }
            }

            return recommendations.OrderByDescending(r => r.Score).Take(3).ToList();
        }

        private string GetRecommendationReason(string fontName, ThemeType themeType)
        {
            var category = _categoryService.ClassifyFont(fontName);

            return (fontName, themeType) switch
            {
                ("Segoe UI", ThemeType.Light) => "现代简洁,阅读友好,适合浅色界面",
                ("Microsoft YaHei UI", ThemeType.Light) => "中文优化,清晰易读,Windows标准字体",
                ("微软雅黑", ThemeType.Light) => "中文优化,清晰易读,Windows标准字体",
                ("Calibri", ThemeType.Light) => "专业办公,易读性高,Office默认字体",
                ("Arial", ThemeType.Light) => "经典通用,广泛兼容,跨平台标准",
                ("Noto Sans", ThemeType.Light) => "Google设计,多语言支持,开源优选",

                ("SF Pro Display", ThemeType.Dark) => "Apple设计,深色优化,现代美观",
                ("苹方", ThemeType.Dark) => "Apple中文字体,深色界面清晰,专业设计",
                ("PingFang SC", ThemeType.Dark) => "Apple中文字体,深色界面清晰,专业设计",
                ("Roboto", ThemeType.Dark) => "Material Design,深色优化,现代简洁",
                ("Helvetica Neue", ThemeType.Dark) => "经典无衬线,深色界面友好,专业设计",

                _ when category == FontCategory.SansSerif => "无衬线字体,界面友好,易读性好",
                _ when category == FontCategory.Serif => "衬线字体,专业正式,适合长文本",
                _ when category == FontCategory.Monospace => "等宽字体,代码友好,数字对齐",
                _ when category == FontCategory.CJK => "中日韩优化,本地化支持,字符完整",
                _ => "通用字体,兼容性好,适用多数场景"
            };
        }

        private double CalculateScore(string fontName, ThemeType themeType)
        {
            double score = 50;

            var category = _categoryService.ClassifyFont(fontName);
            if (category == FontCategory.SansSerif || category == FontCategory.CJK)
            {
                score += 20;
            }

            if (_themeToFontMap.TryGetValue(themeType, out var recommendedFonts))
            {
                int index = recommendedFonts.IndexOf(fontName);
                if (index >= 0)
                {
                    score += 30 - (index * 5);
                }
            }

            if (fontName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("Segoe", StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            return Math.Min(score, 100);
        }

        public ThemeType GetCurrentThemeType()
        {
            try
            {
                return _themeManager.CurrentTheme;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetCurrentThemeType), ex);
                return ThemeType.Light;
            }
        }
    }
}

