namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public class CrawlOptions
    {
        public CrawlMode Mode { get; set; } = CrawlMode.All;

        public int FirstNCount { get; set; } = 50;

        public int RangeStart { get; set; } = 1;

        public int RangeEnd { get; set; } = 100;

        public bool SkipVipChapters { get; set; } = true;

        public int MinDelayMs { get; set; } = 1000;

        public int MaxDelayMs { get; set; } = 3000;
    }

    public enum CrawlMode
    {
        All,

        FirstN,

        Range
    }
}
