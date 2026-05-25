using System;
using System.Globalization;
using System.Threading;

namespace TM.Framework.User.Preferences.Locale
{
    public class LocaleService
    {
        private readonly LocaleSettings _settings;

        public LocaleService(LocaleSettings settings)
        {
            _settings = settings;
        }

        public async System.Threading.Tasks.Task ApplyAtStartupAsync()
        {
            try
            {
                var data = await _settings.LoadSettingsAsync();

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

        public async System.Threading.Tasks.Task UpdateLanguageAsync(string language, string languageName)
        {
            var settings = await _settings.LoadSettingsAsync();
            settings.Language = language;
            settings.LanguageName = languageName;
            _settings.SaveSettings(settings);
            TM.App.Log($"[LocaleService] 更新语言: {languageName} ({language})");
        }

        public async System.Threading.Tasks.Task UpdateTimeZoneAsync(string timeZoneId)
        {
            var loadedSettings = await _settings.LoadSettingsAsync();
            loadedSettings.TimeZoneId = timeZoneId;
            _settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新时区: {timeZoneId}");
        }

        public async System.Threading.Tasks.Task UpdateDateFormatAsync(string format)
        {
            var loadedSettings = await _settings.LoadSettingsAsync();
            loadedSettings.DateFormat = format;
            _settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新日期格式: {format}");
        }

        public async System.Threading.Tasks.Task UpdateNumberFormatAsync(string format)
        {
            var loadedSettings = await _settings.LoadSettingsAsync();
            loadedSettings.NumberFormat = format;
            _settings.SaveSettings(loadedSettings);
            TM.App.Log($"[LocaleService] 更新数字格式: {format}");
        }

        public void ResetToDefaults()
        {
            _settings.ResetToDefaults();
            TM.App.Log("[LocaleService] 重置为默认语言区域设置");
        }
    }
}

