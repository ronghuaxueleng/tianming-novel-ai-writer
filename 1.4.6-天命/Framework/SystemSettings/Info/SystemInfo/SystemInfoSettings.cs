using System;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public class SystemInfoSettings
    {
        private static readonly object _lock = new object();

        private readonly string _settingsFilePath;

        [System.Text.Json.Serialization.JsonPropertyName("AutoRefreshIntervalSeconds")] public int AutoRefreshIntervalSeconds { get; set; } = 30;

        [System.Text.Json.Serialization.JsonPropertyName("EnableAutoRefresh")] public bool EnableAutoRefresh { get; set; } = false;

        [System.Text.Json.Serialization.JsonPropertyName("StorageSizeUnit")] public string StorageSizeUnit { get; set; } = "GB";

        [System.Text.Json.Serialization.JsonPropertyName("ShowDetailedInfo")] public bool ShowDetailedInfo { get; set; } = true;

        [System.Text.Json.Serialization.JsonPropertyName("LastRefreshTime")] public DateTime LastRefreshTime { get; set; } = DateTime.Now;

        public SystemInfoSettings()
        {
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Info/SystemInfo",
                "settings.json"
            );
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var dto = JsonSerializer.Deserialize<SystemInfoSettingsDto>(json);
                    if (dto != null)
                    {
                        AutoRefreshIntervalSeconds = dto.AutoRefreshIntervalSeconds;
                        EnableAutoRefresh = dto.EnableAutoRefresh;
                        StorageSizeUnit = dto.StorageSizeUnit ?? "GB";
                        ShowDetailedInfo = dto.ShowDetailedInfo;
                        LastRefreshTime = dto.LastRefreshTime;

                        TM.App.Log("[SystemInfoSettings] 设置已加载");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfoSettings] 加载设置失败: {ex.Message}");
            }
        }

        private class SystemInfoSettingsDto
        {
            [System.Text.Json.Serialization.JsonPropertyName("AutoRefreshIntervalSeconds")] public int AutoRefreshIntervalSeconds { get; set; } = 30;
            [System.Text.Json.Serialization.JsonPropertyName("EnableAutoRefresh")] public bool EnableAutoRefresh { get; set; } = false;
            [System.Text.Json.Serialization.JsonPropertyName("StorageSizeUnit")] public string? StorageSizeUnit { get; set; } = "GB";
            [System.Text.Json.Serialization.JsonPropertyName("ShowDetailedInfo")] public bool ShowDetailedInfo { get; set; } = true;
            [System.Text.Json.Serialization.JsonPropertyName("LastRefreshTime")] public DateTime LastRefreshTime { get; set; } = DateTime.Now;
        }

        public void SaveSettings()
        {
            try
            {
                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(this, JsonHelper.CnDefault);

                    var directory = Path.GetDirectoryName(_settingsFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var tmpSis = _settingsFilePath + ".tmp";
                    File.WriteAllText(tmpSis, json);
                    File.Move(tmpSis, _settingsFilePath, overwrite: true);
                    TM.App.Log("[SystemInfoSettings] 设置已保存");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfoSettings] 保存设置失败: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonHelper.CnDefault);

                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tmpSisA = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpSisA, json);
                File.Move(tmpSisA, _settingsFilePath, overwrite: true);
                TM.App.Log("[SystemInfoSettings] 设置已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfoSettings] 异步保存设置失败: {ex.Message}");
            }
        }
    }
}

