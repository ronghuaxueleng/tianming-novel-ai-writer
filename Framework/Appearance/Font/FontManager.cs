using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TM.Framework.Appearance.Font.Models;

namespace TM.Framework.Appearance.Font
{
    public static class FontManager
    {
        private static ResourceDictionary? _uiFontDict;
        private static ResourceDictionary? _editorFontDict;
        public static FontConfiguration LoadConfiguration()
        {
            return ServiceLocator.Get<FontConfigurationSettings>().GetConfiguration();
        }

        public static void SaveConfiguration(FontConfiguration config)
        {
            ServiceLocator.Get<FontConfigurationSettings>().UpdateConfiguration(config);
        }

        public static void ApplyUIFont(FontSettings settings)
        {
            try
            {
                void ApplyUIFontCore()
                {
                    if (_uiFontDict == null)
                    {
                        _uiFontDict = new ResourceDictionary { ["_FontManagerTag"] = true };
                        Application.Current.Resources.MergedDictionaries.Add(_uiFontDict);
                    }
                    var d = _uiFontDict;
                    d["GlobalFontFamily"] = new FontFamily(settings.FontFamily);
                    d["GlobalFontSize"] = settings.FontSize;
                    d["GlobalFontWeight"] = ParseFontWeight(settings.FontWeight);
                    d["GlobalLineHeight"] = settings.LineHeight;
                    d["GlobalLetterSpacing"] = settings.LetterSpacing;
                    d["FontSizeXXL"] = settings.FontSize * 2.29;
                    d["FontSizeXL"] = settings.FontSize * 1.71;
                    d["FontSizeLarge"] = settings.FontSize * 1.29;
                    d["FontSizeMedium"] = settings.FontSize * 1.14;
                    d["FontSizeNormal"] = settings.FontSize;
                    d["FontSizeSmall"] = settings.FontSize * 0.93;
                    d["FontSizeXS"] = settings.FontSize * 0.86;
                    d["FontSizeTiny"] = settings.FontSize * 0.79;
                    d["GlobalTextRenderingMode"] = ParseTextRenderingMode(settings.TextRendering);
                    d["GlobalTextFormattingMode"] = ParseTextFormattingMode(settings.TextFormatting);
                    d["GlobalTextHintingMode"] = ParseTextHintingMode(settings.TextHinting);
                }

                if (Application.Current.Dispatcher.CheckAccess())
                    ApplyUIFontCore();
                else
                    Application.Current.Dispatcher.BeginInvoke(ApplyUIFontCore);

                TM.App.Log($"[FontManager] UI字体已应用: {settings.FontFamily}, {settings.FontSize}px (已更新所有比例字体和渲染选项)");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontManager] 应用UI字体失败: {ex.Message}");
                throw;
            }
        }

