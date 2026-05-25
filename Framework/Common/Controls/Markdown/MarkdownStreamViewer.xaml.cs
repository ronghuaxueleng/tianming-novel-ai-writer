using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Markdig;

namespace TM.Framework.Common.Controls.Markdown
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class MarkdownStreamViewer : UserControl
    {
        private static readonly MarkdownPipeline _pipeline;
        private static readonly System.Windows.Media.FontFamily _docFontFamily =
            new System.Windows.Media.FontFamily("Microsoft YaHei, Segoe UI");
        private readonly System.Text.StringBuilder _contentBuilder = new();
        private string _currentContent = string.Empty;
        private bool _isStreaming = false;
        private DispatcherTimer? _textRefreshTimer;
        private bool _textUpdatePending;
        private int _lastDisplayedLength;
        private bool _useTextMode;

        private DispatcherTimer? _streamMarkdownTimer;
        private bool _streamMarkdownDirty;
        private bool _streamMarkdownRendering;
        private bool _streamMarkdownRenderedOnce;
        private bool _streamUseMarkdown;
        private string _streamMarkdownLatest = string.Empty;
        private int _renderRequestId;

        #region 依赖属性（支持 XAML 绑定）

        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownStreamViewer),
                new PropertyMetadata(string.Empty, OnMarkdownChanged));

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public static readonly DependencyProperty IsStreamModeProperty =
            DependencyProperty.Register(nameof(IsStreamMode), typeof(bool), typeof(MarkdownStreamViewer),
                new PropertyMetadata(false, OnIsStreamModeChanged));

        public bool IsStreamMode
        {
            get => (bool)GetValue(IsStreamModeProperty);
            set => SetValue(IsStreamModeProperty, value);
        }

        public static readonly DependencyProperty DocumentFontSizeProperty =
            DependencyProperty.Register(nameof(DocumentFontSize), typeof(double), typeof(MarkdownStreamViewer),
                new PropertyMetadata(13.0, OnDocumentTypographyChanged));

        public double DocumentFontSize
        {
            get => (double)GetValue(DocumentFontSizeProperty);
            set => SetValue(DocumentFontSizeProperty, value);
        }

        public static readonly DependencyProperty DocumentForegroundProperty =
            DependencyProperty.Register(nameof(DocumentForeground), typeof(System.Windows.Media.Brush), typeof(MarkdownStreamViewer),
                new PropertyMetadata(null, OnDocumentTypographyChanged));

        public System.Windows.Media.Brush? DocumentForeground
        {
            get => (System.Windows.Media.Brush?)GetValue(DocumentForegroundProperty);
            set => SetValue(DocumentForegroundProperty, value);
        }

        public static readonly DependencyProperty DocumentLineHeightProperty =
            DependencyProperty.Register(nameof(DocumentLineHeight), typeof(double), typeof(MarkdownStreamViewer),
                new PropertyMetadata(20.0, OnDocumentTypographyChanged));

        public double DocumentLineHeight
        {
            get => (double)GetValue(DocumentLineHeightProperty);
            set => SetValue(DocumentLineHeightProperty, value);
        }

        public static readonly DependencyProperty EnableMarkdownProperty =
            DependencyProperty.Register(nameof(EnableMarkdown), typeof(bool), typeof(MarkdownStreamViewer),
                new PropertyMetadata(true));

        public bool EnableMarkdown
        {
            get => (bool)GetValue(EnableMarkdownProperty);
            set => SetValue(EnableMarkdownProperty, value);
        }

        private static void OnDocumentTypographyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownStreamViewer viewer)
                viewer.ApplyDocumentTypography();
        }

        private void ApplyDocumentTypography()
        {
            var brush = DocumentForeground
                ?? (TryFindResource("TextPrimary") as System.Windows.Media.Brush)
                ?? System.Windows.Media.Brushes.Black;

            if (StreamTextBlock != null)
            {
                StreamTextBlock.FontSize = DocumentFontSize;
                StreamTextBlock.LineHeight = DocumentLineHeight;
                StreamTextBlock.Foreground = brush;
            }
            if (MarkdownDocument != null)
            {
                MarkdownDocument.Foreground = brush;
                if (MarkdownDocument.Document != null)
                {
                    MarkdownDocument.Document.FontSize = DocumentFontSize;
                    MarkdownDocument.Document.Foreground = brush;
                }
            }
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownStreamViewer viewer)
                viewer.HandleMarkdownChanged((string)e.NewValue);
        }

        private static void OnIsStreamModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownStreamViewer viewer)
                viewer.HandleStreamModeChanged((bool)e.NewValue);
        }

        private string _lastBoundContent = string.Empty;

        private void HandleMarkdownChanged(string newContent)
        {
            if (string.IsNullOrEmpty(newContent))
            {
                Clear();
                _lastBoundContent = string.Empty;
                return;
            }

            if (_isStreaming)
            {
                if (newContent.Length > _lastBoundContent.Length
                    && newContent.StartsWith(_lastBoundContent, StringComparison.Ordinal))
                {
                    var delta = newContent.Substring(_lastBoundContent.Length);
                    AppendContent(delta);
                }
                else if (newContent != _lastBoundContent)
                {
                    ResetStreamingText(newContent);
                }

                if (EnableMarkdown && !_streamUseMarkdown && IsMarkdownStreamingCandidate(newContent))
                {
                    _streamUseMarkdown = true;
                }

                if (_streamUseMarkdown)
                {
                    RequestStreamMarkdownRender(newContent);
                }
            }
            else
            {
                SetMarkdown(newContent);
            }
            _lastBoundContent = newContent;
        }

        private void ResetStreamingText(string newContent)
        {
            _contentBuilder.Clear();
            _contentBuilder.Append(newContent);
            _lastDisplayedLength = newContent.Length;
            _textUpdatePending = false;
            _useTextMode = newContent.Length > 500;
            StreamTextBlock.Inlines.Clear();
            StreamTextBlock.Text = newContent;
        }

        private void HandleStreamModeChanged(bool isStream)
        {
            if (isStream)
            {
                StartStreaming();
                _lastBoundContent = string.Empty;
            }
            else if (_isStreaming)
            {
                _isStreaming = false;
                _textRefreshTimer?.Stop();
                _streamMarkdownTimer?.Stop();
                _streamUseMarkdown = false;
                _streamMarkdownDirty = false;
                _streamMarkdownRendering = false;
                _streamMarkdownRenderedOnce = false;
                var finalContent = Markdown ?? string.Empty;
                _currentContent = finalContent;
                SetMarkdown(finalContent);
            }
        }

        #endregion

        static MarkdownStreamViewer()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
        }

        public MarkdownStreamViewer()
        {
            InitializeComponent();

            ApplyDocumentTypography();

            PreviewMouseWheel += OnViewerPreviewMouseWheel;

            _textRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
            _textRefreshTimer.Tick += (_, _) =>
            {
                if (!_textUpdatePending) return;
                _textUpdatePending = false;
                var newLength = _contentBuilder.Length;
                if (newLength > _lastDisplayedLength)
                {
                    if (!_useTextMode && StreamTextBlock.Inlines.Count > 50)
                    {
                        _useTextMode = true;
                        if (_textRefreshTimer!.Interval != TimeSpan.FromMilliseconds(200))
                            _textRefreshTimer!.Interval = TimeSpan.FromMilliseconds(200);
                    }

                    if (_useTextMode)
                    {
                        StreamTextBlock.Text = _contentBuilder.ToString(0, newLength);
                    }
                    else
                    {
                        var delta = _contentBuilder.ToString(_lastDisplayedLength, newLength - _lastDisplayedLength);
                        StreamTextBlock.Inlines.Add(new System.Windows.Documents.Run(delta));
                    }
                    _lastDisplayedLength = newLength;
                }
            };

            Unloaded += (_, _) =>
            {
                _textRefreshTimer?.Stop();
                _streamMarkdownTimer?.Stop();
            };
        }

        public void StartStreaming()
        {
            _isStreaming = true;
            _contentBuilder.Clear();
            _currentContent = string.Empty;
            _textUpdatePending = false;
            _lastDisplayedLength = 0;
            _useTextMode = false;
            _streamUseMarkdown = false;
            _streamMarkdownDirty = false;
            _streamMarkdownRendering = false;
            _streamMarkdownRenderedOnce = false;
            _streamMarkdownLatest = string.Empty;
            _streamMarkdownTimer?.Stop();
            StreamTextBlock.Text = string.Empty;
            if (_textRefreshTimer != null && _textRefreshTimer.Interval != TimeSpan.FromMilliseconds(80))
                _textRefreshTimer.Interval = TimeSpan.FromMilliseconds(80);

            StreamTextBlock.Visibility = Visibility.Visible;
            MarkdownDocument.Visibility = Visibility.Collapsed;
            _textRefreshTimer?.Start();

            TM.App.Log("[MarkdownStreamViewer] 开始流式接收");
        }

        public void AppendContent(string content)
        {
            if (!_isStreaming)
            {
                StartStreaming();
            }

            _contentBuilder.Append(content);
            _textUpdatePending = true;
        }

        public async void CompleteStreaming()
        {
            try
            {
                _isStreaming = false;
                _textRefreshTimer?.Stop();
                _currentContent = _contentBuilder.ToString();
                if (_lastDisplayedLength < _currentContent.Length)
                {
                    if (_useTextMode)
                        StreamTextBlock.Text = _currentContent;
                    else
                        StreamTextBlock.Inlines.Add(new System.Windows.Documents.Run(_currentContent[_lastDisplayedLength..]));
                    _lastDisplayedLength = _currentContent.Length;
                }
                _textUpdatePending = false;

                TM.App.Log($"[MarkdownStreamViewer] 流式接收完成，内容长度: {_currentContent.Length}");

                if (EnableMarkdown && IsMarkdownContent(_currentContent))
                {
                    var requestId = System.Threading.Interlocked.Increment(ref _renderRequestId);
                    await RenderMarkdownAsync(_currentContent, requestId);
                    if (requestId != _renderRequestId) return;
                    StreamTextBlock.Visibility = Visibility.Collapsed;
                    MarkdownDocument.Visibility = Visibility.Visible;
                }
                else
                {
                    StreamTextBlock.Visibility = Visibility.Visible;
                    MarkdownDocument.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MarkdownStreamViewer] CompleteStreaming 失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task RenderMarkdownAsync(string markdown, int requestId)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                if (requestId == _renderRequestId)
                    MarkdownDocument.Document = new FlowDocument();
                return;
            }

            try
            {
                var pipeline = _pipeline;
                var docFontFamily = _docFontFamily;

                const int MaxBlocks = 80;
                var ast = await System.Threading.Tasks.Task.Run(() =>
                {
                    var parsed = Markdig.Markdown.Parse(markdown, pipeline);
                    while (parsed.Count > MaxBlocks)
                        parsed.RemoveAt(parsed.Count - 1);
                    return parsed;
                }).ConfigureAwait(true);

                if (requestId != _renderRequestId)
                    return;

                var doc = new System.Windows.Documents.FlowDocument();
                var renderer = new Markdig.Renderers.WpfRenderer(doc);
                pipeline.Setup(renderer);
                renderer.Render(ast);
                if (doc != null)
                {
                    doc.PagePadding = new Thickness(5);
                    doc.FontFamily = docFontFamily;
                    doc.FontSize = DocumentFontSize;
                    doc.Foreground = DocumentForeground
                        ?? (TryFindResource("TextPrimary") as System.Windows.Media.Brush)
                        ?? System.Windows.Media.Brushes.Black;
                    doc.LineHeight = 1.5;
                    StyleFlowDocument(doc);
                    if (requestId == _renderRequestId)
                        MarkdownDocument.Document = doc;
                }
            }
            catch (Exception ex)
            {
                if (requestId == _renderRequestId)
                {
                    TM.App.Log($"[MarkdownStreamViewer] 渲染Markdown失败: {ex.Message}");
                    StreamTextBlock.Text = markdown;
                    StreamTextBlock.Visibility = Visibility.Visible;
                    MarkdownDocument.Visibility = Visibility.Collapsed;
                }
            }
        }

        public async void SetMarkdown(string markdown)
        {
            try
            {
                _isStreaming = false;
                _currentContent = markdown;

                if (EnableMarkdown && IsMarkdownContent(markdown))
                {
                    var requestId = System.Threading.Interlocked.Increment(ref _renderRequestId);
                    await RenderMarkdownAsync(markdown, requestId);
                    if (requestId != _renderRequestId) return;
                    StreamTextBlock.Visibility = Visibility.Collapsed;
                    MarkdownDocument.Visibility = Visibility.Visible;
                }
                else
                {
                    StreamTextBlock.Text = markdown;
                    StreamTextBlock.Visibility = Visibility.Visible;
                    MarkdownDocument.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { TM.App.Log($"[MarkdownStreamViewer] SetMarkdown 失败: {ex.Message}"); }
        }

        private bool IsMarkdownContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            return content.Contains("```") ||
                   content.Contains("##") ||
                   content.Contains("**") ||
                   content.Contains("__") ||
                   content.Contains("- ") ||
                   content.Contains("* ") ||
                   content.Contains("1. ") ||
                   content.Contains('[') && content.Contains("](") ||
                   content.Contains("| ") ||
                   content.Contains("\\(") ||
                   content.Contains("\\[");
        }

        private void StyleFlowDocument(FlowDocument doc)
        {
            var primaryBrush = TryFindResource("PrimaryColor") as System.Windows.Media.Brush;
            var textPrimaryBrush = TryFindResource("TextPrimary") as System.Windows.Media.Brush;
            if (primaryBrush == null) return;

            foreach (var block in doc.Blocks)
            {
                if (block is Paragraph para)
                {
                    if (para.FontSize >= 16 || para.Tag?.ToString() == "Heading")
                    {
                        para.Foreground = primaryBrush;
                    }
                    StyleInlines(para.Inlines, primaryBrush);
                }
                else if (block is System.Windows.Documents.List list)
                {
                    foreach (var item in list.ListItems)
                    {
                        foreach (var itemBlock in item.Blocks)
                        {
                            if (itemBlock is Paragraph itemPara)
                                StyleInlines(itemPara.Inlines, primaryBrush);
                        }
                    }
                }
                else if (block is Section section)
                {
                    foreach (var sBlock in section.Blocks)
                    {
                        if (sBlock is Paragraph sPara)
                        {
                            if (sPara.FontSize >= 16 || sPara.Tag?.ToString() == "Heading")
                                sPara.Foreground = primaryBrush;
                            StyleInlines(sPara.Inlines, primaryBrush);
                        }
                    }
                }
            }
        }

        private static void StyleInlines(InlineCollection inlines, System.Windows.Media.Brush primaryBrush)
        {
            foreach (var inline in inlines)
            {
                if (inline is Bold bold)
                {
                    bold.Foreground = primaryBrush;
                    StyleInlines(bold.Inlines, primaryBrush);
                }
                else if (inline is Span span)
                {
                    StyleInlines(span.Inlines, primaryBrush);
                }
            }
        }

        private static bool IsMarkdownStreamingCandidate(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            if (content.Contains("```")) return true;
            if (content.Contains("**") || content.Contains("__")) return true;
            if (content.StartsWith("- ", StringComparison.Ordinal) || content.StartsWith("* ", StringComparison.Ordinal) || content.StartsWith("1. ", StringComparison.Ordinal)) return true;
            if (content.Contains("\n- ") || content.Contains("\n* ") || content.Contains("\n1. ")) return true;
            if (content.Contains("\r\n- ") || content.Contains("\r\n* ") || content.Contains("\r\n1. ")) return true;
            if (content.Contains("](") && content.Contains('[')) return true;
            if (content.Contains("##") || content.Contains("\n#")) return true;

            return false;
        }

        private void EnsureStreamMarkdownTimerRunning()
        {
            if (_streamMarkdownTimer == null)
            {
                _streamMarkdownTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _streamMarkdownTimer.Tick += async (_, _) => await TryRenderStreamMarkdownAsync();
            }

            if (!_streamMarkdownTimer.IsEnabled)
                _streamMarkdownTimer.Start();
        }

        private void RequestStreamMarkdownRender(string content)
        {
            _streamMarkdownLatest = content;
            _streamMarkdownDirty = true;
            EnsureStreamMarkdownTimerRunning();
            if (!_streamMarkdownRenderedOnce)
                _ = TryRenderStreamMarkdownAsync();
        }

        private async System.Threading.Tasks.Task TryRenderStreamMarkdownAsync()
        {
            if (!_isStreaming || !_streamUseMarkdown)
                return;
            if (!_streamMarkdownDirty)
                return;
            if (_streamMarkdownRendering)
                return;

            _streamMarkdownDirty = false;
            _streamMarkdownRendering = true;
            try
            {
                var requestId = System.Threading.Interlocked.Increment(ref _renderRequestId);
                var content = _streamMarkdownLatest;
                await RenderMarkdownAsync(content, requestId);
                if (requestId != _renderRequestId) return;

                _streamMarkdownRenderedOnce = true;
                StreamTextBlock.Visibility = Visibility.Collapsed;
                MarkdownDocument.Visibility = Visibility.Visible;
            }
            finally
            {
                _streamMarkdownRendering = false;
            }
        }

        private void OnViewerPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            if (MarkdownDocument == null || MarkdownDocument.Visibility != Visibility.Visible) return;

            e.Handled = true;
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(this) as UIElement;
            if (parent == null) return;
            var forwarded = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parent.RaiseEvent(forwarded);
        }

        public void Clear()
        {
            _isStreaming = false;
            _contentBuilder.Clear();
            _currentContent = string.Empty;
            _streamMarkdownTimer?.Stop();
            _streamUseMarkdown = false;
            _streamMarkdownDirty = false;
            _streamMarkdownRendering = false;
            _streamMarkdownRenderedOnce = false;
            _streamMarkdownLatest = string.Empty;
            StreamTextBlock.Text = string.Empty;
            MarkdownDocument.Document = new FlowDocument();
            StreamTextBlock.Visibility = Visibility.Visible;
            MarkdownDocument.Visibility = Visibility.Collapsed;
        }
    }
}

