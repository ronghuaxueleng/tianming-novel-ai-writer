using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TM.Framework.User.Preferences.Locale
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class LocaleViewModel : INotifyPropertyChanged
    {
        private readonly LocaleService _service;
        private readonly LocaleSettings _settings;

        #region 属性

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[LocaleViewModel] {key}: {ex.Message}");
        }

        private string _selectedLanguage = "zh-CN";
        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                _selectedLanguage = value;
                OnPropertyChanged();
                UpdateLanguageDisplay();
            }
        }

        public ObservableCollection<LanguageItem> AvailableLanguages { get; set; }

        private string _selectedTimeZone = "China Standard Time";
        private string? _cachedTimeZoneDisplay;
        public string SelectedTimeZone
        {
            get => _selectedTimeZone;
            set
            {
                _selectedTimeZone = value;
                _cachedTimeZoneDisplay = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimeZoneDisplay));
                _ = _service.UpdateTimeZoneAsync(value).ContinueWith(t =>
                    TM.App.Log($"[LocaleViewModel] 更新时区失败: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public ObservableCollection<TimeZoneItem> AvailableTimeZones { get; set; }

        public string TimeZoneDisplay
        {
            get
            {
                if (_cachedTimeZoneDisplay != null)
                    return _cachedTimeZoneDisplay;

                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(SelectedTimeZone);
                    var offset = tz.BaseUtcOffset;
                    _cachedTimeZoneDisplay = $"UTC{(offset >= TimeSpan.Zero ? "+" : "")}{offset.TotalHours:F1}";
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(TimeZoneDisplay), ex);
                    _cachedTimeZoneDisplay = "UTC+8.0";
                }
                return _cachedTimeZoneDisplay;
            }
        }

        private string _selectedDateFormat = "yyyy-MM-dd";
        public string SelectedDateFormat
        {
            get => _selectedDateFormat;
            set
            {
                _selectedDateFormat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DateFormatPreview));
                _ = _service.UpdateDateFormatAsync(value).ContinueWith(t =>
                    TM.App.Log($"[LocaleViewModel] 更新日期格式失败: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public ObservableCollection<string> AvailableDateFormats { get; set; }

        public string DateFormatPreview => DateTime.Now.ToString(SelectedDateFormat);

        private string _selectedNumberFormat = "1,234.56";
        public string SelectedNumberFormat
        {
            get => _selectedNumberFormat;
            set
            {
                _selectedNumberFormat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NumberFormatPreview));
                _ = _service.UpdateNumberFormatAsync(value).ContinueWith(t =>
                    TM.App.Log($"[LocaleViewModel] 更新数字格式失败: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public ObservableCollection<string> AvailableNumberFormats { get; set; }

        public string NumberFormatPreview
        {
            get
            {
                var sample = 1234.56;
                return SelectedNumberFormat switch
                {
                    "1,234.56" => sample.ToString("N2", new CultureInfo("en-US")),
                    "1 234,56" => sample.ToString("N2", new CultureInfo("fr-FR")),
                    "1.234,56" => sample.ToString("N2", new CultureInfo("de-DE")),
                    _ => sample.ToString("N2")
                };
            }
        }

        #endregion

        #region 命令

        public ICommand ResetCommand { get; }
        public ICommand ApplyLanguageCommand { get; }

        #endregion

        public LocaleViewModel(LocaleService service, LocaleSettings settings)
        {
            _service = service;
            _settings = settings;
            AvailableLanguages = new ObservableCollection<LanguageItem>();
            AvailableTimeZones = new ObservableCollection<TimeZoneItem>();
            AvailableDateFormats = new ObservableCollection<string>();
            AvailableNumberFormats = new ObservableCollection<string>();

            ResetCommand = new RelayCommand(ResetToDefaults);
            ApplyLanguageCommand = new RelayCommand(ApplyLanguage);

            InitializeOptions();
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var tzList = TimeZoneInfo.GetSystemTimeZones()
                    .Take(20).Select(tz => new TimeZoneItem { Id = tz.Id, DisplayName = tz.DisplayName }).ToArray();
                return () => { AvailableTimeZones = new ObservableCollection<TimeZoneItem>(tzList); OnPropertyChanged(nameof(AvailableTimeZones)); };
            }, "Locale_Tz");
            AsyncSettingsLoader.RunOrDeferAsync(async () =>
            {
                var s = await _settings.LoadSettingsAsync().ConfigureAwait(false);
                return () =>
                {
                    _selectedLanguage = s.Language;
                    _selectedTimeZone = s.TimeZoneId;
                    _cachedTimeZoneDisplay = null;
                    _selectedDateFormat = s.DateFormat;
                    _selectedNumberFormat = s.NumberFormat;
                    OnPropertyChanged(nameof(SelectedLanguage));
                    OnPropertyChanged(nameof(SelectedTimeZone));
                    OnPropertyChanged(nameof(TimeZoneDisplay));
                    OnPropertyChanged(nameof(SelectedDateFormat));
                    OnPropertyChanged(nameof(DateFormatPreview));
                    OnPropertyChanged(nameof(SelectedNumberFormat));
                    OnPropertyChanged(nameof(NumberFormatPreview));
                };
            }, "Locale");
        }

        private void InitializeOptions()
        {
            AvailableLanguages.Add(new LanguageItem { Code = "zh-CN", Name = "简体中文", Icon = "中" });
            AvailableLanguages.Add(new LanguageItem { Code = "en-US", Name = "English", Icon = "EN" });

            AvailableDateFormats.Add("yyyy-MM-dd");
            AvailableDateFormats.Add("dd/MM/yyyy");
            AvailableDateFormats.Add("MM/dd/yyyy");
            AvailableDateFormats.Add("yyyy年MM月dd日");

            AvailableNumberFormats.Add("1,234.56");
            AvailableNumberFormats.Add("1 234,56");
            AvailableNumberFormats.Add("1.234,56");
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settings.LoadSettings();

                _selectedLanguage = settings.Language;
                _selectedTimeZone = settings.TimeZoneId;
                _cachedTimeZoneDisplay = null;
                _selectedDateFormat = settings.DateFormat;
                _selectedNumberFormat = settings.NumberFormat;

                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(SelectedTimeZone));
                OnPropertyChanged(nameof(TimeZoneDisplay));
                OnPropertyChanged(nameof(SelectedDateFormat));
                OnPropertyChanged(nameof(DateFormatPreview));
                OnPropertyChanged(nameof(SelectedNumberFormat));
                OnPropertyChanged(nameof(NumberFormatPreview));

                TM.App.Log("[LocaleViewModel] 语言区域设置已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocaleViewModel] 加载设置失败: {ex.Message}");
                GlobalToast.Error("加载失败", "无法加载语言区域设置");
            }
        }

        private void UpdateLanguageDisplay()
        {
            var lang = AvailableLanguages.FirstOrDefault(l => l.Code == SelectedLanguage);
            if (lang != null)
            {
                _ = _service.UpdateLanguageAsync(lang.Code, lang.Name).ContinueWith(t =>
                    TM.App.Log($"[LocaleViewModel] 更新语言失败: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private void ResetToDefaults()
        {
            try
            {
                var result = StandardDialog.ShowConfirm(
                    "确定要重置为默认语言区域设置吗？",
                    "重置确认"
                );

                if (result != true) return;

                _settings.ResetToDefaults();
                LoadSettings();

                GlobalToast.Success("重置成功", "语言区域设置已恢复为默认值");
                TM.App.Log("[LocaleViewModel] 语言区域设置已重置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocaleViewModel] 重置失败: {ex.Message}");
                GlobalToast.Error("重置失败", $"重置失败：{ex.Message}");
            }
        }

        private void ApplyLanguage()
        {
            try
            {
                var result = StandardDialog.ShowConfirm(
                    "语言更改需要重启应用程序才能生效。\n\n是否现在重启？",
                    "需要重启"
                );

                if (result == true)
                {
                    _ = _service.UpdateLanguageAsync(SelectedLanguage,
                        AvailableLanguages.First(l => l.Code == SelectedLanguage).Name)
                        .ContinueWith(t =>
                            TM.App.Log($"[LocaleViewModel] 保存语言设置失败: {t.Exception?.InnerException?.Message}"),
                            TaskContinuationOptions.OnlyOnFaulted);

                    GlobalToast.Info("重启应用", "应用将在2秒后重启...");
                    TM.App.Log($"[LocaleViewModel] 准备重启应用以应用语言: {SelectedLanguage}");

                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    System.Diagnostics.Process.Start(exePath);
                                    TM.App.Log("[LocaleViewModel] 已启动新实例");

                                    System.Windows.Application.Current.Shutdown();
                                }
                                else
                                {
                                    GlobalToast.Error("重启失败", "无法获取应用程序路径");
                                    TM.App.Log("[LocaleViewModel] 重启失败：无法获取应用路径");
                                }
                            }
                            catch (Exception ex)
                            {
                                GlobalToast.Error("重启失败", $"重启失败：{ex.Message}");
                                TM.App.Log($"[LocaleViewModel] 重启应用异常: {ex.Message}");
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocaleViewModel] 应用语言失败: {ex.Message}");
                GlobalToast.Error("应用失败", $"应用失败：{ex.Message}");
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region 辅助类

    public class LanguageItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Display => $"{Icon} {Name}";
    }

    public class TimeZoneItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    #endregion
}

