using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.Animation.UIResolution
{
    public class UIResolutionService
    {
        private static bool _windowLoadedHandlerRegistered = false;

        private UIResolutionSettings _currentSettings;
        private double _currentScalePercent;
        private volatile bool _settingsCacheReady;
        private int _settingsVersion;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public UIResolutionService()
        {
            _currentSettings = UIResolutionSettings.CreateDefault();
            _currentScalePercent = _currentSettings.ScalePercent;
            RegisterWindowLoadedHandler();
            var loadVersion = Volatile.Read(ref _settingsVersion);
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var loaded = await LoadSettingsAsync().ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _settingsVersion))
                        return;
                    _currentSettings = loaded;
                    _currentScalePercent = loaded.ScalePercent;
                    _settingsCacheReady = true;
                }
                catch (Exception ex) { TM.App.Log($"[UIResolution] 后台设置加载失败: {ex.Message}"); }
            });
        }

        private void RegisterWindowLoadedHandler()
        {
            if (_windowLoadedHandlerRegistered) return;

            _windowLoadedHandlerRegistered = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is Window w)
                    {
                        ApplyScaleToWindow(w, _currentScalePercent);
                    }
                }));
        }

        public UIResolutionSettings GetCurrentSettings() => _currentSettings.Clone();

        public UIResolutionSettings LoadSettings()
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                TM.App.Log("[UIResolution] WARNING: LoadSettings on UI thread, returning cached");
                var loadVersion = Volatile.Read(ref _settingsVersion);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var loaded = await LoadSettingsAsync().ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _settingsVersion))
                        return;
                    _currentSettings = loaded;
                    _currentScalePercent = loaded.ScalePercent;
                });
                return _currentSettings.Clone();
            }

            if (_settingsCacheReady)
                return _currentSettings.Clone();

            TM.App.Log("[UIResolution] WARNING: 非UI线程冷路径, 触发后台加载并返回默认值");
            var coldLoadVersion = Volatile.Read(ref _settingsVersion);
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var loaded = await LoadSettingsAsync().ConfigureAwait(false);
                if (coldLoadVersion != Volatile.Read(ref _settingsVersion))
                    return;
                _currentSettings = loaded;
                _currentScalePercent = loaded.ScalePercent;
                _settingsCacheReady = true;
            });
            return UIResolutionSettings.CreateDefault();
        }

        public async System.Threading.Tasks.Task<UIResolutionSettings> LoadSettingsAsync()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/UIResolution",
                    "settings.json"
                );

                if (File.Exists(settingsFile))
                {
                    var json = await File.ReadAllTextAsync(settingsFile).ConfigureAwait(false);
                    var settings = JsonSerializer.Deserialize<UIResolutionSettings>(json);
                    if (settings != null)
                    {
                        TM.App.Log($"[UIResolution] 异步配置加载成功: {settings.WindowWidth}×{settings.WindowHeight}, 缩放{settings.ScalePercent}%");
                        return settings;
                    }
                }

                TM.App.Log("[UIResolution] 配置文件不存在，使用默认设置");
                return UIResolutionSettings.CreateDefault();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 异步加载设置失败: {ex.Message}");
                return UIResolutionSettings.CreateDefault();
            }
        }

        public void SaveSettings(UIResolutionSettings settings)
        {
            Interlocked.Increment(ref _settingsVersion);
            _currentSettings = settings;
            _currentScalePercent = settings.ScalePercent;
            _settingsCacheReady = true;
            _ = SaveSettingsAsync(settings);
        }

        public async System.Threading.Tasks.Task SaveSettingsAsync(UIResolutionSettings settings)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/Animation/UIResolution",
                    "settings.json"
                );

                var options = JsonHelper.Default;
                var tmpUrsA = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpUrsA))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, options).ConfigureAwait(false);
                }
                File.Move(tmpUrsA, settingsFile, overwrite: true);

                _currentSettings = settings;
                TM.App.Log($"[UIResolution] 设置已异步保存到: {settingsFile}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 异步保存设置失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void ApplyWindowSize(int width, int height)
        {
            try
            {
                void ApplyWindowSizeCore()
                {
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.Width = width;
                        mainWindow.Height = height;

                        mainWindow.Left = (SystemParameters.PrimaryScreenWidth - width) / 2;
                        mainWindow.Top = (SystemParameters.PrimaryScreenHeight - height) / 2;

                        TM.App.Log($"[UIResolution] 应用窗口尺寸: {width}×{height}");
                    }
                }

                if (Application.Current.Dispatcher.CheckAccess())
                    ApplyWindowSizeCore();
                else
                    Application.Current.Dispatcher.BeginInvoke(ApplyWindowSizeCore);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 应用窗口尺寸失败: {ex.Message}");
                throw;
            }
        }

        public void ApplyUIScale(double scalePercent)
        {
            try
            {
                void ApplyUIScaleCore()
                {
                    _currentScalePercent = scalePercent;

                    foreach (Window w in Application.Current.Windows)
                    {
                        ApplyScaleToWindow(w, scalePercent);
                    }

                    TM.App.Log($"[UIResolution] 应用UI缩放: {scalePercent}%");
                }

                if (Application.Current.Dispatcher.CheckAccess())
                    ApplyUIScaleCore();
                else
                    Application.Current.Dispatcher.BeginInvoke(ApplyUIScaleCore);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 应用UI缩放失败: {ex.Message}");
                throw;
            }
        }

        private static void ApplyScaleToWindow(Window window, double scalePercent)
        {
            if (window.Content is not FrameworkElement rootElement) return;

            double scale = scalePercent / 100.0;
            UIScaleHelper.Apply(rootElement, scale);
        }

        public void ApplySettings(UIResolutionSettings settings)
        {
            try
            {
                Interlocked.Increment(ref _settingsVersion);
                ApplyWindowSize(settings.WindowWidth, settings.WindowHeight);

                ApplyUIScale(settings.ScalePercent);
                _currentSettings = settings;
                _settingsCacheReady = true;

                _ = SaveSettingsAsync(settings);

                TM.App.Log($"[UIResolution] 设置应用成功");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 应用设置失败: {ex.Message}");
                throw;
            }
        }

        public (int width, int height) GetScreenResolution()
        {
            try
            {
                return ((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 获取屏幕分辨率失败: {ex.Message}");
                return (1920, 1080);
            }
        }

        public (int width, int height) GetCurrentWindowSize()
        {
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    return ((int)mainWindow.ActualWidth, (int)mainWindow.ActualHeight);
                }
                return (1920, 1080);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIResolution] 获取当前窗口尺寸失败: {ex.Message}");
                return (1920, 1080);
            }
        }

        public bool ValidateResolution(int width, int height)
        {
            var (maxWidth, maxHeight) = GetScreenResolution();
            return _currentSettings.ValidateSize(width, height, maxWidth, maxHeight);
        }

        public async System.Threading.Tasks.Task ReloadSettingsAsync()
        {
            var loadVersion = Volatile.Read(ref _settingsVersion);
            var settings = await LoadSettingsAsync().ConfigureAwait(false);
            if (loadVersion != Volatile.Read(ref _settingsVersion))
                return;
            _currentSettings = settings;
            _currentScalePercent = settings.ScalePercent;
            _settingsCacheReady = true;
        }

        private static class UIScaleHelper
        {
            private static readonly DependencyProperty OriginalLayoutTransformProperty =
                DependencyProperty.RegisterAttached(
                    "OriginalLayoutTransform",
                    typeof(Transform),
                    typeof(UIScaleHelper),
                    new PropertyMetadata(null));

            private static readonly DependencyProperty ScaleTransformProperty =
                DependencyProperty.RegisterAttached(
                    "ScaleTransform",
                    typeof(ScaleTransform),
                    typeof(UIScaleHelper),
                    new PropertyMetadata(null));

            public static void Apply(FrameworkElement element, double scale)
            {
                if (element == null) return;

                if (Math.Abs(scale - 1.0) < 0.0001)
                {
                    Remove(element);
                    return;
                }

                if (!element.ReadLocalValue(OriginalLayoutTransformProperty).Equals(DependencyProperty.UnsetValue))
                {
                }
                else
                {
                    element.SetValue(OriginalLayoutTransformProperty, element.LayoutTransform);
                }

                var scaleTransform = element.GetValue(ScaleTransformProperty) as ScaleTransform;
                if (scaleTransform == null)
                {
                    scaleTransform = new ScaleTransform(scale, scale);
                    element.SetValue(ScaleTransformProperty, scaleTransform);

                    var group = new TransformGroup();
                    group.Children.Add(scaleTransform);

                    var original = element.GetValue(OriginalLayoutTransformProperty) as Transform;
                    if (original != null)
                    {
                        group.Children.Add(original);
                    }

                    element.LayoutTransform = group;
                }
                else
                {
                    scaleTransform.ScaleX = scale;
                    scaleTransform.ScaleY = scale;
                }
            }

            private static void Remove(FrameworkElement element)
            {
                var original = element.GetValue(OriginalLayoutTransformProperty) as Transform;
                element.LayoutTransform = original;

                element.ClearValue(OriginalLayoutTransformProperty);
                element.ClearValue(ScaleTransformProperty);
            }
        }
    }
}

