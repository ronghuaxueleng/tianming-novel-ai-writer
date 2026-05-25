using System;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class LayerCompletionStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("Layers")] public Dictionary<string, LayerStatus> Layers { get; set; } = new()
        {
            ["SmartParsing"] = new LayerStatus(),
            ["Templates"] = new LayerStatus(),
            ["Worldview"] = new LayerStatus(),
            ["Characters"] = new LayerStatus(),
            ["Plot"] = new LayerStatus()
        };
    }

    public class LayerStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsCompleted")] public bool IsCompleted { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("CompletedAt")] public DateTime? CompletedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DataVersion")] public long DataVersion { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SummaryVersion")] public long SummaryVersion { get; set; }

        public bool NeedsRegeneration => DataVersion > SummaryVersion;
    }
}
