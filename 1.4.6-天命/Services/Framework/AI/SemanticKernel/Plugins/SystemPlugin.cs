using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Framework.UI.Workspace.Services;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true)]
    public class SystemPlugin
    {
        private readonly PanelCommunicationService _comm = ServiceLocator.Get<PanelCommunicationService>();

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
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SystemPlugin] {key}: {ex.Message}");
        }

        private static bool TryResolveSafePath(string relativePath, out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                error = "路径为空";
                return false;
            }

            if (Path.IsPathRooted(relativePath))
            {
                error = "不允许绝对路径";
                return false;
            }

            var projectRoot = Path.GetFullPath(StoragePathHelper.GetProjectRoot());
            var combined = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            var rootWithSep = projectRoot.EndsWith(Path.DirectorySeparatorChar)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                error = "路径越界";
                return false;
            }

            fullPath = combined;
            return true;
        }

        [KernelFunction("GetCurrentTime")]
        [Description("获取当前系统时间")]
        public Task<string> GetCurrentTimeAsync()
        {
            return Task.FromResult(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        [KernelFunction("GetProjectInfo")]
        [Description("获取当前项目的基本信息")]
        public Task<string> GetProjectInfoAsync()
        {
            TM.App.Log("[SystemPlugin] GetProjectInfo");

            try
            {
                var projectRoot = StoragePathHelper.GetProjectRoot();
                var projectName = Path.GetFileName(projectRoot);

                return Task.FromResult($@"# 项目信息
- 项目名称: {projectName}
- 项目路径: {projectRoot}
- 当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"[获取失败] {ex.Message}");
            }
        }

        [KernelFunction("SaveToFile")]
        [Description("将内容保存到指定文件")]
        public async Task<string> SaveToFileAsync(
            [Description("文件相对路径")] string relativePath,
            [Description("文件内容")] string content)
        {
            TM.App.Log($"[SystemPlugin] SaveToFile: {relativePath}");

            try
            {
                if (!TryResolveSafePath(relativePath, out var fullPath, out var error))
                {
                    return $"[保存失败] {error}";
                }

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tmp = fullPath + ".tmp";
                await File.WriteAllTextAsync(tmp, content).ConfigureAwait(false);
                File.Move(tmp, fullPath, overwrite: true);

                TM.App.Log($"[SystemPlugin] 文件已保存: {fullPath}");
                return $"[已保存] {relativePath}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemPlugin] 保存失败: {ex.Message}");
                return $"[保存失败] {ex.Message}";
            }
        }

        [KernelFunction("ReadFile")]
        [Description("读取指定文件的内容")]
        public async Task<string> ReadFileAsync(
            [Description("文件相对路径")] string relativePath)
        {
            TM.App.Log($"[SystemPlugin] ReadFile: {relativePath}");

            try
            {
                if (!TryResolveSafePath(relativePath, out var fullPath, out var error))
                {
                    return $"[读取失败] {error}";
                }

                if (!File.Exists(fullPath))
                {
                    return $"[文件不存在] {relativePath}";
                }

                var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                return content;
            }
            catch (Exception ex)
            {
                return $"[读取失败] {ex.Message}";
            }
        }

        [KernelFunction("NotifyUser")]
        [Description("向用户显示通知消息")]
        public Task<string> NotifyUserAsync(
            [Description("通知标题")] string title,
            [Description("通知内容")] string message)
        {
            TM.App.Log($"[SystemPlugin] NotifyUser: {title}");

            try
            {
                GlobalToast.Info(title, message);
                return Task.FromResult("[已通知]");
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(NotifyUserAsync), ex);
                return Task.FromResult("[通知失败]");
            }
        }

        [KernelFunction("RefreshChapterList")]
        [Description("刷新左栏的章节列表")]
        public Task<string> RefreshChapterListAsync()
        {
            TM.App.Log("[SystemPlugin] RefreshChapterList");

            try
            {
                _comm.PublishRefreshChapterList();
                return Task.FromResult("[已刷新]");
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(RefreshChapterListAsync), ex);
                return Task.FromResult("[刷新失败]");
            }
        }
    }
}
