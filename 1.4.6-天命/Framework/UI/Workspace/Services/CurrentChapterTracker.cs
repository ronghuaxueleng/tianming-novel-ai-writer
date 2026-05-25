using System;

namespace TM.Framework.UI.Workspace.Services
{
    public static class CurrentChapterTracker
    {
        private static string _currentChapterId = string.Empty;
        private static string _currentChapterTitle = string.Empty;

        public static event EventHandler<string>? ChapterChanged;

        public static string CurrentChapterId
        {
            get => _currentChapterId;
            private set
            {
                if (_currentChapterId != value)
                {
                    _currentChapterId = value;
                    ChapterChanged?.Invoke(null, value);
                }
            }
        }

        public static string CurrentChapterTitle
        {
            get => _currentChapterTitle;
            private set => _currentChapterTitle = value;
        }

        public static bool HasCurrentChapter => !string.IsNullOrEmpty(_currentChapterId);

        public static void SetCurrentChapter(string chapterId, string? chapterTitle = null)
        {
            CurrentChapterTitle = chapterTitle ?? string.Empty;
            CurrentChapterId = chapterId ?? string.Empty;

            if (!string.IsNullOrEmpty(chapterId))
            {
                TM.App.Log($"[CurrentChapterTracker] 当前章节: {chapterId} - {chapterTitle}");
            }
        }

        public static void Clear()
        {
            CurrentChapterId = string.Empty;
            CurrentChapterTitle = string.Empty;
            TM.App.Log("[CurrentChapterTracker] 已清除当前章节");
        }
    }
}
