using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;

namespace TM.Framework.Appearance.ThemeManagement.ThemeImportExport
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
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

            ExportCurrentCommand = new AsyncRelayCommand(ExportCurrentTheme);
            ExportAllCommand = new AsyncRelayCommand(ExportAllThemes);
            ImportThemeCommand = new AsyncRelayCommand(ImportTheme);
            OpenExportFolderCommand = new RelayCommand(OpenExportFolder);
            ClearExportListCommand = new RelayCommand(ClearExportList);

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                if (!Directory.Exists(_exportPath))
                    Directory.CreateDirectory(_exportPath);

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

        private async System.Threading.Tasks.Task ExportCurrentTheme()
        {
            try
            {
                var currentTheme = _themeManager.CurrentTheme;
                var themeFileName = GetThemeFileName(currentTheme);

                if (string.IsNullOrEmpty(themeFileName)) { ShowError("无法导出当前主题"); return; }

                var sourcePath = Path.Combine(_themesPath, themeFileName);
                if (!File.Exists(sourcePath)) { ShowError($"主题文件不存在：{themeFileName}"); return; }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportFileName = $"{Path.GetFileNameWithoutExtension(themeFileName)}_{timestamp}.xaml";
                var exportFilePath = Path.Combine(_exportPath, exportFileName);

                await using var src = File.OpenRead(sourcePath);
                await using var dst = File.Create(exportFilePath);
                await src.CopyToAsync(dst).ConfigureAwait(true);

                AddExportedTheme(exportFileName, currentTheme.ToString());
                ShowSuccess($"✓ 已导出主题：{ThemeManager.GetThemeDisplayName(currentTheme)}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeImportExport] 导出当前主题失败: {ex.Message}");
                ShowError($"导出失败：{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ExportAllThemes()
        {
            try
            {
                if (!Directory.Exists(_themesPath)) { ShowError("主题目录不存在"); return; }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var exportFolderName = $"所有主题_{timestamp}";
                var exportFolder = Path.Combine(_exportPath, exportFolderName);

                var exportedNames = new System.Collections.Generic.List<string>();
                int successCount = await System.Threading.Tasks.Task.Run(() =>
                {
                    var themeFiles = Directory.GetFiles(_themesPath, "*.xaml");
                    if (themeFiles.Length == 0) return -1;
                    Directory.CreateDirectory(exportFolder);
                    int success = 0;
                    foreach (var themeFile in themeFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileName(themeFile);
                            File.Copy(themeFile, Path.Combine(exportFolder, fileName), true);
                            exportedNames.Add($"{exportFolderName}/{fileName}");
                            success++;
                        }
                        catch (Exception ex) { DebugLogOnce("ExportAllThemes_CopyFile", themeFile, ex); }
                    }
                    return success;
                }).ConfigureAwait(true);
                if (successCount == -1) { ShowError("没有找到主题文件"); return; }

                foreach (var n in exportedNames) AddExportedTheme(n, "批量导出");

                if (successCount > 0) ShowSuccess($"✓ 已导出 {successCount} 个主题到：{exportFolderName}");
                else ShowError("未能导出任何主题");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeImportExport] 批量导出主题失败: {ex.Message}");
                ShowError($"批量导出失败：{ex.Message}");
            }
        }

        #endregion

        #region 导入功能

        private async System.Threading.Tasks.Task ImportTheme()
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

                if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

                var allFiles = dialog.FileNames;

                var validFiles = await System.Threading.Tasks.Task.Run(() =>
                    System.Linq.Enumerable.Where(allFiles, ValidateThemeFile).ToArray()
                ).ConfigureAwait(true);

                int skipCount = allFiles.Length - validFiles.Length;

                var filesToCopy = new System.Collections.Generic.List<(string src, string dest)>();
                foreach (var sourceFile in validFiles)
                {
                    var fileName = Path.GetFileName(sourceFile);
                    var destPath = Path.Combine(_themesPath, fileName);
                    if (File.Exists(destPath))
                    {
                        if (!StandardDialog.ShowConfirm($"主题 '{fileName}' 已存在，是否覆盖？", "确认覆盖"))
                        { skipCount++; continue; }
                    }
                    filesToCopy.Add((sourceFile, destPath));
                }

                if (filesToCopy.Count == 0) { if (skipCount > 0) ShowError($"导入失败，跳过 {skipCount} 个文件"); return; }

                var successCount = await System.Threading.Tasks.Task.Run(() =>
                {
                    int success = 0;
                    foreach (var (src, dest) in filesToCopy)
                    {
                        try { File.Copy(src, dest, true); success++; }
                        catch (Exception ex) { DebugLogOnce("ImportTheme_CopyFile", src, ex); skipCount++; }
                    }
                    return success;
                }).ConfigureAwait(true);

                if (successCount > 0)
                {
                    ShowSuccess($"✓ 已导入 {successCount} 个主题" + (skipCount > 0 ? $"，跳过 {skipCount} 个" : ""));
                    StandardDialog.ShowInfo("导入完成", "主题导入成功！\n\n请在\"主题选择\"中刷新列表以查看新主题。");
                }
                else { ShowError($"导入失败，跳过 {skipCount} 个文件"); }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ThemeImportExport] 导入主题失败: {ex.Message}");
                ShowError($"导入失败：{ex.Message}");
            }
        }

        #endregion

        private static bool ValidateThemeFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var buffer = new char[2048];
                using var reader = new StreamReader(filePath);
                var charsRead = reader.Read(buffer, 0, buffer.Length);
                var header = new string(buffer, 0, charsRead);

                if (!header.Contains("<ResourceDictionary") ||
                    !header.Contains("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""))
                    return false;

                var requiredKeys = new[] { "PrimaryColor", "ContentBackground", "TextPrimary" };
                return requiredKeys.Any(key => header.Contains(key));
            }
            catch (Exception ex)
            {
                DebugLogOnce("ValidateThemeFile", filePath, ex);
                return false;
            }
        }

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
                TM.App.Log($"[ThemeImportExport] 打开导出目录失败: {ex.Message}");
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
            StatusMessage = $"✗ {message}";
            StandardDialog.ShowError(message, "错误");
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

