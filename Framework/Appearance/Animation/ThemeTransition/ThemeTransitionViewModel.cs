using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Text.Json;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ThemeTransitionViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ThemeTransitionService _transitionService;
        private ThemeTransitionSettings _currentSettings = ThemeTransitionSettings.CreateDefault();

        private TransitionEffectItem? _selectedEffectItem;
        private EasingFunctionItem? _selectedEasingFunction;
        private (double x, double y)[]? _cachedCurvePoints;
        private EasingFunctionType? _cachedCurveType;
        private int _duration;
        private int _targetFPS;
        private int _detectedMonitorFPS;
        private double _intensity;
        private bool _viewSwitchEnabled = true;
        private int _viewSwitchOutMs = 60;
        private int _viewSwitchInMs = 120;
        private ViewSwitchEffect _viewSwitchEffect = ViewSwitchEffect.Fade;
        private bool _disposed;
        private System.Windows.Threading.DispatcherTimer? _saveSettingsDebounceTimer;

        public ThemeTransitionViewModel(ThemeTransitionService transitionService)
        {
            _transitionService = transitionService;

            TransitionEffects = new ObservableCollection<TransitionEffectItem>
            {
                new TransitionEffectItem { Effect = TransitionEffect.None, Icon = IconHelper.TryGet("Icon.Forbidden"), DisplayName = "无动画" },
                new TransitionEffectItem { Effect = TransitionEffect.Rotate, Icon = IconHelper.TryGet("Icon.Refresh"), DisplayName = "旋转" },
                new TransitionEffectItem { Effect = TransitionEffect.Blur, Icon = IconHelper.TryGet("Icon.Sparkle"), DisplayName = "模糊" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideLeft, Icon = IconHelper.TryGet("Icon.ChevronLeft"), DisplayName = "左滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideRight, Icon = IconHelper.TryGet("Icon.ChevronRight"), DisplayName = "右滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideUp, Icon = IconHelper.TryGet("Icon.ChevronUp"), DisplayName = "上滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideDown, Icon = IconHelper.TryGet("Icon.ChevronDown"), DisplayName = "下滑" },
                new TransitionEffectItem { Effect = TransitionEffect.FlipHorizontal, Icon = IconHelper.TryGet("Icon.Shuffle"), DisplayName = "水平翻转" },
                new TransitionEffectItem { Effect = TransitionEffect.FlipVertical, Icon = IconHelper.TryGet("Icon.Shuffle"), DisplayName = "垂直翻转" }
            };

            EasingFunctions = new ObservableCollection<EasingFunctionItem>
            {
                new EasingFunctionItem { Type = EasingFunctionType.Linear, Icon = IconHelper.TryGet("Icon.Minus"), DisplayName = "线性", Description = "匀速运动" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInQuad, Icon = IconHelper.TryGet("Icon.Chart"), DisplayName = "二次缓入", Description = "加速进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutQuad, Icon = IconHelper.TryGet("Icon.Chart"), DisplayName = "二次缓出", Description = "减速退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutQuad, Icon = IconHelper.TryGet("Icon.Sparkle"), DisplayName = "二次缓入缓出", Description = "先加速后减速" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInCubic, Icon = IconHelper.TryGet("Icon.Chart"), DisplayName = "三次缓入", Description = "强加速进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutCubic, Icon = IconHelper.TryGet("Icon.Chart"), DisplayName = "三次缓出", Description = "强减速退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutCubic, Icon = IconHelper.TryGet("Icon.Sparkle"), DisplayName = "三次缓入缓出", Description = "先强加速后强减速" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInElastic, Icon = IconHelper.TryGet("Icon.Refresh"), DisplayName = "弹性缓入", Description = "弹簧效果进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutElastic, Icon = IconHelper.TryGet("Icon.Target"), DisplayName = "弹性缓出", Description = "弹簧效果退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInBounce, Icon = IconHelper.TryGet("Icon.Lightning"), DisplayName = "弹跳缓入", Description = "弹跳效果进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutBounce, Icon = IconHelper.TryGet("Icon.Lightning"), DisplayName = "弹跳缓出", Description = "弹跳效果退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutBounce, Icon = IconHelper.TryGet("Icon.Lightning"), DisplayName = "弹跳缓入缓出", Description = "两端弹跳效果" }
            };

            foreach (var effect in TransitionEffects)
            {
                effect.PropertyChanged += OnEffectPropertyChanged;
            }

            _currentSettings = ThemeTransitionSettings.CreateDefault();
            var _ttSettingsFile = StoragePathHelper.GetFilePath("Framework", "Appearance/Animation/ThemeTransition", "settings.json");
            AsyncSettingsLoader.LoadOrDefer<ThemeTransitionSettings>(_ttSettingsFile, s =>
            {
                _currentSettings = s;
                ApplySettingsToUI();
            }, "ThemeTransition");

            DetectMonitorFPS();

            ApplyPresetCommand = new RelayCommand<string>(ApplyPreset);
            TestTransitionCommand = new RelayCommand(TestTransition);
            ApplySettingsCommand = new RelayCommand(ApplySettings);
            ResetToDefaultCommand = new RelayCommand(ResetToDefault);
        }

        #region 属性

        public ObservableCollection<TransitionEffectItem> TransitionEffects { get; }

        public ObservableCollection<EasingFunctionItem> EasingFunctions { get; }

        public TransitionEffectItem? SelectedEffectItem
        {
            get => _selectedEffectItem;
            set
            {
                if (_selectedEffectItem != value)
                {
                    _selectedEffectItem = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        _currentSettings.Effect = value.Effect;
                    }
                }
            }
        }

        public EasingFunctionItem? SelectedEasingFunction
        {
            get => _selectedEasingFunction;
            set
            {
                if (_selectedEasingFunction != value)
                {
                    _selectedEasingFunction = value;
                    _cachedCurvePoints = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EasingCurvePoints));
                    if (value != null)
                    {
                        _currentSettings.EasingType = value.Type;
                    }
                }
            }
        }

        public (double x, double y)[] EasingCurvePoints
        {
            get
            {
                var type = SelectedEasingFunction?.Type ?? EasingFunctionType.Linear;
                if (_cachedCurvePoints != null && _cachedCurveType == type)
                    return _cachedCurvePoints;
                _cachedCurveType = type;
                _cachedCurvePoints = ThemeTransition.EasingFunctions.GetCurvePoints(type, 50);
                return _cachedCurvePoints;
            }
        }

        public int Duration
        {
            get => _duration;
            set
            {
                var clampedValue = Math.Clamp(value, 300, 3000);
                if (_duration != clampedValue)
                {
                    _duration = clampedValue;
                    _currentSettings.Duration = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public int TargetFPS
        {
            get => _targetFPS;
            set
            {
                var clampedValue = Math.Clamp(value, 1, 1000);
                if (_targetFPS != clampedValue)
                {
                    _targetFPS = clampedValue;
                    _currentSettings.TargetFPS = clampedValue;
                    OnPropertyChanged();
                    DebounceSaveSettings();
                    if (InfoLogDedup.ShouldLog("ThemeTransition:FPS"))
                        TM.App.Log($"[ThemeTransition] FPS已更新: {clampedValue}");
                }
            }
        }

        public int DetectedMonitorFPS
        {
            get => _detectedMonitorFPS;
            private set
            {
                if (_detectedMonitorFPS != value)
                {
                    _detectedMonitorFPS = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Intensity
        {
            get => _intensity;
            set
            {
                var clampedValue = Math.Clamp(value, 0.5, 2.0);
                if (Math.Abs(_intensity - clampedValue) > 0.01)
                {
                    _intensity = clampedValue;
                    _currentSettings.IntensityMultiplier = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public List<ViewSwitchEffectItem> ViewSwitchEffects { get; } = new()
        {
            new(ViewSwitchEffect.None,      IconHelper.TryGet("Icon.Forbidden"), "无动画",         "直接切换，无过渡"),
            new(ViewSwitchEffect.Fade,      IconHelper.TryGet("Icon.Sparkle"), "淡入淡出(默认)",  "仅透明度过渡，性能最优"),
            new(ViewSwitchEffect.FadeScale, IconHelper.TryGet("Icon.Sparkle"), "淡入+缩放",       "淡入淡出+微缩放+轻微位移（开销略大）"),
            new(ViewSwitchEffect.SlideUp,   IconHelper.TryGet("Icon.ChevronUp"), "上滑入",         "新视图从下方滑入"),
            new(ViewSwitchEffect.SlideDown, IconHelper.TryGet("Icon.ChevronDown"), "下滑入",         "新视图从上方滑入"),
            new(ViewSwitchEffect.SlideLeft, IconHelper.TryGet("Icon.ChevronLeft"), "左滑入",         "新视图从右向左滑入"),
            new(ViewSwitchEffect.SlideRight,IconHelper.TryGet("Icon.ChevronRight"), "右滑入",         "新视图从左向右滑入"),
        };

        public ViewSwitchEffectItem? SelectedViewSwitchEffect
        {
            get => ViewSwitchEffects.FirstOrDefault(x => x.Effect == _viewSwitchEffect);
            set
            {
                if (value == null || value.Effect == _viewSwitchEffect) return;
                _viewSwitchEffect = value.Effect;
                _currentSettings.ViewSwitchEffect = value.Effect;
                OnPropertyChanged();
                SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[ThemeTransitionViewModel] {ex.Message}"));
            }
        }

        public bool ViewSwitchEnabled
        {
            get => _viewSwitchEnabled;
            set { if (_viewSwitchEnabled != value) { _viewSwitchEnabled = value; _currentSettings.ViewSwitchEnabled = value; OnPropertyChanged(); SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[ThemeTransitionViewModel] {ex.Message}")); } }
        }

        public int ViewSwitchOutMs
        {
            get => _viewSwitchOutMs;
            set { var v = Math.Clamp(value, 50, 500); if (_viewSwitchOutMs != v) { _viewSwitchOutMs = v; _currentSettings.ViewSwitchOutMs = v; OnPropertyChanged(); } }
        }

        public int ViewSwitchInMs
        {
            get => _viewSwitchInMs;
            set { var v = Math.Clamp(value, 50, 500); if (_viewSwitchInMs != v) { _viewSwitchInMs = v; _currentSettings.ViewSwitchInMs = v; OnPropertyChanged(); } }
        }

        #endregion

        #region 命令

        public ICommand ApplyPresetCommand { get; }

        public ICommand TestTransitionCommand { get; }

        public ICommand ApplySettingsCommand { get; }

        public ICommand ResetToDefaultCommand { get; }

        #endregion

        #region 方法

        private void ApplyPreset(string? preset)
        {
            try
            {
                TM.App.Log($"[ThemeTransition] 应用预设: {preset}");

                switch (preset)
                {
                    case "Fast":
                        Duration = 300;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Fast;
                        break;

                    case "Smooth":
                        Duration = 600;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Smooth;
                        break;

                    case "Fancy":
                        Duration = 1000;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[1];
                        _currentSettings.Preset = TransitionPreset.Fancy;
                        break;

                    case "Simple":
                        Duration = 400;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Simple;
                        break;

                    case "Dynamic":
                        Duration = 800;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[3];
                        _currentSettings.Preset = TransitionPreset.Dynamic;
                        break;

                    case "Cool":
                        Duration = 1200;
                        TargetFPS = _detectedMonitorFPS;
                        SelectedEffectItem = TransitionEffects[1];
                        _currentSettings.Preset = TransitionPreset.Cool;
                        break;
                }

                TM.App.Log($"[ThemeTransition] 预设应用成功: {preset}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 应用预设失败: {ex.Message}");
            }
        }

        private void TestTransition()
        {
            try
            {
                TM.App.Log("[ThemeTransition] 开始测试动画");

                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null && mainWindow.Content is FrameworkElement content)
                {
                    _transitionService.PrepareElement(content);
                    _transitionService.PlayTransition(content, _currentSettings, () =>
                    {
                        TM.App.Log("[ThemeTransition] 测试动画完成");
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 测试动画失败: {ex.Message}");
            }
        }

        private void ApplySettings()
        {
            try
            {
                SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[ThemeTransitionViewModel] {ex.Message}"));

                TM.App.Log("[ThemeTransition] 设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 应用设置失败: {ex.Message}");
            }
        }

        private void ResetToDefault()
        {
            try
            {
                TM.App.Log("[ThemeTransition] 重置为默认设置");

                var defaultSettings = ThemeTransitionSettings.CreateDefault();

                Duration = defaultSettings.Duration;
                TargetFPS = _detectedMonitorFPS;
                SelectedEffectItem = TransitionEffects[0];
                _currentSettings = defaultSettings;

                SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[ThemeTransitionViewModel] {ex.Message}"));

                TM.App.Log("[ThemeTransition] 已重置为默认设置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 重置失败: {ex.Message}");
            }
        }

        private void ApplySettingsToUI()
        {
            _duration = _currentSettings.Duration;
            _targetFPS = _currentSettings.TargetFPS;
            _intensity = _currentSettings.IntensityMultiplier;
            _viewSwitchEnabled = _currentSettings.ViewSwitchEnabled;
            _viewSwitchOutMs = _currentSettings.ViewSwitchOutMs;
            _viewSwitchInMs = _currentSettings.ViewSwitchInMs;
            _viewSwitchEffect = _currentSettings.ViewSwitchEffect;
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(TargetFPS));
            OnPropertyChanged(nameof(Intensity));
            OnPropertyChanged(nameof(ViewSwitchEnabled));
            OnPropertyChanged(nameof(ViewSwitchOutMs));
            OnPropertyChanged(nameof(ViewSwitchInMs));
            OnPropertyChanged(nameof(SelectedViewSwitchEffect));

            foreach (var item in TransitionEffects)
            {
                if (item.Effect == _currentSettings.Effect)
                {
                    SelectedEffectItem = item;
                    break;
                }
            }

            SyncTargetFPSIfNeeded();

            foreach (var effect in TransitionEffects)
            {
                effect.IsSelected = _currentSettings.CombinedEffects.Contains(effect.Effect);
            }

            foreach (var item in EasingFunctions)
            {
                if (item.Type == _currentSettings.EasingType)
                {
                    _selectedEasingFunction = item;
                    OnPropertyChanged(nameof(SelectedEasingFunction));
                    OnPropertyChanged(nameof(EasingCurvePoints));
                    break;
                }
            }
        }

        private void DebounceSaveSettings()
        {
            if (_saveSettingsDebounceTimer == null)
            {
                _saveSettingsDebounceTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _saveSettingsDebounceTimer.Tick += (_, _) =>
                {
                    _saveSettingsDebounceTimer.Stop();
                    SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[ThemeTransitionViewModel] {ex.Message}"));
                };
            }
            _saveSettingsDebounceTimer.Stop();
            _saveSettingsDebounceTimer.Start();
        }

        private async Task SaveSettings()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/ThemeTransition",
                    "settings.json"
                );

                var options = JsonHelper.Default;
                var tmpTtv = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = System.IO.File.Create(tmpTtv))
                {
                    await JsonSerializer.SerializeAsync(stream, _currentSettings, options);
                }
                System.IO.File.Move(tmpTtv, settingsFile, overwrite: true);

                if (InfoLogDedup.ShouldLog("ThemeTransition:SaveSettings"))
                {
                    TM.App.Log($"[ThemeTransition] 设置已异步保存到: {settingsFile}");
                    TM.App.Log($"[ThemeTransition] 配置详情: {_currentSettings.Effect}, {_currentSettings.Duration}ms, {_currentSettings.TargetFPS}fps");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 保存设置失败: {ex.Message}");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint dmFields;
            public int dmPositionX, dmPositionY;
            public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode);
        private const uint ENUM_CURRENT_SETTINGS = unchecked((uint)-1);

        private void DetectMonitorFPS()
        {
            try
            {
                var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 0)
                {
                    DetectedMonitorFPS = (int)dm.dmDisplayFrequency;
                    if (InfoLogDedup.ShouldLog("ThemeTransition:DetectFPS"))
                        TM.App.Log($"[ThemeTransition] 检测到主屏刷新率: {DetectedMonitorFPS}Hz");
                    SyncTargetFPSIfNeeded();
                    return;
                }
                DetectedMonitorFPS = 60;
                TM.App.Log("[ThemeTransition] EnumDisplaySettings 返回0，使用默认值60Hz");
            }
            catch (Exception ex)
            {
                DetectedMonitorFPS = 60;
                TM.App.Log($"[ThemeTransition] 检测显示器刷新率失败: {ex.Message}");
            }
            SyncTargetFPSIfNeeded();
        }

        private void SyncTargetFPSIfNeeded()
        {
            if (_targetFPS != _detectedMonitorFPS && _detectedMonitorFPS > 0)
            {
                if (InfoLogDedup.ShouldLog("ThemeTransition:Sync"))
                    TM.App.Log($"[ThemeTransition] TargetFPS({_targetFPS}) != 检测值({_detectedMonitorFPS})，自动同步");
                TargetFPS = _detectedMonitorFPS;
            }
        }

        private void UpdateCombinedEffects()
        {
            try
            {
                var selectedEffects = TransitionEffects
                    .Where(e => e.IsSelected && e.Effect != TransitionEffect.None)
                    .Select(e => e.Effect)
                    .ToList();

                _currentSettings.CombinedEffects = selectedEffects;

                TM.App.Log($"[ThemeTransition] 组合效果已更新: {string.Join(", ", selectedEffects)}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 更新组合效果失败: {ex.Message}");
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region 事件处理和资源释放

        private void OnEffectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TransitionEffectItem.IsSelected))
            {
                UpdateCombinedEffects();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_saveSettingsDebounceTimer != null)
                {
                    _saveSettingsDebounceTimer.Stop();
                    _saveSettingsDebounceTimer = null;
                }

                foreach (var effect in TransitionEffects)
                {
                    effect.PropertyChanged -= OnEffectPropertyChanged;
                }

                TM.App.Log("[ThemeTransitionViewModel] 资源已释放");
            }

            _disposed = true;
        }

        #endregion
    }
}

