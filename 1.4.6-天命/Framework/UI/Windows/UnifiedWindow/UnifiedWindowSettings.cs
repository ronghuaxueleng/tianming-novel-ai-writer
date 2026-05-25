using System;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.UI.Windows
{
    public class UnifiedWindowSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("Left")] public double Left { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("Top")] public double Top { get; set; } = -1;
        [System.Text.Json.Serialization.JsonPropertyName("Width")] public double Width { get; set; } = 1000;
        [System.Text.Json.Serialization.JsonPropertyName("Height")] public double Height { get; set; } = 700;
        [System.Text.Json.Serialization.JsonPropertyName("IsMaximized")] public bool IsMaximized { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("LeftColumnWidth")] public double LeftColumnWidth { get; set; } = 220;
        [System.Text.Json.Serialization.JsonPropertyName("CurrentMode")] public string CurrentMode { get; set; } = "Settings";
        [System.Text.Json.Serialization.JsonPropertyName("SelectedTabName")] public string SelectedTabName { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("IsPinned")] public bool IsPinned { get; set; } = false;

        private static string GetConfigPath()
        {
            return StoragePathHelper.GetFilePath("Framework", "UI/Windows/UnifiedWindow", "window_settings.json");
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonHelper.Default);

                var path = GetConfigPath();
                var tmpUws = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmpUws, json);
                File.Move(tmpUws, path, overwrite: true);

                TM.App.Log($"[UnifiedWindow] 窗口设置已保存: {path}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 保存窗口设置失败: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonHelper.Default);

                var path = GetConfigPath();
                var tmpUwsA = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpUwsA, json);
                File.Move(tmpUwsA, path, overwrite: true);

                TM.App.Log($"[UnifiedWindow] 窗口设置已异步保存: {path}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 异步保存窗口设置失败: {ex.Message}");
            }
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
                var settings = JsonSerializer.Deserialize<UnifiedWindowSettings>(json);

                TM.App.Log($"[UnifiedWindow] 窗口设置已加载: {path}");
                return settings ?? new UnifiedWindowSettings();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载窗口设置失败: {ex.Message}");
                return new UnifiedWindowSettings();
            }
        }
    }
}
