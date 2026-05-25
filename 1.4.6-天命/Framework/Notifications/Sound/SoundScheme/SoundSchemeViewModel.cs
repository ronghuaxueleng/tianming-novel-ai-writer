using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundSchemeViewModel : INotifyPropertyChanged
    {
        private SoundSchemeInfo? _selectedScheme;
        private SoundEffectInfo? _selectedCustomSound;

        private readonly SoundSchemeSettings _settings;
        private readonly string _customSoundsDirectory;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SoundSchemeViewModel(SoundSchemeSettings settings)
        {
            _settings = settings;
            _customSoundsDirectory = StoragePathHelper.GetFilePath("Framework", "Notifications/Sound/SoundScheme/CustomSounds", "");

            if (!Directory.Exists(_customSoundsDirectory))
            {
                Directory.CreateDirectory(_customSoundsDirectory);
                TM.App.Log($"[声音方案] 创建自定义音效目录: {_customSoundsDirectory}");
            }

            SelectSchemeCommand = new RelayCommand<SoundSchemeInfo>(SelectScheme);
            PlaySoundCommand = new RelayCommand<string>(PlaySound);
            UploadSoundCommand = new RelayCommand(UploadSound);
            DeleteCustomSoundCommand = new RelayCommand<SoundEffectInfo>(DeleteCustomSound);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetToDefaultCommand = new RelayCommand(ResetToDefault);

            InitializeSchemes();
            InitializeBuiltInSounds();
            InitializeEventConfigs();

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var customSounds = new System.Collections.Generic.List<SoundEffectInfo>();
                try
                {
                    if (Directory.Exists(_customSoundsDirectory))
                    {
                        var soundFiles = Directory.GetFiles(_customSoundsDirectory, "*.wav");
                        foreach (var filePath in soundFiles)
                        {
                            var fileInfo = new FileInfo(filePath);
                            customSounds.Add(new SoundEffectInfo
                            {
                                FileName = fileInfo.Name,
                                DisplayName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                                FilePath = filePath,
                                FileSize = fileInfo.Length,
                                IsBuiltIn = false
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[声音方案] 加载自定义音效失败: {ex.Message}");
                }

                _settings.LoadSettings();

                return () =>
                {
                    foreach (var sound in customSounds)
                    {
                        CustomSounds.Add(sound);
                        AvailableSounds.Add(sound);
                    }
                    TM.App.Log($"[声音方案] 加载自定义音效: {CustomSounds.Count}个");

                    var scheme = Schemes.FirstOrDefault(s => s.SchemeId == _settings.ActiveSchemeId);
                    if (scheme != null) SelectScheme(scheme, showToast: false);
                    foreach (var mapping in _settings.EventSoundMappings)
                    {
                        var config = EventConfigs.FirstOrDefault(c => c.EventName == mapping.Key);
                        if (config != null) config.SelectedSound = mapping.Value;
                    }
                };
            }, "SoundScheme");

            TM.App.Log("[声音方案] ViewModel初始化完成");
        }

        #region 属性

        public ObservableCollection<SoundSchemeInfo> Schemes { get; } = new();

        public SoundSchemeInfo? SelectedScheme
        {
            get => _selectedScheme;
            set
            {
                if (_selectedScheme != value)
                {
                    _selectedScheme = value;
                    OnPropertyChanged(nameof(SelectedScheme));
                }
            }
        }

        public ObservableCollection<SoundEffectInfo> AvailableSounds { get; } = new();

        public ObservableCollection<SoundEffectInfo> CustomSounds { get; } = new();

        public SoundEffectInfo? SelectedCustomSound
        {
            get => _selectedCustomSound;
            set
            {
                if (_selectedCustomSound != value)
                {
                    _selectedCustomSound = value;
                    OnPropertyChanged(nameof(SelectedCustomSound));
                }
            }
        }

        public ObservableCollection<EventSoundConfig> EventConfigs { get; } = new();

        #endregion

        #region 命令

        public ICommand SelectSchemeCommand { get; }
        public ICommand PlaySoundCommand { get; }
        public ICommand UploadSoundCommand { get; }
        public ICommand DeleteCustomSoundCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetToDefaultCommand { get; }

        #endregion

        #region 方法

        private void InitializeSchemes()
        {
            Schemes.Add(new SoundSchemeInfo
            {
                SchemeId = "default",
                SchemeName = "默认方案",
                Description = "系统默认声音方案，平衡的音效体验",
                IsBuiltIn = true,
                IsActive = true
            });

            Schemes.Add(new SoundSchemeInfo
            {
                SchemeId = "silent",
                SchemeName = "静音方案",
                Description = "所有通知静音，适合专注工作",
                IsBuiltIn = true,
                IsActive = false
            });

            Schemes.Add(new SoundSchemeInfo
            {
                SchemeId = "minimal",
                SchemeName = "简约方案",
                Description = "简洁的音效，不打扰但有提醒",
                IsBuiltIn = true,
                IsActive = false
            });

            Schemes.Add(new SoundSchemeInfo
            {
                SchemeId = "rich",
                SchemeName = "丰富方案",
                Description = "丰富多样的音效，不同事件有明显区分",
                IsBuiltIn = true,
                IsActive = false
            });

            _selectedScheme = Schemes.FirstOrDefault(s => s.IsActive);
        }

        private void InitializeBuiltInSounds()
        {
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "无", FileName = "none", IsBuiltIn = true });
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "默认提示音", FileName = "default.wav", IsBuiltIn = true });
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "警告音", FileName = "warning.wav", IsBuiltIn = true });
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "错误音", FileName = "error.wav", IsBuiltIn = true });
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "成功音", FileName = "success.wav", IsBuiltIn = true });
            AvailableSounds.Add(new SoundEffectInfo { DisplayName = "信息音", FileName = "info.wav", IsBuiltIn = true });
        }

        private void InitializeEventConfigs()
        {
            EventConfigs.Add(new EventSoundConfig { EventName = "通知到达", SelectedSound = "默认提示音" });
            EventConfigs.Add(new EventSoundConfig { EventName = "警告", SelectedSound = "警告音" });
            EventConfigs.Add(new EventSoundConfig { EventName = "错误", SelectedSound = "错误音" });
            EventConfigs.Add(new EventSoundConfig { EventName = "成功", SelectedSound = "成功音" });
            EventConfigs.Add(new EventSoundConfig { EventName = "信息提示", SelectedSound = "信息音" });
        }

        private void SelectScheme(SoundSchemeInfo? scheme)
        {
            SelectScheme(scheme, showToast: true);
        }

        private void SelectScheme(SoundSchemeInfo? scheme, bool showToast)
        {
            if (scheme == null) return;

            foreach (var s in Schemes)
            {
                s.IsActive = false;
            }

            scheme.IsActive = true;
            SelectedScheme = scheme;

            ApplySchemeSettings(scheme);

            TM.App.Log($"[声音方案] 切换方案: {scheme.SchemeName}");

            if (showToast)
            {
                GlobalToast.Success("声音方案", $"已切换到 {scheme.SchemeName}");
            }
        }

        private void ApplySchemeSettings(SoundSchemeInfo scheme)
        {
            switch (scheme.SchemeId)
            {
                case "silent":
                    foreach (var config in EventConfigs)
                    {
                        config.SelectedSound = "无";
                    }
                    break;
                case "minimal":
                    foreach (var config in EventConfigs)
                    {
                        if (config.EventName == "错误")
                            config.SelectedSound = "警告音";
                        else
                            config.SelectedSound = "默认提示音";
                    }
                    break;
                case "rich":
                    foreach (var config in EventConfigs)
                    {
                        config.SelectedSound = config.EventName switch
                        {
                            "通知到达" => "默认提示音",
                            "警告" => "警告音",
                            "错误" => "错误音",
                            "成功" => "成功音",
                            "信息提示" => "信息音",
                            _ => "默认提示音"
                        };
                    }
                    break;
                default:
                    foreach (var config in EventConfigs)
                    {
                        config.SelectedSound = config.EventName switch
                        {
                            "通知到达" => "默认提示音",
                            "警告" => "警告音",
                            "错误" => "错误音",
                            "成功" => "成功音",
                            "信息提示" => "信息音",
                            _ => "默认提示音"
                        };
                    }
                    break;
            }
        }

        public string? GetSoundPathForEvent(string eventName)
        {
            try
            {
                var eventConfig = EventConfigs.FirstOrDefault(e => e.EventName == eventName);
                if (eventConfig == null || string.IsNullOrEmpty(eventConfig.SelectedSound) || eventConfig.SelectedSound == "无")
                {
                    return null;
                }

                var customSound = CustomSounds.FirstOrDefault(s => s.DisplayName == eventConfig.SelectedSound);
                if (customSound != null && File.Exists(customSound.FilePath))
                {
                    return customSound.FilePath;
                }

                return null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 获取事件音效路径失败: {ex.Message}");
                return null;
            }
        }

        private void PlaySound(string? soundName)
        {
            if (string.IsNullOrEmpty(soundName) || soundName == "无") return;

            try
            {
                switch (soundName)
                {
                    case "默认提示音":
                        System.Media.SystemSounds.Beep.Play();
                        break;
                    case "警告音":
                        System.Media.SystemSounds.Exclamation.Play();
                        break;
                    case "错误音":
                        System.Media.SystemSounds.Hand.Play();
                        break;
                    case "成功音":
                        System.Media.SystemSounds.Asterisk.Play();
                        break;
                    case "信息音":
                        System.Media.SystemSounds.Beep.Play();
                        break;
                    default:
                        var customSound = CustomSounds.FirstOrDefault(s => s.DisplayName == soundName);
                        if (customSound != null && File.Exists(customSound.FilePath))
                        {
                            using var player = new System.Media.SoundPlayer(customSound.FilePath);
                            player.Play();
                        }
                        else
                        {
                            System.Media.SystemSounds.Beep.Play();
                        }
                        break;
                }

                TM.App.Log($"[声音方案] 播放音效: {soundName}");
                GlobalToast.Success("播放音效", $"正在播放: {soundName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 播放音效失败: {ex.Message}");
                StandardDialog.ShowError($"播放音效失败: {ex.Message}", "播放失败");
            }
        }

        private void UploadSound()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "选择音效文件",
                    Filter = "WAV文件|*.wav|所有文件|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string sourceFile = openFileDialog.FileName;
                    string fileName = Path.GetFileName(sourceFile);
                    string targetFile = Path.Combine(_customSoundsDirectory, fileName);

                    if (File.Exists(targetFile))
                    {
                        bool overwrite = StandardDialog.ShowConfirm(
                            $"文件 {fileName} 已存在，是否覆盖？",
                            "确认覆盖"
                        );

                        if (!overwrite) return;
                    }

                    File.Copy(sourceFile, targetFile, true);

                    var fileInfo = new FileInfo(targetFile);
                    var soundInfo = new SoundEffectInfo
                    {
                        FileName = fileName,
                        DisplayName = Path.GetFileNameWithoutExtension(fileName),
                        FilePath = targetFile,
                        FileSize = fileInfo.Length,
                        IsBuiltIn = false
                    };

                    CustomSounds.Add(soundInfo);
                    AvailableSounds.Add(soundInfo);

                    TM.App.Log($"[声音方案] 上传音效: {fileName}");
                    GlobalToast.Success("上传成功", $"音效文件 {fileName} 已添加");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 上传音效失败: {ex.Message}");
                StandardDialog.ShowError($"上传失败: {ex.Message}", "错误");
            }
        }

        private void DeleteCustomSound(SoundEffectInfo? soundInfo)
        {
            if (soundInfo == null || soundInfo.IsBuiltIn)
            {
                GlobalToast.Warning("删除失败", "无法删除内置音效");
                return;
            }

            try
            {
                if (File.Exists(soundInfo.FilePath))
                {
                    File.Delete(soundInfo.FilePath);
                }

                CustomSounds.Remove(soundInfo);
                AvailableSounds.Remove(soundInfo);

                TM.App.Log($"[声音方案] 删除音效: {soundInfo.FileName}");
                GlobalToast.Success("删除成功", "音效文件已删除");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 删除音效失败: {ex.Message}");
                StandardDialog.ShowError($"删除失败: {ex.Message}", "错误");
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.ActiveSchemeId = SelectedScheme?.SchemeId ?? "default";
                _settings.EventSoundMappings = EventConfigs.ToDictionary(
                    config => config.EventName,
                    config => config.SelectedSound
                );
                _settings.CustomSoundFiles = CustomSounds.Select(s => s.FileName).ToList();

                _settings.SaveSettings();

                TM.App.Log("[声音方案] 设置已保存");
                GlobalToast.Success("保存成功", "声音方案设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 保存设置失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败: {ex.Message}", "错误");
            }
        }

        private void LoadSettings()
        {
            try
            {
                _settings.LoadSettings();

                var scheme = Schemes.FirstOrDefault(s => s.SchemeId == _settings.ActiveSchemeId);
                if (scheme != null)
                {
                    SelectScheme(scheme, showToast: false);
                }

                foreach (var mapping in _settings.EventSoundMappings)
                {
                    var config = EventConfigs.FirstOrDefault(c => c.EventName == mapping.Key);
                    if (config != null)
                    {
                        config.SelectedSound = mapping.Value;
                    }
                }

                TM.App.Log("[声音方案] 设置已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[声音方案] 加载设置失败: {ex.Message}");
            }
        }

        private void ResetToDefault()
        {
            _settings.ResetToDefaults();

            LoadSettings();

            TM.App.Log("[声音方案] 设置已重置");
            GlobalToast.Success("重置成功", "已恢复默认声音方案");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

