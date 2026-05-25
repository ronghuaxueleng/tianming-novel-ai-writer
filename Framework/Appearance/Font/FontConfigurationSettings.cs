using System;
using System.IO;
using System.Text.Json;
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
                SetData(FontConfiguration.GetDefault());
                SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[FontConfigurationSettings] 保存失败: {ex.Message}"));
            }
        }

        private readonly object _lock = new object();

        protected override void SetData(FontConfiguration data) { lock (_lock) { Data = data; } }

        public FontConfiguration GetConfiguration() { lock (_lock) { return Data; } }

        public FontSettings GetUIFont() { lock (_lock) { return Data.UIFont; } }
        public FontSettings GetEditorFont() { lock (_lock) { return Data.EditorFont; } }

        public void UpdateUIFont(FontSettings uiFont)
        {
            ArgumentNullException.ThrowIfNull(uiFont);
            lock (_lock) { Data.UIFont = uiFont; }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[FontConfigurationSettings] 保存失败: {ex.Message}"));
            TM.App.Log($"[FontConfigurationSettings] UI字体已更新: {uiFont.FontFamily}, {uiFont.FontSize}px");
        }

        public void UpdateEditorFont(FontSettings editorFont)
        {
            ArgumentNullException.ThrowIfNull(editorFont);
            lock (_lock) { Data.EditorFont = editorFont; }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[FontConfigurationSettings] 保存失败: {ex.Message}"));
            TM.App.Log($"[FontConfigurationSettings] 编辑器字体已更新: {editorFont.FontFamily}, {editorFont.FontSize}px");
        }

        public void UpdateConfiguration(FontConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            lock (_lock) { Data = config; }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[FontConfigurationSettings] 保存失败: {ex.Message}"));
            TM.App.Log("[FontConfigurationSettings] 完整配置已更新");
        }

        public FontConfiguration ResetToDefault()
        {
            FontConfiguration result;
            lock (_lock) { Data = FontConfiguration.GetDefault(); result = Data; }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[FontConfigurationSettings] 保存失败: {ex.Message}"));
            TM.App.Log("[FontConfigurationSettings] 配置已重置为默认值");
            return result;
        }

        public async System.Threading.Tasks.Task<bool> ExportConfigurationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            try
            {
                var tmpFcsA = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpFcsA))
                {
                    await JsonSerializer.SerializeAsync(stream, Data, JsonHelper.CnDefault);
                }
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

    }
}

