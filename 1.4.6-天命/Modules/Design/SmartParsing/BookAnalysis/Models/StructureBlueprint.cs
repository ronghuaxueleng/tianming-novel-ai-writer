using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Models
{
    public class StructureBlueprint
    {
        [JsonPropertyName("BookId")] public string BookId { get; set; } = string.Empty;
        [JsonPropertyName("BookTitle")] public string BookTitle { get; set; } = string.Empty;
        [JsonPropertyName("Author")] public string Author { get; set; } = string.Empty;
        [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [JsonPropertyName("Strategy")] public string Strategy { get; set; } = string.Empty;
        [JsonPropertyName("TargetCount")] public int TargetCount { get; set; } = 12;
        [JsonPropertyName("GoldenIndexes")] public List<int> GoldenIndexes { get; set; } = new();
        [JsonPropertyName("AnchorIndexes")] public Dictionary<string, int> AnchorIndexes { get; set; } = new();
        [JsonPropertyName("SelectedIndexes")] public List<int> SelectedIndexes { get; set; } = new();
        [JsonPropertyName("ReasonsByIndex")] public Dictionary<int, string> ReasonsByIndex { get; set; } = new();
        [JsonPropertyName("RawAiContent")] public string RawAiContent { get; set; } = string.Empty;
        [JsonPropertyName("TotalChapters")] public int TotalChapters { get; set; }
        [JsonPropertyName("TotalWords")] public int TotalWords { get; set; }
    }
}
