namespace TM.Framework.Common.Helpers
{
    public static class WordCountHelper
    {
        public static int CountRaw(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c)) count++;
            }
            return count;
        }
    }
}
