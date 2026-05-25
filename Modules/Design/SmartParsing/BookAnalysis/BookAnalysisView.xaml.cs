using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class BookAnalysisView : UserControl
    {
        private BookAnalysisViewModel? _viewModel;
        private WebView2? _webView;

        public BookAnalysisView(BookAnalysisViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                _viewModel = viewModel;
                DataContext = _viewModel;

                _viewModel.NavigateRequested += OnNavigateRequested;
                _viewModel.GoBackRequested += OnGoBackRequested;
                this.IsVisibleChanged += OnIsVisibleChanged;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisView] 初始化失败: {ex.Message}");
                throw;
            }
        }

        private void NavigateTo(string url)
        {
            if (_webView?.CoreWebView2 != null && !string.IsNullOrEmpty(url))
            {
                try
                {
                    _webView.CoreWebView2.Navigate(url);
                    if (!string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
                    {
                        TM.App.Log($"[BookAnalysisView] 导航到: {url}");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisView] 导航失败: {ex.Message}");
                }
            }
        }

        private void OnGoBackRequested()
        {
            if (_webView?.CoreWebView2 != null && _webView.CoreWebView2.CanGoBack)
            {
                try
                {
                    _webView.CoreWebView2.GoBack();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisView] GoBack 失败: {ex.Message}");
                }
            }
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (_viewModel == null) return;
            var url = _viewModel.CurrentUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            _viewModel.NavigateCommand.Execute(null);
            e.Handled = true;
        }

        private void OnNavigateRequested(string url)
        {
            NavigateTo(url);
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is true && e.NewValue is false)
            {
                if (_viewModel != null)
                {
                    _viewModel.IsWebViewVisible = false;
                    _viewModel.CurrentUrl = string.Empty;
                }
                _ = FreezeWebViewAsync();
            }
            else if (e.OldValue is false && e.NewValue is true)
            {
                ResumeWebView();
            }
        }

        private async System.Threading.Tasks.Task FreezeWebViewAsync()
        {
            if (_webView?.CoreWebView2 == null) return;
            try
            {
                _webView.CoreWebView2.Navigate("about:blank");
                var suspended = await _webView.CoreWebView2.TrySuspendAsync();
                TM.App.Log($"[BookAnalysisView] WebView2 已冻结 (suspended={suspended})");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisView] WebView2 冻结失败: {ex.Message}");
            }
        }

        private void ResumeWebView()
        {
            if (_webView?.CoreWebView2 == null) return;
            try
            {
                _webView.CoreWebView2.Resume();
                TM.App.Log("[BookAnalysisView] WebView2 已恢复");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisView] WebView2 恢复失败: {ex.Message}");
            }
        }

        private void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            _ = WebView_LoadedAsync(sender, e);
        }

        private async System.Threading.Tasks.Task WebView_LoadedAsync(object sender, RoutedEventArgs e)
        {
            if (sender is not WebView2 wv) return;
            _webView = wv;
            await InitWebViewCoreAsync(wv);
        }

        private async System.Threading.Tasks.Task InitWebViewCoreAsync(WebView2 wv)
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--disable-logging --log-level=3"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, null, options);
                await wv.EnsureCoreWebView2Async(env);

                SubscribeWebViewEvents(wv);

                TM.App.Log("[BookAnalysisView] WebView2 初始化成功");

                if (_viewModel != null)
                {
                    _viewModel.SetWebCrawlerService(new Crawler.WebCrawlerService(wv));
                    TM.App.Log("[BookAnalysisView] 爬虫服务已注入");
                }

                if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.CurrentUrl))
                {
                    wv.CoreWebView2.Navigate(_viewModel.CurrentUrl);
                    TM.App.Log($"[BookAnalysisView] 自动导航到: {_viewModel.CurrentUrl}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisView] WebView2 初始化失败: {ex.Message}");
            }
        }

        private void SubscribeWebViewEvents(WebView2 wv)
        {
            wv.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                try
                {
                    args.Handled = true;
                    wv.CoreWebView2.Navigate(args.Uri);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisView] NewWindowRequested 处理失败: {ex.Message}");
                }
            };

            wv.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                try
                {
                    if (_viewModel != null && args.IsSuccess)
                    {
                        var currentUri = wv.CoreWebView2?.Source;
                        if (string.IsNullOrEmpty(currentUri)
                            || string.Equals(currentUri, "about:blank", StringComparison.OrdinalIgnoreCase))
                            return;

                        if (_viewModel.CurrentUrl != currentUri)
                            _viewModel.CurrentUrl = currentUri;

                        _ = wv.ExecuteScriptAsync(Crawler.ContentExtractor.GetAdBlockScript());
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisView] NavigationCompleted 处理失败: {ex.Message}");
                }
            };
        }
    }
}
