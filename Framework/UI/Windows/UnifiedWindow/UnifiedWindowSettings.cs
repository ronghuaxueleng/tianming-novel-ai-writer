using System;
using System.IO;
using System.Text.Json;

namespace TM.Framework.UI.Windows
{
    public class UnifiedWindowSettings
    {
        private static readonly object FileLock = new();
        private static UnifiedWindowSettings _inMemorySettings = new();
        private static System.Threading.CancellationTokenSource? _saveCts;
        private static volatile bool _saveInFlight;

        private static void ScheduleSave()
        {
            _saveCts?.Cancel();
            _saveCts = new System.Threading.CancellationTokenSource();
            var token = _saveCts.Token;
            _ = System.Threading.Tasks.Task.Delay(150, token).ContinueWith(async _ =>
            {
                if (_saveInFlight) return;
                _saveInFlight = true;
                try { await _inMemorySettings.SaveAsync().ConfigureAwait(false); }
                catch (Exception ex) { TM.App.Log($"[UnifiedWindow] 保存设置失败: {ex.Message}"); }
                finally { _saveInFlight = false; }
            }, token,
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion,
                System.Threading.Tasks.TaskScheduler.Default);
        }

        [System.Text.Json.Serialization.JsonPropertyName("Left")] public double Left { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("Top")] public double Top { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("Width")] public double Width { get; set; } = 1400;
        [System.Text.Json.Serialization.JsonPropertyName("Height")] public double Height { get; set; } = 1000;
        [System.Text.Json.Serialization.JsonPropertyName("IsMaximized")] public bool IsMaximized { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("LeftColumnWidth")] public double LeftColumnWidth { get; set; } = 220;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentMode")] public string CurrentMode { get; set; } = "Writing";
        [System.Text.Json.Serialization.JsonPropertyName("SelectedTabName")] public string SelectedTabName { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("IsPinned")] public bool IsPinned { get; set; } = false;

        private static string GetConfigPath()
        {
            return StoragePathHelper.GetFilePath("Framework", "UI/Windows/UnifiedWindow", "window_settings.json");
        }

        public static UnifiedWindowSettings Load()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    TM.App.Log("[UnifiedWindow] 配置文件不存在，使用默认设置");
                    return new UnifiedWindowSettings();
                }
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<UnifiedWindowSettings>(json) ?? new UnifiedWindowSettings();
                TM.App.Log($"[UnifiedWindow] 窗口设置已加载: {path}");
                lock (FileLock) { _inMemorySettings = settings; }
                return settings;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载窗口设置失败: {ex.Message}");
                return new UnifiedWindowSettings();
            }
        }

        public static System.Threading.Tasks.Task FlushAsync() => _inMemorySettings.SaveAsync();

        public async System.Threading.Tasks.Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonHelper.Default);
                var path = GetConfigPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
                TM.App.Log($"[UnifiedWindow] 窗口设置已保存: {path}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 异步保存窗口设置失败: {ex.Message}");
            }
        }

        public static async System.Threading.Tasks.Task<UnifiedWindowSettings> LoadAsync()
        {
            try
            {
                var path = GetConfigPath();
                UnifiedWindowSettings settings;
                if (!File.Exists(path))
                {
                    settings = new UnifiedWindowSettings();
                }
                else
                {
                    var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    settings = JsonSerializer.Deserialize<UnifiedWindowSettings>(json) ?? new UnifiedWindowSettings();
                }
                TM.App.Log($"[UnifiedWindow] 窗口设置已加载: {path}");
                lock (FileLock) { _inMemorySettings = settings; }
                return settings;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载窗口设置失败: {ex.Message}");
                return new UnifiedWindowSettings();
            }
        }

        public static UnifiedWindowSettings GetCurrent()
        {
            lock (FileLock) { return _inMemorySettings; }
        }

        public static UnifiedWindowSettings Update(Action<UnifiedWindowSettings> updateAction)
        {
            try
            {
                lock (FileLock) { updateAction(_inMemorySettings); }
                ScheduleSave();
                TM.App.Log("[UnifiedWindow] 窗口设置已更新");
                return _inMemorySettings;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 更新窗口设置失败: {ex.Message}");
                throw;
            }
        }
    }
}
