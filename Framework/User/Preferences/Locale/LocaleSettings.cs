using System;
using System.Collections.Generic;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Preferences.Locale
{
    public class LocaleSettings : BaseSettings<LocaleSettings, LocaleModel>
    {
        public LocaleSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Preferences/Locale", "locale_settings.json");

        protected override LocaleModel CreateDefaultData() => _objectFactory.Create<LocaleModel>();

        private LocaleModel? _cachedSettings;

        public LocaleModel LoadSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;

            _cachedSettings = Data;
            TM.App.Log("[LocaleSettings] loaded (from constructor default/async)");
            return _cachedSettings;
        }

        public bool SaveSettings(LocaleModel settings)
        {
            settings.LastModified = DateTime.Now;
            Data = settings;
            _cachedSettings = settings;
            _ = SaveDataAsync();
            TM.App.Log("[LocaleSettings] saved");
            return true;
        }

        public async System.Threading.Tasks.Task<LocaleModel> LoadSettingsAsync()
        {
            if (_cachedSettings != null) return _cachedSettings;
            await LoadDataAsync().ConfigureAwait(false);
            _cachedSettings = Data;
            TM.App.Log("[LocaleSettings] async loaded");
            return _cachedSettings;
        }

        public LocaleModel GetCurrentSettings() => _cachedSettings ?? LoadSettings();
        public void ClearCache() => _cachedSettings = null;
    }
}

