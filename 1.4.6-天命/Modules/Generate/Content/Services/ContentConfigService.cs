using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;

namespace TM.Modules.Generate.Content.Services
{
    public class ContentConfigService
    {
        private const string ConfigFileName = "content_config.json";

        private static string ConfigPath => Path.Combine(
            StoragePathHelper.GetProjectConfigPath(), ConfigFileName);

        private ContentConfig _config;

        public ContentConfigService()
        {
            _config = LoadConfig();

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _config = LoadConfig();
                    TM.App.Log("[ContentConfigService] 项目切换，已重新加载配置");
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
            foreach (var (path, enabled) in states)
            {
                _config.EnabledModules[path] = enabled;
            }
            SaveConfig();
        }

        private ContentConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
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
            var json = JsonSerializer.Serialize(_config, JsonHelper.Default);
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentConfigService] 保存配置失败: {ex.Message}");
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
