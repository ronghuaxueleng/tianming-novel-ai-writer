namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public static class ChapterDiffContext
    {
        private static readonly object _lock = new();
        private static string? _oldContent;
        private static string? _newContent;
        private static string? _chapterId;

        public static void SetOld(string chapterId, string oldContent)
        {
            lock (_lock)
            {
                _chapterId = chapterId;
                _oldContent = oldContent;
                _newContent = null;
            }
        }

        public static void SetNew(string chapterId, string newContent)
        {
            lock (_lock)
            {
                if (_chapterId == chapterId)
                    _newContent = newContent;
            }
        }

        public static (string? Old, string? New) Take()
        {
            lock (_lock)
            {
                var result = (_oldContent, _newContent);
                _oldContent = null;
                _newContent = null;
                _chapterId = null;
                return result;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _oldContent = null;
                _newContent = null;
                _chapterId = null;
            }
        }
    }
}
