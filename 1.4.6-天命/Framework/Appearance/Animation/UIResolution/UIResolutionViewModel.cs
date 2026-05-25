using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Animation.UIResolution
{
    public class PresetResolutionItem
    {
        public PresetResolution Type { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ScaleLevelItem
    {
        public UIScaleLevel Level { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class UIResolutionViewModel : INotifyPropertyChanged
    {
        private readonly UIResolutionService _resolutionService;
        private UIResolutionService ResolutionService => _resolutionService;

        private UIResolutionSettings _currentSettings = null!;
        private PresetResolutionItem _selectedPreset = null!;
        private ScaleLevelItem _selectedScale = null!;

        private int _windowWidth;
        private int _windowHeight;
        private bool _usePreset;

        private int _scalePercent;

        private string _currentWindowInfo = string.Empty;
        private string _screenResolutionInfo = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<PresetResolutionItem> PresetResolutions { get; set; } = null!;
        public ObservableCollection<ScaleLevelItem> ScaleLevels { get; set; } = null!;

        public ICommand PreviewCommand { get; set; } = null!;
        public ICommand ApplySettingsCommand { get; set; } = null!;
        public ICommand ResetToDefaultCommand { get; set; } = null!;

        public UIResolutionViewModel(UIResolutionService resolutionService)
        {
            _resolutionService = resolutionService;
            PresetResolutions = new ObservableCollection<PresetResolutionItem>
            {
                new PresetResolutionItem { Type = PresetResolution.HD, Icon = "📺", DisplayName = "720p (1280×720)" },
                new PresetResolutionItem { Type = PresetResolution.FullHD, Icon = "🖥️", DisplayName = "1080p (1920×1080)" },
                new PresetResolutionItem { Type = PresetResolution.QHD, Icon = "🖥️", DisplayName = "1440p (2560×1440)" },
                new PresetResolutionItem { Type = PresetResolution.Custom, Icon = "⚙️", DisplayName = "自定义" }
            };

            ScaleLevels = new ObservableCollection<ScaleLevelItem>
            {
                new ScaleLevelItem { Level = UIScaleLevel.Scale100, Icon = "🔍", DisplayName = "100% (标准)" },
                new ScaleLevelItem { Level = UIScaleLevel.Scale125, Icon = "🔍", DisplayName = "125% (稍大)" },
                new ScaleLevelItem { Level = UIScaleLevel.Scale150, Icon = "🔍", DisplayName = "150% (较大)" },
                new ScaleLevelItem { Level = UIScaleLevel.Scale200, Icon = "🔍", DisplayName = "200% (很大)" }
            };

            _currentSettings = UIResolutionSettings.CreateDefault();
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                UIResolutionSettings s;
                try { s = ResolutionService.LoadSettings(); }
                catch { s = UIResolutionSettings.CreateDefault(); }
                return () =>
                {
                    _currentSettings = s;
                    ApplySettingsToUI();
                    UpdateInfoDisplay();
                };
            }, "UIResolution");

            PreviewCommand = new RelayCommand(PreviewSettings);
            ApplySettingsCommand = new RelayCommand(ApplySettings);
            ResetToDefaultCommand = new RelayCommand(ResetToDefault);
        }

        #region 属性

        public PresetResolutionItem SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    if (value != null)
                    {
                        _currentSettings.Preset = value.Type;

                        if (value.Type != PresetResolution.Custom)
                        {
                            var (width, height) = UIResolutionSettings.GetPresetResolution(value.Type);
                            WindowWidth = width;
                            WindowHeight = height;
                        }
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCustomResolution));
                }
            }
        }

        public ScaleLevelItem SelectedScale
        {
            get => _selectedScale;
            set
            {
                if (_selectedScale != value)
                {
                    _selectedScale = value;
                    if (value != null)
                    {
                        ScalePercent = (int)value.Level;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public int WindowWidth
        {
            get => _windowWidth;
            set
            {
                var clampedValue = Math.Clamp(value, _currentSettings.MinWidth, GetMaxWidth());
                if (_windowWidth != clampedValue)
                {
                    _windowWidth = clampedValue;
                    _currentSettings.WindowWidth = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public int WindowHeight
        {
            get => _windowHeight;
            set
            {
                var clampedValue = Math.Clamp(value, _currentSettings.MinHeight, GetMaxHeight());
                if (_windowHeight != clampedValue)
                {
                    _windowHeight = clampedValue;
                    _currentSettings.WindowHeight = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public bool UsePreset
        {
            get => _usePreset;
            set
            {
                if (_usePreset != value)
                {
                    _usePreset = value;
                    _currentSettings.UsePreset = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ScalePercent
        {
            get => _scalePercent;
            set
            {
                if (_scalePercent != value)
                {
                    _scalePercent = value;
                    _currentSettings.ScalePercent = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentWindowInfo
        {
            get => _currentWindowInfo;
            set
            {
                if (_currentWindowInfo != value)
                {
                    _currentWindowInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ScreenResolutionInfo
        {
            get => _screenResolutionInfo;
            set
            {
                if (_screenResolutionInfo != value)
                {
                    _screenResolutionInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCustomResolution => _selectedPreset?.Type == PresetResolution.Custom;

        #endregion

        #region 命令方法

        private void PreviewSettings()
        {
            try
            {
                TM.App.Log("[UIResolution] 预览设置...");

                ResolutionService.ApplyWindowSize(_currentSettings.WindowWidth, _currentSettings.WindowHeight);
                ResolutionService.ApplyUIScale(_currentSettings.ScalePercent);

                UpdateInfoDisplay();

                GlobalToast.Info("预览", $"已应用 {_currentSettings.WindowWidth}×{_currentSettings.WindowHeight}, 缩放{_currentSettings.ScalePercent}%");
                TM.App.Log("[UIResolution] 预览完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 预览失败: {ex.Message}");
                StandardDialog.ShowError($"预览失败：\n\n{ex.Message}", "错误", null);
            }
        }

        private void ApplySettings()
        {
            try
            {
                ResolutionService.ApplySettings(_currentSettings);

                UpdateInfoDisplay();

                TM.App.Log("[UIResolution] 设置已应用");
                GlobalToast.Success("应用成功", "UI分辨率设置已应用并保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 应用设置失败: {ex.Message}");
                StandardDialog.ShowError($"应用设置失败：\n\n{ex.Message}", "错误", null);
            }
        }

        private void ResetToDefault()
        {
            try
            {
                _currentSettings = UIResolutionSettings.CreateDefault();
                ApplySettingsToUI();

                TM.App.Log("[UIResolution] 已重置为默认设置");
                GlobalToast.Success("重置成功", "已恢复为默认设置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 重置失败: {ex.Message}");
            }
        }

        #endregion

        #region 工具方法

        private void ApplySettingsToUI()
        {
            _windowWidth = _currentSettings.WindowWidth;
            _windowHeight = _currentSettings.WindowHeight;
            _usePreset = _currentSettings.UsePreset;
            _scalePercent = _currentSettings.ScalePercent;

            OnPropertyChanged(nameof(WindowWidth));
            OnPropertyChanged(nameof(WindowHeight));
            OnPropertyChanged(nameof(UsePreset));
            OnPropertyChanged(nameof(ScalePercent));

            SelectedPreset = PresetResolutions.FirstOrDefault(x => x.Type == _currentSettings.Preset)!;
            SelectedScale = ScaleLevels.FirstOrDefault(x => (int)x.Level == _currentSettings.ScalePercent)!;
        }

        private void UpdateInfoDisplay()
        {
            var (currentWidth, currentHeight) = ResolutionService.GetCurrentWindowSize();
            CurrentWindowInfo = $"当前窗口: {currentWidth}×{currentHeight}";

            var (screenWidth, screenHeight) = ResolutionService.GetScreenResolution();
            ScreenResolutionInfo = $"屏幕分辨率: {screenWidth}×{screenHeight}";
        }

        private int GetMaxWidth()
        {
            var (maxWidth, _) = ResolutionService.GetScreenResolution();
            return maxWidth;
        }

        private int GetMaxHeight()
        {
            var (_, maxHeight) = ResolutionService.GetScreenResolution();
            return maxHeight;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

