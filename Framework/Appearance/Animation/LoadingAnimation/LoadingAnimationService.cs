using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    public class LoadingAnimationService
    {
        private Window? _loadingWindow;
        private volatile LoadingAnimationSettings _settings;
        private int _settingsVersion;
        private DateTime _showStartTime;
        private bool _isShowing;
        private ProgressBar? _progressBar;
        private TextBlock? _percentText;
        private TextBlock? _loadingText;
        private SolidColorBrush? _cachedOverlayBrush;
        private SolidColorBrush? _cachedTextBrush;
        private string? _cachedOverlayColorKey;
        private string? _cachedTextColorKey;
        private BlurEffect? _cachedBlurEffect;
        private int _cachedBlurRadius = -1;

        private SolidColorBrush GetOrCreateBrush(ref SolidColorBrush? cache, ref string? cacheKey, string colorStr)
        {
            if (cache != null && cacheKey == colorStr) return cache;
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
            b.Freeze();
            cache = b;
            cacheKey = colorStr;
            return b;
        }

        public LoadingAnimationService()
        {
            _settings = new LoadingAnimationSettings();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var loadVersion = Volatile.Read(ref _settingsVersion);
                try
                {
                    var loaded = await LoadSettingsAsync().ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _settingsVersion))
                        return;
                    _settings = loaded;
                }
                catch (Exception ex) { TM.App.Log($"[LoadingAnimation] 加载设置失败: {ex.Message}"); }
            });
        }

        public void Show(string? message = null, Window? owner = null)
        {
            try
            {
                if (_isShowing)
                {
                    TM.App.Log("[LoadingAnimation] 加载指示器已在显示中");
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _isShowing = true;
                    dispatcher.BeginInvoke(() =>
                    {
                        _showStartTime = DateTime.Now;

                        if (_loadingWindow == null)
                        {
                            _loadingWindow = CreateLoadingWindow(owner);
                            ApplySettings(_loadingWindow, message);
                        }
                        else
                        {
                            RepositionWindow(_loadingWindow, owner);
                            if (_loadingText != null)
                                _loadingText.Text = message ?? _settings.LoadingText;
                        }

                        _loadingWindow.Show();

                        TM.App.Log($"[LoadingAnimation] 显示加载指示器: {message ?? _settings.LoadingText}");
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 显示加载指示器失败: {ex.Message}");
            }
        }

        public void UpdateProgress(double percentage, string? message = null)
        {
            if (!_isShowing || _loadingWindow == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        percentage = Math.Max(0, Math.Min(100, percentage));

                        if (_progressBar != null)
                        {
                            _progressBar.Value = percentage;
                        }

                        if (_settings.ShowPercentage && _percentText != null)
                        {
                            _percentText.Text = $"{percentage:F1}%";
                        }

                        if (!string.IsNullOrEmpty(message) && _loadingText != null)
                        {
                            _loadingText.Text = message;
                        }

                        TM.App.Log($"[LoadingAnimation] 更新进度: {percentage:F1}% - {message}");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[LoadingAnimation] 更新进度失败: {ex.Message}");
                    }
                });
            }
        }

        private T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T? foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T tChild && (child as FrameworkElement)?.Name == childName)
                {
                    foundChild = tChild;
                    break;
                }

                foundChild = FindChild<T>(child, childName);
                if (foundChild != null) break;
            }

            return foundChild;
        }

        public void Hide()
        {
            if (!_isShowing) return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        var displayDuration = (DateTime.Now - _showStartTime).TotalMilliseconds;
                        var minTime = _settings.MinDisplayTime;

                        if (displayDuration < minTime)
                        {
                            var delay = (int)(minTime - displayDuration);
                            _ = Task.Delay(delay).ContinueWith(_ =>
                            {
                                try
                                {
                                    Application.Current?.Dispatcher.BeginInvoke(() => CloseLoadingWindow());
                                }
                                catch (Exception ex) { TM.App.Log($"[LoadingAnimation] 延迟关闭失败: {ex.Message}"); }
                            });
                        }
                        else
                        {
                            CloseLoadingWindow();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 隐藏加载指示器失败: {ex.Message}");
            }
        }

        public async Task<T> ExecuteWithLoading<T>(Func<Task<T>> action, string? message = null, Window? owner = null)
        {
            try
            {
                var delayTask = Task.Delay(_settings.DelayTime);
                var actionTask = action();

                var completedTask = await Task.WhenAny(delayTask, actionTask);

                if (completedTask == delayTask)
                {
                    Show(message, owner);
                }

                var result = await actionTask;

                Hide();

                return result;
            }
            catch (Exception ex)
            {
                Hide();
                TM.App.Log($"[LoadingAnimation] 执行操作失败: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task<LoadingAnimationSettings> LoadSettingsAsync()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/LoadingAnimation",
                    "settings.json"
                );

                if (File.Exists(settingsFile))
                {
                    var json = await File.ReadAllTextAsync(settingsFile).ConfigureAwait(false);
                    var settings = JsonSerializer.Deserialize<LoadingAnimationSettings>(json);
                    if (settings != null)
                    {
                        TM.App.Log($"[LoadingAnimation] 配置加载成功: {settings.AnimationType}");
                        return settings;
                    }
                }

                TM.App.Log("[LoadingAnimation] 使用默认配置");
                return LoadingAnimationSettings.CreateDefault();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoadingAnimation] 加载配置失败: {ex.Message}");
                return LoadingAnimationSettings.CreateDefault();
            }
        }

        public void ReloadSettings()
        {
            var loadVersion = Volatile.Read(ref _settingsVersion);
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var loaded = await LoadSettingsAsync().ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _settingsVersion))
                        return;
                    _settings = loaded;
                    TM.App.Log("[LoadingAnimation] 配置已重新加载");
                }
                catch (Exception ex) { TM.App.Log($"[LoadingAnimation] 重载失败: {ex.Message}"); }
            });
        }

        public void UpdateSettings(LoadingAnimationSettings settings)
        {
            Interlocked.Increment(ref _settingsVersion);
            _settings = settings.Clone();
            TM.App.Log("[LoadingAnimation] 配置已从内存更新");
        }

        private Window CreateLoadingWindow(Window? owner)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Topmost = true
            };

            StandardDialog.EnsureOwnerAndTopmost(window, owner);

            var resolvedOwner = window.Owner;
            if (resolvedOwner != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = resolvedOwner.Left;
                window.Top = resolvedOwner.Top;
                window.Width = resolvedOwner.ActualWidth > 0 ? resolvedOwner.ActualWidth : resolvedOwner.Width;
                window.Height = resolvedOwner.ActualHeight > 0 ? resolvedOwner.ActualHeight : resolvedOwner.Height;
            }
            else
            {
                window.Width = SystemParameters.PrimaryScreenWidth;
                window.Height = SystemParameters.PrimaryScreenHeight;
            }

            if (_settings.CancelOnClick)
            {
                window.MouseLeftButtonDown += (s, e) =>
                {
                    Hide();
                };
            }

            return window;
        }

        private void ApplySettings(Window window, string? customMessage)
        {
            var grid = new Grid();

            if (_settings.Overlay != OverlayMode.None)
            {
                var overlayBrush = GetOrCreateBrush(ref _cachedOverlayBrush, ref _cachedOverlayColorKey, _settings.OverlayColor);
                var overlay = new Border
                {
                    Background = overlayBrush,
                    Opacity = _settings.OverlayOpacity
                };

                if (_settings.Overlay == OverlayMode.Blur)
                {
                    if (_cachedBlurEffect == null || _cachedBlurRadius != _settings.BlurRadius)
                    {
                        _cachedBlurEffect = new BlurEffect { Radius = _settings.BlurRadius };
                        _cachedBlurEffect.Freeze();
                        _cachedBlurRadius = _settings.BlurRadius;
                    }
                    overlay.Effect = _cachedBlurEffect;
                }

                grid.Children.Add(overlay);
            }

            var container = new StackPanel
            {
                HorizontalAlignment = GetHorizontalAlignment(_settings.Position),
                VerticalAlignment = GetVerticalAlignment(_settings.Position)
            };

            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Width = _settings.Size * 3,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = _settings.ShowPercentage ? Visibility.Visible : Visibility.Collapsed
            };
            container.Children.Add(_progressBar);

            _percentText = new TextBlock
            {
                Text = "0.0%",
                Foreground = GetOrCreateBrush(ref _cachedTextBrush, ref _cachedTextColorKey, _settings.TextColor),
                FontSize = _settings.TextSize - 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = _settings.ShowPercentage ? Visibility.Visible : Visibility.Collapsed
            };
            container.Children.Add(_percentText);

            _loadingText = new TextBlock
            {
                Text = customMessage ?? _settings.LoadingText,
                Foreground = GetOrCreateBrush(ref _cachedTextBrush, ref _cachedTextColorKey, _settings.TextColor),
                FontSize = _settings.TextSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            if (_settings.ShowText)
            {
                container.Children.Add(_loadingText);
            }

            grid.Children.Add(container);
            window.Content = grid;
        }

        private HorizontalAlignment GetHorizontalAlignment(LoadingPosition position)
        {
            return position switch
            {
                LoadingPosition.TopRight => HorizontalAlignment.Right,
                LoadingPosition.BottomRight => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center
            };
        }

        private VerticalAlignment GetVerticalAlignment(LoadingPosition position)
        {
            return position switch
            {
                LoadingPosition.Top => VerticalAlignment.Top,
                LoadingPosition.TopRight => VerticalAlignment.Top,
                LoadingPosition.Bottom => VerticalAlignment.Bottom,
                LoadingPosition.BottomRight => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center
            };
        }

        private void RepositionWindow(Window window, Window? owner)
        {
            StandardDialog.EnsureOwnerAndTopmost(window, owner);
            var resolvedOwner = window.Owner;
            if (resolvedOwner != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = resolvedOwner.Left;
                window.Top = resolvedOwner.Top;
                window.Width = resolvedOwner.ActualWidth > 0 ? resolvedOwner.ActualWidth : resolvedOwner.Width;
                window.Height = resolvedOwner.ActualHeight > 0 ? resolvedOwner.ActualHeight : resolvedOwner.Height;
            }
            else
            {
                window.Width = SystemParameters.PrimaryScreenWidth;
                window.Height = SystemParameters.PrimaryScreenHeight;
            }
        }

        private void CloseLoadingWindow()
        {
            if (_loadingWindow != null)
            {
                _loadingWindow.Hide();
                _isShowing = false;
                TM.App.Log("[LoadingAnimation] 加载指示器已隐藏");
            }
        }
    }
}

