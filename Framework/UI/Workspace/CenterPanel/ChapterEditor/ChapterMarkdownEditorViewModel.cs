using System;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ChapterMarkdownEditorViewModel : INotifyPropertyChanged
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
        private bool _isEditMode = true;
        private bool _isPolishSplitMode;
        private bool _isDiffMode;
        private string _diffOriginalContent = "";
        private string _diffModifiedContent = "";
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
        private DispatcherTimer? _statisticsDebounceTimer;
        private DispatcherTimer? _searchDebounceTimer;

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
                    ScheduleStatisticsUpdate();
                }
            }
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
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

        public bool IsDiffMode
        {
            get => _isDiffMode;
            set
            {
                if (_isDiffMode != value)
                {
                    _isDiffMode = value;
                    if (value)
                    {
                        IsEditMode = false;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public string DiffOriginalContent
        {
            get => _diffOriginalContent;
            set { if (_diffOriginalContent != value) { _diffOriginalContent = value; OnPropertyChanged(); } }
        }

        public string DiffModifiedContent
        {
            get => _diffModifiedContent;
            set { if (_diffModifiedContent != value) { _diffModifiedContent = value; OnPropertyChanged(); } }
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

        public RangeObservableCollection<int> LineNumberList { get; } = new();

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
                    ScheduleSearchUpdate();
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
            if (!string.IsNullOrEmpty(Content) && !Content.EndsWith('\n'))
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

            WordCount = WordCountHelper.CountRaw(Content);

            int lineCount = 1;
            foreach (char c in Content)
            {
                if (c == '\n') lineCount++;
            }

            var paragraphs = Content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            ParagraphCount = paragraphs.Count(p => !string.IsNullOrWhiteSpace(p));

            UpdateLineNumbers(lineCount);
        }

        private void ScheduleStatisticsUpdate()
        {
            if (_statisticsDebounceTimer == null)
            {
                _statisticsDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _statisticsDebounceTimer.Tick += (_, _) => { _statisticsDebounceTimer.Stop(); UpdateStatistics(); };
            }
            _statisticsDebounceTimer.Stop();
            _statisticsDebounceTimer.Start();
        }

        private void UpdateLineNumbers(int precomputedLineCount = -1)
        {
            var newLineCount = precomputedLineCount >= 1
                ? precomputedLineCount
                : string.IsNullOrEmpty(Content) ? 1 : Content.Count(c => c == '\n') + 1;

            if (newLineCount == LineCount) return;

            LineCount = newLineCount;
            LineNumberList.ReplaceAll(Enumerable.Range(1, LineCount).ToList());
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
            FlushPendingSearch();
            if (_matchPositions.Count == 0) return;

            _currentMatchIndex = (_currentMatchIndex + 1) % _matchPositions.Count;
            MatchInfo = $"{_currentMatchIndex + 1}/{_matchPositions.Count} 个匹配";

            SelectionRequested?.Invoke(_matchPositions[_currentMatchIndex], SearchText.Length);
        }

        private void FindPrevious()
        {
            FlushPendingSearch();
            if (_matchPositions.Count == 0) return;

            _currentMatchIndex = _currentMatchIndex <= 0 ? _matchPositions.Count - 1 : _currentMatchIndex - 1;
            MatchInfo = $"{_currentMatchIndex + 1}/{_matchPositions.Count} 个匹配";

            SelectionRequested?.Invoke(_matchPositions[_currentMatchIndex], SearchText.Length);
        }

        private void ReplaceOne()
        {
            FlushPendingSearch();
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

        private void FlushPendingSearch()
        {
            if (_searchDebounceTimer != null && _searchDebounceTimer.IsEnabled)
            {
                _searchDebounceTimer.Stop();
                UpdateSearchMatches();
            }
        }

        private void ScheduleSearchUpdate()
        {
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _searchDebounceTimer.Tick += (_, _) => { _searchDebounceTimer.Stop(); UpdateSearchMatches(); };
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        #endregion
    }
}
