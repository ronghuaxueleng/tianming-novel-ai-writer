using System;
using System.IO;
using System.Windows.Threading;

namespace TM.Framework.UI.Workspace.Services
{
    public class ChapterFileWatcher : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _debounceTimer;
        private bool _disposed;

        public event EventHandler? FilesChanged;

        public ChapterFileWatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;

            ResetWatcher();

            try
            {
                StoragePathHelper.CurrentProjectChanged += OnCurrentProjectChanged;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterFileWatcher] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private void OnCurrentProjectChanged(string oldProject, string newProject)
        {
            try
            {
                ResetWatcher();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterFileWatcher] 切换监听目录失败: {ex.Message}");
            }
        }

        private void ResetWatcher()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFileChanged;
                    _watcher.Changed -= OnFileChanged;
                    _watcher.Deleted -= OnFileChanged;
                    _watcher.Renamed -= OnFileRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                }
            }
            catch { }

            var chaptersDir = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersDir))
                Directory.CreateDirectory(chaptersDir);

            try
            {
                _watcher = new FileSystemWatcher(chaptersDir)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.CreationTime,
                    Filter = "*.md",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileChanged;
                _watcher.Changed += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;

                TM.App.Log($"[ChapterFileWatcher] 开始监听: {chaptersDir}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterFileWatcher] 初始化失败: {ex.Message}");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            TM.App.Log($"[ChapterFileWatcher] 文件变化: {e.ChangeType} - {e.Name}");
            TriggerDebounce();
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            TM.App.Log($"[ChapterFileWatcher] 文件重命名: {e.OldName} -> {e.Name}");
            TriggerDebounce();
        }

        private void TriggerDebounce()
        {
            _dispatcher.BeginInvoke(() =>
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            });
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            FilesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }
        }

        public void Resume()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                StoragePathHelper.CurrentProjectChanged -= OnCurrentProjectChanged;
            }
            catch { }

            _debounceTimer.Stop();

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileChanged;
                _watcher.Changed -= OnFileChanged;
                _watcher.Deleted -= OnFileChanged;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Dispose();
            }

            TM.App.Log("[ChapterFileWatcher] 已释放");
        }
    }
}
