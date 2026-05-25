using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TM.Framework.UI.Workspace.Services.Spec
{
    public static class GoldenChapterConfig
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly SemaphoreSlim _saveLock = new(1, 1);
        private static bool? _cachedEnabled;
        private static string? _cachedProject;
        private static int _cacheVersion;

        public static void Invalidate() { Interlocked.Increment(ref _cacheVersion); _cachedEnabled = null; _cachedProject = null; }

        public static bool Load()
        {
            var projectPath = StoragePathHelper.GetCurrentProjectPath();
            if (_cachedEnabled.HasValue && _cachedProject == projectPath)
                return _cachedEnabled.Value;

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                _ = Task.Run(LoadAsync);
                return _cachedEnabled ?? false;
            }

            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    _cachedEnabled = false;
                    _cachedProject = projectPath;
                    return false;
                }
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var result = doc.RootElement.TryGetProperty("enabled", out var prop) && prop.GetBoolean();
                _cachedEnabled = result;
                _cachedProject = projectPath;
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GoldenChapterConfig] 同步读取失败: {ex.Message}");
                return false;
            }
        }

        private static string GetConfigPath()
        {
            var projectPath = StoragePathHelper.GetCurrentProjectPath();
            return Path.Combine(projectPath, "Config", "golden_chapter.json");
        }

        public static async Task<bool> LoadAsync()
        {
            var projectPath = StoragePathHelper.GetCurrentProjectPath();
            if (_cachedEnabled.HasValue && _cachedProject == projectPath)
                return _cachedEnabled.Value;
            var loadVersion = Volatile.Read(ref _cacheVersion);

            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    if (loadVersion == Volatile.Read(ref _cacheVersion))
                    {
                        _cachedEnabled = false;
                        _cachedProject = projectPath;
                    }
                    return false;
                }
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var result = doc.RootElement.TryGetProperty("enabled", out var prop) && prop.GetBoolean();
                if (loadVersion == Volatile.Read(ref _cacheVersion))
                {
                    _cachedEnabled = result;
                    _cachedProject = projectPath;
                    return result;
                }
                return _cachedEnabled ?? result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GoldenChapterConfig] 异步读取失败: {ex.Message}");
                return false;
            }
        }

        public static void Save(bool enabled)
        {
            _ = SaveAsync(enabled);
        }

        public static async Task SaveAsync(bool enabled)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetConfigPath();
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new { enabled }, JsonOptions);
                Interlocked.Increment(ref _cacheVersion);
                _cachedEnabled = enabled;
                _cachedProject = StoragePathHelper.GetCurrentProjectPath();
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GoldenChapterConfig] 保存失败: {ex.Message}");
                Invalidate();
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
