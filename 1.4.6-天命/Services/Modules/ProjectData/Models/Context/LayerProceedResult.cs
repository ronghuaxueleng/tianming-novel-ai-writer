using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class LayerProceedResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("CanProceed")] public bool CanProceed { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("Warnings")] public List<string> Warnings { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("IncompletedLayers")] public List<string> IncompletedLayers { get; set; } = new();

        public bool HasWarnings => Warnings.Count > 0;

        public string WarningSummary => HasWarnings 
            ? string.Join("；", Warnings) 
            : string.Empty;
    }
}
