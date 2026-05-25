using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum FontCategory
    {
        All,
        Monospace,
        Serif,
        SansSerif,
        Script,
        Decorative,
        CJK,
        System
    }

    public class FontItem
    {
        public string FontName { get; set; } = string.Empty;
        public FontCategory Category { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsMonospace { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class FontCategoryService
    {
        private static readonly object _debugLogLock = new object();
        private static readonly HashSet<string> _debugLoggedKeys = new HashSet<string>();

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

            System.Diagnostics.Debug.WriteLine($"[FontCategoryService] {key}: {ex.Message}");
        }

        private readonly Dictionary<string, FontCategory> _fontCategoryMap;
        private readonly HashSet<string> _monospaceFonts;
        private readonly HashSet<string> _cjkFonts;

        public FontCategoryService()
        {
            _fontCategoryMap = new Dictionary<string, FontCategory>(StringComparer.OrdinalIgnoreCase)
            {
                {"Consolas", FontCategory.Monospace},
                {"Courier", FontCategory.Monospace},
                {"Courier New", FontCategory.Monospace},
                {"Lucida Console", FontCategory.Monospace},
                {"Monaco", FontCategory.Monospace},
                {"Menlo", FontCategory.Monospace},
                {"Source Code Pro", FontCategory.Monospace},
                {"Fira Code", FontCategory.Monospace},
                {"JetBrains Mono", FontCategory.Monospace},
                {"Cascadia Code", FontCategory.Monospace},
                {"Cascadia Mono", FontCategory.Monospace},
                {"Inconsolata", FontCategory.Monospace},
                {"DejaVu Sans Mono", FontCategory.Monospace},
                {"Roboto Mono", FontCategory.Monospace},
                {"IBM Plex Mono", FontCategory.Monospace},

                {"Times New Roman", FontCategory.Serif},
                {"Georgia", FontCategory.Serif},
                {"Palatino", FontCategory.Serif},
                {"Garamond", FontCategory.Serif},
                {"Baskerville", FontCategory.Serif},
                {"宋体", FontCategory.Serif},
                {"SimSun", FontCategory.Serif},
                {"NSimSun", FontCategory.Serif},
                {"FangSong", FontCategory.Serif},
                {"仿宋", FontCategory.Serif},

                {"Arial", FontCategory.SansSerif},
                {"Helvetica", FontCategory.SansSerif},
                {"Verdana", FontCategory.SansSerif},
                {"Tahoma", FontCategory.SansSerif},
                {"Trebuchet MS", FontCategory.SansSerif},
                {"Segoe UI", FontCategory.SansSerif},
                {"Calibri", FontCategory.SansSerif},
                {"Roboto", FontCategory.SansSerif},
                {"Open Sans", FontCategory.SansSerif},
                {"Lato", FontCategory.SansSerif},
                {"Microsoft YaHei", FontCategory.SansSerif},
                {"Microsoft YaHei UI", FontCategory.SansSerif},
                {"微软雅黑", FontCategory.SansSerif},
                {"黑体", FontCategory.SansSerif},
                {"SimHei", FontCategory.SansSerif},

                {"Comic Sans MS", FontCategory.Script},
                {"Brush Script MT", FontCategory.Script},
                {"Lucida Handwriting", FontCategory.Script},
                {"楷体", FontCategory.Script},
                {"KaiTi", FontCategory.Script},
                {"行楷", FontCategory.Script},
                {"华文行楷", FontCategory.Script},

                {"Impact", FontCategory.Decorative},
                {"Papyrus", FontCategory.Decorative},
                {"Curlz MT", FontCategory.Decorative},
                {"Jokerman", FontCategory.Decorative}
            };

            _monospaceFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Consolas", "Courier", "Courier New", "Lucida Console", "Monaco", 
                "Menlo", "Source Code Pro", "Fira Code", "JetBrains Mono", 
                "Cascadia Code", "Cascadia Mono", "Inconsolata", "DejaVu Sans Mono",
                "Roboto Mono", "IBM Plex Mono", "Noto Sans Mono", "Ubuntu Mono"
            };

            _cjkFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑", "SimSun", "宋体", 
                "SimHei", "黑体", "KaiTi", "楷体", "FangSong", "仿宋", "NSimSun", 
                "Microsoft JhengHei", "微软正黑体", "PMingLiU", "新细明体", "DFKai-SB", 
                "标楷体", "Hiragino Sans", "ヒラギノ角ゴシック", "Hiragino Kaku Gothic Pro", 
                "Yu Gothic", "游ゴシック", "Meiryo", "メイリオ", "MS Gothic", "MS UI Gothic",
                "SimSun-ExtB", "MingLiU", "MingLiU_HKSCS", "Apple LiGothic", "LiHei Pro",
                "Noto Sans CJK", "Source Han Sans", "思源黑体", "Noto Serif CJK", 
                "Source Han Serif", "思源宋体", "华文黑体", "华文宋体", "华文仿宋", 
                "华文楷体", "华文细黑", "华文中宋", "华文新魏", "方正", "文泉驿"
            };
        }

        public FontCategory ClassifyFont(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return FontCategory.System;

            if (_fontCategoryMap.TryGetValue(fontName, out var category))
                return category;

            if (_cjkFonts.Any(cjk => fontName.Contains(cjk, StringComparison.OrdinalIgnoreCase)))
                return FontCategory.CJK;

            if (_monospaceFonts.Contains(fontName) || 
                fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
                fontName.Contains("Console", StringComparison.OrdinalIgnoreCase))
                return FontCategory.Monospace;

            return FontCategory.SansSerif;
        }

        public bool IsMonospace(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return false;

            if (_monospaceFonts.Contains(fontName))
                return true;

            try
            {
                var fontFamily = new FontFamily(fontName);
                var typeface = new Typeface(fontFamily, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);

                if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    var testChars = new[] { 'i', 'm', 'W', '0', '1' };
                    var widths = new List<double>();

                    foreach (var ch in testChars)
                    {
                        if (glyphTypeface.CharacterToGlyphMap.TryGetValue(ch, out var glyphIndex))
                        {
                            widths.Add(glyphTypeface.AdvanceWidths[glyphIndex]);
                        }
                    }

                    if (widths.Count >= 3)
                    {
                        var avgWidth = widths.Average();
                        var maxDeviation = widths.Max(w => Math.Abs(w - avgWidth));

                        return maxDeviation / avgWidth < 0.05;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(IsMonospace), ex);
            }

            return false;
        }

        public List<string> GenerateTags(string fontName, FontCategory category)
        {
            var tags = new List<string>();

            if (IsMonospace(fontName))
                tags.Add("等宽");

            if (category == FontCategory.CJK)
                tags.Add("CJK");
            else if (category == FontCategory.Serif)
                tags.Add("衬线");
            else if (category == FontCategory.SansSerif)
                tags.Add("非衬线");
            else if (category == FontCategory.Script)
                tags.Add("手写");
            else if (category == FontCategory.Decorative)
                tags.Add("装饰");

            return tags;
        }

        public static string GetCategoryDisplayName(FontCategory category)
        {
            return category switch
            {
                FontCategory.All => "全部",
                FontCategory.Monospace => "等宽",
                FontCategory.Serif => "衬线",
                FontCategory.SansSerif => "非衬线",
                FontCategory.Script => "手写",
                FontCategory.Decorative => "装饰",
                FontCategory.CJK => "中日韩",
                FontCategory.System => "系统",
                _ => "未分类"
            };
        }
    }
}

