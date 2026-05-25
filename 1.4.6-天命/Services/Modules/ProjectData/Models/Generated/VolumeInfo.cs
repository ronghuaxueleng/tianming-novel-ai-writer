using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generated
{
    public class VolumeInfo
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Icon")] public string Icon { get; set; } = "📁";

        [JsonPropertyName("Number")] public int Number { get; set; }

        [JsonPropertyName("Order")] public int Order { get; set; }

    }
}
