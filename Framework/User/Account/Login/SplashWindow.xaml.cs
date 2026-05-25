using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using TM.Framework.User.Account.Login.Bootstrap;

namespace TM.Framework.User.Account.Login
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
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

            TM.Framework.Common.Helpers.UI.AppIconLoader.Load(AppIconBorder, 128, FallbackIconImage, "SplashWindow");

            TM.App.Log("[SplashWindow] 启动进度窗口已初始化");
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
                    StandardDialog.ShowError("启动失败，请重启应用程序", "启动失败");
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
