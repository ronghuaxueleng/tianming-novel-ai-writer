using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterMarkdownEditorViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<string>? ContentSaved;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[MarkdownEditor] {key}: {ex.Message}");
        }

        private string _content = "";
        private string _previewContent = "";
        private bool _isEditMode = true;
        private bool _isPreviewMode;
        private bool _isSplitMode;
        private bool _isPolishSplitMode;
        private int _wordCount;
        private int _paragraphCount;
        private int _lineCount;
        private string _statusText = "就绪";
        private bool _isDirty;

        private bool _showSearchBar;
        private string _searchText = "";
        private string _replaceText = "";
        private string _matchInfo = "";
        private int _currentMatchIndex = -1;
        private System.Collections.Generic.List<int> _matchPositions = new();

        public ChapterMarkdownEditorViewModel()
        {
            BoldCommand = new RelayCommand(InsertBold);
            ItalicCommand = new RelayCommand(InsertItalic);
            Heading1Command = new RelayCommand(InsertHeading1);
            Heading2Command = new RelayCommand(InsertHeading2);
            QuoteCommand = new RelayCommand(InsertQuote);
            ListCommand = new RelayCommand(InsertList);
            OrderedListCommand = new RelayCommand(InsertOrderedList);
            SaveCommand = new RelayCommand(() => SaveAsync(), () => IsDirty);
            ExitPolishModeCommand = new RelayCommand(() => { IsPolishSplitMode = false; IsEditMode = true; });
            SearchCommand = new RelayCommand(ShowSearch);
            ReplaceCommand = new RelayCommand(ShowSearch);
            FindNextCommand = new RelayCommand(FindNext);
            FindPreviousCommand = new RelayCommand(FindPrevious);
            ReplaceOneCommand = new RelayCommand(ReplaceOne);
            ReplaceAllCommand = new RelayCommand(ReplaceAll);
            CloseSearchCommand = new RelayCommand(() => ShowSearchBar = false);
            ToggleOutlineCommand = new RelayCommand(() => ShowOutline = !ShowOutline);

            UpdateLineNumbers();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    UpdateStatistics();
                    UpdatePreview();
                }
            }
        }

        public string PreviewContent
        {
            get => _previewContent;
            set { if (_previewContent != value) { _previewContent = value; OnPropertyChanged(); } }
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    if (value)
                    {
                        IsPreviewMode = false;
                        IsSplitMode = false;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPreviewMode
        {
            get => _isPreviewMode;
            set
            {
                if (_isPreviewMode != value)
                {
                    _isPreviewMode = value;
                    if (value)
                    {
                        IsEditMode = false;
                        IsSplitMode = false;
                        UpdatePreview();
                    }
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSplitMode
        {
            get => _isSplitMode;
            set
            {
                if (_isSplitMode != value)
                {
                    _isSplitMode = value;
                    if (value)
                    {
                        IsEditMode = false;
                        IsPreviewMode = false;
                        UpdatePreview();
                    }
                    else
                    {
                        IsPolishSplitMode = false;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPolishSplitMode
        {
            get => _isPolishSplitMode;
            set
            {
                if (_isPolishSplitMode != value)
                {
                    _isPolishSplitMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int WordCount
        {
            get => _wordCount;
            set { if (_wordCount != value) { _wordCount = value; OnPropertyChanged(); } }
        }

        public int ParagraphCount
        {
            get => _paragraphCount;
            set { if (_paragraphCount != value) { _paragraphCount = value; OnPropertyChanged(); } }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } }
        }

        public int LineCount
        {
            get => _lineCount;
            set { if (_lineCount != value) { _lineCount = value; OnPropertyChanged(); } }
        }

        private int _currentLine = 1;
        private int _currentColumn = 1;

        public int CurrentLine
        {
            get => _currentLine;
            set { if (_currentLine != value) { _currentLine = value; OnPropertyChanged(); } }
        }

        public int CurrentColumn
        {
            get => _currentColumn;
            set { if (_currentColumn != value) { _currentColumn = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<int> LineNumberList { get; } = new();

        #endregion

        #region 命令

        public ICommand BoldCommand { get; }
        public ICommand ItalicCommand { get; }
        public ICommand Heading1Command { get; }
        public ICommand Heading2Command { get; }
        public ICommand QuoteCommand { get; }
        public ICommand ListCommand { get; }
        public ICommand OrderedListCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ExitPolishModeCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand FindNextCommand { get; }
        public ICommand FindPreviousCommand { get; }
        public ICommand ReplaceOneCommand { get; }
        public ICommand ReplaceAllCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand ToggleOutlineCommand { get; }

        #endregion

        #region 搜索替换属性

        public bool ShowSearchBar
        {
            get => _showSearchBar;
            set { if (_showSearchBar != value) { _showSearchBar = value; OnPropertyChanged(); } }
        }

        public string SearchText
        {
            get => _searchText;
            set 
            { 
                if (_searchText != value) 
                { 
                    _searchText = value; 
                    OnPropertyChanged();
                    UpdateSearchMatches();
                } 
            }
        }

        public string ReplaceText
        {
            get => _replaceText;
            set { if (_replaceText != value) { _replaceText = value; OnPropertyChanged(); } }
        }

        public string MatchInfo
        {
            get => _matchInfo;
            set { if (_matchInfo != value) { _matchInfo = value; OnPropertyChanged(); } }
        }

        private FlowDocument? _previewDocument;
        private FlowDocument? _splitPreviewDocument;

        public FlowDocument? PreviewDocument
        {
            get => _previewDocument;
            set { if (_previewDocument != value) { _previewDocument = value; OnPropertyChanged(); } }
        }

        public FlowDocument? SplitPreviewDocument
        {
            get => _splitPreviewDocument;
            set { if (_splitPreviewDocument != value) { _splitPreviewDocument = value; OnPropertyChanged(); } }
        }

        private bool _showOutline;
        private OutlineItem? _selectedOutlineItem;

        public bool ShowOutline
        {
            get => _showOutline;
            set { if (_showOutline != value) { _showOutline = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<OutlineItem> OutlineItems { get; } = new();

        public OutlineItem? SelectedOutlineItem
        {
            get => _selectedOutlineItem;
            set
            {
                if (_selectedOutlineItem != value)
                {
                    _selectedOutlineItem = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        JumpToLineRequested?.Invoke(value.LineNumber);
                    }
                }
            }
        }

        public event Action<int>? JumpToLineRequested;

        #endregion

        #region 方法

        private void InsertBold()
        {
            InsertMarkdown("**", "**", "粗体文本");
        }

        private void InsertItalic()
        {
            InsertMarkdown("*", "*", "斜体文本");
        }

        private void InsertHeading1()
        {
            InsertLinePrefix("# ", "标题");
        }

        private void InsertHeading2()
        {
            InsertLinePrefix("## ", "标题");
        }

        private void InsertQuote()
        {
            InsertLinePrefix("> ", "引用内容");
        }

        private void InsertList()
        {
            InsertLinePrefix("- ", "列表项");
        }

        private void InsertOrderedList()
        {
            InsertLinePrefix("1. ", "列表项");
        }

        private void InsertMarkdown(string prefix, string suffix, string placeholder)
        {
            Content += $"{prefix}{placeholder}{suffix}";
        }

        private void InsertLinePrefix(string prefix, string placeholder)
        {
            if (!string.IsNullOrEmpty(Content) && !Content.EndsWith("\n"))
            {
                Content += "\n";
            }
            Content += $"{prefix}{placeholder}";
        }

        private void SaveAsync()
        {
            try
            {
                StatusText = "保存中...";
                ContentSaved?.Invoke(Content);
            }
            catch (Exception ex)
            {
                StatusText = $"保存失败: {ex.Message}";
                TM.App.Log($"[MarkdownEditor] 保存失败: {ex.Message}");
            }
        }

        private void UpdateStatistics()
        {
            if (string.IsNullOrEmpty(Content))
            {
                WordCount = 0;
                ParagraphCount = 0;
                UpdateLineNumbers();
                return;
            }

            int count = 0;
            bool inWord = false;

            foreach (char c in Content)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    inWord = false;
                }
                else if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    count++;
                    inWord = false;
                }
                else if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }

            WordCount = count;

            var paragraphs = Content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            ParagraphCount = paragraphs.Count(p => !string.IsNullOrWhiteSpace(p));

            UpdateLineNumbers();
            UpdateOutline();
        }

        private void UpdateLineNumbers()
        {
            var newLineCount = string.IsNullOrEmpty(Content) ? 1 : Content.Split('\n').Length;

            if (newLineCount == LineCount) return;

            LineCount = newLineCount;
            LineNumberList.Clear();
            for (int i = 1; i <= LineCount; i++)
            {
                LineNumberList.Add(i);
            }
        }

        #region 搜索替换方法

        private void ShowSearch()
        {
            ShowSearchBar = !ShowSearchBar;
        }

        private void UpdateSearchMatches()
        {
            _matchPositions.Clear();
            _currentMatchIndex = -1;

            if (string.IsNullOrEmpty(SearchText) || string.IsNullOrEmpty(Content))
            {
                MatchInfo = "";
                return;
            }

            int index = 0;
            while ((index = Content.IndexOf(SearchText, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                _matchPositions.Add(index);
                index += SearchText.Length;
            }

            if (_matchPositions.Count > 0)
            {
                _currentMatchIndex = 0;
                MatchInfo = $"1/{_matchPositions.Count} 个匹配";
            }
            else
            {
                MatchInfo = "无匹配";
            }
        }

        private void FindNext()
        {
            if (_matchPositions.Count == 0) return;

            _currentMatchIndex = (_currentMatchIndex + 1) % _matchPositions.Count;
            MatchInfo = $"{_currentMatchIndex + 1}/{_matchPositions.Count} 个匹配";

            SelectionRequested?.Invoke(_matchPositions[_currentMatchIndex], SearchText.Length);
        }

        private void FindPrevious()
        {
            if (_matchPositions.Count == 0) return;

            _currentMatchIndex = _currentMatchIndex <= 0 ? _matchPositions.Count - 1 : _currentMatchIndex - 1;
            MatchInfo = $"{_currentMatchIndex + 1}/{_matchPositions.Count} 个匹配";

            SelectionRequested?.Invoke(_matchPositions[_currentMatchIndex], SearchText.Length);
        }

        private void ReplaceOne()
        {
            if (_matchPositions.Count == 0 || _currentMatchIndex < 0) return;

            var pos = _matchPositions[_currentMatchIndex];
            Content = Content.Remove(pos, SearchText.Length).Insert(pos, ReplaceText);

            UpdateSearchMatches();
            StatusText = "已替换 1 处";
        }

        private void ReplaceAll()
        {
            if (string.IsNullOrEmpty(SearchText)) return;

            var count = _matchPositions.Count;
            if (count == 0) return;

            Content = Content.Replace(SearchText, ReplaceText, StringComparison.OrdinalIgnoreCase);

            UpdateSearchMatches();
            StatusText = $"已替换 {count} 处";
        }

        public event Action<int, int>? SelectionRequested;

        #endregion

        private void UpdatePreview()
        {
            if (string.IsNullOrEmpty(Content))
            {
                PreviewContent = "";
                PreviewDocument = new FlowDocument();
                SplitPreviewDocument = new FlowDocument();
                return;
            }

            var preview = Content;
            preview = Regex.Replace(preview, @"^#{1,6}\s+", "", RegexOptions.Multiline);
            preview = Regex.Replace(preview, @"\*\*(.+?)\*\*", "$1");
            preview = Regex.Replace(preview, @"\*(.+?)\*", "$1");
            preview = Regex.Replace(preview, @"^>\s+", "「", RegexOptions.Multiline);
            preview = Regex.Replace(preview, @"^[-*]\s+", "• ", RegexOptions.Multiline);
            preview = Regex.Replace(preview, @"^\d+\.\s+", "", RegexOptions.Multiline);
            PreviewContent = preview;

            PreviewDocument = MarkdownToFlowDocument(Content);
            SplitPreviewDocument = MarkdownToFlowDocument(Content);
        }

        private FlowDocument MarkdownToFlowDocument(string markdown)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 14,
                LineHeight = 24
            };

            var lines = markdown.Split('\n');
            bool inCodeBlock = false;
            var codeBlockContent = new StringBuilder();
            string codeLanguage = "";

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd('\r');

                if (trimmed.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeLanguage = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                        codeBlockContent.Clear();
                    }
                    else
                    {
                        var codeBlock = new Paragraph
                        {
                            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                            Foreground = new SolidColorBrush(Color.FromRgb(171, 178, 191)),
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 13,
                            Padding = new Thickness(12),
                            Margin = new Thickness(0, 8, 0, 8)
                        };
                        codeBlock.Inlines.Add(new Run(codeBlockContent.ToString().TrimEnd()));
                        doc.Blocks.Add(codeBlock);

                        inCodeBlock = false;
                        codeBlockContent.Clear();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    if (codeBlockContent.Length > 0) codeBlockContent.AppendLine();
                    codeBlockContent.Append(trimmed);
                    continue;
                }

                if (trimmed.StartsWith("# "))
                {
                    var heading = new Paragraph(new Run(trimmed.Substring(2)))
                    {
                        FontSize = 24,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 12, 0, 8)
                    };
                    doc.Blocks.Add(heading);
                }
                else if (trimmed.StartsWith("## "))
                {
                    var heading = new Paragraph(new Run(trimmed.Substring(3)))
                    {
                        FontSize = 20,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 10, 0, 6)
                    };
                    doc.Blocks.Add(heading);
                }
                else if (trimmed.StartsWith("### "))
                {
                    var heading = new Paragraph(new Run(trimmed.Substring(4)))
                    {
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    doc.Blocks.Add(heading);
                }
                else if (trimmed.StartsWith("> "))
                {
                    var quote = new Paragraph(new Run(trimmed.Substring(2)))
                    {
                        Margin = new Thickness(16, 4, 0, 4),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(8, 0, 0, 0),
                        Foreground = Brushes.Gray
                    };
                    doc.Blocks.Add(quote);
                }
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    var listItem = new Paragraph(new Run("• " + trimmed.Substring(2)))
                    {
                        Margin = new Thickness(16, 2, 0, 2)
                    };
                    doc.Blocks.Add(listItem);
                }
                else if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
                {
                    var match = Regex.Match(trimmed, @"^(\d+)\.\s(.*)");
                    if (match.Success)
                    {
                        var listItem = new Paragraph(new Run($"{match.Groups[1].Value}. {match.Groups[2].Value}"))
                        {
                            Margin = new Thickness(16, 2, 0, 2)
                        };
                        doc.Blocks.Add(listItem);
                    }
                }
                else if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    var separator = new Paragraph
                    {
                        Margin = new Thickness(0, 12, 0, 12)
                    };
                    separator.Inlines.Add(new Run("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")
                    {
                        Foreground = Brushes.LightGray
                    });
                    doc.Blocks.Add(separator);
                }
                else if (string.IsNullOrWhiteSpace(trimmed))
                {
                }
                else
                {
                    var para = new Paragraph
                    {
                        Margin = new Thickness(0, 4, 0, 4)
                    };

                    ParseInlineMarkdown(trimmed, para.Inlines);
                    doc.Blocks.Add(para);
                }
            }

            return doc;
        }

        private void ParseInlineMarkdown(string text, InlineCollection inlines)
        {
            var pattern = @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)|(\[(.+?)\]\((.+?)\))";
            var lastIndex = 0;

            foreach (Match match in Regex.Matches(text, pattern))
            {
                if (match.Index > lastIndex)
                {
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                if (match.Groups[2].Success)
                {
                    inlines.Add(new Bold(new Run(match.Groups[2].Value)));
                }
                else if (match.Groups[4].Success)
                {
                    inlines.Add(new Italic(new Run(match.Groups[4].Value)));
                }
                else if (match.Groups[6].Success)
                {
                    var codeRun = new Run(match.Groups[6].Value)
                    {
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 50, 50))
                    };
                    inlines.Add(codeRun);
                }
                else if (match.Groups[8].Success)
                {
                    var linkText = match.Groups[8].Value;
                    var linkUrl = match.Groups[9].Value;
                    var hyperlink = new Hyperlink(new Run(linkText))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(66, 133, 244)),
                        TextDecorations = null
                    };
                    hyperlink.Click += (s, e) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = linkUrl,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            DebugLogOnce("OpenHyperlink", ex);
                        }
                    };
                    inlines.Add(hyperlink);
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                inlines.Add(new Run(text.Substring(lastIndex)));
            }

            if (inlines.Count == 0)
            {
                inlines.Add(new Run(text));
            }
        }

        #endregion

        private void UpdateOutline()
        {
            OutlineItems.Clear();
            if (string.IsNullOrEmpty(Content)) return;

            var lines = Content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.StartsWith("# "))
                {
                    OutlineItems.Add(new OutlineItem
                    {
                        Title = line.Substring(2),
                        Level = 1,
                        LineNumber = i + 1,
                        Indent = new Thickness(0, 2, 0, 2)
                    });
                }
                else if (line.StartsWith("## "))
                {
                    OutlineItems.Add(new OutlineItem
                    {
                        Title = line.Substring(3),
                        Level = 2,
                        LineNumber = i + 1,
                        Indent = new Thickness(12, 2, 0, 2)
                    });
                }
                else if (line.StartsWith("### "))
                {
                    OutlineItems.Add(new OutlineItem
                    {
                        Title = line.Substring(4),
                        Level = 3,
                        LineNumber = i + 1,
                        Indent = new Thickness(24, 2, 0, 2)
                    });
                }
            }
        }
    }

    public class OutlineItem
    {
        public string Title { get; set; } = "";
        public int Level { get; set; }
        public int LineNumber { get; set; }
        public Thickness Indent { get; set; }
    }
}