        public static void ApplyEditorFont(FontSettings settings)
        {
            try
            {
                void ApplyEditorFontCore()
                {
                    if (_editorFontDict == null)
                    {
                        _editorFontDict = new ResourceDictionary { ["_EditorFontManagerTag"] = true };
                        Application.Current.Resources.MergedDictionaries.Add(_editorFontDict);
                    }
                    var d = _editorFontDict;
                    d["EditorFontFamily"] = new FontFamily(settings.FontFamily);
                    d["EditorFontSize"] = settings.FontSize;
                    d["EditorFontWeight"] = ParseFontWeight(settings.FontWeight);
                    d["EditorLineHeight"] = settings.LineHeight;
                    d["EditorLetterSpacing"] = settings.LetterSpacing;
                    d["EditorFontSizeLarge"] = settings.FontSize * 1.15;
                    d["EditorFontSizeSmall"] = settings.FontSize * 0.85;
                }

                if (Application.Current.Dispatcher.CheckAccess())
                    ApplyEditorFontCore();
                else
                    Application.Current.Dispatcher.BeginInvoke(ApplyEditorFontCore);

                TM.App.Log($"[FontManager] 编辑器字体已应用: {settings.FontFamily}, {settings.FontSize}px (已更新比例字体)");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontManager] 应用编辑器字体失败: {ex.Message}");
                throw;
            }
        }

        private static readonly Lazy<List<string>> _cachedSystemFonts = new(
            () =>
            {
                try
                {
                    return Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[FontManager] 获取系统字体失败: {ex.Message}");
                    return new List<string> { "Microsoft YaHei UI", "Consolas", "Arial" };
                }
            }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static List<string> GetSystemFonts() => _cachedSystemFonts.Value;

        private static FontWeight ParseFontWeight(string weightString)
        {
            return weightString switch
            {
                "Thin" => FontWeights.Thin,
                "ExtraLight" => FontWeights.ExtraLight,
                "Light" => FontWeights.Light,
                "Normal" => FontWeights.Normal,
                "Medium" => FontWeights.Medium,
                "SemiBold" => FontWeights.SemiBold,
                "Bold" => FontWeights.Bold,
                "ExtraBold" => FontWeights.ExtraBold,
                "Black" => FontWeights.Black,
                _ => FontWeights.Normal
            };
        }

        private static System.Windows.Media.TextRenderingMode ParseTextRenderingMode(Models.TextRenderingMode mode)
        {
            return mode switch
            {
                Models.TextRenderingMode.Auto => System.Windows.Media.TextRenderingMode.Auto,
                Models.TextRenderingMode.Aliased => System.Windows.Media.TextRenderingMode.Aliased,
                Models.TextRenderingMode.Grayscale => System.Windows.Media.TextRenderingMode.Grayscale,
                Models.TextRenderingMode.ClearType => System.Windows.Media.TextRenderingMode.ClearType,
                _ => System.Windows.Media.TextRenderingMode.Auto
            };
        }

        private static System.Windows.Media.TextFormattingMode ParseTextFormattingMode(Models.TextFormattingMode mode)
        {
            return mode switch
            {
                Models.TextFormattingMode.Ideal => System.Windows.Media.TextFormattingMode.Ideal,
                Models.TextFormattingMode.Display => System.Windows.Media.TextFormattingMode.Display,
                _ => System.Windows.Media.TextFormattingMode.Ideal
            };
        }

        private static System.Windows.Media.TextHintingMode ParseTextHintingMode(Models.TextHintingMode mode)
        {
            return mode switch
            {
                Models.TextHintingMode.Auto => System.Windows.Media.TextHintingMode.Auto,
                Models.TextHintingMode.Fixed => System.Windows.Media.TextHintingMode.Fixed,
                _ => System.Windows.Media.TextHintingMode.Auto
            };
        }

        public static void ApplyFontFallback(Services.FontFallbackChain chain)
        {
            try
            {
                var fallbackService = ServiceLocator.Get<Services.FontFallbackService>();
                fallbackService.SetFallbackChain(chain);

                var config = LoadConfiguration();
                ApplyUIFont(config.UIFont);
                ApplyEditorFont(config.EditorFont);

                TM.App.Log("[FontManager] 字体回退链已应用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontManager] 应用回退链失败: {ex.Message}");
            }
        }

        public static System.Threading.Tasks.Task<bool> ExportConfigurationAsync(string? filePath = null)
            => ServiceLocator.Get<Services.FontImportExportService>().ExportConfigurationAsync(filePath);

        public static System.Threading.Tasks.Task<bool> ImportConfigurationAsync(string? filePath = null)
            => ServiceLocator.Get<Services.FontImportExportService>().ImportConfigurationAsync(filePath);

        public static System.Threading.Tasks.Task<bool> ExportAsShareableAsync(string? filePath = null)
            => ServiceLocator.Get<Services.FontImportExportService>().ExportAsShareableAsync(filePath);

        public static FontConfiguration ResetToDefault()
        {
            var defaultConfig = ServiceLocator.Get<FontConfigurationSettings>().ResetToDefault();
            ApplyUIFont(defaultConfig.UIFont);
            ApplyEditorFont(defaultConfig.EditorFont);
            TM.App.Log("[FontManager] 字体配置已重置为默认值并应用到UI");
            return defaultConfig;
        }
    }
}

