using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Framework.Common.ViewModels
{
    public class AIGenerationConfig
    {
        public string Category { get; set; } = string.Empty;

        public AIServiceType ServiceType { get; set; } = AIServiceType.ChatEngine;

        public ResponseFormat ResponseFormat { get; set; } = ResponseFormat.Json;

        public string MessagePrefix { get; set; } = "AI生成";

        public string ProgressMessage { get; set; } = "AI正在生成中...";

        public string CompleteMessage { get; set; } = "AI生成完成";

        public Dictionary<string, Func<string>> InputVariables { get; set; } = new();

        public Dictionary<string, Action<string>> OutputFields { get; set; } = new();

        public Dictionary<string, Func<string>>? OutputFieldGetters { get; set; }

        public Dictionary<string, string[]>? FieldAliases { get; set; }

        public bool EnableKeywordExtract { get; set; } = false;

        public Func<Task<string>>? ContextProvider { get; set; }

        public Dictionary<string, string>? BatchFieldKeyMap { get; set; }

        public string? SequenceFieldName { get; set; }

        public Func<string?, string, int>? GetCurrentMaxSequence { get; set; }

        public List<string>? BatchIndexFields { get; set; }
    }
}
