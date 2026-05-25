namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public interface INovelParser
    {
        string[] SupportedDomains { get; }

        string SiteName { get; }

        NovelInfo? ParseNovelInfo(string html);

        NovelCatalog? ParseCatalog(string html);

        string ParseChapterContent(string html);
    }
}
