using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TM.Modules.Generate.Content.Services
{
    public class ContentConfigService
    {
        private const string ConfigFileName = "content_config.json";

        private static string ConfigPath => Path.Combine(
            StoragePathHelper.GetProjectConfigPath(), ConfigFileName);

        private volatile ContentConfig _config;
        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
        private volatile string? _latestConfigJson;
        private int _configVersion;

        public ContentConfigService()
        {
            _config = new ContentConfig();
            var initVersion = System.Threading.Volatile.Read(ref _configVersion);
            System.Threading.Tasks.Task.Run(async () =>
            {
                var loaded = await LoadConfigAsync().ConfigureAwait(false);
                if (initVersion == System.Threading.Volatile.Read(ref _configVersion))
                    _config = loaded;
            }).SafeFireAndForget(ex => TM.App.Log($"[ContentConfigService] 初始化失败: {ex.Message}"));

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    var switchVersion = System.Threading.Volatile.Read(ref _configVersion);
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var loaded = await LoadConfigAsync().ConfigureAwait(false);
                        if (switchVersion == System.Threading.Volatile.Read(ref _configVersion))
                            _config = loaded;
                    }).SafeFireAndForget(ex => TM.App.Log($"[ContentConfigService] 项目切换重载失败: {ex.Message}"));
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentConfigService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public bool IsModuleEnabled(string modulePath)
        {
            if (_config.EnabledModules.TryGetValue(modulePath, out var enabled))
            {
                return enabled;
            }
            return true;
        }

        public void SetModuleEnabled(string modulePath, bool enabled)
        {
            System.Threading.Interlocked.Increment(ref _configVersion);
            _config.EnabledModules[modulePath] = enabled;
            SaveConfig();
            TM.App.Log($"[ContentConfigService] 保存模块状态: {modulePath} = {enabled}");
        }

        public Dictionary<string, bool> GetAllEnabledStates()
        {
            return new Dictionary<string, bool>(_config.EnabledModules);
        }

        public void SetAllEnabledStates(Dictionary<string, bool> states)
        {
            System.Threading.Interlocked.Increment(ref _configVersion);
            foreach (var (path, enabled) in states)
            {
                _config.EnabledModules[path] = enabled;
            }
            SaveConfig();
        }

        private async System.Threading.Tasks.Task<ContentConfig> LoadConfigAsync()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = await File.ReadAllTextAsync(ConfigPath).ConfigureAwait(false);
                    var config = JsonSerializer.Deserialize<ContentConfig>(json);
                    if (config != null)
                    {
                        TM.App.Log($"[ContentConfigService] 配置已加载，共 {config.EnabledModules.Count} 个模块状态");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentConfigService] 加载配置失败: {ex.Message}");
            }

            return new ContentConfig();
        }

        private void SaveConfig()
        {
            var path = ConfigPath;
            _latestConfigJson = JsonSerializer.Serialize(_config, JsonHelper.Default);
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await _saveSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var json = _latestConfigJson;
                    if (json == null) return;
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentConfigService] 保存配置失败: {ex.Message}");
                }
                finally
                {
                    _saveSemaphore.Release();
                }
            });
        }
    }

    public class ContentConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("EnabledModules")] public Dictionary<string, bool> EnabledModules { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("HistoryRetainCount")] public int HistoryRetainCount { get; set; } = 5;
    }
}
