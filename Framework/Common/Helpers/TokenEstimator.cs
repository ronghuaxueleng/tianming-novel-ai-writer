namespace TM.Framework.Common.Helpers
{
    public static class TokenEstimator
    {
        public static int CountTokens(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return FallbackEstimate(text);
        }

        public static int CountTokens(Microsoft.SemanticKernel.ChatCompletion.ChatHistory history)
        {
            if (history == null || history.Count == 0) return 0;

            int total = 0;
            foreach (var msg in history)
            {
                total += CountTokens(msg.Content);
                total += 4;
            }
            return total;
        }

        private static int FallbackEstimate(string text)
        {
            int chineseCount = 0;
            int otherCount = 0;

            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF)
                {
                    chineseCount++;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    otherCount++;
                }
            }

            return (int)(chineseCount * 1.5 + otherCount * 0.25);
        }
    }
}
