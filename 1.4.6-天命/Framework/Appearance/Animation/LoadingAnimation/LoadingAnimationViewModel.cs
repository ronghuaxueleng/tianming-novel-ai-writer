using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    public class AnimationTypeItem
    {
        public LoadingAnimationType Type { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class PositionItem
    {
        public LoadingPosition Position { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class OverlayModeItem
    {
        public OverlayMode Mode { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LoadingAnimationViewModel : INotifyPropertyChanged
    {
        private readonly LoadingAnimationService _loadingService;
        private LoadingAnimationSettings _currentSettings = null!;
        private AnimationTypeItem _selectedAnimationType = null!;
        private PositionItem _selectedPosition = null!;
        private OverlayModeItem _selectedOverlayMode = null!;

        private int _animationSpeed;
        private int _size;
        private Color _primaryColor;
        private Color _secondaryColor;
        private double _opacity;

        private bool _showText;
        private string _loadingText = string.Empty;
        private int _textSize;
        private Color _textColor;
        private bool _showPercentage;

        private double _overlayOpacity;
        private Color _overlayColor;
        private int _blurRadius;

        private int _minDisplayTime;
        private int _delayTime;
        private bool _cancelOnClick;

        private bool _enableSound;
        private string _soundPath = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<AnimationTypeItem> AnimationTypes { get; set; } = null!;
        public ObservableCollection<PositionItem> Positions { get; set; } = null!;
        public ObservableCollection<OverlayModeItem> OverlayModes { get; set; } = null!;

        public ICommand TestLoadingCommand { get; set; } = null!;
        public ICommand ApplySettingsCommand { get; set; } = null!;
        public ICommand ResetToDefaultCommand { get; set; } = null!;
        public ICommand PickPrimaryColorCommand { get; set; } = null!;
        public ICommand PickSecondaryColorCommand { get; set; } = null!;
        public ICommand PickTextColorCommand { get; set; } = null!;
        public ICommand PickOverlayColorCommand { get; set; } = null!;
        public ICommand PickSoundFileCommand { get; set; } = null!;

        public LoadingAnimationViewModel(LoadingAnimationService loadingService)
        {
            _loadingService = loadingService;
            AnimationTypes = new ObservableCollection<AnimationTypeItem>
            {
                new AnimationTypeItem { Type = LoadingAnimationType.Spinner, Icon = "🔄", DisplayName = "旋转圈" },
                new AnimationTypeItem { Type = LoadingAnimationType.Dots, Icon = "⏺️", DisplayName = "跳动点" },
                new AnimationTypeItem { Type = LoadingAnimationType.Bars, Icon = "📊", DisplayName = "跳动条" },
                new AnimationTypeItem { Type = LoadingAnimationType.Pulse, Icon = "💓", DisplayName = "脉冲" },
                new AnimationTypeItem { Type = LoadingAnimationType.Ring, Icon = "⭕", DisplayName = "环形" },
                new AnimationTypeItem { Type = LoadingAnimationType.Wave, Icon = "🌊", DisplayName = "波浪" },
                new AnimationTypeItem { Type = LoadingAnimationType.Progress, Icon = "📈", DisplayName = "进度条" },
                new AnimationTypeItem { Type = LoadingAnimationType.Skeleton, Icon = "💀", DisplayName = "骨架屏" },
                new AnimationTypeItem { Type = LoadingAnimationType.Custom1, Icon = "🎨", DisplayName = "自定义1" },
                new AnimationTypeItem { Type = LoadingAnimationType.Custom2, Icon = "🖌️", DisplayName = "自定义2" }
            };

            Positions = new ObservableCollection<PositionItem>
            {
                new PositionItem { Position = LoadingPosition.Center, Icon = "⊙", DisplayName = "居中" },
                new PositionItem { Position = LoadingPosition.Top, Icon = "⬆️", DisplayName = "顶部" },
                new PositionItem { Position = LoadingPosition.Bottom, Icon = "⬇️", DisplayName = "底部" },
                new PositionItem { Position = LoadingPosition.TopRight, Icon = "↗️", DisplayName = "右上角" },
                new PositionItem { Position = LoadingPosition.BottomRight, Icon = "↘️", DisplayName = "右下角" }
            };

            OverlayModes = new ObservableCollection<OverlayModeItem>
            {
                new OverlayModeItem { Mode = OverlayMode.None, Icon = "🚫", DisplayName = "无遮罩" },
                new OverlayModeItem { Mode = OverlayMode.Transparent, Icon = "⚪", DisplayName = "半透明" },
                new OverlayModeItem { Mode = OverlayMode.Blur, Icon = "🌫️", DisplayName = "模糊" },
                new OverlayModeItem { Mode = OverlayMode.Full, Icon = "⬛", DisplayName = "完全遮罩" }
            };

            _currentSettings = LoadingAnimationSettings.CreateDefault();
            var _laSettingsFile = StoragePathHelper.GetFilePath("Framework", "Appearance/Animation/LoadingAnimation", "settings.json");
            AsyncSettingsLoader.LoadOrDefer<LoadingAnimationSettings>(_laSettingsFile, s =>
            {
                _currentSettings = s;
                ApplySettingsToUI();
            }, "LoadingAnimation");

            TestLoadingCommand = new RelayCommand(TestLoading);
            ApplySettingsCommand = new RelayCommand(ApplySettings);
            ResetToDefaultCommand = new RelayCommand(ResetToDefault);
            PickPrimaryColorCommand = new RelayCommand(PickPrimaryColor);
            PickSecondaryColorCommand = new RelayCommand(PickSecondaryColor);
            PickTextColorCommand = new RelayCommand(PickTextColor);
            PickOverlayColorCommand = new RelayCommand(PickOverlayColor);
            PickSoundFileCommand = new RelayCommand(PickSoundFile);
        }

        #region 属性

        public AnimationTypeItem SelectedAnimationType
        {
            get => _selectedAnimationType;
            set
            {
                if (_selectedAnimationType != value)
                {
                    _selectedAnimationType = value;
                    if (value != null)
                    {
                        _currentSettings.AnimationType = value.Type;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public PositionItem SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (_selectedPosition != value)
                {
                    _selectedPosition = value;
                    if (value != null)
                    {
                        _currentSettings.Position = value.Position;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public OverlayModeItem SelectedOverlayMode
        {
            get => _selectedOverlayMode;
            set
            {
                if (_selectedOverlayMode != value)
                {
                    _selectedOverlayMode = value;
                    if (value != null)
                    {
                        _currentSettings.Overlay = value.Mode;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBlurMode));
                }
            }
        }

        public int AnimationSpeed
        {
            get => _animationSpeed;
            set
            {
                var clampedValue = Math.Clamp(value, 1, 200);
                if (_animationSpeed != clampedValue)
                {
                    _animationSpeed = clampedValue;
                    _currentSettings.AnimationSpeed = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public int Size
        {
            get => _size;
            set
            {
                var clampedValue = Math.Clamp(value, 24, 96);
                if (_size != clampedValue)
                {
                    _size = clampedValue;
                    _currentSettings.Size = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public Color PrimaryColor
        {
            get => _primaryColor;
            set
            {
                if (_primaryColor != value)
                {
                    _primaryColor = value;
                    _currentSettings.PrimaryColor = value.ToString();
                    OnPropertyChanged();
                }
            }
        }

        public Color SecondaryColor
        {
            get => _secondaryColor;
            set
            {
                if (_secondaryColor != value)
                {
                    _secondaryColor = value;
                    _currentSettings.SecondaryColor = value.ToString();
                    OnPropertyChanged();
                }
            }
        }

        public double Opacity
        {
            get => _opacity;
            set
            {
                var clampedValue = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_opacity - clampedValue) > 0.01)
                {
                    _opacity = clampedValue;
                    _currentSettings.Opacity = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowText
        {
            get => _showText;
            set
            {
                if (_showText != value)
                {
                    _showText = value;
                    _currentSettings.ShowText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoadingText
        {
            get => _loadingText;
            set
            {
                if (_loadingText != value)
                {
                    _loadingText = value;
                    _currentSettings.LoadingText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TextSize
        {
            get => _textSize;
            set
            {
                var clampedValue = Math.Clamp(value, 10, 24);
                if (_textSize != clampedValue)
                {
                    _textSize = clampedValue;
                    _currentSettings.TextSize = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public Color TextColor
        {
            get => _textColor;
            set
            {
                if (_textColor != value)
                {
                    _textColor = value;
                    _currentSettings.TextColor = value.ToString();
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowPercentage
        {
            get => _showPercentage;
            set
            {
                if (_showPercentage != value)
                {
                    _showPercentage = value;
                    _currentSettings.ShowPercentage = value;
                    OnPropertyChanged();
                }
            }
        }

        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set
            {
                var clampedValue = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_overlayOpacity - clampedValue) > 0.01)
                {
                    _overlayOpacity = clampedValue;
                    _currentSettings.OverlayOpacity = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public Color OverlayColor
        {
            get => _overlayColor;
            set
            {
                if (_overlayColor != value)
                {
                    _overlayColor = value;
                    _currentSettings.OverlayColor = value.ToString();
                    OnPropertyChanged();
                }
            }
        }

        public int BlurRadius
        {
            get => _blurRadius;
            set
            {
                var clampedValue = Math.Clamp(value, 0, 20);
                if (_blurRadius != clampedValue)
                {
                    _blurRadius = clampedValue;
                    _currentSettings.BlurRadius = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public int MinDisplayTime
        {
            get => _minDisplayTime;
            set
            {
                var clampedValue = Math.Clamp(value, 0, 2000);
                if (_minDisplayTime != clampedValue)
                {
                    _minDisplayTime = clampedValue;
                    _currentSettings.MinDisplayTime = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public int DelayTime
        {
            get => _delayTime;
            set
            {
                var clampedValue = Math.Clamp(value, 0, 1000);
                if (_delayTime != clampedValue)
                {
                    _delayTime = clampedValue;
                    _currentSettings.DelayTime = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public bool CancelOnClick
        {
            get => _cancelOnClick;
            set
            {
                if (_cancelOnClick != value)
                {
                    _cancelOnClick = value;
                    _currentSettings.CancelOnClick = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableSound
        {
            get => _enableSound;
            set
            {
                if (_enableSound != value)
                {
                    _enableSound = value;
                    _currentSettings.EnableSound = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SoundPath
        {
            get => _soundPath;
            set
            {
                if (_soundPath != value)
                {
                    _soundPath = value;
                    _currentSettings.SoundPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsBlurMode => _selectedOverlayMode?.Mode == OverlayMode.Blur;

        #endregion

        #region 命令方法

        private void TestLoading()
        {
            _ = TestLoadingAsync();
        }

        private async Task TestLoadingAsync()
        {
            try
            {
                TM.App.Log("[LoadingAnimation] 开始测试加载效果...");

                await SaveSettingsAsync();

                _loadingService.ReloadSettings();

                _loadingService.Show(LoadingText, null);

                await Task.Delay(3000);

                _loadingService.Hide();

                TM.App.Log("[LoadingAnimation] 测试完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 测试失败: {ex.Message}");
                StandardDialog.ShowError($"测试失败：\n\n{ex.Message}", "错误", null);
            }
        }

        private async void ApplySettings()
        {
            try
            {
                await SaveSettingsAsync();
                _loadingService.ReloadSettings();

                TM.App.Log("[LoadingAnimation] 设置已应用");
                ToastNotification.ShowSuccess("应用成功", "加载动画设置已应用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 应用设置失败: {ex.Message}");
                StandardDialog.ShowError($"应用设置失败：\n\n{ex.Message}", "错误", null);
            }
        }

        private void ResetToDefault()
        {
            try
            {
                _currentSettings = LoadingAnimationSettings.CreateDefault();
                ApplySettingsToUI();

                TM.App.Log("[LoadingAnimation] 已重置为默认设置");
                ToastNotification.ShowSuccess("重置成功", "已恢复为默认设置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 重置失败: {ex.Message}");
            }
        }

        private void PickPrimaryColor()
        {
            var selected = ColorPickerDialog.Show(_primaryColor, Application.Current?.MainWindow);
            if (selected.HasValue)
                PrimaryColor = selected.Value;
        }

        private void PickSecondaryColor()
        {
            var selected = ColorPickerDialog.Show(_secondaryColor, Application.Current?.MainWindow);
            if (selected.HasValue)
                SecondaryColor = selected.Value;
        }

        private void PickTextColor()
        {
            var selected = ColorPickerDialog.Show(_textColor, Application.Current?.MainWindow);
            if (selected.HasValue)
                TextColor = selected.Value;
        }

        private void PickOverlayColor()
        {
            var selected = ColorPickerDialog.Show(_overlayColor, Application.Current?.MainWindow);
            if (selected.HasValue)
                OverlayColor = selected.Value;
        }

        private void PickSoundFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                SoundPath = dialog.FileName;
            }
        }

        #endregion

        #region 工具方法

        private void ApplySettingsToUI()
        {
            _animationSpeed = _currentSettings.AnimationSpeed;
            _size = _currentSettings.Size;
            _primaryColor = (Color)ColorConverter.ConvertFromString(_currentSettings.PrimaryColor);
            _secondaryColor = (Color)ColorConverter.ConvertFromString(_currentSettings.SecondaryColor);
            _opacity = _currentSettings.Opacity;

            _showText = _currentSettings.ShowText;
            _loadingText = _currentSettings.LoadingText;
            _textSize = _currentSettings.TextSize;
            _textColor = (Color)ColorConverter.ConvertFromString(_currentSettings.TextColor);
            _showPercentage = _currentSettings.ShowPercentage;

            _overlayOpacity = _currentSettings.OverlayOpacity;
            _overlayColor = (Color)ColorConverter.ConvertFromString(_currentSettings.OverlayColor);
            _blurRadius = _currentSettings.BlurRadius;

            _minDisplayTime = _currentSettings.MinDisplayTime;
            _delayTime = _currentSettings.DelayTime;
            _cancelOnClick = _currentSettings.CancelOnClick;

            _enableSound = _currentSettings.EnableSound;
            _soundPath = _currentSettings.SoundPath;

            OnPropertyChanged(nameof(AnimationSpeed));
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(PrimaryColor));
            OnPropertyChanged(nameof(SecondaryColor));
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(ShowText));
            OnPropertyChanged(nameof(LoadingText));
            OnPropertyChanged(nameof(TextSize));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(ShowPercentage));
            OnPropertyChanged(nameof(OverlayOpacity));
            OnPropertyChanged(nameof(OverlayColor));
            OnPropertyChanged(nameof(BlurRadius));
            OnPropertyChanged(nameof(MinDisplayTime));
            OnPropertyChanged(nameof(DelayTime));
            OnPropertyChanged(nameof(CancelOnClick));
            OnPropertyChanged(nameof(EnableSound));
            OnPropertyChanged(nameof(SoundPath));

            SelectedAnimationType = AnimationTypes.FirstOrDefault(x => x.Type == _currentSettings.AnimationType)!;
            SelectedPosition = Positions.FirstOrDefault(x => x.Position == _currentSettings.Position)!;
            SelectedOverlayMode = OverlayModes.FirstOrDefault(x => x.Mode == _currentSettings.Overlay)!;
        }

        private void SaveSettings()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/LoadingAnimation",
                    "settings.json"
                );

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(_currentSettings, options);
                var tmpLav = settingsFile + ".tmp";
                File.WriteAllText(tmpLav, json);
                File.Move(tmpLav, settingsFile, overwrite: true);

                TM.App.Log($"[LoadingAnimation] 设置已保存到: {settingsFile}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 保存设置失败: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/LoadingAnimation",
                    "settings.json"
                );

                var options = JsonHelper.Default;
                var json = JsonSerializer.Serialize(_currentSettings, options);
                var tmpLavA = settingsFile + ".tmp";
                await File.WriteAllTextAsync(tmpLavA, json);
                File.Move(tmpLavA, settingsFile, overwrite: true);

                TM.App.Log($"[LoadingAnimation] 设置已异步保存到: {settingsFile}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 异步保存设置失败: {ex.Message}");
                throw;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

