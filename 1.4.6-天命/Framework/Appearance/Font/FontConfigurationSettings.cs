using System;
using System.IO;
using System.Text.Json;
using System.Text;
using TM.Framework.Appearance.Font.Models;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Appearance.Font
{
    public class FontConfigurationSettings : BaseSettings<FontConfigurationSettings, FontConfiguration>
    {
        public FontConfigurationSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Appearance/Font", "font_config.json");

        protected override FontConfiguration CreateDefaultData() => FontConfiguration.GetDefault();

        protected override void OnDataLoaded()
        {
            if (Data.UIFont == null || Data.EditorFont == null)
            {
                TM.App.Log("[FontConfigurationSettings] 配置不完整，使用默认配置");
                Data = FontConfiguration.GetDefault();
                SaveData();
            }
        }

        private readonly object _lock = new object();

        public FontConfiguration GetConfiguration() { lock (_lock) { return Data; } }

        public FontSettings GetUIFont() { lock (_lock) { return Data.UIFont; } }
        public FontSettings GetEditorFont() { lock (_lock) { return Data.EditorFont; } }

        public void UpdateUIFont(FontSettings uiFont)
        {
            if (uiFont == null) throw new ArgumentNullException(nameof(uiFont));
            lock (_lock) { Data.UIFont = uiFont; SaveData(); TM.App.Log($"[FontConfigurationSettings] UI字体已更新: {uiFont.FontFamily}, {uiFont.FontSize}px"); }
        }

        public void UpdateEditorFont(FontSettings editorFont)
        {
            if (editorFont == null) throw new ArgumentNullException(nameof(editorFont));
            lock (_lock) { Data.EditorFont = editorFont; SaveData(); TM.App.Log($"[FontConfigurationSettings] 编辑器字体已更新: {editorFont.FontFamily}, {editorFont.FontSize}px"); }
        }

        public void UpdateConfiguration(FontConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_lock) { Data = config; SaveData(); TM.App.Log("[FontConfigurationSettings] 完整配置已更新"); }
        }

        public FontConfiguration ResetToDefault()
        {
            lock (_lock) { Data = FontConfiguration.GetDefault(); SaveData(); TM.App.Log("[FontConfigurationSettings] 配置已重置为默认值"); return Data; }
        }

        public bool ExportConfiguration(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            try
            {
                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(Data, JsonHelper.CnDefault);
                    var tmpFcs = filePath + ".tmp";
                    File.WriteAllText(tmpFcs, json, Encoding.UTF8);
                    File.Move(tmpFcs, filePath, overwrite: true);
                    TM.App.Log($"[FontConfigurationSettings] 配置已导出到: {filePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontConfigurationSettings] 导出配置失败: {ex.Message}");
                return false;
            }
        }

        public async System.Threading.Tasks.Task<bool> ExportConfigurationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            try
            {
                var json = JsonSerializer.Serialize(Data, JsonHelper.CnDefault);
                var tmpFcsA = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpFcsA, json, Encoding.UTF8);
                File.Move(tmpFcsA, filePath, overwrite: true);
                TM.App.Log($"[FontConfigurationSettings] 配置已异步导出到: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontConfigurationSettings] 异步导出配置失败: {ex.Message}");
                return false;
            }
        }

        public bool ImportConfiguration(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath))
            {
                TM.App.Log($"[FontConfigurationSettings] 文件不存在: {filePath}");
                return false;
            }
            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var config = JsonSerializer.Deserialize<FontConfiguration>(json);
                if (config != null)
                {
                    lock (_lock) { Data = config; SaveData(); TM.App.Log($"[FontConfigurationSettings] 配置已从文件导入: {filePath}"); return true; }
                }
                TM.App.Log($"[FontConfigurationSettings] 导入的配置无效");
                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontConfigurationSettings] 导入配置失败: {ex.Message}");
                return false;
            }
        }
    }
}

