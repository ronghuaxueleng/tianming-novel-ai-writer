using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    internal static class AppIconLoader
    {
        private static readonly ConcurrentDictionary<int, Lazy<ImageSource?>> _cache = new();

        internal static void Load(Border iconBorder, int targetSize, FrameworkElement? fallbackElement = null, string logTag = "AppIconLoader")
        {
            iconBorder.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                try
                {
                    var source = GetOrDecode(targetSize);
                    if (source == null)
                    {
                        iconBorder.Background = null;
                        iconBorder.Visibility = Visibility.Collapsed;
                        if (fallbackElement != null) fallbackElement.Visibility = Visibility.Visible;
                        return;
                    }

                    var brush = new ImageBrush(source)
                    {
                        Stretch = Stretch.UniformToFill,
                        Viewbox = new Rect(0.05, 0.05, 0.90, 0.90)
                    };
                    if (brush.CanFreeze) brush.Freeze();
                    iconBorder.Background = brush;
                    iconBorder.Visibility = Visibility.Visible;
                    if (fallbackElement != null) fallbackElement.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{logTag}] 加载应用图标失败: {ex.Message}");
                    iconBorder.Background = null;
                    iconBorder.Visibility = Visibility.Collapsed;
                    if (fallbackElement != null) fallbackElement.Visibility = Visibility.Visible;
                }
            });
        }

        private static ImageSource? GetOrDecode(int targetSize)
        {
            var lazy = _cache.GetOrAdd(targetSize, size => new Lazy<ImageSource?>(() =>
            {
                try
                {
                    var uri = new Uri("pack://application:,,,/Framework/UI/Icons/app.ico", UriKind.Absolute);
                    var resourceInfo = System.Windows.Application.GetResourceStream(uri);
                    if (resourceInfo == null) return null;

                    using var stream = resourceInfo.Stream;
                    var decoder = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var best = decoder.Frames
                        .OrderBy(f => Math.Abs(f.PixelWidth - size))
                        .ThenByDescending(f => f.PixelWidth)
                        .FirstOrDefault();
                    if (best?.CanFreeze == true) best.Freeze();
                    return (ImageSource?)best;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AppIconLoader] pack URI 加载 app.ico 失败: {ex.Message}");
                    return null;
                }
            }));
            return lazy.Value;
        }
    }
}
