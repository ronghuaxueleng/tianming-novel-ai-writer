using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Validation;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.TaskContexts
{
    public class ValidateTaskContext
    {
        [JsonPropertyName("ChapterId")]
        public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("Characters")]
        public List<CharacterRulesData> Characters { get; set; } = new();
        [JsonPropertyName("Locations")]
        public List<LocationRulesData> Locations { get; set; } = new();
        [JsonPropertyName("PlotRules")]
        public List<PlotRulesData> PlotRules { get; set; } = new();

        [JsonPropertyName("PreviousChapterSummary")]
        public string PreviousChapterSummary { get; set; } = string.Empty;
        [JsonPropertyName("CurrentChapterContent")]
        public string CurrentChapterContent { get; set; } = string.Empty;

        [JsonPropertyName("ConsistencyRules")]
        public List<ConsistencyRule> ConsistencyRules { get; set; } = new();
        [JsonPropertyName("QualityRules")]
        public List<QualityRule> QualityRules { get; set; } = new();
    }
}
