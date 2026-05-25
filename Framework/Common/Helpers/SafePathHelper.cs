using System;
using System.IO;

namespace TM.Framework.Common.Helpers
{
    public static class SafePathHelper
    {
        private static readonly string[] BusinessProtectedPrefixes =
        {
            Path.Combine("Generated", "chapters"),
            "Config",
        };

        public static bool TryResolveSafePath(
            string relativePath,
            out string fullPath,
            out string error,
            bool allowBusinessPaths = false)
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

            var projectRoot = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
            var combined = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            var rootWithSep = projectRoot.EndsWith(Path.DirectorySeparatorChar)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                error = "路径越界";
                return false;
            }

            if (!allowBusinessPaths)
            {
                var relativeFromRoot = combined.Substring(rootWithSep.Length);
                foreach (var prefix in BusinessProtectedPrefixes)
                {
                    if (relativeFromRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"此目录由专用编辑器管理（{prefix}），请使用 ContentEditPlugin 或 DataEditPlugin";
                        return false;
                    }
                }
            }

            fullPath = combined;
            return true;
        }

        public static bool TryResolveSafePath(string relativePath, out string fullPath, out string error)
        {
            return TryResolveSafePath(relativePath, out fullPath, out error, allowBusinessPaths: false);
        }
    }
}
