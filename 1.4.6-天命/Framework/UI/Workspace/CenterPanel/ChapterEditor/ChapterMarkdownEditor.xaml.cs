using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.Common.Controls;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ChapterMarkdownEditor : UserControl
    {
        private readonly IGeneratedContentService _contentService;
        private readonly ChapterMarkdownEditorViewModel _viewModel;
        private string _currentId = string.Empty;
        private string _originalContent = string.Empty;
        private bool _isUpdatingText = false;

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

            System.Diagnostics.Debug.WriteLine($"[ChapterMarkdownEditor] {key}: {ex.Message}");
        }

        private static readonly SolidColorBrush HeadingColor = new(Color.FromRgb(66, 133, 244));
        private static readonly SolidColorBrush BoldColor = new(Color.FromRgb(219, 68, 55));
        private static readonly SolidColorBrush ItalicColor = new(Color.FromRgb(244, 160, 0));
        private static readonly SolidColorBrush CodeColor = new(Color.FromRgb(15, 157, 88));
        private static readonly SolidColorBrush QuoteColor = new(Color.FromRgb(128, 128, 128));
        private static readonly SolidColorBrush ListColor = new(Color.FromRgb(171, 71, 188));
        private static readonly SolidColorBrush LinkColor = new(Color.FromRgb(66, 133, 244));

        public event EventHandler<ContentModifiedEventArgs>? ContentModified;

        public event Action<string, string>? ChapterSaved;

        public ChapterMarkdownEditor()
        {
            InitializeComponent();
            _contentService = ServiceLocator.Get<GeneratedContentService>();
            _viewModel = new ChapterMarkdownEditorViewModel();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ChapterMarkdownEditorViewModel.Content))
                {
                    OnContentChanged();
                }
                else if (e.PropertyName == nameof(ChapterMarkdownEditorViewModel.IsSplitMode))
                {
                    if (_viewModel.IsSplitMode)
                    {
                        SetSplitEditorText(_viewModel.Content);
                    }
                }
            };

            InlineEditPopup.PopupClosed += () =>
            {
                if (_viewModel.IsPolishSplitMode)
                {
                    _viewModel.IsPolishSplitMode = false;
                    _viewModel.IsEditMode = true;
                }
            };

            InlineEditPopup.Rejected += () =>
            {
                if (_viewModel.IsPolishSplitMode)
                {
                    _viewModel.IsPolishSplitMode = false;
                    _viewModel.IsEditMode = true;
                }
            };

            _viewModel.ContentSaved += content =>
            {
                _ = SaveAsync();
            };

            _viewModel.SelectionRequested += OnSelectionRequested;

            _viewModel.JumpToLineRequested += OnJumpToLine;

            Loaded += (_, _) =>
            {
                EditorTextBox.SizeChanged += (s, e) =>
                {
                    if (e.WidthChanged)
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateVisualLineNumbers));
                };
            };
        }

        private void OnJumpToLine(int lineNumber)
        {
            if (EditorTextBox?.Document == null) return;

            var text = GetRichTextBoxText();
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Split('\n');
            int charIndex = 0;
            for (int i = 0; i < lineNumber - 1 && i < lines.Length; i++)
            {
                charIndex += lines[i].Length + 1;
            }

            EditorTextBox.Focus();
            var pos = EditorTextBox.Document.ContentStart.GetPositionAtOffset(charIndex + lineNumber);
            if (pos != null)
            {
                EditorTextBox.CaretPosition = pos;
                EditorTextBox.ScrollToVerticalOffset(Math.Max(0, (lineNumber - 5) * 21));
            }
        }

        private void OnEditorScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (LineNumberScroller != null)
            {
                LineNumberScroller.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void OnSelectionRequested(int start, int length)
        {
            if (EditorTextBox?.Document == null) return;

            EditorTextBox.Focus();
            var startPos = EditorTextBox.Document.ContentStart.GetPositionAtOffset(start);
            var endPos = EditorTextBox.Document.ContentStart.GetPositionAtOffset(start + length);

            if (startPos != null && endPos != null)
            {
                EditorTextBox.Selection.Select(startPos, endPos);
            }
        }

        private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (EditorTextBox != null)
            {
                var text = GetRichTextBoxText();
                var caretPos = EditorTextBox.Document.ContentStart.GetOffsetToPosition(EditorTextBox.CaretPosition);

                var line = 1;
                var column = 1;
                int charCount = 0;
                foreach (char c in text)
                {
                    if (charCount >= caretPos) break;
                    if (c == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                    charCount++;
                }

                _viewModel.CurrentLine = line;
                _viewModel.CurrentColumn = column;
            }
        }

        private void OnRichTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText) return;

            var text = GetRichTextBoxText();
            _viewModel.Content = text;

            ApplySyntaxHighlighting();
        }

        private void OnSplitEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText) return;

            var text = GetSplitEditorText();
            _viewModel.Content = text;
        }

        private string GetSplitEditorText()
        {
            if (SplitEditorTextBox?.Document == null) return "";
            var textRange = new TextRange(SplitEditorTextBox.Document.ContentStart, SplitEditorTextBox.Document.ContentEnd);
            return textRange.Text;
        }

        private void SetSplitEditorText(string text)
        {
            if (SplitEditorTextBox?.Document == null) return;

            _isUpdatingText = true;
            SplitEditorTextBox.Document.Blocks.Clear();

            if (!string.IsNullOrEmpty(text))
            {
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(text));
                SplitEditorTextBox.Document.Blocks.Add(paragraph);
            }

            _isUpdatingText = false;
        }

        private string GetRichTextBoxText()
        {
            if (EditorTextBox?.Document == null) return "";
            var textRange = new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd);
            return textRange.Text.TrimEnd('\r', '\n');
        }

        private static readonly Regex ChangesSeparatorLineRegex = new(
            @"(?m)^\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*CHANGES\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MdChangesHeaderRegex = new(
            @"(?m)^(?:---\s*\n+\s*)?#{1,3}\s*(?:CHANGES|变更记录|变更摘要|状态变更)\s*\n",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int FindChangesStartIndex(string content)
        {
            var idx = content.IndexOf(GenerationGate.ChangesSeparator, StringComparison.Ordinal);
            if (idx >= 0) return idx;

            var regexMatch = ChangesSeparatorLineRegex.Match(content);
            if (regexMatch.Success) return regexMatch.Index;

            var mdMatch = MdChangesHeaderRegex.Match(content);
            if (mdMatch.Success) return mdMatch.Index;

            return -1;
        }

        private static string? ExtractChangesBlock(string content)
        {
            var idx = FindChangesStartIndex(content);
            if (idx < 0) return null;
            return content.Substring(idx).TrimEnd();
        }

        private void SetRichTextBoxText(string text)
        {
            if (EditorTextBox?.Document == null) return;

            _isUpdatingText = true;
            EditorTextBox.Document.Blocks.Clear();

            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Split('\n');
                foreach (var line in lines)
                {
                    var para = new Paragraph { Margin = new Thickness(0), LineHeight = 21 };
                    para.Inlines.Add(new Run(line.TrimEnd('\r')));
                    EditorTextBox.Document.Blocks.Add(para);
                }
            }

            _isUpdatingText = false;
            ApplySyntaxHighlighting();
        }

        private void ApplySyntaxHighlighting()
        {
            if (EditorTextBox?.Document == null || _isUpdatingText) return;

            _isUpdatingText = true;

            try
            {
                var text = _viewModel.Content;
                if (string.IsNullOrEmpty(text))
                {
                    _isUpdatingText = false;
                    return;
                }

                var caretOffset = EditorTextBox.Document.ContentStart.GetOffsetToPosition(EditorTextBox.CaretPosition);

                EditorTextBox.Document.Blocks.Clear();
                var lines = text.Split('\n');

                foreach (var line in lines)
                {
                    var para = new Paragraph { Margin = new Thickness(0), LineHeight = 21 };
                    var trimmed = line.TrimEnd('\r');

                    if (trimmed.StartsWith("# "))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = HeadingColor, FontWeight = FontWeights.Bold, FontSize = 18 });
                    }
                    else if (trimmed.StartsWith("## "))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = HeadingColor, FontWeight = FontWeights.SemiBold, FontSize = 16 });
                    }
                    else if (trimmed.StartsWith("### "))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = HeadingColor, FontWeight = FontWeights.SemiBold });
                    }
                    else if (trimmed.StartsWith("> "))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = QuoteColor, FontStyle = FontStyles.Italic });
                    }
                    else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || Regex.IsMatch(trimmed, @"^\d+\.\s"))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = ListColor });
                    }
                    else if (trimmed.StartsWith("```"))
                    {
                        para.Inlines.Add(new Run(trimmed) { Foreground = CodeColor, FontFamily = new FontFamily("Consolas") });
                    }
                    else
                    {
                        HighlightInlineMarkdown(trimmed, para.Inlines);
                    }

                    EditorTextBox.Document.Blocks.Add(para);
                }

                try
                {
                    var newPos = EditorTextBox.Document.ContentStart.GetPositionAtOffset(caretOffset);
                    if (newPos != null)
                    {
                        EditorTextBox.CaretPosition = newPos;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("RestoreCaretPosition", ex);
                }
            }
            finally
            {
                _isUpdatingText = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateVisualLineNumbers));
            }
        }

        private void UpdateVisualLineNumbers()
        {
            if (EditorTextBox == null) return;

            try
            {
                EditorTextBox.UpdateLayout();

                var scrollViewer = FindVisualChild<ScrollViewer>(EditorTextBox);
                if (scrollViewer == null) return;

                double contentHeight = scrollViewer.ExtentHeight;
                if (contentHeight <= 0) return;

                int visualLineCount = Math.Max(1, (int)Math.Round(contentHeight / 21.0));
                if (visualLineCount == _viewModel.LineNumberList.Count) return;

                _viewModel.LineNumberList.Clear();
                for (int i = 1; i <= visualLineCount; i++)
                    _viewModel.LineNumberList.Add(i);
                _viewModel.LineCount = visualLineCount;
            }
            catch (Exception ex)
            {
                DebugLogOnce("UpdateVisualLineNumbers", ex);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void HighlightInlineMarkdown(string text, InlineCollection inlines)
        {
            if (string.IsNullOrEmpty(text))
            {
                inlines.Add(new Run(""));
                return;
            }

            var pattern = @"(\*\*(.+?)\*\*)|(\*([^*]+?)\*)|(`([^`]+?)`)|(\[([^\]]+?)\]\(([^)]+?)\))";
            var lastIndex = 0;

            foreach (Match match in Regex.Matches(text, pattern))
            {
                if (match.Index > lastIndex)
                {
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                if (match.Groups[2].Success)
                {
                    inlines.Add(new Run(match.Value) { Foreground = BoldColor, FontWeight = FontWeights.Bold });
                }
                else if (match.Groups[4].Success)
                {
                    inlines.Add(new Run(match.Value) { Foreground = ItalicColor, FontStyle = FontStyles.Italic });
                }
                else if (match.Groups[6].Success)
                {
                    inlines.Add(new Run(match.Value) { Foreground = CodeColor, FontFamily = new FontFamily("Consolas") });
                }
                else if (match.Groups[8].Success)
                {
                    inlines.Add(new Run(match.Value) { Foreground = LinkColor, TextDecorations = TextDecorations.Underline });
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

        public string CurrentContent => _viewModel.Content;

        public string? CurrentChapterId => string.IsNullOrEmpty(_currentId) ? null : _currentId;

        public bool HasUnsavedChanges => _viewModel.IsDirty;

        public void SetContent(string content)
        {
            _viewModel.Content = content;
            SetRichTextBoxText(content);
        }

        public string GetContent()
        {
            return _viewModel.Content;
        }

        public async Task<string?> LoadChapterContentAsync(ChapterInfo chapter)
        {
            try
            {
                var content = await _contentService.GetChapterAsync(chapter.Id);
                return content;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterMarkdownEditor] 加载章节失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
                return null;
            }
        }

        public void LoadTabContent(string id, string content, string originalContent)
        {
            _currentId = id;
            _originalContent = originalContent;
            _viewModel.Content = content;
            _viewModel.IsDirty = content != originalContent;
            SetRichTextBoxText(content);

            if (_viewModel.IsSplitMode)
            {
                SetSplitEditorText(content);
            }

            ResetEditorScrollToTop();

            TM.App.Log($"[ChapterMarkdownEditor] 切换到标签: {id}");
        }

        public void LoadNewContent(string chapterId, string title, string content)
        {
            _currentId = chapterId;
            _originalContent = string.Empty;
            _viewModel.Content = content;
            _viewModel.IsDirty = true;
            SetRichTextBoxText(content);

            if (_viewModel.IsSplitMode)
            {
                SetSplitEditorText(content);
            }

            ResetEditorScrollToTop();

            TM.App.Log($"[ChapterMarkdownEditor] 加载新生成内容: {chapterId}");
        }

        public void Clear()
        {
            _currentId = string.Empty;
            _originalContent = string.Empty;
            _viewModel.Content = string.Empty;
            _viewModel.IsDirty = false;
            SetRichTextBoxText("");
            ResetEditorScrollToTop();
        }

        private void ResetEditorScrollToTop()
        {
            if (EditorTextBox == null)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    EditorTextBox.ScrollToHome();
                    EditorTextBox.ScrollToVerticalOffset(0);

                    if (LineNumberScroller != null)
                    {
                        LineNumberScroller.ScrollToVerticalOffset(0);
                    }

                    if (_viewModel.IsSplitMode && SplitEditorTextBox != null)
                    {
                        SplitEditorTextBox.ScrollToHome();
                        SplitEditorTextBox.ScrollToVerticalOffset(0);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("ResetEditorScrollToTop", ex);
                }
            }));
        }

        public void SwitchToEditMode()
        {
            _viewModel.IsEditMode = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentId)) return;

            try
            {
                var chapterId = _currentId;
                var content = _viewModel.Content ?? string.Empty;

                var _edParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_edParsed.HasValue && _edParsed.Value.chapterNumber == 1 && _edParsed.Value.volumeNumber > 1)
                {
                    var _edPrevVol = _edParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _edArchiveStore = ServiceLocator.Get<VolumeFactArchiveStore>();
                        var _edPrevArchives = await _edArchiveStore.GetPreviousArchivesAsync(_edParsed.Value.volumeNumber);
                        if (!_edPrevArchives.Any(a => a.VolumeNumber == _edPrevVol))
                        {
                            TM.App.Log($"[ChapterEditor] 编辑器保存检测到新卷第1章，自动存档第{_edPrevVol}卷...");
                            var _edReconciler = ServiceLocator.Get<ConsistencyReconciler>();
                            await _edReconciler.AutoArchiveVolumeIfNeededAsync(_edPrevVol);
                        }
                    }
                    catch (Exception _edEx)
                    {
                        TM.App.Log($"[ChapterEditor] 第{_edParsed.Value.volumeNumber - 1}卷存檔检查失败（不阻断保存）: {_edEx.Message}");
                    }
                }

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                await callback.OnExternalContentSavedAsync(chapterId, content);

                var persisted = await _contentService.GetChapterAsync(chapterId) ?? content;

                _originalContent = persisted;
                _viewModel.Content = persisted;
                _viewModel.IsDirty = false;
                _viewModel.StatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
                SetRichTextBoxText(persisted);
                ChapterSaved?.Invoke(chapterId, persisted);
                GlobalToast.Success("已保存", $"章节 {chapterId} 已保存");
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"保存失败: {ex.Message}";
                StandardDialog.ShowError($"保存失败: {ex.Message}", "错误");
            }
        }

        private void OnContentChanged()
        {
            if (!string.IsNullOrEmpty(_currentId))
            {
                ContentModified?.Invoke(this, new ContentModifiedEventArgs
                {
                    Id = _currentId,
                    Content = _viewModel.Content
                });
            }
        }

        private void OnInlineEditClick(object sender, RoutedEventArgs e)
        {
            if (EditorTextBox?.Document == null)
            {
                return;
            }

            var selectedRange = new TextRange(EditorTextBox.Selection.Start, EditorTextBox.Selection.End);
            var selectedText = selectedRange.Text;

            var targetRange = string.IsNullOrWhiteSpace(selectedText)
                ? new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd)
                : selectedRange;

            selectedText = targetRange.Text;
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                GlobalToast.Warning("暂无内容", "当前章节没有可润色的文本");
                return;
            }

            var fullContentBeforeEdit = GetRichTextBoxText();
            var changesBlockToRestore = ExtractChangesBlock(fullContentBeforeEdit);

            InlineEditPopup.Show(
                selectedText,
                onAccept: (original, modified) =>
                {
                    targetRange.Text = modified;

                    var newText = GetRichTextBoxText();
                    if (changesBlockToRestore != null && FindChangesStartIndex(newText) < 0)
                    {
                        newText = newText.TrimEnd() + "\n\n" + changesBlockToRestore;
                        var fullRange = new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd);
                        fullRange.Text = newText;
                        TM.App.Log("[ChapterMarkdownEditor] 内联编辑：已补回原CHANGES块");
                    }

                    newText = GetRichTextBoxText();
                    _viewModel.Content = newText;
                    _viewModel.IsDirty = newText != _originalContent;

                    TM.App.Log("[ChapterMarkdownEditor] 应用内联编辑");
                },
                onShowDiff: (original, modified) =>
                {
                    _viewModel.IsSplitMode = true;
                    _viewModel.IsPolishSplitMode = true;
                    SetSplitEditorText(modified);
                    InlineEditPopup.Hide();
                    GlobalToast.Info("分屏对比", "左侧为原文，右侧为修改后内容。点击\"润色中\"按钮确认或拒绝");
                });
        }

        private void OnPolishButtonClick(object sender, RoutedEventArgs e)
        {
            InlineEditPopup.Visibility = System.Windows.Visibility.Visible;
        }

        private void OnSplitInlineEditClick(object sender, RoutedEventArgs e)
        {
            if (SplitEditorTextBox?.Document == null)
            {
                return;
            }

            var selectedRange = new TextRange(SplitEditorTextBox.Selection.Start, SplitEditorTextBox.Selection.End);
            var selectedText = selectedRange.Text;

            var targetRange = string.IsNullOrWhiteSpace(selectedText)
                ? new TextRange(SplitEditorTextBox.Document.ContentStart, SplitEditorTextBox.Document.ContentEnd)
                : selectedRange;

            selectedText = targetRange.Text;
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                GlobalToast.Warning("暂无内容", "当前章节没有可润色的文本");
                return;
            }

            InlineEditPopup.Show(
                selectedText,
                onAccept: (original, modified) =>
                {
                    targetRange.Text = modified;

                    var newText = GetSplitEditorText();
                    _viewModel.Content = newText;
                    _viewModel.IsDirty = newText != _originalContent;
                    TM.App.Log("[ChapterMarkdownEditor] 分屏编辑器应用AIGC");
                },
                onShowDiff: (original, modified) =>
                {
                    _viewModel.IsPolishSplitMode = true;
                    SetSplitEditorText(modified);
                    InlineEditPopup.Hide();
                    GlobalToast.Info("分屏对比", "左侧为原文，右侧为修改后内容。点击\"润色中\"按钮确认或拒绝");
                });
        }

        public void ApplyInlineDiff(string original, string modified)
        {
            if (string.IsNullOrEmpty(original))
            {
                return;
            }

            var text = GetRichTextBoxText();
            var index = text.IndexOf(original, StringComparison.Ordinal);
            if (index < 0)
            {
                TM.App.Log("[ChapterMarkdownEditor] 未在当前内容中找到原文片段，无法应用 Diff");
                return;
            }

            var newText = text.Remove(index, original.Length).Insert(index, modified);
            SetRichTextBoxText(newText);

            _viewModel.Content = newText;
            _viewModel.IsDirty = newText != _originalContent;
        }
    }
}
