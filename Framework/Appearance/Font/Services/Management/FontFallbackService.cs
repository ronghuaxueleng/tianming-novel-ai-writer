using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;

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
        private static readonly string[] MonoFallbacks = ["Consolas", "Courier New", "Lucida Console", "Microsoft YaHei UI"];
        private static readonly string[] ProportionalFallbacks = ["Segoe UI", "Arial", "Microsoft YaHei", "SimSun"];
        private static readonly string[] CjkFallbacks = ["Microsoft YaHei", "SimSun", "Microsoft JhengHei", "Malgun Gothic"];

        private readonly string _configPath;
        private FontFallbackChain _currentChain;
        private readonly MonospaceFontDetector _monoDetector;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _chainVersion;

        public FontFallbackService(MonospaceFontDetector monoDetector)
        {
            _monoDetector = monoDetector;
            _configPath = StoragePathHelper.GetFilePath("Framework", "Appearance/Font", "fallback_chain.json");
            _currentChain = DefaultChain();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var loadVersion = Volatile.Read(ref _chainVersion);
                try
                {
                    var loaded = await LoadChainAsync().ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _chainVersion))
                        return;
                    lock (_lock)
                    {
                        if (loadVersion != Volatile.Read(ref _chainVersion))
                            return;
                        _currentChain = loaded;
                    }
                }
                catch { }
            });
        }

        private static FontFallbackChain DefaultChain() => new FontFallbackChain
        {
            PrimaryFont = "Consolas",
            FallbackFonts = new List<string> { "Microsoft YaHei", "SimSun" },
            AutoDetectMissing = true
        };

        public FontFallbackChain GetFallbackChain()
        {
            lock (_lock) return _currentChain;
        }

        public void SetFallbackChain(FontFallbackChain chain)
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _chainVersion);
                _currentChain = chain;
            }
            _ = SaveChainAsync();
            TM.App.Log($"[FontFallback] 更新回退链: {chain.PrimaryFont} + {chain.FallbackFonts.Count}个回退字体");
        }

        public void AddFallbackFont(string fontName)
        {
            bool changed = false;
            lock (_lock)
            {
                if (!_currentChain.FallbackFonts.Contains(fontName))
                {
                    Interlocked.Increment(ref _chainVersion);
                    _currentChain.FallbackFonts.Add(fontName);
                    changed = true;
                }
            }

            if (changed)
            {
                _ = SaveChainAsync();
                TM.App.Log($"[FontFallback] 添加回退字体: {fontName}");
            }
        }

        public void RemoveFallbackFont(string fontName)
        {
            bool changed = false;
            lock (_lock)
            {
                if (_currentChain.FallbackFonts.Remove(fontName))
                {
                    Interlocked.Increment(ref _chainVersion);
                    changed = true;
                }
            }

            if (changed)
            {
                _ = SaveChainAsync();
                TM.App.Log($"[FontFallback] 移除回退字体: {fontName}");
            }
        }

        public FontFamily BuildFontFamily()
        {
            FontFallbackChain snapshot;
            lock (_lock) snapshot = _currentChain;

            var allFonts = new List<string> { snapshot.PrimaryFont };
            allFonts.AddRange(snapshot.FallbackFonts);

            var fontFamilyName = string.Join(", ", allFonts);
            TM.App.Log($"[FontFallback] 构建字体族: {fontFamilyName}");

            return new FontFamily(fontFamilyName);
        }

        public List<string> RecommendFallbacks(string primaryFont)
        {
            var recommendations = new List<string>();

            if (_monoDetector.IsMonospace(primaryFont))
            {
                recommendations.AddRange(MonoFallbacks);
            }
            else
            {
                recommendations.AddRange(ProportionalFallbacks);
            }

            recommendations.AddRange(CjkFallbacks);

            return recommendations.Where(f => f != primaryFont).Distinct().ToList();
        }

        private async System.Threading.Tasks.Task<FontFallbackChain> LoadChainAsync()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);
                    var chain = JsonSerializer.Deserialize<FontFallbackChain>(json);
                    if (chain != null)
                    {
                        TM.App.Log($"[FontFallback] 异步加载回退链配置: {chain.PrimaryFont}");
                        return chain;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFallback] 异步加载配置失败: {ex.Message}");
            }

            return new FontFallbackChain
            {
                PrimaryFont = "Consolas",
                FallbackFonts = new List<string> { "Microsoft YaHei", "SimSun" },
                AutoDetectMissing = true
            };
        }

        private async System.Threading.Tasks.Task SaveChainAsync()
        {
            int saveVersion;
            string json;
            lock (_lock)
            {
                saveVersion = _chainVersion;
                json = JsonSerializer.Serialize(_currentChain, JsonHelper.Default);
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _chainVersion))
                    return;
                var tmpFfbA = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpFfbA, json).ConfigureAwait(false);
                File.Move(tmpFfbA, _configPath, overwrite: true);
                TM.App.Log($"[FontFallback] 异步保存回退链配置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontFallback] 异步保存配置失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}

