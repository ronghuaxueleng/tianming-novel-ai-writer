using System;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Preferences.Display
{
    public class DisplaySettings : BaseSettings<DisplaySettings, DisplayModel>
    {
        public DisplaySettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Preferences/Display", "display_settings.json");

        protected override DisplayModel CreateDefaultData() => _objectFactory.Create<DisplayModel>();

        private DisplayModel? _cachedSettings;

        public DisplayModel LoadSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;

            LoadData();
            _cachedSettings = Data;
            TM.App.Log("[DisplaySettings] loaded");
            return _cachedSettings;
        }

        public bool SaveSettings(DisplayModel settings)
        {
            try
            {
                settings.LastModified = DateTime.Now;
                Data = settings;
                SaveData();
                _cachedSettings = settings;
                TM.App.Log($"[DisplaySettings] 保存显示设置: 功能栏={settings.ShowFunctionBar}, 密度={settings.ListDensity}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DisplaySettings] 保存设置失败: {ex.Message}");
                return false;
            }
        }

        public DisplayModel GetCurrentSettings()
        {
            return _cachedSettings ?? LoadSettings();
        }

        public void ClearCache()
        {
            _cachedSettings = null;
        }
    }
}

