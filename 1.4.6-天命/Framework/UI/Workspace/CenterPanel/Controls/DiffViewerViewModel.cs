using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DiffViewerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _originalContent = string.Empty;
        private string _modifiedContent = string.Empty;
        private string _chapterId = string.Empty;
        private int _paragraphIndex = -1;

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

        public string DiffSummary
        {
            get
            {
                var originalWords = CountWords(OriginalContent);
                var modifiedWords = CountWords(ModifiedContent);
                var diff = modifiedWords - originalWords;
                var sign = diff >= 0 ? "+" : "";
                return $"原文 {originalWords} 字 → 修改后 {modifiedWords} 字（{sign}{diff}）";
            }
        }

        public ICommand AcceptCommand { get; }

        public ICommand RejectCommand { get; }

        public event Action<string, int, string>? Accepted;

        public event Action? Rejected;

        public void SetDiff(string chapterId, int paragraphIndex, string original, string modified)
        {
            ChapterId = chapterId;
            ParagraphIndex = paragraphIndex;
            OriginalContent = original;
            ModifiedContent = modified;
        }

        private void OnAccept()
        {
            Accepted?.Invoke(ChapterId, ParagraphIndex, ModifiedContent);
        }

        private void OnReject()
        {
            Rejected?.Invoke();
        }

        private int CountWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int count = 0;
            bool inWord = false;

            foreach (char c in text)
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

            return count;
        }
    }
}
