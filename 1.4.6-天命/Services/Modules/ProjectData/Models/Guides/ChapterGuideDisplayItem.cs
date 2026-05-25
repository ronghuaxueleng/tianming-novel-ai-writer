using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class ChapterGuideDisplayItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PaceType")] public string PaceType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("EmotionalTone")] public string EmotionalTone { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Characters")] public List<CharacterDisplayInfo> Characters { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Locations")] public List<LocationDisplayInfo> Locations { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ForeshadowingsSetup")] public List<ForeshadowingDisplayInfo> ForeshadowingsSetup { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ForeshadowingsPayoff")] public List<ForeshadowingDisplayInfo> ForeshadowingsPayoff { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Scenes")] public List<SceneDisplayInfo> Scenes { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("PreviousChapterSummary")] public string PreviousChapterSummary { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
    }

    public class CharacterDisplayInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    }

    public class LocationDisplayInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    }

    public class ForeshadowingDisplayInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Tier")] public string Tier { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;
    }

    public class SceneDisplayInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("SceneNumber")] public int SceneNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Purpose")] public string Purpose { get; set; } = string.Empty;
    }
}
