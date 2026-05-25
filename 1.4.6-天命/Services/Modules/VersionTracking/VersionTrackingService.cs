using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.VersionTracking.Models;

namespace TM.Services.Modules.VersionTracking
{
    public class VersionTrackingService
    {
        private string _registryPath;
        private VersionRegistry _registry = new();

        public VersionTrackingService()
        {
            _registryPath = BuildRegistryPath();
            LoadRegistry();
        }

        private static string BuildRegistryPath()
            => Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "version_registry.json");

        private void EnsureCurrentProject()
        {
            var currentPath = BuildRegistryPath();
            if (_registryPath == currentPath) return;
            _registryPath = currentPath;
            _registry = new VersionRegistry();
            LoadRegistry();
            TM.App.Log("[VersionTracking] 检测到项目变化，已重载版本注册表");
        }

        public int GetModuleVersion(string moduleName)
        {
            EnsureCurrentProject();
            return _registry.ModuleVersions.TryGetValue(moduleName, out var v) ? v : 0;
        }

        private System.Threading.Tasks.Task _saveTask = System.Threading.Tasks.Task.CompletedTask;
        private readonly object _saveTaskLock = new object();

        public int IncrementModuleVersion(string moduleName, bool showDownstreamToast = true)
        {
            EnsureCurrentProject();
            if (!_registry.ModuleVersions.ContainsKey(moduleName))
                _registry.ModuleVersions[moduleName] = 0;

            _registry.ModuleVersions[moduleName]++;

            var json = JsonSerializer.Serialize(_registry, JsonHelper.Default);
            var path = _registryPath;

            lock (_saveTaskLock)
            {
                _saveTask = _saveTask.ContinueWith(_ =>
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[VersionTracking] 保存版本注册表失败: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
            }

            TM.App.Log($"[VersionTracking] 模块版本自增: {moduleName} → {_registry.ModuleVersions[moduleName]}");

            if (showDownstreamToast)
            {
                NotifyDownstreamModules(moduleName);
            }

            return _registry.ModuleVersions[moduleName];
        }

        private readonly HashSet<string> _pendingDownstreamNotifications = new();

        public bool SuppressDownstreamToast { get; set; }

        public void FlushPendingDownstreamNotifications()
        {
            var pending = _pendingDownstreamNotifications.ToList();
            _pendingDownstreamNotifications.Clear();
            foreach (var moduleName in pending)
                NotifyDownstreamModules(moduleName);
        }

        private void NotifyDownstreamModules(string moduleName)
        {
            if (SuppressDownstreamToast)
            {
                _pendingDownstreamNotifications.Add(moduleName);
                return;
            }
            var downstream = DependencyConfig.GetDownstreamModules(moduleName);
            if (downstream.Count > 0)
            {
                var displayName = DependencyConfig.GetDisplayName(moduleName);
                var downstreamNames = DependencyConfig.GetDisplayNames(downstream);
                GlobalToast.Info($"{displayName}已更新", $"下游模块({downstreamNames})可能需要重新生成");
                TM.App.Log($"[VersionTracking] 下游影响提示: {moduleName} → {string.Join(", ", downstream)}");
            }
        }

        public Dictionary<string, int> GetDependencySnapshot(string currentModule)
        {
            EnsureCurrentProject();
            var depModules = DependencyConfig.ModuleDependencies
                .GetValueOrDefault(currentModule, Array.Empty<string>());

            var snapshot = depModules.ToDictionary(
                m => m, 
                m => GetModuleVersion(m));

            TM.App.Log($"[VersionTracking] 获取依赖快照: {currentModule} → {snapshot.Count}个依赖");
            return snapshot;
        }

        public List<string> CheckOutdatedDependencies(Dictionary<string, int> savedVersions)
        {
            EnsureCurrentProject();
            if (savedVersions == null || savedVersions.Count == 0)
                return new List<string>();

            var outdated = new List<string>();

            foreach (var kv in savedVersions)
            {
                var currentVersion = GetModuleVersion(kv.Key);
                if (currentVersion > kv.Value)
                {
                    outdated.Add(kv.Key);
                }
            }

            return outdated;
        }

        private void LoadRegistry()
        {
            if (!File.Exists(_registryPath))
            {
                _registry = new VersionRegistry();
                return;
            }

            try
            {
                var json = File.ReadAllText(_registryPath);
                _registry = JsonSerializer.Deserialize<VersionRegistry>(json) 
                    ?? new VersionRegistry();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VersionTracking] 加载版本注册表失败: {ex.Message}");
                _registry = new VersionRegistry();
            }
        }

    }
}
