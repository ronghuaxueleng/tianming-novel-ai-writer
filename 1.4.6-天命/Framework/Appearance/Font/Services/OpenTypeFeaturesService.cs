using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.Appearance.Font.Services
{
    public class OpenTypeFeature
    {
        public string Tag { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PreviewText { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = false;
    }

    public class OpenTypeFeaturesService
    {
        private readonly Dictionary<string, List<string>> _knownFontFeatures;

        private readonly Dictionary<string, (string Name, string Description, string Preview)> _featureDefinitions;

        public OpenTypeFeaturesService()
        {
            _knownFontFeatures = InitializeKnownFontFeatures();
            _featureDefinitions = InitializeFeatureDefinitions();
        }

        public List<OpenTypeFeature> GetSupportedFeatures(string fontName)
        {
            var features = new List<OpenTypeFeature>();

            if (string.IsNullOrWhiteSpace(fontName))
            {
                return features;
            }

            var supportedTags = new List<string>();
            foreach (var kvp in _knownFontFeatures)
            {
                if (fontName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    supportedTags.AddRange(kvp.Value);
                    break;
                }
            }

            if (supportedTags.Count == 0)
            {
                supportedTags.AddRange(new[] { "liga", "calt" });
            }

            foreach (var tag in supportedTags.Distinct())
            {
                if (_featureDefinitions.TryGetValue(tag, out var definition))
                {
                    features.Add(new OpenTypeFeature
                    {
                        Tag = tag,
                        Name = definition.Name,
                        Description = definition.Description,
                        PreviewText = definition.Preview,
                        IsEnabled = tag == "liga" || tag == "calt"
                    });
                }
            }

            TM.App.Log($"[OpenType] {fontName}: 支持 {features.Count} 个特性");
            return features;
        }

        public bool SupportsFeature(string fontName, string featureTag)
        {
            var features = GetSupportedFeatures(fontName);
            return features.Any(f => f.Tag.Equals(featureTag, StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<string, List<string>> InitializeKnownFontFeatures()
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fira Code"] = new List<string> 
                { 
                    "liga", "calt", "ss01", "ss02", "ss03", "ss04", "ss05", 
                    "ss06", "ss07", "ss08", "ss09", "ss10", "zero", "onum" 
                },

                ["JetBrains Mono"] = new List<string> 
                { 
                    "liga", "calt", "ss01", "ss02", "ss03", "ss04", "ss05", 
                    "zero", "cv01", "cv02", "cv03", "cv04", "cv05" 
                },

                ["Cascadia Code"] = new List<string> 
                { 
                    "liga", "calt", "ss01", "ss02", "ss03", "ss19", "ss20", 
                    "zero", "cv01", "cv02", "cv03", "cv04" 
                },

                ["Cascadia Mono"] = new List<string> 
                { 
                    "calt", "ss01", "ss02", "ss03", "ss19", "ss20", "zero" 
                },

                ["Source Code Pro"] = new List<string> 
                { 
                    "liga", "calt", "zero", "onum", "ss01", "ss02" 
                },

                ["Consolas"] = new List<string> 
                { 
                    "calt" 
                },

                ["Courier New"] = new List<string> 
                { 
                    "liga", "calt" 
                },

                ["Inconsolata"] = new List<string> 
                { 
                    "liga", "calt" 
                },

                ["Monaco"] = new List<string> 
                { 
                    "liga", "calt" 
                }
            };
        }

        private Dictionary<string, (string Name, string Description, string Preview)> InitializeFeatureDefinitions()
        {
            return new Dictionary<string, (string, string, string)>
            {
                ["liga"] = ("标准连字", "启用标准编程连字（->, =>, !=等）", "-> => >= != == ==="),
                ["calt"] = ("上下文替代", "基于上下文的字形替换", "fi fl ff ffi ffl"),
                ["dlig"] = ("自由连字", "装饰性连字", "ct st"),

                ["zero"] = ("带斜线的零", "区分数字0和字母O", "0 O 00 10"),
                ["onum"] = ("旧式数字", "使用旧式数字样式", "0123456789"),
                ["tnum"] = ("等宽数字", "数字等宽对齐", "1234567890"),
                ["lnum"] = ("衬线数字", "衬线风格的数字", "0123456789"),

                ["ss01"] = ("样式集01", "替代字形样式1", "a @ r"),
                ["ss02"] = ("样式集02", "替代字形样式2", "g & l"),
                ["ss03"] = ("样式集03", "替代字形样式3", "i j 1"),
                ["ss04"] = ("样式集04", "替代字形样式4", "$ %"),
                ["ss05"] = ("样式集05", "替代字形样式5", "@ # &"),
                ["ss06"] = ("样式集06", "替代字形样式6", "\\ / |"),
                ["ss07"] = ("样式集07", "替代字形样式7", "=~ !~"),
                ["ss08"] = ("样式集08", "替代字形样式8", "== === !="),
                ["ss09"] = ("样式集09", "替代字形样式9", ">>= <<="),
                ["ss10"] = ("样式集10", "替代字形样式10", "&& ||"),

                ["ss19"] = ("Powerline符号", "Powerline状态栏符号", "   "),
                ["ss20"] = ("图标符号", "编程图标符号", "☰ ⚙ ▶"),

                ["cv01"] = ("字符变体01", "字符a的替代形状", "a ɑ α"),
                ["cv02"] = ("字符变体02", "字符g的替代形状", "g ɡ"),
                ["cv03"] = ("字符变体03", "字符i的替代形状", "i ı"),
                ["cv04"] = ("字符变体04", "字符l的替代形状", "l ɭ"),
                ["cv05"] = ("字符变体05", "字符0的替代形状", "0 O Ø"),

                ["smcp"] = ("小型大写字母", "小号大写字母", "Abc → ABC"),
                ["c2sc"] = ("大写转小型大写", "大写字母转小型大写", "ABC → ABC"),

                ["frac"] = ("分数", "自动分数格式", "1/2 → ½"),
                ["sups"] = ("上标", "上标字符", "x² x³"),
                ["subs"] = ("下标", "下标字符", "H₂O"),

                ["case"] = ("大小写敏感", "标点符号适配大写", "A-B A/B"),
                ["cpsp"] = ("大写间距", "大写字母间距调整", "CAPS")
            };
        }

        public (string Name, string Description, string Preview) GetFeatureInfo(string featureTag)
        {
            if (_featureDefinitions.TryGetValue(featureTag, out var info))
            {
                return info;
            }
            return (featureTag, "未知特性", "");
        }
    }
}

