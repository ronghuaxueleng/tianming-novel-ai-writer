using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class DiffViewerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _originalContent = string.Empty;
        private string _modifiedContent = string.Empty;
        private string _chapterId = string.Empty;
        private int _paragraphIndex = -1;
        private string _filePath = string.Empty;
        private string _filePreviewId = string.Empty;
        private bool _isFileMode;
        private bool _isPolishMode;
        private string _title = "内容对比";

        public DiffViewerViewModel()
        {
            AcceptCommand = new RelayCommand(OnAccept);
            RejectCommand = new RelayCommand(OnReject);
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string OriginalContent
        {
            get => _originalContent;
            set
            {
                if (_originalContent != value)
                {
                    _originalContent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DiffSummary));
                }
            }
        }

        public string ModifiedContent
        {
            get => _modifiedContent;
            set
            {
                if (_modifiedContent != value)
                {
                    _modifiedContent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DiffSummary));
                }
            }
        }

        public string ChapterId
        {
            get => _chapterId;
            set { if (_chapterId != value) { _chapterId = value; OnPropertyChanged(); } }
        }

        public int ParagraphIndex
        {
            get => _paragraphIndex;
            set { if (_paragraphIndex != value) { _paragraphIndex = value; OnPropertyChanged(); } }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FilePathVisibility));
                }
            }
        }

        public string FilePreviewId
        {
            get => _filePreviewId;
            set { if (_filePreviewId != value) { _filePreviewId = value; OnPropertyChanged(); } }
        }

        public bool IsFileMode
        {
            get => _isFileMode;
            set { if (_isFileMode != value) { _isFileMode = value; OnPropertyChanged(); } }
        }

        public bool IsPolishMode
        {
            get => _isPolishMode;
            set { if (_isPolishMode != value) { _isPolishMode = value; OnPropertyChanged(); } }
        }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public Visibility FilePathVisibility =>
            string.IsNullOrEmpty(_filePath) ? Visibility.Collapsed : Visibility.Visible;

        public string DiffSummary
        {
            get
            {
                if (_isFileMode)
                {
                    var oldLines = string.IsNullOrEmpty(_originalContent) ? 0 : _originalContent.Split('\n').Length;
                    var newLines = string.IsNullOrEmpty(_modifiedContent) ? 0 : _modifiedContent.Split('\n').Length;
                    var diff = newLines - oldLines;
                    var sign = diff >= 0 ? "+" : "";
                    return $"{oldLines} 行 → {newLines} 行（{sign}{diff}）";
                }
                else
                {
                    var originalWords = CountWords(OriginalContent);
                    var modifiedWords = CountWords(ModifiedContent);
                    var diff = modifiedWords - originalWords;
                    var sign = diff >= 0 ? "+" : "";
                    return $"原文 {originalWords} 字 → 修改后 {modifiedWords} 字（{sign}{diff}）";
                }
            }
        }

        public ICommand AcceptCommand { get; }
        public ICommand RejectCommand { get; }

        public event Action<string, int, string>? Accepted;

        public event Action? Rejected;

        public event Action<string>? FileAccepted;

        public event Action<string>? FileRejected;

        public void SetDiff(string chapterId, int paragraphIndex, string original, string modified)
        {
            IsFileMode = false;
            IsPolishMode = false;
            Title = "内容对比";
            ChapterId = chapterId;
            ParagraphIndex = paragraphIndex;
            FilePath = string.Empty;
            FilePreviewId = string.Empty;
            OriginalContent = original;
            ModifiedContent = modified;
        }

        public void SetFileDiff(string previewId, string filePath, string original, string modified)
        {
            IsFileMode = true;
            Title = "文件修改预览";
            ChapterId = string.Empty;
            ParagraphIndex = -1;
            FilePreviewId = previewId;
            FilePath = filePath;
            OriginalContent = original;
            ModifiedContent = modified;
        }

        private void OnAccept()
        {
            if (_isFileMode && !string.IsNullOrEmpty(_filePreviewId))
                FileAccepted?.Invoke(_filePreviewId);
            else
                Accepted?.Invoke(ChapterId, ParagraphIndex, ModifiedContent);
        }

        private void OnReject()
        {
            if (_isFileMode && !string.IsNullOrEmpty(_filePreviewId))
                FileRejected?.Invoke(_filePreviewId);
            else
                Rejected?.Invoke();
        }

        private int CountWords(string text) => WordCountHelper.CountRaw(text);
    }
}
