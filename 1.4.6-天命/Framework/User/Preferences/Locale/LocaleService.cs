using System;
using System.Globalization;
using System.Threading;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Preferences.Locale
{
    public class LocaleService
    {
        private LocaleSettings Settings => ServiceLocator.Get<LocaleSettings>();

        public LocaleService()
        {
        }

        public void ApplyAtStartup()
        {
            try
            {
                var data = Settings.LoadSettings();

                if (!string.IsNullOrEmpty(data.Language))
                {
                    var culture = new CultureInfo(data.Language);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                }

                if (!string.IsNullOrEmpty(data.TimeZoneId) &&
                    data.TimeZoneId != TimeZoneInfo.Local.Id)
                {
                    try
                    {
                        var tz = TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId);
                        TimeZoneInfo.ClearCachedData();
                        _ = tz;
                    }
                    catch { }
                }

                TM.App.Log($"[LocaleService] 文化区域已应用: {data.Language}, 时区={data.TimeZoneId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocaleService] 应用文化区域失败: {ex.Message}");
            }
        }

        public void UpdateLanguage(string language, string languageName)
        {
            var settings = Settings.LoadSettings();
            settings.Language = language;
            settings.LanguageName = languageName;
            Settings.SaveSettings(settings);
            TM.App.Log($"[LocaleService] 更新语言: {languageName} ({language})");
        }

        public void UpdateTimeZone(string timeZoneId)
        {
            var loadedSettings = Settings.LoadSettings();
            loadedSettings.TimeZoneId = timeZoneId;
            Settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新时区: {timeZoneId}");
        }

        public void UpdateDateFormat(string format)
        {
            var loadedSettings = Settings.LoadSettings();
            loadedSettings.DateFormat = format;
            Settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新日期格式: {format}");
        }

        public void UpdateNumberFormat(string format)
        {
            var loadedSettings = Settings.LoadSettings();
            loadedSettings.NumberFormat = format;
            Settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新数字格式: {format}");
        }

        public void ResetToDefaults()
        {
            Settings.ResetToDefaults();
            TM.App.Log("[LocaleService] 重置为默认语言区域设置");
        }
    }
}

