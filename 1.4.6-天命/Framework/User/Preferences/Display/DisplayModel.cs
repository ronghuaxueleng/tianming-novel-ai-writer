using System;
using System.Text.Json.Serialization;

namespace TM.Framework.User.Preferences.Display
{
    public class DisplayModel
    {
        [JsonPropertyName("ShowFunctionBar")] public bool ShowFunctionBar { get; set; } = true;
        [JsonPropertyName("ListDensity")] public ListDensity ListDensity { get; set; } = ListDensity.Standard;
        [JsonPropertyName("LastModified")] public DateTime LastModified { get; set; } = DateTime.Now;
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ListDensity
    {
        Compact,

        Standard,

        Comfortable
    }
}

