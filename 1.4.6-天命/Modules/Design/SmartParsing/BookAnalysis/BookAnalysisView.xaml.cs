using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class BookAnalysisView : UserControl
    {
        private BookAnalysisViewModel? _viewModel;
        private WebView2? _webView;

        private static bool IsAllowedNovelUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host?.ToLowerInvariant() ?? string.Empty;
            return host == "www.shuquta.com" || host == "shuquta.com" ||
                   host == "www.xheiyan.info" || host == "xheiyan.info" ||
                   host == "m.bqgde.de" || host == "www.bqgde.de" || host == "bqgde.de" ||
                   host == "m.bqg78.com" || host == "www.bqg78.com" || host == "bqg78.com";
        }

        public BookAnalysisView(BookAnalysisViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                _viewModel = viewModel;
                DataContext = _viewModel;

                _viewModel.NavigateRequested += OnNavigateRequested;
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
                    if (!IsAllowedNovelUrl(url))
                    {
                        GlobalToast.Warning("提示", "仅支持 shuquta.com / xheiyan.info / bqgde.de 站点");
                        return;
                    }

                    _webView.CoreWebView2.Navigate(url);
                    TM.App.Log($"[BookAnalysisView] 导航到: {url}");

                    if (_viewModel != null)
                    {
                        if (IsAllowedNovelUrl(url) && !_viewModel.UrlHistory.Contains(url))
                        {
                            _viewModel.UrlHistory.Insert(0, url);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[BookAnalysisView] 导航失败: {ex.Message}");
                }
            }
        }

        private void UrlComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is string url)
            {
                NavigateTo(url);
            }
        }

        private void UrlComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            if (_viewModel == null)
                return;

            var url = _viewModel.CurrentUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;

            NavigateTo(url);
            e.Handled = true;
        }

        private void OnNavigateRequested(string url)
        {
            NavigateTo(url);
        }

        private void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            _ = WebView_LoadedAsync(sender, e);
        }

        private async System.Threading.Tasks.Task WebView_LoadedAsync(object sender, RoutedEventArgs e)
        {
            if (sender is WebView2 wv)
            {
                _webView = wv;

                try
                {
                    var options = new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = "--disable-logging --log-level=3"
                    };

                    var env = await CoreWebView2Environment.CreateAsync(null, null, options);
                    await wv.EnsureCoreWebView2Async(env);

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
                                var currentUri = wv.CoreWebView2.Source;
                                if (!string.IsNullOrEmpty(currentUri) && _viewModel.CurrentUrl != currentUri)
                                {
                                    _viewModel.CurrentUrl = currentUri;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[BookAnalysisView] NavigationCompleted 处理失败: {ex.Message}");
                        }
                    };

                    TM.App.Log("[BookAnalysisView] WebView2 初始化成功");

                    if (_viewModel != null)
                    {
                        var crawlerService = new Crawler.WebCrawlerService(wv);
                        _viewModel.SetWebCrawlerService(crawlerService);
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
        }
    }
}
