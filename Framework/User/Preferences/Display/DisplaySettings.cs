using System;
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

            _cachedSettings = Data;
            TM.App.Log("[DisplaySettings] loaded (from constructor default/async)");
            return _cachedSettings;
        }

        public bool SaveSettings(DisplayModel settings)
        {
            settings.LastModified = DateTime.Now;
            Data = settings;
            _cachedSettings = settings;
            _ = SaveDataAsync();
            TM.App.Log($"[DisplaySettings] 保存显示设置: 功能栏={settings.ShowFunctionBar}, 密度={settings.ListDensity}");
            return true;
        }

        public async System.Threading.Tasks.Task<DisplayModel> LoadSettingsAsync()
        {
            if (_cachedSettings != null) return _cachedSettings;
            await LoadDataAsync().ConfigureAwait(false);
            _cachedSettings = Data;
            TM.App.Log("[DisplaySettings] async loaded");
            return _cachedSettings;
        }

        public DisplayModel GetCurrentSettings()
        {
            return _cachedSettings ?? LoadSettings();
        }

        public override void ResetToDefaults()
        {
            base.ResetToDefaults();
            _cachedSettings = Data;
        }

        public void ClearCache()
        {
            _cachedSettings = null;
        }
    }
}

