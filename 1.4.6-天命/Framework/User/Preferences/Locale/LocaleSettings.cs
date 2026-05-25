using System;
using System.Collections.Generic;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Preferences.Locale
{
    public class LocaleSettings : BaseSettings<LocaleSettings, LocaleModel>
    {
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        public LocaleSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[LocaleSettings] {key}: {ex.Message}");
        }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Preferences/Locale", "locale_settings.json");

        protected override LocaleModel CreateDefaultData() => _objectFactory.Create<LocaleModel>();

        private LocaleModel? _cachedSettings;

        public LocaleModel LoadSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;
            LoadData();
            _cachedSettings = Data;
            TM.App.Log("[LocaleSettings] loaded");
            return _cachedSettings;
        }

        public bool SaveSettings(LocaleModel settings)
        {
            try
            {
                settings.LastModified = DateTime.Now;
                Data = settings;
                SaveData();
                _cachedSettings = settings;
                TM.App.Log("[LocaleSettings] saved");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(SaveSettings), ex);
                return false;
            }
        }

        public LocaleModel GetCurrentSettings() => _cachedSettings ?? LoadSettings();
        public void ClearCache() => _cachedSettings = null;
    }
}

