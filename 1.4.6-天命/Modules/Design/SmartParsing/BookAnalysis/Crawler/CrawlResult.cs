using System.Collections.Generic;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public class CrawlResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("BookTitle")] public string BookTitle { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Author")] public string Author { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SourceUrl")] public string SourceUrl { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Chapters")] public List<ChapterContent> Chapters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("TotalWords")] public int TotalWords { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("CrawlTime")] public System.DateTime CrawlTime { get; set; } = System.DateTime.Now;
    }

    public class ChapterContent
    {
        [System.Text.Json.Serialization.JsonPropertyName("Index")] public int Index { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FileName")] public string FileName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Url")] public string Url { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Content")] public string Content { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("WordCount")] public int WordCount { get; set; }
    }
}
