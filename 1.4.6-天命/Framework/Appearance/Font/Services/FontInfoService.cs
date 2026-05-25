using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontInfoModel
    {
        public string FontName { get; set; } = string.Empty;
        public string Designer { get; set; } = "未知";
        public string Version { get; set; } = "未知";
        public string License { get; set; } = "未知";
        public string Copyright { get; set; } = "未知";
        public List<string> SupportedScripts { get; set; } = new();
        public int MinWeight { get; set; } = 400;
        public int MaxWeight { get; set; } = 400;
        public bool SupportsItalic { get; set; } = false;
        public bool IsMonospace { get; set; } = false;
        public int GlyphCount { get; set; } = 0;
        public bool SupportsLatin { get; set; } = false;
        public bool SupportsCyrillic { get; set; } = false;
        public bool SupportsCJK { get; set; } = false;
        public bool SupportsArabic { get; set; } = false;
        public bool SupportsSymbols { get; set; } = false;
    }

    public class FontInfoService
    {
        private readonly Dictionary<string, FontInfoModel> _cache = new();
        private readonly FontCategoryService _categoryService;

        public FontInfoService(FontCategoryService categoryService)
        {
            _categoryService = categoryService;
        }

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

            System.Diagnostics.Debug.WriteLine($"[FontInfoService] {key}: {ex.Message}");
        }

        public FontInfoModel GetFontInfo(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
            {
                return new FontInfoModel { FontName = "未选择字体" };
            }

            if (_cache.TryGetValue(fontName, out var cachedInfo))
            {
                return cachedInfo;
            }

            var info = ParseFontInfo(fontName);
            _cache[fontName] = info;
            return info;
        }

        private FontInfoModel ParseFontInfo(string fontName)
        {
            var info = new FontInfoModel { FontName = fontName };

            try
            {
                var fontFamily = new FontFamily(fontName);
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    info.Designer = GetNameTableEntry(glyphTypeface, "Designer") ?? "未知";
                    info.Version = glyphTypeface.Version.ToString() ?? "未知";
                    info.License = GetNameTableEntry(glyphTypeface, "License") ?? "未知";
                    info.Copyright = GetNameTableEntry(glyphTypeface, "Copyright") ?? "未知";

                    info.GlyphCount = glyphTypeface.CharacterToGlyphMap.Count;

                    info.MinWeight = 400;
                    info.MaxWeight = 700;

                    var italicTypeface = new Typeface(fontFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
                    info.SupportsItalic = italicTypeface.TryGetGlyphTypeface(out _);

                    info.IsMonospace = _categoryService.IsMonospace(fontName);

                    DetectCharacterSetSupport(glyphTypeface, info);

                    TM.App.Log($"[FontInfoService] 解析字体信息: {fontName}, 字形数:{info.GlyphCount}");
                }
                else
                {
                    TM.App.Log($"[FontInfoService] 无法获取字体字形信息: {fontName}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontInfoService] 解析字体信息失败: {fontName}, 错误:{ex.Message}");
            }

            return info;
        }

        private string? GetNameTableEntry(GlyphTypeface glyphTypeface, string key)
        {
            try
            {
                return null;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetNameTableEntry), ex);
                return null;
            }
        }

        private void DetectCharacterSetSupport(GlyphTypeface glyphTypeface, FontInfoModel info)
        {
            try
            {
                var charMap = glyphTypeface.CharacterToGlyphMap;

                info.SupportsLatin = charMap.ContainsKey('A') && charMap.ContainsKey('z');

                info.SupportsCyrillic = charMap.ContainsKey('\u0410');

                info.SupportsCJK = charMap.ContainsKey('\u4E00') ||
                                   charMap.ContainsKey('\u4E2D') ||
                                   charMap.ContainsKey('\u6587');

                info.SupportsArabic = charMap.ContainsKey('\u0627');

                info.SupportsSymbols = charMap.ContainsKey('©') || 
                                       charMap.ContainsKey('®') || 
                                       charMap.ContainsKey('™');

                info.SupportedScripts.Clear();
                if (info.SupportsLatin) info.SupportedScripts.Add("拉丁");
                if (info.SupportsCyrillic) info.SupportedScripts.Add("西里尔");
                if (info.SupportsCJK) info.SupportedScripts.Add("中日韩");
                if (info.SupportsArabic) info.SupportedScripts.Add("阿拉伯");
                if (info.SupportsSymbols) info.SupportedScripts.Add("符号");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontInfoService] 字符集检测失败: {ex.Message}");
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            TM.App.Log("[FontInfoService] 缓存已清除");
        }

        public List<string> GetFontVariants(string fontName)
        {
            var variants = new List<string>();

            try
            {
                var fontFamily = new FontFamily(fontName);

                foreach (var typeface in fontFamily.GetTypefaces())
                {
                    string variant = $"{typeface.Weight} {typeface.Style} {typeface.Stretch}";
                    variants.Add(variant);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontInfoService] 获取字体变体失败: {fontName}, 错误:{ex.Message}");
            }

            return variants;
        }
    }
}

