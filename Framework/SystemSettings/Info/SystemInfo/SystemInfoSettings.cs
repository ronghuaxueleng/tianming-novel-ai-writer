using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    public class SystemInfoSettings
    {
        private static readonly object _lock = new object();
        private int _settingsVersion;

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
            _ = System.Threading.Tasks.Task.Run(async () => await LoadSettingsInternalAsync().ConfigureAwait(false));
        }

        private async System.Threading.Tasks.Task LoadSettingsInternalAsync()
        {
            var loadVersion = Volatile.Read(ref _settingsVersion);
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
                    var dto = JsonSerializer.Deserialize<SystemInfoSettingsDto>(json);
                    if (dto != null)
                    {
                        if (loadVersion != Volatile.Read(ref _settingsVersion))
                            return;
                        lock (_lock)
                        {
                            if (loadVersion != Volatile.Read(ref _settingsVersion))
                                return;
                            AutoRefreshIntervalSeconds = dto.AutoRefreshIntervalSeconds;
                            EnableAutoRefresh = dto.EnableAutoRefresh;
                            StorageSizeUnit = dto.StorageSizeUnit ?? "GB";
                            ShowDetailedInfo = dto.ShowDetailedInfo;
                            LastRefreshTime = dto.LastRefreshTime;
                        }
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

        public async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Interlocked.Increment(ref _settingsVersion);
                var tmpSisA = _settingsFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpSisA))
                {
                    await JsonSerializer.SerializeAsync(stream, this, JsonHelper.CnDefault);
                }
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

