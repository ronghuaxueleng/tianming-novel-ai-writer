using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Guides
{
    public class CharacterStateGuide
    {
        [System.Text.Json.Serialization.JsonPropertyName("Module")] public string Module { get; set; } = "CharacterStateGuide";
        [System.Text.Json.Serialization.JsonPropertyName("SourceBookId")] public string SourceBookId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Characters")] public Dictionary<string, CharacterStateEntry> Characters { get; set; } = new();
    }

    public class CharacterStateEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BaseProfile")] public string BaseProfile { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("StateHistory")] public List<CharacterState> StateHistory { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DriftWarnings")] public List<string> DriftWarnings { get; set; } = new();
    }

    public class CharacterState
    {
        [System.Text.Json.Serialization.JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Phase")] public string Phase { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Level")] public string Level { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Abilities")] public List<string> Abilities { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("Relationships")] public Dictionary<string, RelationshipState> Relationships { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("MentalState")] public string MentalState { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class RelationshipState
    {
        [System.Text.Json.Serialization.JsonPropertyName("Relation")] public string Relation { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Trust")] public int Trust { get; set; }
    }
}
