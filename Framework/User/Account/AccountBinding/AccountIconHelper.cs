using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.User.Account.AccountBinding
{
    public static class AccountIconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();

        public static event Action? IconCacheUpdated;

        public static System.Threading.Tasks.Task WarmUpAsync(IEnumerable<string> iconFileNames)
        {
            foreach (var fileName in iconFileNames)
            {
                if (string.IsNullOrEmpty(fileName)) continue;
                _ = GetIcon(fileName);
            }
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => IconCacheUpdated?.Invoke()));
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public static ImageSource? GetIcon(string iconFileName)
        {
            if (string.IsNullOrEmpty(iconFileName))
                return null;

            if (_iconCache.TryGetValue(iconFileName, out var cachedIcon))
                return cachedIcon;

            try
            {
                var escaped = Uri.EscapeDataString(iconFileName);
                var uri = new Uri(
                    $"pack://application:,,,/Framework/UI/Icons/Functions%20Icon/{escaped}",
                    UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                _iconCache[iconFileName] = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountIconHelper] pack URI 加载失败: {iconFileName}, {ex.Message}");
                _iconCache.TryAdd(iconFileName, null);
                return null;
            }
        }
    }
}
