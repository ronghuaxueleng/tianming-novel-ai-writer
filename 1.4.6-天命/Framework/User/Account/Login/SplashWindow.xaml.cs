using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TM.Framework.User.Account.Login.Bootstrap;

namespace TM.Framework.User.Account.Login
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SplashWindow : Window
    {
        private readonly BootstrapManager _bootstrapManager;
        private readonly TaskCompletionSource<bool> _completionSource;

        public Task<bool> CompletionTask => _completionSource.Task;

        public SplashWindow(BootstrapManager bootstrapManager)
        {
            InitializeComponent();
            _bootstrapManager = bootstrapManager;
            _bootstrapManager.ProgressChanged += OnProgressChanged;
            _completionSource = new TaskCompletionSource<bool>();

            LoadAppIcon();

            TM.App.Log("[SplashWindow] 启动进度窗口已初始化");
        }

        private void LoadAppIcon()
        {
            try
            {
                var iconPath = StoragePathHelper.GetFrameworkPath("UI/Icons/app.ico");
                if (!File.Exists(iconPath))
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var decoder = new IconBitmapDecoder(new Uri(iconPath, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var target = 30;
                var best = decoder.Frames
                    .OrderBy(f => Math.Abs(f.PixelWidth - target))
                    .ThenByDescending(f => f.PixelWidth)
                    .FirstOrDefault();

                var source = best ?? decoder.Frames.FirstOrDefault();
                if (source == null)
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var brush = new ImageBrush(source)
                {
                    Stretch = Stretch.Uniform
                };
                if (brush.CanFreeze)
                    brush.Freeze();

                AppIconBorder.Background = brush;
                AppIconBorder.Visibility = Visibility.Visible;
                FallbackIconTextBlock.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SplashWindow] 加载应用图标失败: {ex.Message}");
                AppIconBorder.Background = null;
                AppIconBorder.Visibility = Visibility.Collapsed;
                FallbackIconTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = StartBootstrapAsync();
        }

        private void OnProgressChanged(object? sender, BootstrapProgressEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                ProgressBar.Value = e.ProgressPercentage;
                PercentageTextBlock.Text = $"{e.ProgressPercentage:F0}%";
                TaskDescriptionTextBlock.Text = e.CurrentTaskDescription;

                TM.App.Log($"[SplashWindow] 进度更新: {e.ProgressPercentage:F0}% - {e.CurrentTaskDescription}");
            });
        }

        private async Task StartBootstrapAsync()
        {
            try
            {
                await _bootstrapManager.ExecuteAllAsync();

                await Task.Delay(500);

                _ = Dispatcher.BeginInvoke(() =>
                {
                    _completionSource.SetResult(true);
                    DialogResult = true;
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SplashWindow] 启动任务执行失败: {ex.Message}");

                _ = Dispatcher.BeginInvoke(() =>
                {
                    StandardDialog.ShowError($"启动失败: {ex.Message}", "错误");
                    _completionSource.SetResult(false);
                    DialogResult = false;
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _bootstrapManager.ProgressChanged -= OnProgressChanged;
            base.OnClosed(e);
        }
    }
}
