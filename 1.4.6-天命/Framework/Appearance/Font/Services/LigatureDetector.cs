using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    public class LigatureDetector
    {
        private readonly Dictionary<string, List<string>> _cache = new();

        private static readonly string[] CommonLigatures = new[]
        {
            "->", "=>", ">=", "<=", "!=", "==", "===", "!==",
            "&&", "||", "++", "--", "<<", ">>", "::", "..",
            "...", "/*", "*/", "//", "/**", "**/",
            "<-", "<->", "==>", "<==>", "<==", "|>", "<|",
            "~>", "<~", ">>=", "<<=", "***", "|||", "///"
        };

        private static readonly Dictionary<string, List<string>> KnownLigatureFonts = new()
        {
            { "fira code", new List<string>(CommonLigatures) },
            { "jetbrains mono", new List<string>(CommonLigatures) },
            { "cascadia code", new List<string>(CommonLigatures) },
            { "cascadia mono", new List<string>(CommonLigatures) },
            { "monoid", new List<string>(CommonLigatures) },
            { "hasklig", new List<string>(CommonLigatures) },
            { "victor mono", new List<string>(CommonLigatures) }
        };

        public LigatureDetector() { }

        public bool SupportsLigatures(string fontFamilyName)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName))
                return false;

            var ligatures = GetSupportedLigatures(fontFamilyName);
            return ligatures.Count > 0;
        }

        public List<string> GetSupportedLigatures(string fontFamilyName)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName))
                return new List<string>();

            if (_cache.TryGetValue(fontFamilyName, out var cached))
                return new List<string>(cached);

            var lowerName = fontFamilyName.ToLowerInvariant();
            foreach (var kvp in KnownLigatureFonts)
            {
                if (lowerName.Contains(kvp.Key))
                {
                    _cache[fontFamilyName] = kvp.Value;
                    TM.App.Log($"[LigatureDetector] {fontFamilyName}: 已知连字字体, 支持 {kvp.Value.Count} 个连字");
                    return new List<string>(kvp.Value);
                }
            }

            var detected = DetectOpenTypeLigatures(fontFamilyName);
            _cache[fontFamilyName] = detected;
            TM.App.Log($"[LigatureDetector] {fontFamilyName}: OpenType检测, 支持 {detected.Count} 个连字");
            return detected;
        }

        private List<string> DetectOpenTypeLigatures(string fontFamilyName)
        {
            var supportedLigatures = new List<string>();

            try
            {
                var fontFamily = new FontFamily(fontFamilyName);
                var typeface = new Typeface(fontFamily, System.Windows.FontStyles.Normal, 
                    System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);

                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    return supportedLigatures;
                }

                var charMap = glyphTypeface.CharacterToGlyphMap;

                foreach (var ligature in CommonLigatures.Take(10))
                {
                    if (charMap.Count > 500)
                    {
                        supportedLigatures.Add(ligature);
                    }
                }

                if (charMap.Count > 1000)
                {
                    supportedLigatures.AddRange(CommonLigatures.Skip(10));
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LigatureDetector] OpenType检测失败: {fontFamilyName}, 错误:{ex.Message}");
            }

            return supportedLigatures;
        }

        public string GenerateLigaturePreviewText(List<string> supportedLigatures)
        {
            if (supportedLigatures == null || supportedLigatures.Count == 0)
                return "此字体不支持编程连字";

            var lines = new List<string>();
            lines.Add("// 编程连字预览");
            lines.Add("");

            var arrows = supportedLigatures.Where(l => l.Contains("->") || l.Contains("=>") || l.Contains("<-")).ToList();
            if (arrows.Any())
            {
                lines.Add($"箭头: {string.Join("  ", arrows)}");
            }

            var comparisons = supportedLigatures.Where(l => l.Contains("=") || l.Contains("!")).ToList();
            if (comparisons.Any())
            {
                lines.Add($"比较: {string.Join("  ", comparisons)}");
            }

            var logical = supportedLigatures.Where(l => l.Contains("&&") || l.Contains("||")).ToList();
            if (logical.Any())
            {
                lines.Add($"逻辑: {string.Join("  ", logical)}");
            }

            var others = supportedLigatures.Except(arrows).Except(comparisons).Except(logical).ToList();
            if (others.Any())
            {
                lines.Add($"其他: {string.Join("  ", others)}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public void ClearCache()
        {
            _cache.Clear();
            TM.App.Log("[LigatureDetector] 缓存已清除");
        }
    }
}

