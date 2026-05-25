using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Appearance.ThemeManagement.ThemeImportExport
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ThemeImportExportViewModel : INotifyPropertyChanged
    {
        private string _statusMessage = "";
        private readonly string _themesPath;
        private readonly string _exportPath;
        private readonly ThemeManager _themeManager;

        private static readonly object _debugLogLock = new object();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new System.Collections.Generic.HashSet<string>();

        private static void DebugLogOnce(string key, string message, Exception ex)
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

            System.Diagnostics.Debug.WriteLine($"[ThemeImportExport] {key}: {message} - {ex.Message}");
        }

        public ThemeImportExportViewModel(ThemeManager themeManager)
        {
            _themeManager = themeManager;
            _themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            StoragePathHelper.EnsureDirectoryExists(_themesPath);

            _exportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "天命", "导出的主题"
            );

            Directory.CreateDirectory(_exportPath);

            ExportCurrentCommand = new RelayCommand(ExportCurrentTheme);
            ExportAllCommand = new RelayCommand(ExportAllThemes);
            ImportThemeCommand = new RelayCommand(ImportTheme);
            OpenExportFolderCommand = new RelayCommand(OpenExportFolder);
            ClearExportListCommand = new RelayCommand(ClearExportList);

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var items = new System.Collections.Generic.List<ExportedThemeItem>();
                try
                {
                    if (Directory.Exists(_exportPath))
                    {
                        var files = Directory.GetFiles(_exportPath, "*.xaml", SearchOption.AllDirectories)
                            .OrderByDescending(f => File.GetCreationTime(f))
                            .Take(20);

                        foreach (var file in files)
                        {
                            var relativePath = Path.GetRelativePath(_exportPath, file);
                            var fileInfo = new FileInfo(file);
                            items.Add(new ExportedThemeItem
                            {
                                FileName = relativePath,
                                ExportTime = fileInfo.CreationTime,
                                FileSize = FormatFileSize(fileInfo.Length),
                                FullPath = file
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("LoadExportedThemes", _exportPath, ex);
                }

                return () =>
                {
                    foreach (var item in items)
                    {
                        ExportedThemes.Add(item);
                    }
                };
            }, "ThemeImportExport.Load");
        }

        #region 属性

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ExportedThemeItem> ExportedThemes { get; } = new();

        #endregion

        #region 命令

        public ICommand ExportCurrentCommand { get; }
        public ICommand ExportAllCommand { get; }
        public ICommand ImportThemeCommand { get; }
        public ICommand OpenExportFolderCommand { get; }
        public ICommand ClearExportListCommand { get; }

        #endregion

        #region 导出功能

        private void ExportCurrentTheme()
        {
            try
            {
                var currentTheme = _themeManager.CurrentTheme;
                var themeFileName = GetThemeFileName(currentTheme);

                if (string.IsNullOrEmpty(themeFileName))
                {
                    ShowError("无法导出当前主题");
                    return;
                }

                var sourcePath = Path.Combine(_themesPath, themeFileName);
                if (!File.Exists(sourcePath))
                {
                    ShowError($"主题文件不存在：{themeFileName}");
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportFileName = $"{Path.GetFileNameWithoutExtension(themeFileName)}_{timestamp}.xaml";
                var exportFilePath = Path.Combine(_exportPath, exportFileName);

                File.Copy(sourcePath, exportFilePath, true);

                AddExportedTheme(exportFileName, currentTheme.ToString());

                ShowSuccess($"✓ 已导出主题：{ThemeManager.GetThemeDisplayName(currentTheme)}");
            }
            catch (Exception ex)
            {
                ShowError($"导出失败：{ex.Message}");
            }
        }

        private void ExportAllThemes()
        {
            try
            {
                if (!Directory.Exists(_themesPath))
                {
                    ShowError("主题目录不存在");
                    return;
                }

                var themeFiles = Directory.GetFiles(_themesPath, "*.xaml");
                if (themeFiles.Length == 0)
                {
                    ShowError("没有找到主题文件");
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportFolderName = $"所有主题_{timestamp}";
                var exportFolder = Path.Combine(_exportPath, exportFolderName);
                Directory.CreateDirectory(exportFolder);

                int successCount = 0;
                foreach (var themeFile in themeFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(themeFile);
                        var destPath = Path.Combine(exportFolder, fileName);
                        File.Copy(themeFile, destPath, true);

                        AddExportedTheme($"{exportFolderName}/{fileName}", "批量导出");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("ExportAllThemes_CopyFile", themeFile, ex);
                    }
                }

                if (successCount > 0)
                {
                    ShowSuccess($"✓ 已导出 {successCount} 个主题到：{exportFolderName}");
                }
                else
                {
                    ShowError("未能导出任何主题");
                }
            }
            catch (Exception ex)
            {
                ShowError($"导出失败：{ex.Message}");
            }
        }

        #endregion

        #region 导入功能

        private void ImportTheme()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择要导入的主题文件",
                    Filter = "主题文件 (*.xaml)|*.xaml|所有文件 (*.*)|*.*",
                    Multiselect = true,
                    InitialDirectory = _exportPath
                };

                if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
                    return;

                int successCount = 0;
                int skipCount = 0;

                foreach (var sourceFile in dialog.FileNames)
                {
                    try
                    {
                        if (!ValidateThemeFile(sourceFile))
                        {
                            skipCount++;
                            continue;
                        }

                        var fileName = Path.GetFileName(sourceFile);
                        var destPath = Path.Combine(_themesPath, fileName);

                        if (File.Exists(destPath))
                        {
                            var result = StandardDialog.ShowConfirm(
                                $"主题 '{fileName}' 已存在，是否覆盖？",
                                "确认覆盖"
                            );

                            if (!result)
                            {
                                skipCount++;
                                continue;
                            }
                        }

                        File.Copy(sourceFile, destPath, true);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("ImportTheme_CopyFile", sourceFile, ex);
                        skipCount++;
                    }
                }

                if (successCount > 0)
                {
                    ShowSuccess($"✓ 已导入 {successCount} 个主题" + 
                               (skipCount > 0 ? $"，跳过 {skipCount} 个" : ""));

                    StandardDialog.ShowInfo(
                        "导入完成",
                        "主题导入成功！\n\n请在\"主题选择\"中刷新列表以查看新主题。"
                    );
                }
                else
                {
                    ShowError($"导入失败，跳过 {skipCount} 个文件");
                }
            }
            catch (Exception ex)
            {
                ShowError($"导入失败：{ex.Message}");
            }
        }

        private bool ValidateThemeFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                var content = File.ReadAllText(filePath);

                if (!content.Contains("<ResourceDictionary") || 
                    !content.Contains("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""))
                {
                    return false;
                }

                var requiredKeys = new[] { "PrimaryColor", "ContentBackground", "TextPrimary" };
                return requiredKeys.Any(key => content.Contains(key));
            }
            catch (Exception ex)
            {
                DebugLogOnce("ValidateThemeFile", filePath, ex);
                return false;
            }
        }

        #endregion

        #region 导出记录管理

        private void AddExportedTheme(string fileName, string themeName)
        {
            var item = new ExportedThemeItem
            {
                FileName = fileName,
                ExportTime = DateTime.Now,
                FileSize = "刚刚导出",
                FullPath = Path.Combine(_exportPath, fileName)
            };

            ExportedThemes.Insert(0, item);

            while (ExportedThemes.Count > 20)
            {
                ExportedThemes.RemoveAt(ExportedThemes.Count - 1);
            }
        }

        private void OpenExportFolder()
        {
            try
            {
                if (!Directory.Exists(_exportPath))
                {
                    Directory.CreateDirectory(_exportPath);
                }

                System.Diagnostics.Process.Start("explorer.exe", _exportPath);
            }
            catch (Exception ex)
            {
                ShowError($"无法打开文件夹：{ex.Message}");
            }
        }

        private void ClearExportList()
        {
            ExportedThemes.Clear();
            StatusMessage = "已清空列表";
        }

        #endregion

        #region 辅助方法

        private string GetThemeFileName(ThemeType theme)
        {
            return theme switch
            {
                ThemeType.Light => "LightTheme.xaml",
                ThemeType.Dark => "DarkTheme.xaml",
                ThemeType.Green => "GreenTheme.xaml",
                ThemeType.Business => "BusinessTheme.xaml",
                ThemeType.ModernBlue => "ModernBlueTheme.xaml",
                ThemeType.Violet => "VioletTheme.xaml",
                ThemeType.WarmOrange => "WarmOrangeTheme.xaml",
                ThemeType.Pink => "PinkTheme.xaml",
                ThemeType.TechCyan => "TechCyanTheme.xaml",
                ThemeType.MinimalBlack => "MinimalBlackTheme.xaml",
                ThemeType.Arctic => "ArcticTheme.xaml",
                ThemeType.Forest => "ForestTheme.xaml",
                ThemeType.Sunset => "SunsetTheme.xaml",
                ThemeType.Morandi => "MorandiTheme.xaml",
                ThemeType.HighContrast => "HighContrastTheme.xaml",
                _ => ""
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ShowSuccess(string message)
        {
            StatusMessage = message;
        }

        private void ShowError(string message)
        {
            StatusMessage = $"❌ {message}";
            StandardDialog.ShowError("错误", message);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    public class ExportedThemeItem
    {
        public string FileName { get; set; } = "";
        public DateTime ExportTime { get; set; }
        public string FileSize { get; set; } = "";
        public string FullPath { get; set; } = "";

        public string DisplayTime => ExportTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

