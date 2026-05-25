using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class CharacterStateChange
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("NewLevel")] public string NewLevel { get; set; } = string.Empty;
        [JsonPropertyName("NewAbilities")] public List<string> NewAbilities { get; set; } = new();
        [JsonPropertyName("LostAbilities")] public List<string> LostAbilities { get; set; } = new();
        [JsonPropertyName("RelationshipChanges")] public Dictionary<string, RelationshipChange> RelationshipChanges { get; set; } = new();
        [JsonPropertyName("NewMentalState")] public string NewMentalState { get; set; } = string.Empty;
        [JsonPropertyName("KeyEvent")] public string KeyEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class RelationshipChange
    {
        [JsonPropertyName("Relation")] public string Relation { get; set; } = string.Empty;
        [JsonPropertyName("TrustDelta")] public int TrustDelta { get; set; }
    }

    public class ConflictProgressChange
    {
        [JsonPropertyName("ConflictId")] public string ConflictId { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class PlotPointChange
    {
        [JsonPropertyName("Keywords")] public List<string> Keywords { get; set; } = new();
        [JsonPropertyName("Context")] public string Context { get; set; } = string.Empty;
        [JsonPropertyName("InvolvedCharacters")] public List<string> InvolvedCharacters { get; set; } = new();
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("Storyline")] public string Storyline { get; set; } = "main";
    }

    public class ForeshadowingAction
    {
        [JsonPropertyName("ForeshadowId")] public string ForeshadowId { get; set; } = string.Empty;
        [JsonPropertyName("Action")] public string Action { get; set; } = string.Empty;
    }

    public class LocationStateChange
    {
        [JsonPropertyName("LocationId")] public string LocationId { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class FactionStateChange
    {
        [JsonPropertyName("FactionId")] public string FactionId { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = string.Empty;
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class TimeProgressionChange
    {
        [JsonPropertyName("TimePeriod")] public string TimePeriod { get; set; } = string.Empty;
        [JsonPropertyName("ElapsedTime")] public string ElapsedTime { get; set; } = string.Empty;
        [JsonPropertyName("KeyTimeEvent")] public string KeyTimeEvent { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class CharacterMovementChange
    {
        [JsonPropertyName("CharacterId")] public string CharacterId { get; set; } = string.Empty;
        [JsonPropertyName("FromLocation")] public string FromLocation { get; set; } = string.Empty;
        [JsonPropertyName("ToLocation")] public string ToLocation { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class ItemTransferChange
    {
        [JsonPropertyName("ItemId")] public string ItemId { get; set; } = string.Empty;
        [JsonPropertyName("ItemName")] public string ItemName { get; set; } = string.Empty;
        [JsonPropertyName("FromHolder")] public string FromHolder { get; set; } = string.Empty;
        [JsonPropertyName("ToHolder")] public string ToHolder { get; set; } = string.Empty;
        [JsonPropertyName("NewStatus")] public string NewStatus { get; set; } = "active";
        [JsonPropertyName("Event")] public string Event { get; set; } = string.Empty;
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
    }

    public class ChapterChanges
    {
        [JsonPropertyName("CharacterStateChanges")] public List<CharacterStateChange> CharacterStateChanges { get; set; } = new();
        [JsonPropertyName("ConflictProgress")] public List<ConflictProgressChange> ConflictProgress { get; set; } = new();
        [JsonPropertyName("NewPlotPoints")] public List<PlotPointChange> NewPlotPoints { get; set; } = new();
        [JsonPropertyName("ForeshadowingActions")] public List<ForeshadowingAction> ForeshadowingActions { get; set; } = new();
        [JsonPropertyName("LocationStateChanges")] public List<LocationStateChange> LocationStateChanges { get; set; } = new();
        [JsonPropertyName("FactionStateChanges")] public List<FactionStateChange> FactionStateChanges { get; set; } = new();
        [JsonPropertyName("TimeProgression")] public TimeProgressionChange? TimeProgression { get; set; }
        [JsonPropertyName("CharacterMovements")] public List<CharacterMovementChange> CharacterMovements { get; set; } = new();
        [JsonPropertyName("ItemTransfers")] public List<ItemTransferChange> ItemTransfers { get; set; } = new();
    }

    public class PlotPointEntry
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("Chapter")] public string Chapter { get; set; } = string.Empty;
        [JsonPropertyName("Keywords")] public List<string> Keywords { get; set; } = new();
        [JsonPropertyName("Context")] public string Context { get; set; } = string.Empty;
        [JsonPropertyName("InvolvedCharacters")] public List<string> InvolvedCharacters { get; set; } = new();
        [JsonPropertyName("Importance")] public string Importance { get; set; } = "normal";
        [JsonPropertyName("Storyline")] public string Storyline { get; set; } = "main";
    }

    public class ForeshadowingStatistics
    {
        [JsonPropertyName("TotalCount")] public int TotalCount { get; set; }
        [JsonPropertyName("SetupCount")] public int SetupCount { get; set; }
        [JsonPropertyName("ResolvedCount")] public int ResolvedCount { get; set; }
        [JsonPropertyName("OverdueCount")] public int OverdueCount { get; set; }
        [JsonPropertyName("Tier1Stats")] public ForeshadowingTierStats Tier1Stats { get; set; } = new();
        [JsonPropertyName("Tier2Stats")] public ForeshadowingTierStats Tier2Stats { get; set; } = new();
        [JsonPropertyName("Tier3Stats")] public ForeshadowingTierStats Tier3Stats { get; set; } = new();
    }

    public class ForeshadowingTierStats
    {
        [JsonPropertyName("Total")] public int Total { get; set; }
        [JsonPropertyName("Setup")] public int Setup { get; set; }
        [JsonPropertyName("Resolved")] public int Resolved { get; set; }
        [JsonPropertyName("Overdue")] public int Overdue { get; set; }
    }
}
