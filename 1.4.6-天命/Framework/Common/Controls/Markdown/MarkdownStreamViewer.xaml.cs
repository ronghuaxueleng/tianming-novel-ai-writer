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
    public partial class MarkdownStreamViewer : UserControl
    {
        private static readonly MarkdownPipeline _pipeline;
        private static readonly System.Windows.Media.FontFamily _docFontFamily =
            new System.Windows.Media.FontFamily("Microsoft YaHei, Segoe UI");
        private readonly System.Text.StringBuilder _contentBuilder = new();
        private string _currentContent = string.Empty;
        private string _lastRenderedContent = string.Empty;
        private bool _isStreaming = false;
        private DispatcherTimer? _renderTimer;
        private DateTime _lastRenderTime = DateTime.MinValue;
        private const int RenderIntervalMs = 500;

        static MarkdownStreamViewer()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
        }

        public MarkdownStreamViewer()
        {
            InitializeComponent();

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RenderIntervalMs)
            };
            _renderTimer.Tick += OnRenderTimerTick;

            Unloaded += (_, _) =>
            {
                _renderTimer?.Stop();
            };
        }

        public void StartStreaming()
        {
            _isStreaming = true;
            _contentBuilder.Clear();
            _currentContent = string.Empty;
            _lastRenderedContent = string.Empty;
            StreamTextBlock.Text = string.Empty;

            StreamScrollViewer.Visibility = Visibility.Visible;
            MarkdownScrollViewer.Visibility = Visibility.Collapsed;

            _renderTimer?.Start();

            TM.App.Log("[MarkdownStreamViewer] 开始流式接收");
        }

        public void AppendContent(string content)
        {
            if (!_isStreaming)
            {
                StartStreaming();
            }

            _contentBuilder.Append(content);
            _currentContent = _contentBuilder.ToString();

            StreamTextBlock.Text = _currentContent;

            StreamScrollViewer.ScrollToEnd();
        }

        public void CompleteStreaming()
        {
            _isStreaming = false;
            _renderTimer?.Stop();

            TM.App.Log($"[MarkdownStreamViewer] 流式接收完成，内容长度: {_currentContent.Length}");

            if (IsMarkdownContent(_currentContent))
            {
                RenderMarkdown(_currentContent);
                StreamScrollViewer.Visibility = Visibility.Collapsed;
                MarkdownScrollViewer.Visibility = Visibility.Visible;
            }
            else
            {
                StreamScrollViewer.Visibility = Visibility.Visible;
                MarkdownScrollViewer.Visibility = Visibility.Collapsed;
            }
        }

        private void OnRenderTimerTick(object? sender, EventArgs e)
        {
            if (!_isStreaming)
            {
                _renderTimer?.Stop();
                return;
            }

            var now = DateTime.Now;
            if ((now - _lastRenderTime).TotalMilliseconds < RenderIntervalMs)
            {
                return;
            }

            _lastRenderTime = now;

            if (_currentContent == _lastRenderedContent)
                return;

            if (IsMarkdownContent(_currentContent))
            {
                try
                {
                    var flowDocument = Markdig.Wpf.Markdown.ToFlowDocument(_currentContent, _pipeline);

                    if (flowDocument != null)
                    {
                        flowDocument.PagePadding = new Thickness(5);
                        flowDocument.FontFamily = _docFontFamily;
                        flowDocument.FontSize = 13;
                        flowDocument.LineHeight = 1.5;

                        MarkdownDocument.Document = flowDocument;
                        _lastRenderedContent = _currentContent;

                        StreamScrollViewer.Visibility = Visibility.Collapsed;
                        MarkdownScrollViewer.Visibility = Visibility.Visible;

                        MarkdownScrollViewer.ScrollToEnd();
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[MarkdownStreamViewer] 流式渲染Markdown失败（可能不完整）: {ex.Message}");

                    StreamScrollViewer.Visibility = Visibility.Visible;
                    MarkdownScrollViewer.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RenderMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                MarkdownDocument.Document = new FlowDocument();
                return;
            }

            try
            {
                var flowDocument = Markdig.Wpf.Markdown.ToFlowDocument(markdown, _pipeline);

                if (flowDocument != null)
                {
                    flowDocument.PagePadding = new Thickness(5);
                    flowDocument.FontFamily = _docFontFamily;
                    flowDocument.FontSize = 13;
                    flowDocument.LineHeight = 1.5;

                    MarkdownDocument.Document = flowDocument;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MarkdownStreamViewer] 渲染Markdown失败: {ex.Message}");

                StreamTextBlock.Text = markdown;
                StreamScrollViewer.Visibility = Visibility.Visible;
                MarkdownScrollViewer.Visibility = Visibility.Collapsed;
            }
        }

        public void SetMarkdown(string markdown)
        {
            _isStreaming = false;
            _renderTimer?.Stop();
            _currentContent = markdown;

            if (IsMarkdownContent(markdown))
            {
                RenderMarkdown(markdown);
                StreamScrollViewer.Visibility = Visibility.Collapsed;
                MarkdownScrollViewer.Visibility = Visibility.Visible;
            }
            else
            {
                StreamTextBlock.Text = markdown;
                StreamScrollViewer.Visibility = Visibility.Visible;
                MarkdownScrollViewer.Visibility = Visibility.Collapsed;
            }
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
                   content.Contains("[") && content.Contains("](") ||
                   content.Contains("| ") ||
                   content.Contains("\\(") ||
                   content.Contains("\\[");
        }

        public void Clear()
        {
            _isStreaming = false;
            _renderTimer?.Stop();
            _contentBuilder.Clear();
            _currentContent = string.Empty;
            _lastRenderedContent = string.Empty;
            StreamTextBlock.Text = string.Empty;
            MarkdownDocument.Document = new FlowDocument();
            StreamScrollViewer.Visibility = Visibility.Visible;
            MarkdownScrollViewer.Visibility = Visibility.Collapsed;
        }
    }
}

