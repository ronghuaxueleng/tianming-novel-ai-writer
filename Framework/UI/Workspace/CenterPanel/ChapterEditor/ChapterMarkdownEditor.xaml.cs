using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ChapterMarkdownEditor : UserControl
    {
        private readonly IGeneratedContentService _contentService;
        private readonly ChapterMarkdownEditorViewModel _viewModel;
        private string _currentId = string.Empty;
        private string _originalContent = string.Empty;
        private string _polishOriginal = string.Empty;
        private string _polishModified = string.Empty;
        private string _pendingPolishSaveContent = string.Empty;
        private bool _isUpdatingText = false;
        private bool _isSaving = false;
        private DispatcherTimer? _highlightDebounceTimer;
        private string _cachedEditorText = string.Empty;
        private DispatcherTimer? _selectionDebounceTimer;

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

        private static readonly SolidColorBrush HeadingColor;

        static ChapterMarkdownEditor()
        {
            HeadingColor = new SolidColorBrush(Color.FromRgb(66, 133, 244)); HeadingColor.Freeze();
        }

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
            };

            InlineEditPopup.PopupClosed += () =>
            {
                if (_viewModel.IsPolishSplitMode)
                {
                    _viewModel.IsPolishSplitMode = false;
                    _viewModel.IsEditMode = true;
                }
                if (_viewModel.IsDiffMode)
                {
                    ExitDiffMode();
                }
                ClearPendingPolishState();
            };

            InlineEditPopup.Rejected += () =>
            {
                if (_viewModel.IsPolishSplitMode)
                {
                    _viewModel.IsPolishSplitMode = false;
                    _viewModel.IsEditMode = true;
                }
                if (_viewModel.IsDiffMode)
                {
                    ExitDiffMode();
                }
                ClearPendingPolishState();
            };

            _viewModel.ContentSaved += content =>
            {
                _ = SaveAsync();
            };

            _viewModel.SelectionRequested += OnSelectionRequested;

            Loaded += (_, _) =>
            {
                EditorTextBox.SizeChanged += (s, e) =>
                {
                    if (e.WidthChanged)
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateVisualLineNumbers));
                };
            };

            Unloaded += (_, _) =>
            {
                _selectionDebounceTimer?.Stop();
                _highlightDebounceTimer?.Stop();
            };
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
            if (EditorTextBox == null) return;
            if (_selectionDebounceTimer == null)
            {
                _selectionDebounceTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(150) };
                _selectionDebounceTimer.Tick += (_, _) => { _selectionDebounceTimer.Stop(); UpdateLineColumn(); };
            }
            _selectionDebounceTimer.Stop();
            _selectionDebounceTimer.Start();
        }

        private void UpdateLineColumn()
        {
            if (EditorTextBox == null || _isUpdatingText) return;
            var (line, col) = SaveCaretPosition();
            _viewModel.CurrentLine = line + 1;
            _viewModel.CurrentColumn = col + 1;
        }

        private (int line, int charCol) SaveCaretPosition()
        {
            var caret = EditorTextBox?.CaretPosition;
            if (caret == null) return (0, 0);

            var para = caret.Paragraph;
            if (para == null) return (0, 0);

            int line = 0;
            for (var b = EditorTextBox!.Document.Blocks.FirstBlock; b != null && b != para; b = b.NextBlock)
                line++;

            var range = new TextRange(para.ContentStart, caret);
            int charCol = range.Text.Length;

            return (line, charCol);
        }

        private void OnRichTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText || _viewModel.IsDiffMode) return;

            _cachedEditorText = GetRichTextBoxText();
            _viewModel.Content = _cachedEditorText;
            ScheduleHeadingHighlight();
        }

        private string GetRichTextBoxText()
        {
            if (EditorTextBox?.Document == null) return "";
            var textRange = new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd);
            var text = textRange.Text;
            if (text.EndsWith("\r\n", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 2);
            return text;
        }

        private static int FindChangesStartIndex(string content)
            => GenerationGate.FindChangesStartIndex(content);

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
                    var trimmed = line.TrimEnd('\r');
                    var para = new Paragraph { Margin = new Thickness(0), LineHeight = 21 };
                    var run = new Run(trimmed);
                    ApplyHeadingStyle(run, trimmed);
                    para.Inlines.Add(run);
                    EditorTextBox.Document.Blocks.Add(para);
                }
            }

            _isUpdatingText = false;
            _cachedEditorText = GetRichTextBoxText();
        }

        private void ScheduleHeadingHighlight()
        {
            if (_highlightDebounceTimer == null)
            {
                _highlightDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(300) };
                _highlightDebounceTimer.Tick += (_, _) =>
                {
                    _highlightDebounceTimer.Stop();
                    ApplyHeadingOnlyHighlight();
                };
            }
            _highlightDebounceTimer.Stop();
            _highlightDebounceTimer.Start();
        }

        private void ApplyHeadingOnlyHighlight()
        {
            if (EditorTextBox?.Document == null || _isUpdatingText) return;
            _isUpdatingText = true;
            try
            {
                EditorTextBox.BeginChange();
                foreach (var block in EditorTextBox.Document.Blocks)
                {
                    if (block is not Paragraph para) continue;
                    if (para.Inlines.FirstInline is not Run run) continue;
                    ApplyHeadingStyle(run, run.Text);
                }
                EditorTextBox.EndChange();
            }
            finally
            {
                _isUpdatingText = false;
            }
        }

        private void ApplyHeadingStyle(Run run, string text)
        {
            if (text.StartsWith("# ", StringComparison.Ordinal))
            {
                run.Foreground = HeadingColor;
                run.FontWeight = FontWeights.Bold;
                run.FontSize = 18;
            }
            else if (text.StartsWith("## ", StringComparison.Ordinal))
            {
                run.Foreground = HeadingColor;
                run.FontWeight = FontWeights.SemiBold;
                run.FontSize = 16;
            }
            else if (text.StartsWith("### ", StringComparison.Ordinal))
            {
                run.Foreground = HeadingColor;
                run.FontWeight = FontWeights.SemiBold;
                run.ClearValue(Run.FontSizeProperty);
            }
            else
            {
                run.ClearValue(Run.ForegroundProperty);
                run.ClearValue(Run.FontWeightProperty);
                run.ClearValue(Run.FontSizeProperty);
            }
        }

        private void UpdateVisualLineNumbers()
        {
            if (EditorTextBox == null) return;

            try
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(EditorTextBox);
                if (scrollViewer == null) return;

                double contentHeight = scrollViewer.ExtentHeight;
                if (contentHeight <= 0) return;

                int visualLineCount = Math.Max(1, (int)Math.Round(contentHeight / 21.0));
                if (visualLineCount == _viewModel.LineNumberList.Count) return;

                _viewModel.LineNumberList.ReplaceAll(Enumerable.Range(1, visualLineCount).ToList());
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

        public string CurrentContent => _viewModel.Content;

        public string? CurrentChapterId => string.IsNullOrEmpty(_currentId) ? null : _currentId;

        public bool HasUnsavedChanges => _viewModel.IsDirty;

    }
}

