using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Storage;

namespace TM.Framework.UI.Workspace.Services
{
    public sealed class CurrentChapterPersistenceService
    {
        private const string FileName = "current_chapter.json";
        private const int MaxSaveRetries = 2;

        private Task _saveChain = Task.CompletedTask;
        private readonly object _chainLock = new();

        public CurrentChapterPersistenceService(ProjectManager projectManager)
        {
            CurrentChapterTracker.ChapterChanged += OnChapterChanged;
            projectManager.ProjectSwitched += OnProjectSwitched;
        }

        public Task FlushPendingAsync()
        {
            Task chain;
            lock (_chainLock)
            {
                chain = _saveChain;
            }
            return chain;
        }

        public Task RestoreAsync() => RestoreAsync(StoragePathHelper.CurrentProjectName);

        private async Task RestoreAsync(string projectName)
        {
            try
            {
                var path = GetFilePath(projectName);
                if (!File.Exists(path))
                    return;

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<CurrentChapterRecord>(json);

                if (data == null || string.IsNullOrEmpty(data.ChapterId))
                    return;

                var chaptersPath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Projects", projectName, "Generated", "chapters");
                var chapterFile = Path.Combine(chaptersPath, $"{data.ChapterId}.md");
                if (!File.Exists(chapterFile))
                    return;

                CurrentChapterTracker.SetCurrentChapter(data.ChapterId, data.ChapterTitle);
                TM.App.Log($"[CurrentChapterPersistence] 已恢复: {data.ChapterId} - {data.ChapterTitle}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CurrentChapterPersistence] 恢复失败: {ex.Message}");
            }
        }

        private void OnChapterChanged(object? sender, string chapterId)
        {
            var title = CurrentChapterTracker.CurrentChapterTitle;

            var projectName = StoragePathHelper.CurrentProjectName;

            lock (_chainLock)
            {
                _saveChain = _saveChain.ContinueWith(
                    _ => SaveCoreAsync(projectName, chapterId, title),
                    TaskScheduler.Default).Unwrap();
            }
        }

        private void OnProjectSwitched(ProjectInfo project)
        {
            var projectName = StoragePathHelper.CurrentProjectName;
            CurrentChapterTracker.Clear();
            _ = Task.Run(() => RestoreAsync(projectName));
        }

        private async Task SaveCoreAsync(string projectName, string chapterId, string title)
        {
            if (string.IsNullOrEmpty(chapterId))
                return;

            var data = new CurrentChapterRecord { ChapterId = chapterId, ChapterTitle = title ?? string.Empty };
            var json = JsonSerializer.Serialize(data);
            var path = GetFilePath(projectName);

            for (var attempt = 0; attempt <= MaxSaveRetries; attempt++)
            {
                try
                {
                    var dir = Path.GetDirectoryName(path)!;
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var tmp = path + ".tmp";
                    await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when (attempt < MaxSaveRetries)
                {
                    TM.App.Log($"[CurrentChapterPersistence] 写入重试 {attempt + 1}: {ex.Message}");
                    await Task.Delay(50).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[CurrentChapterPersistence] 写入失败: {ex.Message}");
                }
            }
        }

        private static string GetFilePath(string projectName)
            => Path.Combine(StoragePathHelper.GetStorageRoot(), "Projects", projectName, "Config", FileName);

        private sealed class CurrentChapterRecord
        {
            [JsonPropertyName("ChapterId")]    public string ChapterId    { get; set; } = string.Empty;
            [JsonPropertyName("ChapterTitle")] public string ChapterTitle { get; set; } = string.Empty;
        }
    }
}
