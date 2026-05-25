using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using TM.Framework.Appearance.Font.Models;

namespace TM.Framework.Appearance.Font.Services
{
    public class EditorFontPreset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public FontSettings Settings { get; set; } = new();
        public bool IsInstalled { get; set; } = false;
        public string DownloadUrl { get; set; } = string.Empty;
        public List<string> Features { get; set; } = new();
    }

    public class EditorFontPresetService
    {
        private readonly List<EditorFontPreset> _builtInPresets;
        private readonly HashSet<string> _installedFonts;

        public EditorFontPresetService()
        {
            _installedFonts = LoadInstalledFonts();
            _builtInPresets = InitializeBuiltInPresets();
            UpdateInstallationStatus();
        }

        public List<EditorFontPreset> GetBuiltInPresets()
        {
            UpdateInstallationStatus();
            return _builtInPresets.ToList();
        }

        public bool IsFontInstalled(string fontName)
        {
            return _installedFonts.Contains(fontName);
        }

        private HashSet<string> LoadInstalledFonts()
        {
            var installedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fontFamily in Fonts.SystemFontFamilies)
            {
                installedFonts.Add(fontFamily.Source);
            }

            TM.App.Log($"[EditorFontPreset] 加载系统字体: {installedFonts.Count} 个");
            return installedFonts;
        }

        private void UpdateInstallationStatus()
        {
            foreach (var preset in _builtInPresets)
            {
                preset.IsInstalled = IsFontInstalled(preset.Settings.FontFamily);
            }
        }

        private List<EditorFontPreset> InitializeBuiltInPresets()
        {
            return new List<EditorFontPreset>
            {
                new EditorFontPreset
                {
                    Name = "Consolas（Windows经典）",
                    Description = "微软经典等宽字体，清晰易读，Windows内置",
                    Settings = new FontSettings
                    {
                        FontFamily = "Consolas",
                        FontSize = 12,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "https://docs.microsoft.com/typography/font-list/consolas",
                    Features = new List<string> { "等宽", "清晰", "内置" }
                },

                new EditorFontPreset
                {
                    Name = "Fira Code（连字推荐）",
                    Description = "Mozilla开发的编程字体，出色的连字支持",
                    Settings = new FontSettings
                    {
                        FontFamily = "Fira Code",
                        FontSize = 11,
                        FontWeight = "Normal",
                        LineHeight = 1.6,
                        LetterSpacing = 0,
                        EnableLigatures = true
                    },
                    DownloadUrl = "https://github.com/tonsky/FiraCode",
                    Features = new List<string> { "等宽", "连字", "开源" }
                },

                new EditorFontPreset
                {
                    Name = "JetBrains Mono（专业）",
                    Description = "JetBrains出品，专为开发者设计，连字美观",
                    Settings = new FontSettings
                    {
                        FontFamily = "JetBrains Mono",
                        FontSize = 12,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = true
                    },
                    DownloadUrl = "https://www.jetbrains.com/lp/mono/",
                    Features = new List<string> { "等宽", "连字", "专业" }
                },

                new EditorFontPreset
                {
                    Name = "Cascadia Code（现代）",
                    Description = "微软现代终端字体，支持连字和多种样式集",
                    Settings = new FontSettings
                    {
                        FontFamily = "Cascadia Code",
                        FontSize = 11,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = true
                    },
                    DownloadUrl = "https://github.com/microsoft/cascadia-code",
                    Features = new List<string> { "等宽", "连字", "现代", "OpenType" }
                },

                new EditorFontPreset
                {
                    Name = "Source Code Pro（Adobe）",
                    Description = "Adobe开源字体，极佳的可读性",
                    Settings = new FontSettings
                    {
                        FontFamily = "Source Code Pro",
                        FontSize = 11,
                        FontWeight = "Normal",
                        LineHeight = 1.6,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "https://github.com/adobe-fonts/source-code-pro",
                    Features = new List<string> { "等宽", "清晰", "开源" }
                },

                new EditorFontPreset
                {
                    Name = "Inconsolata（紧凑）",
                    Description = "紧凑型等宽字体，适合小屏幕",
                    Settings = new FontSettings
                    {
                        FontFamily = "Inconsolata",
                        FontSize = 12,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "https://fonts.google.com/specimen/Inconsolata",
                    Features = new List<string> { "等宽", "紧凑", "Google Fonts" }
                },

                new EditorFontPreset
                {
                    Name = "Monaco（macOS风格）",
                    Description = "macOS经典等宽字体",
                    Settings = new FontSettings
                    {
                        FontFamily = "Monaco",
                        FontSize = 11,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "https://en.wikipedia.org/wiki/Monaco_(typeface)",
                    Features = new List<string> { "等宽", "macOS" }
                },

                new EditorFontPreset
                {
                    Name = "Courier New（经典）",
                    Description = "传统打字机风格，广泛支持",
                    Settings = new FontSettings
                    {
                        FontFamily = "Courier New",
                        FontSize = 12,
                        FontWeight = "Normal",
                        LineHeight = 1.6,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "",
                    Features = new List<string> { "等宽", "经典", "内置" }
                },

                new EditorFontPreset
                {
                    Name = "Lucida Console（内置）",
                    Description = "Windows内置，简洁清晰",
                    Settings = new FontSettings
                    {
                        FontFamily = "Lucida Console",
                        FontSize = 11,
                        FontWeight = "Normal",
                        LineHeight = 1.5,
                        LetterSpacing = 0,
                        EnableLigatures = false
                    },
                    DownloadUrl = "",
                    Features = new List<string> { "等宽", "内置" }
                }
            };
        }
    }
}

