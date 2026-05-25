namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public class CrawlProgress
    {
        public int Current { get; set; }

        public int Total { get; set; }

        public string CurrentChapter { get; set; } = string.Empty;

        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;

        public string StatusMessage { get; set; } = string.Empty;

        public bool IsCrawling { get; set; }

        public bool IsCancelled { get; set; }
    }
}
