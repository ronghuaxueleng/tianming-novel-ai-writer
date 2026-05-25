using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Models
{
    public class CrawledContent
    {
        [JsonPropertyName("BookId")]
        public string BookId { get; set; } = string.Empty;

        [JsonPropertyName("BookTitle")]
        public string BookTitle { get; set; } = string.Empty;

        [JsonPropertyName("Author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("Chapters")]
        public List<CrawledChapter> Chapters { get; set; } = new();

        [JsonPropertyName("TotalChapters")]
        public int TotalChapters { get; set; }

        [JsonPropertyName("TotalWords")]
        public int TotalWords { get; set; }

        [JsonPropertyName("CrawledAt")]
        public DateTime CrawledAt { get; set; } = DateTime.Now;

        [JsonPropertyName("SourceUrl")]
        public string SourceUrl { get; set; } = string.Empty;

        [JsonPropertyName("SourceSite")]
        public string SourceSite { get; set; } = string.Empty;
    }

    public class CrawledChapter
    {
        [JsonPropertyName("Index")]
        public int Index { get; set; }

        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("FileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("Content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("WordCount")]
        public int WordCount { get; set; }

        [JsonPropertyName("Url")]
        public string Url { get; set; } = string.Empty;
    }
}
