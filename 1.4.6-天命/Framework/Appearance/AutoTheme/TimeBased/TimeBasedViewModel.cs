using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class TimeBasedViewModel : INotifyPropertyChanged
    {
        private readonly TimeScheduleService _service;
        private readonly HolidayLibrary _holidayLibrary;
        private TimeBasedSettings _settings = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        private TimeScheduleMode _mode;
        public TimeScheduleMode Mode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; OnPropertyChanged(); } }
        }

        private TimeSpan _dayStartTime;
        public TimeSpan DayStartTime
        {
            get => _dayStartTime;
            set { if (_dayStartTime != value) { _dayStartTime = value; OnPropertyChanged(); } }
        }

        private ThemeType _dayTheme;
        public ThemeType DayTheme
        {
            get => _dayTheme;
            set { if (_dayTheme != value) { _dayTheme = value; OnPropertyChanged(); } }
        }

        private TimeSpan _nightStartTime;
        public TimeSpan NightStartTime
        {
            get => _nightStartTime;
            set { if (_nightStartTime != value) { _nightStartTime = value; OnPropertyChanged(); } }
        }

        private ThemeType _nightTheme;
        public ThemeType NightTheme
        {
            get => _nightTheme;
            set { if (_nightTheme != value) { _nightTheme = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<TimeScheduleItem> Schedules { get; set; } = null!;

        private double _latitude;
        public double Latitude
        {
            get => _latitude;
            set { if (_latitude != value) { _latitude = value; OnPropertyChanged(); } }
        }

        private double _longitude;
        public double Longitude
        {
            get => _longitude;
            set { if (_longitude != value) { _longitude = value; OnPropertyChanged(); } }
        }

        private bool _autoLocation;
        public bool AutoLocation
        {
            get => _autoLocation;
            set { if (_autoLocation != value) { _autoLocation = value; OnPropertyChanged(); } }
        }

        private ThemeType _sunriseTheme;
        public ThemeType SunriseTheme
        {
            get => _sunriseTheme;
            set { if (_sunriseTheme != value) { _sunriseTheme = value; OnPropertyChanged(); } }
        }

        private ThemeType _sunsetTheme;
        public ThemeType SunsetTheme
        {
            get => _sunsetTheme;
            set { if (_sunsetTheme != value) { _sunsetTheme = value; OnPropertyChanged(); } }
        }

        private string _todaySunrise = "未计算";
        public string TodaySunrise
        {
            get => _todaySunrise;
            set { if (_todaySunrise != value) { _todaySunrise = value; OnPropertyChanged(); } }
        }

        private string _todaySunset = "未计算";
        public string TodaySunset
        {
            get => _todaySunset;
            set { if (_todaySunset != value) { _todaySunset = value; OnPropertyChanged(); } }
        }

        private bool _excludeHolidays;
        public bool ExcludeHolidays
        {
            get => _excludeHolidays;
            set { if (_excludeHolidays != value) { _excludeHolidays = value; OnPropertyChanged(); } }
        }

        private bool _useBuiltInHolidays;
        public bool UseBuiltInHolidays
        {
            get => _useBuiltInHolidays;
            set { if (_useBuiltInHolidays != value) { _useBuiltInHolidays = value; OnPropertyChanged(); LoadBuiltInHolidays(); } }
        }

        public ObservableCollection<DateTime> CustomHolidays { get; set; } = null!;
        public ObservableCollection<HolidayInfo> BuiltInHolidays { get; set; } = null!;

        private HolidayThemeOverride _holidayThemeOverride = HolidayThemeOverride.NoChange;
        public HolidayThemeOverride HolidayThemeOverride
        {
            get => _holidayThemeOverride;
            set { if (_holidayThemeOverride != value) { _holidayThemeOverride = value; OnPropertyChanged(); } }
        }

        private ThemeType _holidayTheme = ThemeType.Light;
        public ThemeType HolidayTheme
        {
            get => _holidayTheme;
            set { if (_holidayTheme != value) { _holidayTheme = value; OnPropertyChanged(); } }
        }

        private bool _temporaryDisabled;
        public bool TemporaryDisabled
        {
            get => _temporaryDisabled;
            set { if (_temporaryDisabled != value) { _temporaryDisabled = value; OnPropertyChanged(); } }
        }

        private int _priority = 5;
        public int Priority
        {
            get => _priority;
            set { if (_priority != value) { _priority = value; OnPropertyChanged(); } }
        }

        private DateTime? _disabledUntil;
        public DateTime? DisabledUntil
        {
            get => _disabledUntil;
            set { if (_disabledUntil != value) { _disabledUntil = value; OnPropertyChanged(); } }
        }

        private bool _recordHistory;
        public bool RecordHistory
        {
            get => _recordHistory;
            set { if (_recordHistory != value) { _recordHistory = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<SwitchHistoryRecord> SwitchHistory { get; set; } = null!;

        private string _currentActiveSchedule = "未检测";
        public string CurrentActiveSchedule
        {
            get => _currentActiveSchedule;
            set { if (_currentActiveSchedule != value) { _currentActiveSchedule = value; OnPropertyChanged(); } }
        }

        private ThemeType _currentTheme = ThemeType.Light;
        public ThemeType CurrentTheme
        {
            get => _currentTheme;
            set { if (_currentTheme != value) { _currentTheme = value; OnPropertyChanged(); } }
        }

        private string _nextSwitchTime = "待计算";
        public string NextSwitchTime
        {
            get => _nextSwitchTime;
            set { if (_nextSwitchTime != value) { _nextSwitchTime = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<TimeConflictInfo> ConflictsList { get; set; } = null!;

        private bool _hasConflicts;
        public bool HasConflicts
        {
            get => _hasConflicts;
            set { if (_hasConflicts != value) { _hasConflicts = value; OnPropertyChanged(); } }
        }

        public ICommand ToggleEnabledCommand { get; set; } = null!;
        public ICommand AddScheduleCommand { get; set; } = null!;
        public ICommand EditScheduleCommand { get; set; } = null!;
        public ICommand RemoveScheduleCommand { get; set; } = null!;
        public ICommand DetectConflictsCommand { get; set; } = null!;
        public ICommand CalculateSunTimesCommand { get; set; } = null!;
        public ICommand GetLocationCommand { get; set; } = null!;
        public ICommand TemporaryDisableCommand { get; set; } = null!;
        public ICommand ClearHistoryCommand { get; set; } = null!;
        public ICommand ApplySettingsCommand { get; set; } = null!;
        public ICommand AddHolidayCommand { get; set; } = null!;
        public ICommand RemoveHolidayCommand { get; set; } = null!;
        public ICommand RefreshStatusCommand { get; set; } = null!;

        public TimeBasedViewModel(TimeScheduleService service, HolidayLibrary holidayLibrary)
        {
            _service = service;
            _holidayLibrary = holidayLibrary;

            Schedules = new ObservableCollection<TimeScheduleItem>();
            CustomHolidays = new ObservableCollection<DateTime>();
            SwitchHistory = new ObservableCollection<SwitchHistoryRecord>();
            ConflictsList = new ObservableCollection<TimeConflictInfo>();
            BuiltInHolidays = new ObservableCollection<HolidayInfo>();

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var s = _service.GetSettings();
                return () =>
                {
                    _settings = s;
                    _enabled = s.Enabled; _mode = s.Mode;
                    _dayStartTime = s.DayStartTime; _dayTheme = s.DayTheme;
                    _nightStartTime = s.NightStartTime; _nightTheme = s.NightTheme;
                    _latitude = s.Latitude; _longitude = s.Longitude;
                    _autoLocation = s.AutoLocation; _sunriseTheme = s.SunriseTheme;
                    _sunsetTheme = s.SunsetTheme; _excludeHolidays = s.ExcludeHolidays;
                    _useBuiltInHolidays = s.UseBuiltInHolidays; _holidayThemeOverride = s.HolidayThemeOverride;
                    _holidayTheme = s.HolidayTheme; _temporaryDisabled = s.TemporaryDisabled;
                    _disabledUntil = s.DisabledUntil; _recordHistory = s.RecordHistory;
                    _priority = s.Priority;
                    Schedules.Clear();
                    foreach (var sc in s.Schedules)
                        Schedules.Add(new TimeScheduleItem { StartTime = sc.StartTime, EndTime = sc.EndTime, TargetTheme = sc.TargetTheme, EnabledWeekdays = sc.EnabledWeekdays, UseTransition = sc.UseTransition, Description = sc.Description, Priority = sc.Priority });
                    CustomHolidays.Clear();
                    foreach (var h in s.CustomHolidays) CustomHolidays.Add(h);
                    SwitchHistory.Clear();
                    foreach (var r in s.History.TakeLast(20)) SwitchHistory.Add(r);
                    if (_useBuiltInHolidays) LoadBuiltInHolidays();
                    OnPropertyChanged(string.Empty);
                    CalculateSunTimes();
                };
            }, "TimeBased");

            ToggleEnabledCommand = new RelayCommand(ToggleEnabled);
            AddScheduleCommand = new RelayCommand(AddSchedule);
            EditScheduleCommand = new RelayCommand(param =>
            {
                if (param is TimeScheduleItem item)
                {
                    EditSchedule(item);
                }
            });
            RemoveScheduleCommand = new RelayCommand(param =>
            {
                if (param is TimeScheduleItem item)
                {
                    RemoveSchedule(item);
                }
            });
            DetectConflictsCommand = new RelayCommand(DetectConflicts);
            CalculateSunTimesCommand = new RelayCommand(CalculateSunTimes);
            GetLocationCommand = new RelayCommand(GetLocation);
            TemporaryDisableCommand = new RelayCommand(TemporaryDisable);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            ApplySettingsCommand = new RelayCommand(ApplySettings);
            AddHolidayCommand = new RelayCommand(AddHoliday);
            RemoveHolidayCommand = new RelayCommand(param =>
            {
                if (param is DateTime holiday)
                {
                    RemoveHoliday(holiday);
                }
            });
            RefreshStatusCommand = new RelayCommand(RefreshStatus);

            RefreshStatus();
        }

        private void LoadBuiltInHolidays()
        {
            BuiltInHolidays.Clear();

            if (!UseBuiltInHolidays) return;

            var currentYear = DateTime.Now.Year;
            var holidays = _holidayLibrary.GetHolidaysByYear(currentYear);

            foreach (var holiday in holidays)
            {
                BuiltInHolidays.Add(holiday);
            }

            TM.App.Log($"[TimeBasedViewModel] 已加载{currentYear}年内置节假日{holidays.Count}个");
        }

        private void ToggleEnabled()
        {
            Enabled = !Enabled;

            if (Enabled)
            {
                _service.StartSchedule();
            }
            else
            {
                _service.StopSchedule();
            }
        }

        private void AddSchedule()
        {
            try
            {
                var dialog = new TimeScheduleEditDialog(null);
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);

                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    Schedules.Add(dialog.Result);
                    ToastNotification.ShowSuccess("添加成功", $"已添加时间段：{dialog.Result.Description}");
                    TM.App.Log($"[TimeBasedViewModel] 添加时间段成功: {dialog.Result.Description}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 添加时间段失败: {ex.Message}");
                ToastNotification.ShowError("添加失败", ex.Message);
            }
        }

        private void EditSchedule(TimeScheduleItem item)
        {
            try
            {
                var dialog = new TimeScheduleEditDialog(item);
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);

                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    var index = Schedules.IndexOf(item);
                    if (index >= 0)
                    {
                        Schedules[index] = dialog.Result;
                        ToastNotification.ShowSuccess("编辑成功", $"已更新时间段：{dialog.Result.Description}");
                        TM.App.Log($"[TimeBasedViewModel] 编辑时间段成功: {dialog.Result.Description}");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 编辑时间段失败: {ex.Message}");
                ToastNotification.ShowError("编辑失败", ex.Message);
            }
        }

        private void RemoveSchedule(TimeScheduleItem item)
        {
            Schedules.Remove(item);
        }

        private void DetectConflicts()
        {
            try
            {
                ConflictsList.Clear();

                _settings.Schedules = Schedules.Select(s => new TimeSchedule
                {
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    TargetTheme = s.TargetTheme,
                    EnabledWeekdays = s.EnabledWeekdays,
                    UseTransition = s.UseTransition,
                    Description = s.Description,
                    Priority = s.Priority
                }).ToList();

                var conflicts = _service.DetectConflicts();

                foreach (var conflict in conflicts)
                {
                    ConflictsList.Add(new TimeConflictInfo
                    {
                        ConflictType = "时间段重叠",
                        Description = conflict,
                        Severity = "警告"
                    });
                }

                HasConflicts = ConflictsList.Count > 0;

                if (conflicts.Count == 0)
                {
                    ToastNotification.ShowSuccess("检测完成", "未检测到时间段冲突");
                }
                else
                {
                    var message = $"检测到 {conflicts.Count} 个冲突";
                    ToastNotification.ShowWarning("检测到冲突", message);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 检测冲突失败: {ex.Message}");
                StandardDialog.ShowError($"检测冲突失败: {ex.Message}", "错误", null);
            }
        }

        private void CalculateSunTimes()
        {
            try
            {
                var (sunrise, sunset) = SunCalculator.CalculateSunTimes(DateTime.Now, Latitude, Longitude);
                TodaySunrise = sunrise.ToString(@"hh\:mm");
                TodaySunset = sunset.ToString(@"hh\:mm");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 计算日出日落失败: {ex.Message}");
                TodaySunrise = "计算失败";
                TodaySunset = "计算失败";
            }
        }

        private void GetLocation()
        {
            _ = GetLocationAsync();
        }

        private async Task GetLocationAsync()
        {
            try
            {
                ToastNotification.ShowInfo("正在获取位置", "请稍候...");
                TM.App.Log("[TimeBasedViewModel] 开始获取地理位置");

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetStringAsync("https://ipapi.co/json/");

                var json = System.Text.Json.JsonDocument.Parse(response);
                var root = json.RootElement;

                if (root.TryGetProperty("latitude", out var latProp) && 
                    root.TryGetProperty("longitude", out var lonProp))
                {
                    var lat = latProp.GetDouble();
                    var lon = lonProp.GetDouble();

                    string city = root.TryGetProperty("city", out var cityProp) 
                        ? cityProp.GetString() ?? "未知" 
                        : "未知";
                    string country = root.TryGetProperty("country_name", out var countryProp) 
                        ? countryProp.GetString() ?? "未知" 
                        : "未知";

                    Latitude = lat;
                    Longitude = lon;

                    ToastNotification.ShowSuccess("位置获取成功", 
                        $"{city}, {country}\n纬度: {lat:F4}°, 经度: {lon:F4}°");
                    TM.App.Log($"[TimeBasedViewModel] 位置获取成功: {city}, {country} ({lat:F4}, {lon:F4})");

                    CalculateSunTimes();

                    ApplySettings();
                }
                else
                {
                    ToastNotification.ShowError("获取失败", "无法解析位置数据");
                    TM.App.Log("[TimeBasedViewModel] 位置数据解析失败");
                }
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                ToastNotification.ShowError("网络错误", $"无法连接到位置服务：{ex.Message}");
                TM.App.Log($"[TimeBasedViewModel] 网络请求失败: {ex.Message}");
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                ToastNotification.ShowError("请求超时", "位置服务响应超时，请稍后重试");
                TM.App.Log("[TimeBasedViewModel] 位置请求超时");
            }
            catch (Exception ex)
            {
                ToastNotification.ShowError("获取失败", $"位置获取出错：{ex.Message}");
                TM.App.Log($"[TimeBasedViewModel] 位置获取异常: {ex.Message}");
            }
        }

        private void TemporaryDisable()
        {
            TemporaryDisabled = !TemporaryDisabled;

            if (TemporaryDisabled)
            {
            DisabledUntil = DateTime.Now.AddHours(1);
            ToastNotification.ShowSuccess("临时禁用", "已禁用1小时");
            }
            else
            {
            DisabledUntil = null;
            ToastNotification.ShowSuccess("取消禁用", "已取消临时禁用");
            }
        }

        private void ClearHistory()
        {
            SwitchHistory.Clear();
            _settings.History.Clear();
            _service.UpdateSettings(_settings);
        }

        private void AddHoliday()
        {
            CustomHolidays.Add(DateTime.Now.Date);
        }

        private void RemoveHoliday(DateTime holiday)
        {
            CustomHolidays.Remove(holiday);
        }

        private void RefreshStatus()
        {
            try
            {
                var currentTheme = _service.CalculateCurrentTheme();
                if (currentTheme.HasValue)
                {
                    CurrentTheme = currentTheme.Value;
                    CurrentActiveSchedule = $"当前应使用: {currentTheme.Value}";
                }
                else
                {
                    CurrentActiveSchedule = "无匹配的时间段";
                }

                NextSwitchTime = "待计算";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 刷新状态失败: {ex.Message}");
            }
        }

        private void ApplySettings()
        {
            try
            {
                _settings.Enabled = Enabled;
                _settings.Mode = Mode;
                _settings.DayStartTime = DayStartTime;
                _settings.DayTheme = DayTheme;
                _settings.NightStartTime = NightStartTime;
                _settings.NightTheme = NightTheme;
                _settings.Latitude = Latitude;
                _settings.Longitude = Longitude;
                _settings.AutoLocation = AutoLocation;
                _settings.SunriseTheme = SunriseTheme;
                _settings.SunsetTheme = SunsetTheme;
                _settings.ExcludeHolidays = ExcludeHolidays;
                _settings.UseBuiltInHolidays = UseBuiltInHolidays;
                _settings.HolidayThemeOverride = HolidayThemeOverride;
                _settings.HolidayTheme = HolidayTheme;
                _settings.TemporaryDisabled = TemporaryDisabled;
                _settings.DisabledUntil = DisabledUntil;
                _settings.RecordHistory = RecordHistory;
                _settings.Priority = Priority;

                _settings.Schedules = Schedules.Select(s => new TimeSchedule
                {
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    TargetTheme = s.TargetTheme,
                    EnabledWeekdays = s.EnabledWeekdays,
                    UseTransition = s.UseTransition,
                    Description = s.Description,
                    Priority = s.Priority
                }).ToList();

                _settings.CustomHolidays = CustomHolidays.ToList();

            _service.UpdateSettings(_settings);

            ToastNotification.ShowSuccess("保存成功", "设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[TimeBasedViewModel] 应用设置失败: {ex.Message}");
                StandardDialog.ShowError($"保存设置失败: {ex.Message}", "错误", null);
            }
        }
    }

    public class TimeScheduleItem
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public ThemeType TargetTheme { get; set; }
        public Weekday EnabledWeekdays { get; set; }
        public bool UseTransition { get; set; }
        public string Description { get; set; } = string.Empty;
        public int Priority { get; set; } = 5;

        public string DisplayText => $"{StartTime:hh\\:mm} - {EndTime:hh\\:mm}: {TargetTheme} ({Description}) [优先级:{Priority}]";
    }

    public class HolidayItem
    {
        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;

        public string DisplayText => $"{Date:yyyy-MM-dd}: {Description}";
    }

    public class TimeConflictInfo
    {
        public string ConflictType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Severity { get; set; } = "警告";
    }
}

