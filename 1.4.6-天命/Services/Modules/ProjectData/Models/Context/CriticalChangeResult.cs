using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Context
{
    public class CriticalChangeResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("HasCriticalChanges")] public bool HasCriticalChanges { get; set; } = false;

        [System.Text.Json.Serialization.JsonPropertyName("ChangedFields")] public List<string> ChangedFields { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("ChangedEntityIds")] public List<string> ChangedEntityIds { get; set; } = new();

        public string Summary => HasCriticalChanges
            ? $"检测到{ChangedFields.Count}个关键字段变更"
            : "无关键变更";
    }
}
