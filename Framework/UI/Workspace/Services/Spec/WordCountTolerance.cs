namespace TM.Framework.UI.Workspace.Services.Spec
{
    public static class WordCountTolerance
    {
        public const double LowerBound = 0.62;

        public const double UpperBound = 1.38;

        public static int GetMinWordCount(int targetWordCount) => (int)(targetWordCount * LowerBound);

        public static int GetMaxWordCount(int targetWordCount) => (int)(targetWordCount * UpperBound);
    }
}
