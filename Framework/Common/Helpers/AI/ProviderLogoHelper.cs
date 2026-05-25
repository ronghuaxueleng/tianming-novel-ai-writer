using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.Common.Helpers.AI
{
    public static class ProviderLogoHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _logoCache = new();
        private static readonly string _fallbackLogo = "doudi.png";
        private static Dictionary<string, string>? _nameMapping;
        private static Dictionary<string, string>? _lowerMapping;
        private static bool _mappingLoaded;
        private static volatile bool _mappingLoadingTriggered;
        private static readonly object _mappingLock = new();

        public static event Action? LogoCacheUpdated;

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
                var escaped = Uri.EscapeDataString(logoPath);
                var uri = new Uri(
                    $"pack://application:,,,/Framework/UI/Icons/Providers/{escaped}",
                    UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 32;
                bitmap.EndInit();
                bitmap.Freeze();

                _logoCache[logoPath] = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProviderLogoHelper] pack URI 加载失败: {logoPath}, 错误: {ex.Message}");
                _logoCache.TryAdd(logoPath, null);
                return null;
            }
        }

        public static void PreloadInBackground(IEnumerable<string?> logoPaths)
        {
            if (!_mappingLoaded)
                _ = System.Threading.Tasks.Task.Run(EnsureMappingLoadedAsync);

            var paths = logoPaths
                .Where(p => !string.IsNullOrEmpty(p) && !_logoCache.ContainsKey(p!))
                .Select(p => p!)
                .Distinct()
                .ToList();
            if (paths.Count == 0) return;

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var logoPath in paths)
                {
                    _ = GetLogo(logoPath, string.Empty);
                }
            });
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

            if (!_mappingLoaded)
            {
                if (!_mappingLoadingTriggered)
                {
                    _mappingLoadingTriggered = true;
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await EnsureMappingLoadedAsync().ConfigureAwait(false);
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            new Action(() => LogoCacheUpdated?.Invoke()));
                    });
                }
                return _fallbackLogo;
            }

            if (_nameMapping == null || _nameMapping.Count == 0)
                return null;

            var name = providerName.Trim();

            if (_nameMapping.TryGetValue(name, out var exactMatch))
                return exactMatch;

            if (_lowerMapping != null)
            {
                var nameLower = name.ToLowerInvariant();
                foreach (var kvp in _lowerMapping)
                {
                    if (nameLower.Contains(kvp.Key) || kvp.Key.Contains(nameLower))
                    {
                        lock (_mappingLock) { _nameMapping![name] = kvp.Value; }
                        return kvp.Value;
                    }
                }
            }

            lock (_mappingLock) { _nameMapping![name] = _fallbackLogo; }
            return _fallbackLogo;
        }

        private static async System.Threading.Tasks.Task EnsureMappingLoadedAsync()
        {
            if (_mappingLoaded) return;

            var mapping = await LoadMappingFromDiskAsync().ConfigureAwait(false);

            lock (_mappingLock)
            {
                if (_mappingLoaded) return;
                _nameMapping = mapping;
                var lower = new Dictionary<string, string>(mapping.Count);
                foreach (var kvp in mapping)
                    lower[kvp.Key.ToLowerInvariant()] = kvp.Value;
                _lowerMapping = lower;
                _mappingLoaded = true;
            }
        }

        private static async System.Threading.Tasks.Task<Dictionary<string, string>> LoadMappingFromDiskAsync()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var asm = typeof(ProviderLogoHelper).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("provider-logos.json", StringComparison.Ordinal));

                if (resourceName == null)
                {
                    TM.App.Log("[ProviderLogoHelper] 嵌入资源 provider-logos.json 未找到");
                    return result;
                }

                await using var stream = asm.GetManifestResourceStream(resourceName)!;
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

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

                TM.App.Log($"[ProviderLogoHelper] 嵌入加载 Logo 映射: {result.Count} 条");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProviderLogoHelper] 加载嵌入 Logo 映射失败: {ex.Message}");
            }
            return result;
        }

        public static async System.Threading.Tasks.Task ReloadMappingAsync()
        {
            _mappingLoaded = false;
            _mappingLoadingTriggered = false;
            _nameMapping = null;

            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    await EnsureMappingLoadedAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() => LogoCacheUpdated?.Invoke()));
            }
            else
            {
                await EnsureMappingLoadedAsync().ConfigureAwait(false);
            }
        }

        public static ImageSource? GetLogoByName(string? providerName, string fallbackEmoji = "Icon.Robot")
        {
            var logoFileName = GetLogoFileName(providerName);
            return GetLogo(logoFileName, fallbackEmoji);
        }
    }
}
