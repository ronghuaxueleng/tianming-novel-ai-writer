using System;

namespace TM.Framework.SystemSettings.Info.RuntimeEnv
{
    public class RuntimeEnvSettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("ShowSystemAssemblies")] public bool ShowSystemAssemblies { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("ShowUserAssemblies")] public bool ShowUserAssemblies { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("EnvironmentVariableFilter")] public string EnvironmentVariableFilter { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("ShowFullPath")] public bool ShowFullPath { get; set; } = true;
    }
}

