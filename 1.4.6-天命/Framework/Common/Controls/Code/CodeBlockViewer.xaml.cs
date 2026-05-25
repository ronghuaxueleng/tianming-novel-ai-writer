using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ColorCode;
using ColorCode.Compilation.Languages;
using ColorCode.HTML.Common;

namespace TM.Framework.Common.Controls.Code
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CodeBlockViewer : UserControl
    {
        private static readonly ILanguage[] _supportedLanguages;
        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();
        private string _code = string.Empty;
        private string _language = string.Empty;

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CodeBlockViewer] {key}: {ex.Message}");
        }

        static CodeBlockViewer()
        {
            _supportedLanguages = new ILanguage[]
            {
                Languages.CSharp,
                Languages.Python,
                Languages.JavaScript,
                Languages.Java,
                Languages.Cpp,
                Languages.Html,
                Languages.Css,
                Languages.Sql,
                Languages.Xml,
                Languages.PowerShell,
                Languages.Php,
                Languages.VbDotNet,
                Languages.Aspx,
                Languages.AspxCs,
                Languages.AspxVb
            };
        }

        public static readonly DependencyProperty CodeProperty =
            DependencyProperty.Register(
                nameof(Code),
                typeof(string),
                typeof(CodeBlockViewer),
                new PropertyMetadata(string.Empty, OnCodeChanged));

        public static readonly DependencyProperty CodeLanguageProperty =
            DependencyProperty.Register(
                nameof(CodeLanguage),
                typeof(string),
                typeof(CodeBlockViewer),
                new PropertyMetadata("text", OnLanguageChanged));

        public static readonly DependencyProperty ShowLineNumbersProperty =
            DependencyProperty.Register(
                nameof(ShowLineNumbers),
                typeof(bool),
                typeof(CodeBlockViewer),
                new PropertyMetadata(true, OnShowLineNumbersChanged));

        public string Code
        {
            get => (string)GetValue(CodeProperty);
            set => SetValue(CodeProperty, value);
        }

        public string CodeLanguage
        {
            get => (string)GetValue(CodeLanguageProperty);
            set => SetValue(CodeLanguageProperty, value);
        }

        public bool ShowLineNumbers
        {
            get => (bool)GetValue(ShowLineNumbersProperty);
            set => SetValue(ShowLineNumbersProperty, value);
        }

        public CodeBlockViewer()
        {
            InitializeComponent();
            RenderCode();
        }

        private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeBlockViewer viewer)
            {
                viewer._code = (string)e.NewValue ?? string.Empty;
                viewer.RenderCode();
            }
        }

        private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeBlockViewer viewer)
            {
                viewer._language = (string)e.NewValue ?? string.Empty;
                viewer.RenderCode();

                if (viewer.LanguageLabel != null)
                {
                    viewer.LanguageLabel.Text = string.IsNullOrEmpty(viewer._language) ? "Code" : viewer._language.ToUpper();
                }
            }
        }

        private static void OnShowLineNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeBlockViewer viewer)
            {
                viewer.RenderCode();
            }
        }

        private void RenderCode()
        {
            if (CodeContent == null || string.IsNullOrEmpty(_code))
            {
                return;
            }

            try
            {
                CodeContent.Inlines.Clear();

                if (ShowLineNumbers && LineNumbers != null)
                {
                    var lines = _code.Split('\n');
                    LineNumbers.Text = string.Join("\n", Enumerable.Range(1, lines.Length).Select(n => n.ToString()));
                }

                var language = FindLanguage(_language);

                if (language != null)
                {
                    var formatter = new HtmlClassFormatter();
                    var highlightedCode = formatter.GetHtmlString(_code, language);

                    ConvertHtmlToInlines(highlightedCode, CodeContent.Inlines);
                }
                else
                {
                    CodeContent.Text = _code;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CodeBlockViewer] 渲染代码失败: {ex.Message}");
                CodeContent.Text = _code;
            }
        }

        private ILanguage? FindLanguage(string languageName)
        {
            if (string.IsNullOrWhiteSpace(languageName))
                return null;

            var name = languageName.ToLower();

            var aliasMap = new System.Collections.Generic.Dictionary<string, string>
            {
                { "c#", "csharp" }, { "cs", "csharp" },
                { "py", "python" },
                { "js", "javascript" },
                { "ts", "typescript" },
                { "c++", "cpp" }, { "cc", "cpp" },
                { "ps1", "powershell" }, { "ps", "powershell" },
                { "sh", "bash" }, { "shell", "bash" },
                { "yml", "yaml" },
                { "md", "markdown" }
            };

            if (aliasMap.ContainsKey(name))
            {
                name = aliasMap[name];
            }

            return _supportedLanguages.FirstOrDefault(l => 
                l.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                l.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private void ConvertHtmlToInlines(string html, InlineCollection inlines)
        {
            try
            {
                var content = html.Replace("<div style=\"color:Black;background-color:White;\"><pre>", "")
                                  .Replace("</pre></div>", "");

                var index = 0;
                while (index < content.Length)
                {
                    var spanStart = content.IndexOf("<span", index);

                    if (spanStart == -1)
                    {
                        var remainingText = content.Substring(index);
                        if (!string.IsNullOrEmpty(remainingText))
                        {
                            inlines.Add(new Run(DecodeHtml(remainingText)));
                        }
                        break;
                    }

                    if (spanStart > index)
                    {
                        var textBefore = content.Substring(index, spanStart - index);
                        if (!string.IsNullOrEmpty(textBefore))
                        {
                            inlines.Add(new Run(DecodeHtml(textBefore)));
                        }
                    }

                    var spanEnd = content.IndexOf("</span>", spanStart);
                    if (spanEnd == -1) break;

                    var styleStart = content.IndexOf("style=\"", spanStart);
                    var styleEnd = content.IndexOf("\"", styleStart + 7);
                    var contentStart = content.IndexOf(">", spanStart) + 1;

                    if (styleStart != -1 && styleEnd != -1 && contentStart > 0)
                    {
                        var style = content.Substring(styleStart + 7, styleEnd - styleStart - 7);
                        var spanContent = content.Substring(contentStart, spanEnd - contentStart);

                        var run = new Run(DecodeHtml(spanContent));

                        if (style.Contains("color:"))
                        {
                            var colorStart = style.IndexOf("color:") + 6;
                            var colorEnd = style.IndexOf(";", colorStart);
                            if (colorEnd == -1) colorEnd = style.Length;

                            var colorStr = style.Substring(colorStart, colorEnd - colorStart).Trim();
                            try
                            {
                                var color = (Color)ColorConverter.ConvertFromString(colorStr);
                                run.Foreground = new SolidColorBrush(color);
                            }
                            catch (Exception ex)
                            {
                                DebugLogOnce("ConvertHtmlToInlines_Color", ex);
                            }
                        }

                        inlines.Add(run);
                    }

                    index = spanEnd + 7;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CodeBlockViewer] HTML转换失败: {ex.Message}");
                inlines.Add(new Run(_code));
            }
        }

        private string DecodeHtml(string html)
        {
            return html.Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&amp;", "&")
                       .Replace("&quot;", "\"")
                       .Replace("&#39;", "'")
                       .Replace("&nbsp;", " ");
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_code))
                {
                    System.Windows.Clipboard.SetText(_code);
                    GlobalToast.Success("已复制", "代码已复制到剪贴板");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CodeBlockViewer] 复制代码失败: {ex.Message}");
                GlobalToast.Error("复制失败", ex.Message);
            }
        }

        public void SetCode(string code, string language = "text")
        {
            Code = code;
            CodeLanguage = language;
        }
    }
}
