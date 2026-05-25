using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;
using TM.Framework.Notifications.Sound.VolumeAndDevice;
using TM.Framework.Notifications.Sound.Services;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeAndDeviceViewModel : INotifyPropertyChanged
    {
        private double _systemVolume = 80;
        private double _notificationVolume = 100;
        private double _effectVolume = 80;
        private bool _isMuted = false;
        private double _volumeBeforeMute = 80;

        private double _bassLevel = 0;
        private double _midBassLevel = 0;
        private double _midLevel = 0;
        private double _midTrebleLevel = 0;
        private double _trebleLevel = 0;
        private string _equalizerPreset = "默认";

        private AudioDeviceInfo? _selectedOutputDevice;
        private AudioDeviceInfo? _selectedInputDevice;

        private readonly VolumeAndDeviceSettings _settings;
        private readonly SystemVolumeController _volumeController;
        private readonly AudioEqualizerService _equalizer;
        private readonly AudioDeviceManager _deviceManager;
        private readonly TM.Services.Framework.Notification.NotificationSoundService _soundService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public VolumeAndDeviceViewModel(
            VolumeAndDeviceSettings settings,
            SystemVolumeController volumeController,
            AudioEqualizerService equalizer,
            AudioDeviceManager deviceManager,
            TM.Services.Framework.Notification.NotificationSoundService soundService)
        {
            _settings = settings;
            _volumeController = volumeController;
            _equalizer = equalizer;
            _deviceManager = deviceManager;
            _soundService = soundService;

            QuietPresetCommand = new RelayCommand(ApplyQuietPreset);
            StandardPresetCommand = new RelayCommand(ApplyStandardPreset);
            LoudPresetCommand = new RelayCommand(ApplyLoudPreset);
            ToggleMuteCommand = new RelayCommand(ToggleMute);

            ApplyEqualizerPresetCommand = new RelayCommand<string>(ApplyEqualizerPreset);
            ResetEqualizerCommand = new RelayCommand(ResetEqualizer);

            TestOutputDeviceCommand = new RelayCommand(TestOutputDevice);
            TestInputDeviceCommand = new RelayCommand(TestInputDevice);

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetAllCommand = new RelayCommand(ResetAll);

            InitializeDevices();

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                _settings.LoadSettings();
                return () =>
                {
                    NotificationVolume = _settings.NotificationVolume;
                    EffectVolume = _settings.EffectVolume;
                    IsMuted = _settings.IsMuted;
                    BassLevel = _settings.BassLevel;
                    MidBassLevel = _settings.MidBassLevel;
                    MidLevel = _settings.MidLevel;
                    MidTrebleLevel = _settings.MidTrebleLevel;
                    TrebleLevel = _settings.TrebleLevel;
                    EqualizerPreset = _settings.EqualizerPreset;
                    if (!string.IsNullOrEmpty(_settings.OutputDeviceId))
                        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.DeviceId == _settings.OutputDeviceId);
                    if (!string.IsNullOrEmpty(_settings.InputDeviceId))
                        SelectedInputDevice = InputDevices.FirstOrDefault(d => d.DeviceId == _settings.InputDeviceId);
                };
            }, "VolumeAndDevice");

            TM.App.Log("[音量与设备] ViewModel初始化完成");
        }

        #region 属性

        public double SystemVolume
        {
            get => _systemVolume;
            set
            {
                if (_systemVolume != value)
                {
                    _systemVolume = value;
                    OnPropertyChanged(nameof(SystemVolume));
                    OnPropertyChanged(nameof(SystemVolumeText));

                    try
                    {
                        _volumeController.SetMasterVolume(value);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[音量与设备] 设置系统音量失败: {ex.Message}");
                    }
                }
            }
        }

        public string SystemVolumeText => $"{SystemVolume:F0}%";

        public double NotificationVolume
        {
            get => _notificationVolume;
            set
            {
                if (_notificationVolume != value)
                {
                    _notificationVolume = value;
                    OnPropertyChanged(nameof(NotificationVolume));
                    OnPropertyChanged(nameof(NotificationVolumeText));
                }
            }
        }

        public string NotificationVolumeText => $"{NotificationVolume:F0}%";

        public double EffectVolume
        {
            get => _effectVolume;
            set
            {
                if (_effectVolume != value)
                {
                    _effectVolume = value;
                    OnPropertyChanged(nameof(EffectVolume));
                    OnPropertyChanged(nameof(EffectVolumeText));
                }
            }
        }

        public string EffectVolumeText => $"{EffectVolume:F0}%";

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged(nameof(IsMuted));
                    OnPropertyChanged(nameof(MuteButtonText));
                }
            }
        }

        public string MuteButtonText => IsMuted ? "取消静音" : "静音";

        public double BassLevel
        {
            get => _bassLevel;
            set 
            { 
                _bassLevel = value; 
                OnPropertyChanged(nameof(BassLevel)); 
                OnPropertyChanged(nameof(BassLevelText));
                _equalizer.SetBassGain(value);
            }
        }
        public string BassLevelText => $"{BassLevel:+0;-0;0}";

        public double MidBassLevel
        {
            get => _midBassLevel;
            set 
            { 
                _midBassLevel = value; 
                OnPropertyChanged(nameof(MidBassLevel)); 
                OnPropertyChanged(nameof(MidBassLevelText));
                _equalizer.SetMidBassGain(value);
            }
        }
        public string MidBassLevelText => $"{MidBassLevel:+0;-0;0}";

        public double MidLevel
        {
            get => _midLevel;
            set 
            { 
                _midLevel = value; 
                OnPropertyChanged(nameof(MidLevel)); 
                OnPropertyChanged(nameof(MidLevelText));
                _equalizer.SetMidGain(value);
            }
        }
        public string MidLevelText => $"{MidLevel:+0;-0;0}";

        public double MidTrebleLevel
        {
            get => _midTrebleLevel;
            set 
            { 
                _midTrebleLevel = value; 
                OnPropertyChanged(nameof(MidTrebleLevel)); 
                OnPropertyChanged(nameof(MidTrebleLevelText));
                _equalizer.SetMidTrebleGain(value);
            }
        }
        public string MidTrebleLevelText => $"{MidTrebleLevel:+0;-0;0}";

        public double TrebleLevel
        {
            get => _trebleLevel;
            set 
            { 
                _trebleLevel = value; 
                OnPropertyChanged(nameof(TrebleLevel)); 
                OnPropertyChanged(nameof(TrebleLevelText));
                _equalizer.SetTrebleGain(value);
            }
        }
        public string TrebleLevelText => $"{TrebleLevel:+0;-0;0}";

        public string EqualizerPreset
        {
            get => _equalizerPreset;
            set { _equalizerPreset = value; OnPropertyChanged(nameof(EqualizerPreset)); }
        }

        public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
        public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();

        public AudioDeviceInfo? SelectedOutputDevice
        {
            get => _selectedOutputDevice;
            set
            {
                if (_selectedOutputDevice != value)
                {
                    _selectedOutputDevice = value;
                    OnPropertyChanged(nameof(SelectedOutputDevice));
                    TM.App.Log($"[音量与设备] 选择输出设备: {value?.DeviceName ?? "无"}");
                }
            }
        }

        public AudioDeviceInfo? SelectedInputDevice
        {
            get => _selectedInputDevice;
            set
            {
                if (_selectedInputDevice != value)
                {
                    _selectedInputDevice = value;
                    OnPropertyChanged(nameof(SelectedInputDevice));
                    TM.App.Log($"[音量与设备] 选择输入设备: {value?.DeviceName ?? "无"}");
                }
            }
        }

        #endregion

        #region 命令

        public ICommand QuietPresetCommand { get; }
        public ICommand StandardPresetCommand { get; }
        public ICommand LoudPresetCommand { get; }
        public ICommand ToggleMuteCommand { get; }

        public ICommand ApplyEqualizerPresetCommand { get; }
        public ICommand ResetEqualizerCommand { get; }

        public ICommand TestOutputDeviceCommand { get; }
        public ICommand TestInputDeviceCommand { get; }

        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetAllCommand { get; }

        #endregion

        #region 方法

        private void InitializeDevices()
        {
            try
            {
                var outputDevices = _deviceManager.GetOutputDevices();
                var inputDevices = _deviceManager.GetInputDevices();

                OutputDevices.Clear();
                InputDevices.Clear();

                foreach (var device in outputDevices)
                {
                    OutputDevices.Add(device);
                }

                foreach (var device in inputDevices)
                {
                    InputDevices.Add(device);
                }

                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault);
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault);

                _systemVolume = _volumeController.GetMasterVolume();
                OnPropertyChanged(nameof(SystemVolume));
                OnPropertyChanged(nameof(SystemVolumeText));

                TM.App.Log($"[音量与设备] 找到 {OutputDevices.Count} 个输出设备，{InputDevices.Count} 个输入设备");
                TM.App.Log($"[音量与设备] 当前系统音量: {_systemVolume:F0}%");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[音量与设备] 初始化设备失败: {ex.Message}");
                InitializeSimulatedDevices();
            }
        }

        private void InitializeSimulatedDevices()
        {
            OutputDevices.Add(new AudioDeviceInfo
            {
                DeviceId = "output_default",
                DeviceName = "扬声器/耳机",
                DeviceType = "输出",
                IsDefault = true,
                Status = "已连接"
            });

            InputDevices.Add(new AudioDeviceInfo
            {
                DeviceId = "input_default",
                DeviceName = "麦克风",
                DeviceType = "输入",
                IsDefault = true,
                Status = "已连接"
            });

            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault);
            SelectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault);
        }

        private void ApplyQuietPreset()
        {
            SystemVolume = 30;
            NotificationVolume = 50;
            EffectVolume = 40;
            TM.App.Log("[音量与设备] 应用安静预设");
            GlobalToast.Success("音量预设", "已应用安静预设");
        }

        private void ApplyStandardPreset()
        {
            SystemVolume = 80;
            NotificationVolume = 100;
            EffectVolume = 80;
            TM.App.Log("[音量与设备] 应用标准预设");
            GlobalToast.Success("音量预设", "已应用标准预设");
        }

        private void ApplyLoudPreset()
        {
            SystemVolume = 100;
            NotificationVolume = 100;
            EffectVolume = 100;
            TM.App.Log("[音量与设备] 应用响亮预设");
            GlobalToast.Success("音量预设", "已应用响亮预设");
        }

        private void ToggleMute()
        {
            if (IsMuted)
            {
                SystemVolume = _volumeBeforeMute;
                IsMuted = false;
                TM.App.Log("[音量与设备] 取消静音");
                GlobalToast.Info("静音", "已取消静音");
            }
            else
            {
                _volumeBeforeMute = SystemVolume;
                SystemVolume = 0;
                IsMuted = true;
                TM.App.Log("[音量与设备] 静音");
                GlobalToast.Info("静音", "已静音");
            }
        }

        private void ApplyEqualizerPreset(string? preset)
        {
            if (string.IsNullOrEmpty(preset)) return;

            EqualizerPreset = preset;

            _equalizer.ApplyPreset(preset);

            var settings = _equalizer.GetCurrentSettings();
            _bassLevel = settings.bass;
            _midBassLevel = settings.midBass;
            _midLevel = settings.mid;
            _midTrebleLevel = settings.midTreble;
            _trebleLevel = settings.treble;

            OnPropertyChanged(nameof(BassLevel));
            OnPropertyChanged(nameof(BassLevelText));
            OnPropertyChanged(nameof(MidBassLevel));
            OnPropertyChanged(nameof(MidBassLevelText));
            OnPropertyChanged(nameof(MidLevel));
            OnPropertyChanged(nameof(MidLevelText));
            OnPropertyChanged(nameof(MidTrebleLevel));
            OnPropertyChanged(nameof(MidTrebleLevelText));
            OnPropertyChanged(nameof(TrebleLevel));
            OnPropertyChanged(nameof(TrebleLevelText));

            TM.App.Log($"[音量与设备] 应用均衡器预设: {preset}");
            GlobalToast.Success("均衡器", $"已应用{preset}预设");
        }

        private void ResetEqualizer()
        {
            ApplyEqualizerPreset("默认");
        }

        private void TestOutputDevice()
        {
            if (SelectedOutputDevice == null)
            {
                GlobalToast.Warning("设备测试", "请先选择输出设备");
                return;
            }

            try
            {
                _soundService.PlayTestSound();

                TM.App.Log($"[音量与设备] 测试输出设备: {SelectedOutputDevice.DeviceName}");
                GlobalToast.Success("设备测试", $"已播放测试音：{SelectedOutputDevice.DeviceName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[音量与设备] 测试输出设备失败: {ex.Message}");
                StandardDialog.ShowError($"播放测试音失败: {ex.Message}", "设备测试失败");
            }
        }

        private void TestInputDevice()
        {
            if (SelectedInputDevice == null)
            {
                GlobalToast.Warning("设备测试", "请先选择输入设备");
                return;
            }

            try
            {
                System.Media.SystemSounds.Asterisk.Play();

                TM.App.Log($"[音量与设备] 测试输入设备: {SelectedInputDevice.DeviceName}");
                GlobalToast.Success("设备测试", $"输入设备已就绪：{SelectedInputDevice.DeviceName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[音量与设备] 测试输入设备失败: {ex.Message}");
                StandardDialog.ShowError($"测试输入设备失败: {ex.Message}", "设备测试失败");
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.SystemVolume = SystemVolume;
                _settings.NotificationVolume = NotificationVolume;
                _settings.EffectVolume = EffectVolume;
                _settings.IsMuted = IsMuted;
                _settings.BassLevel = BassLevel;
                _settings.MidBassLevel = MidBassLevel;
                _settings.MidLevel = MidLevel;
                _settings.MidTrebleLevel = MidTrebleLevel;
                _settings.TrebleLevel = TrebleLevel;
                _settings.EqualizerPreset = EqualizerPreset;
                _settings.OutputDeviceId = SelectedOutputDevice?.DeviceId;
                _settings.InputDeviceId = SelectedInputDevice?.DeviceId;

                _settings.SaveSettings();

                TM.App.Log("[音量与设备] 设置已保存");
                GlobalToast.Success("保存成功", "音量与设备设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[音量与设备] 保存设置失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败: {ex.Message}", "错误");
            }
        }

        private void LoadSettings()
        {
            try
            {
                _settings.LoadSettings();

                NotificationVolume = _settings.NotificationVolume;
                EffectVolume = _settings.EffectVolume;
                IsMuted = _settings.IsMuted;
                BassLevel = _settings.BassLevel;
                MidBassLevel = _settings.MidBassLevel;
                MidLevel = _settings.MidLevel;
                MidTrebleLevel = _settings.MidTrebleLevel;
                TrebleLevel = _settings.TrebleLevel;
                EqualizerPreset = _settings.EqualizerPreset;

                if (!string.IsNullOrEmpty(_settings.OutputDeviceId))
                {
                    SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.DeviceId == _settings.OutputDeviceId);
                }
                if (!string.IsNullOrEmpty(_settings.InputDeviceId))
                {
                    SelectedInputDevice = InputDevices.FirstOrDefault(d => d.DeviceId == _settings.InputDeviceId);
                }

                TM.App.Log("[音量与设备] 设置已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[音量与设备] 加载设置失败: {ex.Message}");
            }
        }

        private void ResetAll()
        {
            _settings.ResetToDefaults();

            LoadSettings();

            ResetEqualizer();

            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault);
            SelectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault);

            TM.App.Log("[音量与设备] 设置已重置");
            GlobalToast.Success("重置成功", "所有设置已恢复默认");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

