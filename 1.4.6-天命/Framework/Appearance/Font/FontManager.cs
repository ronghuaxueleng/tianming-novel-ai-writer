using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using TM.Framework.Appearance.Font.Models;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Font
{
    public static class FontManager
    {
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
                    Application.Current.Resources["GlobalFontFamily"] = new FontFamily(settings.FontFamily);
                    Application.Current.Resources["GlobalFontSize"] = settings.FontSize;
                    Application.Current.Resources["GlobalFontWeight"] = ParseFontWeight(settings.FontWeight);
                    Application.Current.Resources["GlobalLineHeight"] = settings.LineHeight;
                    Application.Current.Resources["GlobalLetterSpacing"] = settings.LetterSpacing;

                    Application.Current.Resources["FontSizeXXL"] = settings.FontSize * 2.29;
                    Application.Current.Resources["FontSizeXL"] = settings.FontSize * 1.71;
                    Application.Current.Resources["FontSizeLarge"] = settings.FontSize * 1.29;
                    Application.Current.Resources["FontSizeMedium"] = settings.FontSize * 1.14;
                    Application.Current.Resources["FontSizeNormal"] = settings.FontSize;
                    Application.Current.Resources["FontSizeSmall"] = settings.FontSize * 0.93;
                    Application.Current.Resources["FontSizeXS"] = settings.FontSize * 0.86;
                    Application.Current.Resources["FontSizeTiny"] = settings.FontSize * 0.79;

                    Application.Current.Resources["GlobalTextRenderingMode"] = ParseTextRenderingMode(settings.TextRendering);
                    Application.Current.Resources["GlobalTextFormattingMode"] = ParseTextFormattingMode(settings.TextFormatting);
                    Application.Current.Resources["GlobalTextHintingMode"] = ParseTextHintingMode(settings.TextHinting);
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
                    Application.Current.Resources["EditorFontFamily"] = new FontFamily(settings.FontFamily);
                    Application.Current.Resources["EditorFontSize"] = settings.FontSize;
                    Application.Current.Resources["EditorFontWeight"] = ParseFontWeight(settings.FontWeight);
                    Application.Current.Resources["EditorLineHeight"] = settings.LineHeight;
                    Application.Current.Resources["EditorLetterSpacing"] = settings.LetterSpacing;

                    Application.Current.Resources["EditorFontSizeLarge"] = settings.FontSize * 1.15;
                    Application.Current.Resources["EditorFontSizeSmall"] = settings.FontSize * 0.85;
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

        public static List<string> GetSystemFonts()
        {
            try
            {
                return Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .OrderBy(f => f)
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontManager] 获取系统字体失败: {ex.Message}");
                return new List<string> { "Microsoft YaHei UI", "Consolas", "Arial" };
            }
        }

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

        public static bool ExportConfiguration(string? filePath = null)
        {
            return ServiceLocator.Get<Services.FontImportExportService>().ExportConfiguration(filePath);
        }

        public static bool ImportConfiguration(string? filePath = null)
        {
            return ServiceLocator.Get<Services.FontImportExportService>().ImportConfiguration(filePath);
        }

        public static bool ExportAsShareable(string? filePath = null)
        {
            return ServiceLocator.Get<Services.FontImportExportService>().ExportAsShareable(filePath);
        }

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

