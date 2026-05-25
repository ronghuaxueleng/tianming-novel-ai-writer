using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.SystemSettings.DataBackup.Services;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Framework.SystemSettings.DataBackup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DataBackupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ProjectBackupService _backupService;
        private readonly IGeneratedContentService _contentService;

        private string _chaptersStatusText = "正在统计...";
        private string _backupSizeText = "正在统计...";
        private string _lastBackupText = "尚未备份";
        private bool _isBusy;
        private string _statusMessage = string.Empty;

        public DataBackupViewModel(ProjectBackupService backupService, IGeneratedContentService contentService)
        {
            _backupService = backupService;
            _contentService = contentService;

            ExportChaptersCommand = new AsyncRelayCommand(ExportChaptersAsync, () => !IsBusy);
            ExportFullBackupCommand = new AsyncRelayCommand(ExportFullBackupAsync, () => !IsBusy);
            RestoreFromBackupCommand = new AsyncRelayCommand(RestoreFromBackupAsync, () => !IsBusy);
            RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);

            RefreshStatusAsync().SafeFireAndForget(ex => TM.App.Log($"[DataBackupViewModel] 初始化失败: {ex.Message}"));
        }

        #region 属性

        public string ChaptersStatusText
        {
            get => _chaptersStatusText;
            set { if (_chaptersStatusText != value) { _chaptersStatusText = value; OnPropertyChanged(); } }
        }

        public string BackupSizeText
        {
            get => _backupSizeText;
            set { if (_backupSizeText != value) { _backupSizeText = value; OnPropertyChanged(); } }
        }

        public string LastBackupText
        {
            get => _lastBackupText;
            set { if (_lastBackupText != value) { _lastBackupText = value; OnPropertyChanged(); } }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotBusy));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsNotBusy => !_isBusy;

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        #endregion

        #region 命令

        public ICommand ExportChaptersCommand { get; }
        public ICommand ExportFullBackupCommand { get; }
        public ICommand RestoreFromBackupCommand { get; }
        public ICommand RefreshStatusCommand { get; }

        #endregion

        #region 命令实现 - 章节导出

        private async Task ExportChaptersAsync()
        {
            try
            {
                var defaultName = $"{StoragePathHelper.CurrentProjectName}_章节_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                var dialog = new SaveFileDialog
                {
                    Title = "导出章节",
                    Filter = "ZIP 压缩包 (*.zip)|*.zip",
                    FileName = defaultName,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                if (dialog.ShowDialog() != true)
                    return;

                IsBusy = true;
                StatusMessage = "正在导出章节...";

                var result = await _backupService.ExportChaptersAsync(dialog.FileName);
                if (result.Success)
                {
                    GlobalToast.Success("导出成功",
                        $"已导出到 {Path.GetFileName(result.OutputPath)}（{ProjectBackupService.FormatSize(result.FileSizeBytes)}）");
                    StatusMessage = result.Message;
                }
                else
                {
                    GlobalToast.Error("导出失败", result.Message);
                    StatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 章节导出异常: {ex.Message}");
                GlobalToast.Error("导出失败", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 命令实现 - 数据备份

        private async Task ExportFullBackupAsync()
        {
            try
            {
                var defaultName = $"{StoragePathHelper.CurrentProjectName}_备份_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                var dialog = new SaveFileDialog
                {
                    Title = "保存项目备份",
                    Filter = "ZIP 压缩包 (*.zip)|*.zip",
                    FileName = defaultName,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                if (dialog.ShowDialog() != true)
                    return;

                IsBusy = true;
                StatusMessage = "正在打包项目数据，请稍候...";

                var result = await _backupService.ExportFullBackupAsync(dialog.FileName);
                if (result.Success)
                {
                    GlobalToast.Success("备份成功",
                        $"已保存到 {Path.GetFileName(result.OutputPath)}（{ProjectBackupService.FormatSize(result.FileSizeBytes)}）");
                    StatusMessage = result.Message;

                    SaveLastBackupRecord(result.OutputPath, result.FileSizeBytes);
                    UpdateLastBackupText();
                }
                else
                {
                    GlobalToast.Error("备份失败", result.Message);
                    StatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 数据备份异常: {ex.Message}");
                GlobalToast.Error("备份失败", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 命令实现 - 数据恢复

        private async Task RestoreFromBackupAsync()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择备份文件",
                    Filter = "ZIP 压缩包 (*.zip)|*.zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                if (dialog.ShowDialog() != true)
                    return;

                StatusMessage = "正在校验备份文件...";
                var validation = await _backupService.ValidateBackupAsync(dialog.FileName);
                if (!validation.IsValid)
                {
                    StandardDialog.ShowWarning(validation.Message, "无效的备份文件");
                    StatusMessage = validation.Message;
                    return;
                }

                var manifest = validation.Manifest!;

                if (!string.Equals(manifest.ProjectName, StoragePathHelper.CurrentProjectName, StringComparison.Ordinal))
                {
                    var mismatchMsg =
                        $"⚠ 项目名不一致：\n\n" +
                        $"• 备份项目名：{manifest.ProjectName}\n" +
                        $"• 当前项目名：{StoragePathHelper.CurrentProjectName}\n\n" +
                        $"恢复后会把备份的「{manifest.ProjectName}」数据写入到当前项目目录中。\n\n" +
                        $"是否继续？";
                    if (!StandardDialog.ShowConfirm(mismatchMsg, "项目名不匹配"))
                        return;
                }

                var confirmMsg =
                    $"将从备份恢复全部数据（设计/创作/项目）：\n\n" +
                    $"• 项目名：{manifest.ProjectName}\n" +
                    $"• 备份时间：{manifest.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
                    $"• 应用版本：{manifest.AppVersion}\n" +
                    $"• 范围数：{manifest.Scopes.Count} 项\n\n" +
                    $"为了避免文件被占用导致恢复失败，将采用以下安全流程：\n" +
                    $"  1. 记录恢复任务（不会立即修改任何数据）\n" +
                    $"  2. 应用安全关闭（自动保存当前编辑）\n" +
                    $"  3. 您手动重新打开应用\n" +
                    $"  4. 启动时自动完成恢复，并加载新数据\n\n" +
                    $"⚠️ 当前的设计、创作、项目数据将被覆盖！\n" +
                    $"恢复期间会自动保存到 Storage/_backup_safety/，可手动还原。\n\n" +
                    $"是否继续？";
                if (!StandardDialog.ShowConfirm(confirmMsg, "确认恢复"))
                    return;

                if (!StandardDialog.ShowConfirm(
                    "最终确认\n\n这将永久覆盖当前项目数据，应用即将关闭。\n请再次确认是否继续？",
                    "危险操作"))
                    return;

                IsBusy = true;
                StatusMessage = "正在安排恢复任务...";

                var schedule = await _backupService.SchedulePendingRestoreAsync(dialog.FileName);
                if (!schedule.Success)
                {
                    GlobalToast.Error("安排恢复任务失败", schedule.Message);
                    StandardDialog.ShowError(schedule.Message, "安排失败");
                    StatusMessage = schedule.Message;
                    return;
                }

                StatusMessage = "恢复任务已安排，应用即将关闭";

                StandardDialog.ShowInfo(
                    "恢复任务已安排。\n\n" +
                    "应用即将关闭，请手动重新打开。\n" +
                    "启动时将自动完成数据恢复，无需任何额外操作。",
                    "准备就绪 - 请重启应用");

                TM.App.Log("[DataBackupViewModel] 已安排延迟恢复，触发应用关闭");
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 数据恢复异常: {ex.Message}");
                GlobalToast.Error("恢复失败", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 状态信息

        private async Task RefreshStatusAsync()
        {
            try
            {
                var chapters = await _contentService.GetGeneratedChaptersAsync();
                var chaptersSize = await _backupService.CalculateChaptersSizeAsync();
                if (chapters.Count == 0)
                {
                    ChaptersStatusText = "暂无章节";
                }
                else
                {
                    var totalWords = 0;
                    foreach (var c in chapters) totalWords += c.WordCount;
                    ChaptersStatusText = $"{chapters.Count} 章 / {totalWords:N0} 字 / {ProjectBackupService.FormatSize(chaptersSize)}";
                }

                var projectSize = await _backupService.CalculateProjectSizeAsync();
                BackupSizeText = projectSize > 0
                    ? $"约 {ProjectBackupService.FormatSize(projectSize)}"
                    : "项目无数据";

                UpdateLastBackupText();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 刷新状态失败: {ex.Message}");
                ChaptersStatusText = "统计失败";
                BackupSizeText = "统计失败";
            }
        }

        private void UpdateLastBackupText()
        {
            try
            {
                var record = LoadLastBackupRecord();
                if (record == null)
                {
                    LastBackupText = "尚未备份";
                    return;
                }

                var localTime = record.BackupTimeUtc.ToLocalTime();
                var diff = DateTime.Now - localTime;
                string ago;
                if (diff.TotalMinutes < 1) ago = "刚刚";
                else if (diff.TotalHours < 1) ago = $"{(int)diff.TotalMinutes} 分钟前";
                else if (diff.TotalDays < 1) ago = $"{(int)diff.TotalHours} 小时前";
                else if (diff.TotalDays < 30) ago = $"{(int)diff.TotalDays} 天前";
                else ago = "较久之前";

                LastBackupText = $"{localTime:yyyy-MM-dd HH:mm}（{ago}）";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 读取备份记录失败: {ex.Message}");
                LastBackupText = "尚未备份";
            }
        }

        private static string GetLastBackupRecordPath()
            => StoragePathHelper.GetFilePath("Framework", "SystemSettings/DataBackup", "last_backup.json");

        private static LastBackupRecord? LoadLastBackupRecord()
        {
            var path = GetLastBackupRecordPath();
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JsonHelper.TryDeserialize<LastBackupRecord>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveLastBackupRecord(string? backupFilePath, long fileSize)
        {
            try
            {
                var record = new LastBackupRecord
                {
                    BackupTimeUtc = DateTime.UtcNow,
                    BackupFilePath = backupFilePath ?? string.Empty,
                    FileSizeBytes = fileSize,
                    ProjectName = StoragePathHelper.CurrentProjectName
                };
                var path = GetLastBackupRecordPath();
                File.WriteAllText(path, JsonSerializer.Serialize(record, JsonHelper.CnDefault));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataBackupViewModel] 保存备份记录失败: {ex.Message}");
            }
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class LastBackupRecord
    {
        public DateTime BackupTimeUtc { get; set; }
        public string BackupFilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ProjectName { get; set; } = string.Empty;
    }
}
