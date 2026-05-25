using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    public class SceneDetector
    {
        private static readonly object _debugLogLock = new object();
        private static readonly HashSet<string> _debugLoggedKeys = new HashSet<string>();

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

            System.Diagnostics.Debug.WriteLine($"[SceneDetector] {key}: {ex.Message}");
        }

        public SceneDetector()
        {
            TM.App.Log("[SceneDetector] 初始化完成");
        }

        public DetectedScene DetectCurrentScene(List<SceneRule> rules)
        {
            if (rules == null || rules.Count == 0)
            {
                return new DetectedScene
                {
                    SceneName = "默认",
                    IsActive = false
                };
            }

            var now = DateTime.Now.TimeOfDay;

            foreach (var rule in rules.Where(r => r.Enabled))
            {
                if (IsTimeInRange(now, rule.StartTime, rule.EndTime))
                {
                    TM.App.Log($"[SceneDetector] 检测到场景: {rule.SceneName}");

                    return new DetectedScene
                    {
                        SceneName = rule.SceneName,
                        IsActive = true,
                        DisableSwitching = rule.DisableSwitching,
                        Description = rule.Description,
                        StartTime = rule.StartTime,
                        EndTime = rule.EndTime
                    };
                }
            }

            return new DetectedScene
            {
                SceneName = "默认",
                IsActive = false
            };
        }

        private bool IsTimeInRange(TimeSpan current, TimeSpan start, TimeSpan end)
        {
            if (start <= end)
            {
                return current >= start && current <= end;
            }
            else
            {
                return current >= start || current <= end;
            }
        }

        public List<string> GetRunningApplications()
        {
            var apps = new List<string>();

            try
            {
                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            apps.Add(process.ProcessName);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogOnce("GetRunningApplications_ProcessAccess", ex);
                    }
                }

                TM.App.Log($"[SceneDetector] 检测到 {apps.Count} 个运行中的应用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SceneDetector] 检测应用失败: {ex.Message}");
            }

            return apps.Distinct().ToList();
        }

        public string SmartDetectScene()
        {
            var apps = GetRunningApplications();

            var workApps = new[] { "WINWORD", "EXCEL", "POWERPNT", "Code", "devenv", "Rider" };
            if (apps.Any(app => workApps.Any(work => app.Contains(work))))
            {
                return "工作中";
            }

            var entertainmentApps = new[] { "spotify", "netflix", "vlc", "potplayer" };
            if (apps.Any(app => entertainmentApps.Any(ent => app.ToLower().Contains(ent))))
            {
                return "娱乐中";
            }

            if (apps.Any(app => app.Contains("POWERPNT") || app.Contains("Zoom") || app.Contains("Teams")))
            {
                return "演示中";
            }

            return "默认";
        }
    }

    public class DetectedScene
    {
        public string SceneName { get; set; } = "默认";

        public bool IsActive { get; set; }

        public bool DisableSwitching { get; set; }

        public string Description { get; set; } = string.Empty;

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }
    }
}

