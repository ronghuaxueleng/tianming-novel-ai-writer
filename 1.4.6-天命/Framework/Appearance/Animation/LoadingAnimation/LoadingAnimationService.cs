using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TM.Framework.Common.Controls.Dialogs;

namespace TM.Framework.Appearance.Animation.LoadingAnimation
{
    public class LoadingAnimationService
    {
        private Window? _loadingWindow;
        private LoadingAnimationSettings _settings;
        private DateTime _showStartTime;
        private bool _isShowing;

        public LoadingAnimationService()
        {
            _settings = LoadSettings();
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

                        _loadingWindow = CreateLoadingWindow(owner);

                        ApplySettings(_loadingWindow, message);

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

                        var progressBar = FindChild<ProgressBar>(_loadingWindow, "LoadingProgressBar");
                        if (progressBar != null)
                        {
                            progressBar.Value = percentage;
                        }

                        if (_settings.ShowPercentage)
                        {
                            var percentText = FindChild<TextBlock>(_loadingWindow, "PercentageText");
                            if (percentText != null)
                            {
                                percentText.Text = $"{percentage:F1}%";
                            }
                        }

                        if (!string.IsNullOrEmpty(message))
                        {
                            var messageText = FindChild<TextBlock>(_loadingWindow, "LoadingText");
                            if (messageText != null)
                            {
                                messageText.Text = message;
                            }
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
                            Task.Delay(delay).ContinueWith(_ =>
                            {
                                var innerDispatcher = Application.Current?.Dispatcher;
                                if (innerDispatcher != null)
                                {
                                    innerDispatcher.BeginInvoke(() =>
                                    {
                                        CloseLoadingWindow();
                                    });
                                }
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

        private LoadingAnimationSettings LoadSettings()
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
                    var json = File.ReadAllText(settingsFile);
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
            _settings = LoadSettings();
            TM.App.Log("[LoadingAnimation] 配置已重新加载");
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
                var overlay = new Border
                {
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(_settings.OverlayColor)
                    )
                    {
                        Opacity = _settings.OverlayOpacity
                    }
                };

                if (_settings.Overlay == OverlayMode.Blur)
                {
                    overlay.Effect = new BlurEffect
                    {
                        Radius = _settings.BlurRadius
                    };
                }

                grid.Children.Add(overlay);
            }

            var container = new StackPanel
            {
                HorizontalAlignment = GetHorizontalAlignment(_settings.Position),
                VerticalAlignment = GetVerticalAlignment(_settings.Position)
            };

            var loadingText = new TextBlock
            {
                Text = customMessage ?? _settings.LoadingText,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(_settings.TextColor)
                ),
                FontSize = _settings.TextSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, _settings.Size + 10, 0, 0)
            };

            if (_settings.ShowText)
            {
                container.Children.Add(loadingText);
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

        private void CloseLoadingWindow()
        {
            if (_loadingWindow != null)
            {
                _loadingWindow.Close();
                _loadingWindow = null;
                _isShowing = false;
                TM.App.Log("[LoadingAnimation] 加载指示器已关闭");
            }
        }
    }
}

