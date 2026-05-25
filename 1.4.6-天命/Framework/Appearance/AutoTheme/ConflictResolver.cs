using System;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Appearance.AutoTheme.SystemFollow;
using TM.Framework.Appearance.AutoTheme.TimeBased;

namespace TM.Framework.Appearance.AutoTheme
{
    public class ConflictResolver
    {
        private readonly ThemeManager _themeManager;

        public ConflictResolver(ThemeManager themeManager)
        {
            _themeManager = themeManager;
            TM.App.Log("[ConflictResolver] 初始化完成");
        }

        public ThemeType ResolveConflict(
            ThemeRequest? systemFollowRequest,
            ThemeRequest? timeBasedRequest)
        {
            if (systemFollowRequest == null && timeBasedRequest == null)
            {
                return _themeManager.CurrentTheme;
            }

            if (systemFollowRequest == null)
            {
                TM.App.Log($"[ConflictResolver] 无跟随系统请求，应用定时切换: {timeBasedRequest!.RequestedTheme}");
                return timeBasedRequest!.RequestedTheme;
            }

            if (timeBasedRequest == null)
            {
                TM.App.Log($"[ConflictResolver] 无定时切换请求，应用跟随系统: {systemFollowRequest.RequestedTheme}");
                return systemFollowRequest.RequestedTheme;
            }

            if (systemFollowRequest.Priority > timeBasedRequest.Priority)
            {
                TM.App.Log($"[ConflictResolver] 跟随系统优先级更高 ({systemFollowRequest.Priority} > {timeBasedRequest.Priority})，应用: {systemFollowRequest.RequestedTheme}");
                return systemFollowRequest.RequestedTheme;
            }
            else if (timeBasedRequest.Priority > systemFollowRequest.Priority)
            {
                TM.App.Log($"[ConflictResolver] 定时切换优先级更高 ({timeBasedRequest.Priority} > {systemFollowRequest.Priority})，应用: {timeBasedRequest.RequestedTheme}");
                return timeBasedRequest.RequestedTheme;
            }
            else
            {
                TM.App.Log($"[ConflictResolver] 优先级相同 ({systemFollowRequest.Priority})，定时切换优先: {timeBasedRequest.RequestedTheme}");
                return timeBasedRequest.RequestedTheme;
            }
        }

        public ConflictInfo? DetectConflict(
            bool systemFollowEnabled,
            int systemFollowPriority,
            ThemeType systemFollowTheme,
            bool timeBasedEnabled,
            int timeBasedPriority,
            ThemeType timeBasedTheme)
        {
            if (!systemFollowEnabled || !timeBasedEnabled)
            {
                return null;
            }

            if (systemFollowTheme == timeBasedTheme)
            {
                return null;
            }

            var conflict = new ConflictInfo
            {
                HasConflict = true,
                SystemFollowTheme = systemFollowTheme,
                TimeBasedTheme = timeBasedTheme,
                SystemFollowPriority = systemFollowPriority,
                TimeBasedPriority = timeBasedPriority,
                Winner = systemFollowPriority >= timeBasedPriority ? "跟随系统" : "定时切换",
                ResolvedTheme = systemFollowPriority >= timeBasedPriority ? systemFollowTheme : timeBasedTheme
            };

            return conflict;
        }
    }

    public class ThemeRequest
    {
        public ThemeType RequestedTheme { get; set; }

        public int Priority { get; set; }

        public string Source { get; set; } = string.Empty;

        public DateTime RequestTime { get; set; } = DateTime.Now;
    }

    public class ConflictInfo
    {
        public bool HasConflict { get; set; }

        public ThemeType SystemFollowTheme { get; set; }

        public ThemeType TimeBasedTheme { get; set; }

        public int SystemFollowPriority { get; set; }

        public int TimeBasedPriority { get; set; }

        public string Winner { get; set; } = string.Empty;

        public ThemeType ResolvedTheme { get; set; }

        public string Description => $"冲突：跟随系统({SystemFollowTheme}, 优先级{SystemFollowPriority}) vs 定时切换({TimeBasedTheme}, 优先级{TimeBasedPriority}) → {Winner}胜出";
    }
}

