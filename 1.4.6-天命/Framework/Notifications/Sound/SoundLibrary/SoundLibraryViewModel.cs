using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.Sound.SoundLibrary
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundLibraryViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<SoundFile> _soundFiles;
        private SoundFile? _selectedSound;
        private string _libraryPath = "Sounds/Library";

        public ObservableCollection<SoundFile> SoundFiles
        {
            get => _soundFiles;
            set { _soundFiles = value; OnPropertyChanged(); }
        }

        public SoundFile? SelectedSound
        {
            get => _selectedSound;
            set { _selectedSound = value; OnPropertyChanged(); }
        }

        public string LibraryPath
        {
            get => _libraryPath;
            set { _libraryPath = value; OnPropertyChanged(); }
        }

        public ICommand ImportSoundCommand { get; }
        public ICommand DeleteSoundCommand { get; }
        public ICommand PlaySoundCommand { get; }
        public ICommand OpenLibraryFolderCommand { get; }

        public SoundLibraryViewModel()
        {
            _soundFiles = new ObservableCollection<SoundFile>();

            ImportSoundCommand = new RelayCommand(ImportSound);
            DeleteSoundCommand = new RelayCommand<SoundFile>(DeleteSound);
            PlaySoundCommand = new RelayCommand<SoundFile>(PlaySound);
            OpenLibraryFolderCommand = new RelayCommand(OpenLibraryFolder);

            LoadSounds();
        }

        private void LoadSounds()
        {
            var sampleSounds = new[]
            {
                new SoundFile { Name = "默认提示音.wav", Size = "128 KB", Duration = "0:02", Category = "系统" },
                new SoundFile { Name = "成功提示.wav", Size = "96 KB", Duration = "0:01", Category = "反馈" },
                new SoundFile { Name = "错误警告.wav", Size = "156 KB", Duration = "0:03", Category = "警告" },
            };

            foreach (var sound in sampleSounds)
            {
                SoundFiles.Add(sound);
            }

            TM.App.Log($"[SoundLibrary] 加载音效库，共 {SoundFiles.Count} 个文件");
        }

        private void ImportSound()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择音效文件",
                Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件 (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var filePath in dialog.FileNames)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var newSound = new SoundFile
                        {
                            Name = fileInfo.Name,
                            Size = FormatFileSize(fileInfo.Length),
                            Duration = "0:00",
                            Category = "自定义"
                        };

                        SoundFiles.Add(newSound);
                        TM.App.Log($"[SoundLibrary] 导入音效: {newSound.Name}");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SoundLibrary] 导入音效失败: {ex.Message}");
                        GlobalToast.Error("导入失败", $"无法导入音效文件：{Path.GetFileName(filePath)}");
                    }
                }

                GlobalToast.Success("导入音效", $"成功导入 {dialog.FileNames.Length} 个音效文件");
            }
        }

        private void DeleteSound(SoundFile? sound)
        {
            if (sound == null) return;

            var result = StandardDialog.ShowConfirm("删除音效", $"确定要删除音效 \"{sound.Name}\" 吗？");
            if (result == true)
            {
                SoundFiles.Remove(sound);
                GlobalToast.Success("删除音效", "音效已从库中移除");
                TM.App.Log($"[SoundLibrary] 删除音效: {sound.Name}");
            }
        }

        private void PlaySound(SoundFile? sound)
        {
            if (sound == null) return;

            GlobalToast.Info("播放音效", $"正在播放：{sound.Name}");
            TM.App.Log($"[SoundLibrary] 播放音效: {sound.Name}");
        }

        private void OpenLibraryFolder()
        {
            var projectRoot = StoragePathHelper.GetProjectRoot();
            var fullPath = Path.Combine(projectRoot, LibraryPath);

            try
            {
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }

                System.Diagnostics.Process.Start("explorer.exe", fullPath);
                TM.App.Log($"[SoundLibrary] 打开音效库文件夹: {fullPath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SoundLibrary] 打开文件夹失败: {ex.Message}");
                GlobalToast.Error("打开失败", "无法打开音效库文件夹");
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundFile : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _size = string.Empty;
        private string _duration = string.Empty;
        private string _category = string.Empty;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(); }
        }

        public string Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
