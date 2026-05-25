using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Windows.Media;
using TM.Framework.Appearance.Font.Models;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontFallbackChain
    {
        [System.Text.Json.Serialization.JsonPropertyName("PrimaryFont")] public string PrimaryFont { get; set; } = "Consolas";
        [System.Text.Json.Serialization.JsonPropertyName("FallbackFonts")] public List<string> FallbackFonts { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("AutoDetectMissing")] public bool AutoDetectMissing { get; set; } = true;
    }

    public class FontFallbackService
    {
        private readonly string _configPath;
        private FontFallbackChain _currentChain;
        private readonly MonospaceFontDetector _monoDetector;

        public FontFallbackService(MonospaceFontDetector monoDetector)
        {
            _monoDetector = monoDetector;
            _configPath = StoragePathHelper.GetFilePath("Framework", "Appearance/Font", "fallback_chain.json");
            _currentChain = LoadChain();
        }

        public FontFallbackChain GetFallbackChain()
        {
            return _currentChain;
        }

        public void SetFallbackChain(FontFallbackChain chain)
        {
            _currentChain = chain;
            SaveChain();
            TM.App.Log($"[FontFallback] 更新回退链: {chain.PrimaryFont} + {chain.FallbackFonts.Count}个回退字体");
        }

        public void AddFallbackFont(string fontName)
        {
            if (!_currentChain.FallbackFonts.Contains(fontName))
            {
                _currentChain.FallbackFonts.Add(fontName);
                SaveChain();
                TM.App.Log($"[FontFallback] 添加回退字体: {fontName}");
            }
        }

        public void RemoveFallbackFont(string fontName)
        {
            if (_currentChain.FallbackFonts.Remove(fontName))
            {
                SaveChain();
                TM.App.Log($"[FontFallback] 移除回退字体: {fontName}");
            }
        }

        public FontFamily BuildFontFamily()
        {
            var allFonts = new List<string> { _currentChain.PrimaryFont };
            allFonts.AddRange(_currentChain.FallbackFonts);

            var fontFamilyName = string.Join(", ", allFonts);
            TM.App.Log($"[FontFallback] 构建字体族: {fontFamilyName}");

            return new FontFamily(fontFamilyName);
        }

        public List<string> RecommendFallbacks(string primaryFont)
        {
            var recommendations = new List<string>();

            if (_monoDetector.IsMonospace(primaryFont))
            {
                recommendations.AddRange(new[] { "Consolas", "Courier New", "Lucida Console", "Microsoft YaHei UI" });
            }
            else
            {
                recommendations.AddRange(new[] { "Segoe UI", "Arial", "Microsoft YaHei", "SimSun" });
            }

            recommendations.AddRange(new[] { "Microsoft YaHei", "SimSun", "Microsoft JhengHei", "Malgun Gothic" });

            return recommendations.Where(f => f != primaryFont).Distinct().ToList();
        }

        private FontFallbackChain LoadChain()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var chain = JsonSerializer.Deserialize<FontFallbackChain>(json);
                    if (chain != null)
                    {
                        TM.App.Log($"[FontFallback] 加载回退链配置: {chain.PrimaryFont}");
                        return chain;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFallback] 加载配置失败: {ex.Message}");
            }

            return new FontFallbackChain
            {
                PrimaryFont = "Consolas",
                FallbackFonts = new List<string> { "Microsoft YaHei", "SimSun" },
                AutoDetectMissing = true
            };
        }

        private void SaveChain()
        {
            try
            {
                var json = JsonSerializer.Serialize(_currentChain, JsonHelper.Default);
                var tmpFfb = _configPath + ".tmp";
                File.WriteAllText(tmpFfb, json);
                File.Move(tmpFfb, _configPath, overwrite: true);
                TM.App.Log($"[FontFallback] 保存回退链配置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFallback] 保存配置失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveChainAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_currentChain, JsonHelper.Default);
                var tmpFfbA = _configPath + ".tmp";
                await File.WriteAllTextAsync(tmpFfbA, json);
                File.Move(tmpFfbA, _configPath, overwrite: true);
                TM.App.Log($"[FontFallback] 异步保存回退链配置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFallback] 异步保存配置失败: {ex.Message}");
            }
        }
    }
}

