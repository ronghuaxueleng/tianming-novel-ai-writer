using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using Markdig;
using Markdig.Wpf;

namespace TM.Framework.Common.Controls.Markdown
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class MarkdownViewer : UserControl
    {
        private static readonly MarkdownPipeline _pipeline;

        static MarkdownViewer()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseSupportedExtensions()
                .Build();
        }

        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.Register(
                nameof(Markdown),
                typeof(string),
                typeof(MarkdownViewer),
                new PropertyMetadata(string.Empty, OnMarkdownChanged));

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public MarkdownViewer()
        {
            InitializeComponent();
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownViewer viewer)
            {
                viewer.RenderMarkdown((string)e.NewValue);
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
                    flowDocument.PagePadding = new Thickness(0);
                    flowDocument.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei, Segoe UI");
                    flowDocument.FontSize = 14;
                    flowDocument.LineHeight = 1.6;

                    MarkdownDocument.Document = flowDocument;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MarkdownViewer] 渲染Markdown失败: {ex.Message}");

                var fallbackDocument = new FlowDocument(new Paragraph(new Run(markdown)))
                {
                    PagePadding = new Thickness(0),
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"),
                    FontSize = 14
                };
                MarkdownDocument.Document = fallbackDocument;
            }
        }

        public void SetMarkdown(string markdown)
        {
            Markdown = markdown;
        }
    }
}

