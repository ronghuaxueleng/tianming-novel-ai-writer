using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.Speech.Synthesis;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.Sound.VoiceBroadcast
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VoiceBroadcastViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled = false;
        private int _speed = 0;
        private int _volume = 100;
        private int _pitch = 0;
        private string _testText = "这是一条测试语音播报";
        private bool _broadcastOnNotification = true;
        private bool _broadcastOnError = true;
        private bool _broadcastOnSuccess = false;

        private readonly VoiceBroadcastSettings _settings;
        private readonly SpeechSynthesizer? _synthesizer;
        private bool _isTtsAvailable = false;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[VoiceBroadcast] {key}: {ex.Message}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public VoiceBroadcastViewModel(VoiceBroadcastSettings settings)
        {
            _settings = settings;

            TestBroadcastCommand = new RelayCommand(TestBroadcast);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetToDefaultCommand = new RelayCommand(ResetToDefault);

            try
            {

                _synthesizer = new SpeechSynthesizer();
                _synthesizer.SetOutputToDefaultAudioDevice();

                var voices = _synthesizer.GetInstalledVoices();
                _isTtsAvailable = voices.Count > 0;

                if (_isTtsAvailable)
                {
                    TM.App.Log($"[语音播报] 检测到{voices.Count}个语音引擎");
                }
                else
                {
                    TM.App.Log("[语音播报] 警告：系统未安装语音引擎，将使用系统提示音");
                }

                AsyncSettingsLoader.RunOrDefer(() =>
                {
                    _settings.LoadSettings();
                    return () =>
                    {
                        IsEnabled = _settings.IsEnabled;
                        Speed = _settings.Speed;
                        Volume = _settings.Volume;
                        Pitch = _settings.Pitch;
                        TestText = _settings.TestText;
                        BroadcastOnNotification = _settings.BroadcastOnNotification;
                        BroadcastOnError = _settings.BroadcastOnError;
                        BroadcastOnSuccess = _settings.BroadcastOnSuccess;
                    };
                }, "VoiceBroadcast");

                TM.App.Log("[语音播报] ViewModel初始化完成");
            }
            catch (Exception ex)
            {
                _isTtsAvailable = false;
                TM.App.Log($"[语音播报] 初始化警告: {ex.Message}，TTS不可用，将使用系统提示音");
            }
        }

        public bool IsTtsAvailable => _isTtsAvailable;

        public string TtsStatusText => _isTtsAvailable ? "语音引擎：可用" : "语音引擎：不可用（将使用系统提示音）";

        #region 属性

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                    OnPropertyChanged(nameof(StatusText));
                    TM.App.Log($"[语音播报] 播报状态: {(value ? "已启用" : "已禁用")}");
                }
            }
        }

        public string StatusText => IsEnabled ? "已启用" : "已禁用";

        public int Speed
        {
            get => _speed;
            set
            {
                if (_speed != value)
                {
                    _speed = value;
                    OnPropertyChanged(nameof(Speed));
                    OnPropertyChanged(nameof(SpeedText));
                }
            }
        }

        public string SpeedText
        {
            get
            {
                if (Speed < -5) return "很慢";
                if (Speed < 0) return "慢速";
                if (Speed == 0) return "正常";
                if (Speed <= 5) return "快速";
                return "很快";
            }
        }

        public int Volume
        {
            get => _volume;
            set
            {
                if (_volume != value)
                {
                    _volume = value;
                    OnPropertyChanged(nameof(Volume));
                    OnPropertyChanged(nameof(VolumeText));
                }
            }
        }

        public string VolumeText => $"{Volume}%";

        public int Pitch
        {
            get => _pitch;
            set
            {
                if (_pitch != value)
                {
                    _pitch = value;
                    OnPropertyChanged(nameof(Pitch));
                    OnPropertyChanged(nameof(PitchText));
                }
            }
        }

        public string PitchText
        {
            get
            {
                if (Pitch < -5) return "很低";
                if (Pitch < 0) return "偏低";
                if (Pitch == 0) return "正常";
                if (Pitch <= 5) return "偏高";
                return "很高";
            }
        }

        public string TestText
        {
            get => _testText;
            set
            {
                if (_testText != value)
                {
                    _testText = value;
                    OnPropertyChanged(nameof(TestText));
                }
            }
        }

        public bool BroadcastOnNotification
        {
            get => _broadcastOnNotification;
            set
            {
                if (_broadcastOnNotification != value)
                {
                    _broadcastOnNotification = value;
                    OnPropertyChanged(nameof(BroadcastOnNotification));
                }
            }
        }

        public bool BroadcastOnError
        {
            get => _broadcastOnError;
            set
            {
                if (_broadcastOnError != value)
                {
                    _broadcastOnError = value;
                    OnPropertyChanged(nameof(BroadcastOnError));
                }
            }
        }

        public bool BroadcastOnSuccess
        {
            get => _broadcastOnSuccess;
            set
            {
                if (_broadcastOnSuccess != value)
                {
                    _broadcastOnSuccess = value;
                    OnPropertyChanged(nameof(BroadcastOnSuccess));
                }
            }
        }

        #endregion

        #region 命令

        public ICommand TestBroadcastCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetToDefaultCommand { get; }

        #endregion

        #region 方法

        public void SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                if (_isTtsAvailable && _synthesizer != null)
                {
                    _synthesizer.Rate = Speed;
                    _synthesizer.Volume = Volume;
                    _synthesizer.SpeakAsync(text);

                    TM.App.Log($"[语音播报] TTS播报: {text}");
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    TM.App.Log($"[语音播报] 系统提示音播放（TTS不可用）");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[语音播报] 播报失败: {ex.Message}");

                try
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
                catch (Exception innerEx)
                {
                    DebugLogOnce("SpeakAsync_PlayFallbackSound", innerEx);
                }
            }
        }

        public bool ShouldBroadcast(string notificationType)
        {
            if (!IsEnabled)
            {
                return false;
            }

            return notificationType switch
            {
                "成功" => BroadcastOnSuccess,
                "错误" => BroadcastOnError,
                "警告" => BroadcastOnNotification,
                "信息" => BroadcastOnNotification,
                _ => BroadcastOnNotification
            };
        }

        private void TestBroadcast()
        {
            if (string.IsNullOrWhiteSpace(TestText))
            {
                GlobalToast.Warning("测试播报", "请输入测试文本");
                return;
            }

            try
            {
                if (_isTtsAvailable && _synthesizer != null)
                {
                    _synthesizer.Rate = Speed;
                    _synthesizer.Volume = Volume;
                    _synthesizer.SpeakAsync(TestText);

                    TM.App.Log($"[语音播报] TTS播报: {TestText}");
                    GlobalToast.Success("语音播报", "正在播放TTS语音");
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();

                    TM.App.Log($"[语音播报] 系统提示音播放（TTS不可用）: {TestText}");
                    GlobalToast.Info("语音播报", "TTS不可用，已播放系统提示音");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[语音播报] 播报失败: {ex.Message}");

                try
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    GlobalToast.Warning("语音播报", "TTS失败，已播放系统提示音");
                }
                catch (Exception innerEx)
                {
                    DebugLogOnce("PlayFallbackSound", innerEx);
                    StandardDialog.ShowError($"语音播报失败: {ex.Message}", "播报失败");
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.IsEnabled = IsEnabled;
                _settings.Speed = Speed;
                _settings.Volume = Volume;
                _settings.Pitch = Pitch;
                _settings.TestText = TestText;
                _settings.BroadcastOnNotification = BroadcastOnNotification;
                _settings.BroadcastOnError = BroadcastOnError;
                _settings.BroadcastOnSuccess = BroadcastOnSuccess;

                _settings.SaveSettings();

                TM.App.Log("[语音播报] 设置已保存");
                GlobalToast.Success("保存成功", "语音播报设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[语音播报] 保存设置失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败: {ex.Message}", "错误");
            }
        }

        private void LoadSettings()
        {
            try
            {
                _settings.LoadSettings();

                IsEnabled = _settings.IsEnabled;
                Speed = _settings.Speed;
                Volume = _settings.Volume;
                Pitch = _settings.Pitch;
                TestText = _settings.TestText;
                BroadcastOnNotification = _settings.BroadcastOnNotification;
                BroadcastOnError = _settings.BroadcastOnError;
                BroadcastOnSuccess = _settings.BroadcastOnSuccess;

                TM.App.Log("[语音播报] 设置已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[语音播报] 加载设置失败: {ex.Message}");
            }
        }

        private void ResetToDefault()
        {
            _settings.ResetToDefaults();

            LoadSettings();

            TM.App.Log("[语音播报] 设置已重置");
            GlobalToast.Success("重置成功", "已恢复默认语音播报设置");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

