using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.Common.Helpers.AI
{
    public static class ProviderLogoHelper
    {
        private static readonly Dictionary<string, ImageSource?> _logoCache = new();
        private static readonly string _logoBasePath = "Framework/UI/Icons/Providers/";
        private static readonly string _fallbackLogo = "doudi.png";
        private static Dictionary<string, string>? _nameMapping;
        private static bool _mappingLoaded;

        public static ImageSource? GetLogo(string? logoPath, string fallbackEmoji)
        {
            if (string.IsNullOrEmpty(logoPath))
            {
                return null;
            }

            if (_logoCache.TryGetValue(logoPath, out var cachedLogo))
            {
                return cachedLogo;
            }

            try
            {
                var projectRoot = StoragePathHelper.GetProjectRoot();
                var fullPath = Path.Combine(projectRoot, _logoBasePath, logoPath);

                if (File.Exists(fullPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 32;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    _logoCache[logoPath] = bitmap;
                    return bitmap;
                }
                else
                {
                    _logoCache[logoPath] = null;
                    TM.App.Log($"[ProviderLogoHelper] ⚠️ Logo文件不存在，使用emoji: {logoPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProviderLogoHelper] ❌ 加载Logo失败: {logoPath}, 错误: {ex.Message}");

                _logoCache[logoPath] = null;
                return null;
            }
        }

        public static void PreloadInBackground(IEnumerable<string?> logoPaths)
        {
            var projectRoot = StoragePathHelper.GetProjectRoot();
            var dispatcher  = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            var paths = logoPaths
                .Where(p => !string.IsNullOrEmpty(p) && !_logoCache.ContainsKey(p!))
                .Select(p => p!)
                .Distinct()
                .ToList();
            if (paths.Count == 0) return;

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var results = new Dictionary<string, byte[]?>();
                foreach (var logoPath in paths)
                {
                    var fullPath = Path.Combine(projectRoot, _logoBasePath, logoPath);
                    results[logoPath] = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
                }
                return results;
            }).ContinueWith(task =>
            {
                if (task.IsFaulted || task.Result == null) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var (logoPath, bytes) in task.Result)
                    {
                        if (_logoCache.ContainsKey(logoPath)) continue;
                        if (bytes == null) { _logoCache[logoPath] = null; continue; }
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 32;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            _logoCache[logoPath] = bitmap;
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[ProviderLogoHelper] 预加载Logo失败: {logoPath}, 错误: {ex.Message}");
                            _logoCache[logoPath] = null;
                        }
                    }
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        public static void ClearCache()
        {
            _logoCache.Clear();
            TM.App.Log("[ProviderLogoHelper] 缓存已清除");
        }

        public static string? GetLogoFileName(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return null;

            EnsureMappingLoaded();

            if (_nameMapping == null || _nameMapping.Count == 0)
                return null;

            var name = providerName.Trim();

            foreach (var kvp in _nameMapping)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            var nameLower = name.ToLowerInvariant();
            foreach (var kvp in _nameMapping)
            {
                var keyLower = kvp.Key.ToLowerInvariant();
                if (nameLower.Contains(keyLower) || keyLower.Contains(nameLower))
                    return kvp.Value;
            }

            return _fallbackLogo;
        }

        private static void EnsureMappingLoaded()
        {
            if (_mappingLoaded)
                return;

            _mappingLoaded = true;
            _nameMapping = LoadMappingFromDisk();
        }

        private static Dictionary<string, string> LoadMappingFromDisk()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var configPath = StoragePathHelper.GetFilePath("Services", "AI/Library", "provider-logos.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("mappings", out var mappings))
                    {
                        foreach (var prop in mappings.EnumerateObject())
                        {
                            var logoFile = prop.Value.GetString();
                            if (!string.IsNullOrEmpty(logoFile))
                            {
                                result[prop.Name] = logoFile;
                            }
                        }
                    }

                    TM.App.Log($"[ProviderLogoHelper] 加载Logo映射配置: {result.Count}条");
                }
                else
                {
                    TM.App.Log($"[ProviderLogoHelper] Logo映射配置文件不存在: {configPath}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProviderLogoHelper] 加载Logo映射配置失败: {ex.Message}");
            }
            return result;
        }

        public static void ReloadMapping()
        {
            _mappingLoaded = false;
            _nameMapping = null;
            EnsureMappingLoaded();
        }

        public static ImageSource? GetLogoByName(string? providerName, string fallbackEmoji = "🤖")
        {
            var logoFileName = GetLogoFileName(providerName);
            return GetLogo(logoFileName, fallbackEmoji);
        }
    }
}
