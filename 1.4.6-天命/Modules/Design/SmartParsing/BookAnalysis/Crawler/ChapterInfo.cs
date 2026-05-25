namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public class ChapterInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Index")] public int Index { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Url")] public string Url { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsVip")] public bool IsVip { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsCrawled")] public bool IsCrawled { get; set; }
    }
}
