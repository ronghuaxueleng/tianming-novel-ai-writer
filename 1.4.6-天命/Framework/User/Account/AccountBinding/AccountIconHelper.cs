using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.User.Account.AccountBinding
{
    public static class AccountIconHelper
    {
        private static readonly Dictionary<string, ImageSource?> _iconCache = new();
        private static readonly string _iconBasePath = "Framework/UI/Icons/Functions Icon/";

        public static ImageSource? GetIcon(string iconFileName)
        {
            if (string.IsNullOrEmpty(iconFileName))
                return null;

            if (_iconCache.TryGetValue(iconFileName, out var cachedIcon))
                return cachedIcon;

            try
            {
                var projectRoot = StoragePathHelper.GetProjectRoot();
                var fullPath = Path.Combine(projectRoot, _iconBasePath, iconFileName);

                if (File.Exists(fullPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    _iconCache[iconFileName] = bitmap;
                    return bitmap;
                }
                else
                {
                    _iconCache[iconFileName] = null;
                    TM.App.Log($"[AccountIconHelper] ⚠️ 图标文件不存在: {iconFileName}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountIconHelper] ❌ 加载图标失败: {iconFileName}, 错误: {ex.Message}");
                _iconCache[iconFileName] = null;
                return null;
            }
        }
    }
}
