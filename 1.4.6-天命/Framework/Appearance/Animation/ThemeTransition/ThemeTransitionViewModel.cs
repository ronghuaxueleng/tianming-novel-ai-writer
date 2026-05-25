using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ThemeTransitionViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ThemeTransitionService _transitionService;
        private ThemeTransitionSettings _currentSettings = ThemeTransitionSettings.CreateDefault();

        private TransitionEffectItem? _selectedEffectItem;
        private EasingFunctionItem? _selectedEasingFunction;
        private int _duration;
        private int _targetFPS;
        private int _detectedMonitorFPS;
        private double _intensity;
        private bool _disposed;

        public ThemeTransitionViewModel(ThemeTransitionService transitionService)
        {
            _transitionService = transitionService;

            TransitionEffects = new ObservableCollection<TransitionEffectItem>
            {
                new TransitionEffectItem { Effect = TransitionEffect.None, Icon = "🚫", DisplayName = "无动画" },
                new TransitionEffectItem { Effect = TransitionEffect.Rotate, Icon = "🔄", DisplayName = "旋转" },
                new TransitionEffectItem { Effect = TransitionEffect.Blur, Icon = "💫", DisplayName = "模糊" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideLeft, Icon = "⬅️", DisplayName = "左滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideRight, Icon = "➡️", DisplayName = "右滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideUp, Icon = "⬆️", DisplayName = "上滑" },
                new TransitionEffectItem { Effect = TransitionEffect.SlideDown, Icon = "⬇️", DisplayName = "下滑" },
                new TransitionEffectItem { Effect = TransitionEffect.FlipHorizontal, Icon = "↔️", DisplayName = "水平翻转" },
                new TransitionEffectItem { Effect = TransitionEffect.FlipVertical, Icon = "↕️", DisplayName = "垂直翻转" }
            };

            EasingFunctions = new ObservableCollection<EasingFunctionItem>
            {
                new EasingFunctionItem { Type = EasingFunctionType.Linear, Icon = "➖", DisplayName = "线性", Description = "匀速运动" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInQuad, Icon = "📈", DisplayName = "二次缓入", Description = "加速进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutQuad, Icon = "📉", DisplayName = "二次缓出", Description = "减速退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutQuad, Icon = "〰️", DisplayName = "二次缓入缓出", Description = "先加速后减速" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInCubic, Icon = "📊", DisplayName = "三次缓入", Description = "强加速进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutCubic, Icon = "📉", DisplayName = "三次缓出", Description = "强减速退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutCubic, Icon = "〽️", DisplayName = "三次缓入缓出", Description = "先强加速后强减速" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInElastic, Icon = "🔄", DisplayName = "弹性缓入", Description = "弹簧效果进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutElastic, Icon = "🎯", DisplayName = "弹性缓出", Description = "弹簧效果退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInBounce, Icon = "⚽", DisplayName = "弹跳缓入", Description = "弹跳效果进入" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseOutBounce, Icon = "🏀", DisplayName = "弹跳缓出", Description = "弹跳效果退出" },
                new EasingFunctionItem { Type = EasingFunctionType.EaseInOutBounce, Icon = "🎾", DisplayName = "弹跳缓入缓出", Description = "两端弹跳效果" }
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
                if (SelectedEasingFunction != null)
                {
                    return ThemeTransition.EasingFunctions.GetCurvePoints(SelectedEasingFunction.Type, 50);
                }
                return ThemeTransition.EasingFunctions.GetCurvePoints(EasingFunctionType.Linear, 50);
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
                var maxFPS = Math.Max(60, _detectedMonitorFPS);
                var clampedValue = Math.Clamp(value, 30, maxFPS);
                if (_targetFPS != clampedValue)
                {
                    _targetFPS = clampedValue;
                    _currentSettings.TargetFPS = clampedValue;
                    OnPropertyChanged();
                    SaveSettings();
                    TM.App.Log($"[ThemeTransition] FPS已更新并保存: {clampedValue}");
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
                        TargetFPS = 60;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Fast;
                        break;

                    case "Smooth":
                        Duration = 600;
                        TargetFPS = 60;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Smooth;
                        break;

                    case "Fancy":
                        Duration = 1000;
                        TargetFPS = 60;
                        SelectedEffectItem = TransitionEffects[1];
                        _currentSettings.Preset = TransitionPreset.Fancy;
                        break;

                    case "Simple":
                        Duration = 400;
                        TargetFPS = 60;
                        SelectedEffectItem = TransitionEffects[2];
                        _currentSettings.Preset = TransitionPreset.Simple;
                        break;

                    case "Dynamic":
                        Duration = 800;
                        TargetFPS = 60;
                        SelectedEffectItem = TransitionEffects[3];
                        _currentSettings.Preset = TransitionPreset.Dynamic;
                        break;

                    case "Cool":
                        Duration = 1200;
                        TargetFPS = Math.Min(120, _detectedMonitorFPS);
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
                SaveSettings();

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
                TargetFPS = defaultSettings.TargetFPS;
                SelectedEffectItem = TransitionEffects[0];
                _currentSettings = defaultSettings;

                SaveSettings();

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
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(TargetFPS));
            OnPropertyChanged(nameof(Intensity));

            foreach (var item in TransitionEffects)
            {
                if (item.Effect == _currentSettings.Effect)
                {
                    SelectedEffectItem = item;
                    break;
                }
            }

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

        private async void SaveSettings()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/ThemeTransition",
                    "settings.json"
                );

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(_currentSettings, options);
                var tmpTtv = settingsFile + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmpTtv, json);
                System.IO.File.Move(tmpTtv, settingsFile, overwrite: true);

                TM.App.Log($"[ThemeTransition] 设置已异步保存到: {settingsFile}");
                TM.App.Log($"[ThemeTransition] 配置详情: {_currentSettings.Effect}, {_currentSettings.Duration}ms, {_currentSettings.TargetFPS}fps");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeTransition] 保存设置失败: {ex.Message}");
            }
        }

        private void DetectMonitorFPS()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var refreshRate = obj["CurrentRefreshRate"];
                        if (refreshRate != null && int.TryParse(refreshRate.ToString(), out int fps))
                        {
                            DetectedMonitorFPS = fps;
                            TM.App.Log($"[ThemeTransition] 检测到显示器刷新率: {DetectedMonitorFPS}Hz");
                            return;
                        }
                    }
                }

                DetectedMonitorFPS = 60;
                TM.App.Log("[ThemeTransition] 无法检测显示器刷新率，使用默认值60Hz");
            }
            catch (Exception ex)
            {
                DetectedMonitorFPS = 60;
                TM.App.Log($"[ThemeTransition] 检测显示器刷新率失败: {ex.Message}");
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

