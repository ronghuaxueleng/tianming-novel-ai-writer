using System.ComponentModel;
using System.Reflection;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ShortStoryChapterBlueprintVM : INotifyPropertyChanged
    {
        private int _chapterIndex;
        private string _title = string.Empty;
        private string _keyEvents = string.Empty;
        private string _characters = string.Empty;
        private string _endingNote = string.Empty;
        private string _targetWordCount = string.Empty;

        public int ChapterIndex
        {
            get => _chapterIndex;
            set { _chapterIndex = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string KeyEvents
        {
            get => _keyEvents;
            set { _keyEvents = value; OnPropertyChanged(); }
        }

        public string Characters
        {
            get => _characters;
            set { _characters = value; OnPropertyChanged(); }
        }

        public string EndingNote
        {
            get => _endingNote;
            set { _endingNote = value; OnPropertyChanged(); }
        }

        public string TargetWordCount
        {
            get => _targetWordCount;
            set { _targetWordCount = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
