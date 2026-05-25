using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.Services
{
    public class ProjectManager
    {

        private readonly string _projectsRoot;
        private readonly string _configPath;
        private ProjectConfig? _config;

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

            System.Diagnostics.Debug.WriteLine($"[ProjectManager] {key}: {ex.Message}");
        }

        private const int SwitchThrottleMs = 200;
        private DispatcherTimer? _switchThrottleTimer;
        private string? _pendingSwitchProjectId;
        private CancellationTokenSource? _switchCts;

        private readonly SemaphoreSlim _switchSemaphore = new(1, 1);

        private readonly SemaphoreSlim _saveConfigSemaphore = new(1, 1);

        private readonly Dictionary<string, ProjectCache> _projectCaches = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public event Action<ProjectInfo>? ProjectSwitched;
        public event Action? ProjectListChanged;

        private volatile bool _isConfigLoaded;

        public ProjectManager()
        {
            _projectsRoot = Path.Combine(StoragePathHelper.GetStorageRoot(), "Projects");
            _configPath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Config", "projects.json");

            EnsureDirectoryExists(_projectsRoot);
            _config = new ProjectConfig();
            LoadConfig();
            _isConfigLoaded = true;
            _ = LoadConfigAsync();
        }

        #region 属性

        public ProjectInfo? CurrentProject => _config?.CurrentProject != null 
            ? _config.Projects.Find(p => p.Id == _config.CurrentProject) 
            : null;

        public List<ProjectInfo> Projects => _config?.Projects ?? new List<ProjectInfo>();

        #endregion

        #region 项目操作

        public async Task<ProjectInfo> CreateProjectAsync(string name, string? description = null)
        {
            if (_config?.Projects.Count > 0)
            {
                var existing = _config.Projects.First();
                TM.App.Log($"[ProjectManager] 单书约束：已有项目 '{existing.Name}'，拒绝创建新项目 '{name}'");
                return existing;
            }

            var id = ShortIdGenerator.NewGuid().ToString("N")[..8];

            var safeName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = id;

            var dirName = safeName;
            var dirPath = Path.Combine(_projectsRoot, dirName);
            int suffix = 1;
            while (Directory.Exists(dirPath) || (_config?.Projects.Any(p => string.Equals(p.Path, dirPath, StringComparison.OrdinalIgnoreCase)) == true))
            {
                dirName = $"{safeName}_{suffix++}";
                dirPath = Path.Combine(_projectsRoot, dirName);
            }

            var project = new ProjectInfo
            {
                Id = id,
                Name = name,
                Description = description ?? string.Empty,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Path = dirPath
            };

            EnsureDirectoryExists(project.Path);

            _config ??= new ProjectConfig();
            _config.Projects.Add(project);

            if (_config.CurrentProject == null)
            {
                _config.CurrentProject = project.Id;
                StoragePathHelper.CurrentProjectName = Path.GetFileName(project.Path);
            }

            await SaveConfigAsync();

            TM.App.Log($"[ProjectManager] 创建项目: {name} ({id})");
            ProjectListChanged?.Invoke();

            return project;
        }

        public async Task DeleteProjectAsync(string projectId)
        {
            var project = _config?.Projects.Find(p => p.Id == projectId);
            if (project == null) return;

            if (Directory.Exists(project.Path))
            {
                Directory.Delete(project.Path, true);
            }

            _config?.Projects.Remove(project);

            if (_config?.CurrentProject == projectId)
            {
                var nextProject = _config.Projects.FirstOrDefault();
                _config.CurrentProject = nextProject?.Id;
                if (nextProject != null)
                {
                    StoragePathHelper.CurrentProjectName = Path.GetFileName(nextProject.Path);
                    ServiceLocator.Get<VectorSearchService>().ClearIndex();
                    ProjectSwitched?.Invoke(nextProject);
                }
            }

            await SaveConfigAsync();

            TM.App.Log($"[ProjectManager] 删除项目: {project.Name} ({projectId})");
            ProjectListChanged?.Invoke();
        }

        public async Task RenameProjectAsync(string projectId, string newName)
        {
            var project = _config?.Projects.Find(p => p.Id == projectId);
            if (project == null) return;

            var oldName = project.Name;
            var oldPath = project.Path;
            var newDirName = SanitizeFileName(newName);
            if (string.IsNullOrWhiteSpace(newDirName))
                newDirName = projectId;

            var newPath = Path.Combine(_projectsRoot, newDirName);

            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(oldPath))
            {
                if (Directory.Exists(newPath))
                {
                    TM.App.Log($"[ProjectManager] 重命名目录失败: 目标已存在 {newPath}");
                    return;
                }

                try
                {
                    Directory.Move(oldPath, newPath);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectManager] 重命名目录失败: {ex.Message}");
                    return;
                }

                project.Path = newPath;
            }

            project.Name = newName;
            project.UpdatedAt = DateTime.Now;

            if (_config?.CurrentProject == projectId)
            {
                StoragePathHelper.CurrentProjectName = Path.GetFileName(project.Path);
                ServiceLocator.Get<VectorSearchService>().ClearIndex();
            }

            await SaveConfigAsync();

            TM.App.Log($"[ProjectManager] 重命名项目: {oldName} -> {newName}");
            ProjectListChanged?.Invoke();
        }

        public Task SwitchProjectAsync(string projectId)
        {
            _pendingSwitchProjectId = projectId;

            if (_switchThrottleTimer == null)
            {
                _switchThrottleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(SwitchThrottleMs)
                };
                _switchThrottleTimer.Tick += async (s, e) =>
                {
                    _switchThrottleTimer.Stop();
                    if (_pendingSwitchProjectId != null)
                    {
                        await ExecuteSwitchAsync(_pendingSwitchProjectId);
                        _pendingSwitchProjectId = null;
                    }
                };
            }

            _switchThrottleTimer.Stop();
            _switchThrottleTimer.Start();

            return Task.CompletedTask;
        }

        private async Task ExecuteSwitchAsync(string projectId)
        {
            _switchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _switchCts = cts;

            if (!await _switchSemaphore.WaitAsync(0))
            {
                return;
            }

            try
            {
                var project = _config?.Projects.Find(p => p.Id == projectId);
                if (project == null) return;

                cts.Token.ThrowIfCancellationRequested();

                if (_config != null)
                {
                    _config.CurrentProject = projectId;

                    await Task.Run(async () =>
                    {
                        await SaveConfigAsync();
                    }, cts.Token);
                }

                cts.Token.ThrowIfCancellationRequested();

                UpdateProjectCache(projectId);

                try
                {
                    var guideManager = ServiceLocator.Get<GuideManager>();
                    var flushTask = guideManager.FlushAllAsync();
                    await Task.WhenAny(flushTask, Task.Delay(3000, cts.Token));
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectManager] 切换前Guide刷盘失败（忽略）: {ex.Message}");
                }

                try
                {
                    var chapterPersistence = ServiceLocator.Get<CurrentChapterPersistenceService>();
                    var flushTask = chapterPersistence.FlushPendingAsync();
                    await Task.WhenAny(flushTask, Task.Delay(1000, cts.Token));
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectManager] 切换前章节状态刷盘失败（忽略）: {ex.Message}");
                }

                StoragePathHelper.CurrentProjectName = Path.GetFileName(project.Path);
                ServiceLocator.Get<VectorSearchService>().ClearIndex();

                _ = Task.Run(async () =>
                {
                    try { await ServiceLocator.Get<VectorSearchService>().InitializeAsync(); }
                    catch (Exception ex) { TM.App.Log($"[ProjectManager] 切换后向量索引重建失败: {ex.Message}"); }
                });

                _ = Task.Run(async () =>
                {
                    try { await ServiceLocator.Get<ConsistencyReconciler>().ReconcileAsync(); }
                    catch (Exception ex) { TM.App.Log($"[ProjectManager] 切换后对账失败: {ex.Message}"); }
                });

                ProjectSwitched?.Invoke(project);
            }
            catch (OperationCanceledException ex)
            {
                DebugLogOnce("ExecuteSwitchAsync_Canceled", ex);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectManager] 切换项目失败: {ex.Message}");
            }
            finally
            {
                _switchSemaphore.Release();
            }
        }

        private void UpdateProjectCache(string projectId)
        {
            if (!_projectCaches.ContainsKey(projectId))
            {
                _projectCaches[projectId] = new ProjectCache
                {
                    ProjectId = projectId,
                    LastAccessTime = DateTime.Now
                };
            }
            else
            {
                _projectCaches[projectId].LastAccessTime = DateTime.Now;
            }

            const int maxCacheCount = 5;
            if (_projectCaches.Count > maxCacheCount)
            {
                var oldest = _projectCaches
                    .OrderBy(x => x.Value.LastAccessTime)
                    .First().Key;
                _projectCaches.Remove(oldest);
            }
        }

        public bool IsProjectCached(string projectId)
        {
            return _projectCaches.ContainsKey(projectId);
        }

        public string GetProjectPath(string? projectId = null)
        {
            var id = projectId ?? _config?.CurrentProject;
            if (string.IsNullOrEmpty(id)) return _projectsRoot;

            var project = _config?.Projects.Find(p => p.Id == id);
            return project?.Path ?? _projectsRoot;
        }

        #endregion

        #region 配置管理

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<ProjectConfig>(json, JsonOptions);
                }

                _config ??= new ProjectConfig();

                if (_config.Projects.Count == 0)
                {
                    var defaultProject = new ProjectInfo
                    {
                        Id = "default",
                        Name = "默认项目",
                        Description = "默认创作项目",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        Path = Path.Combine(_projectsRoot, "默认项目")
                    };

                    EnsureDirectoryExists(defaultProject.Path);
                    _config.Projects.Add(defaultProject);
                    _config.CurrentProject = defaultProject.Id;

                    _ = SaveConfigAsync();
                }

                var currentProject = CurrentProject;
                if (currentProject != null && !string.IsNullOrWhiteSpace(currentProject.Path))
                {
                    StoragePathHelper.CurrentProjectName = Path.GetFileName(currentProject.Path);
                    TM.App.Log($"[ProjectManager] 启动时同步当前项目: {currentProject.Name}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectManager] 加载配置失败: {ex.Message}");
                _config = new ProjectConfig();
            }
        }

        private async System.Threading.Tasks.Task LoadConfigAsync()
        {
            try
            {
                if (_isConfigLoaded)
                {
                    return;
                }

                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _config = JsonSerializer.Deserialize<ProjectConfig>(json, JsonOptions);
                }

                _config ??= new ProjectConfig();

                if (_config.Projects.Count == 0)
                {
                    var defaultProject = new ProjectInfo
                    {
                        Id = "default",
                        Name = "默认项目",
                        Description = "默认创作项目",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        Path = Path.Combine(_projectsRoot, "默认项目")
                    };

                    EnsureDirectoryExists(defaultProject.Path);
                    _config.Projects.Add(defaultProject);
                    _config.CurrentProject = defaultProject.Id;

                    await SaveConfigAsync();
                }

                var currentProject = CurrentProject;
                if (currentProject != null && !string.IsNullOrWhiteSpace(currentProject.Path))
                {
                    StoragePathHelper.CurrentProjectName = Path.GetFileName(currentProject.Path);
                    TM.App.Log($"[ProjectManager] 启动时同步当前项目: {currentProject.Name}");
                }

                _isConfigLoaded = true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectManager] 异步加载配置失败: {ex.Message}");
                _config = new ProjectConfig();
            }
        }

        private async Task SaveConfigAsync()
        {
            await _saveConfigSemaphore.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    EnsureDirectoryExists(directory);
                }

                var json = JsonSerializer.Serialize(_config, JsonOptions);
                var tmp = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json);
                File.Move(tmp, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectManager] 保存配置失败: {ex.Message}");
            }
            finally
            {
                _saveConfigSemaphore.Release();
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var result = name.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }
            return result;
        }

        #endregion
    }

    public class ProjectConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("CurrentProject")] public string? CurrentProject { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Projects")] public List<ProjectInfo> Projects { get; set; } = new();
    }

    public class ProjectInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Path")] public string Path { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; }
    }

    public class ProjectCache
    {
        [System.Text.Json.Serialization.JsonPropertyName("ProjectId")] public string ProjectId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastAccessTime")] public DateTime LastAccessTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("CachedData")] public object? CachedData { get; set; }
    }
}
