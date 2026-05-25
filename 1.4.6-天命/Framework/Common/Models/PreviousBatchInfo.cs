using System.Collections.Generic;

namespace TM.Framework.Common.Models
{
    public class PreviousBatchInfo
    {
        public List<string> LastTitles { get; set; } = new();

        public List<string> LastSummaries { get; set; } = new();

        public string CharacterStates { get; set; } = string.Empty;

        public string Foreshadowings { get; set; } = string.Empty;

        public bool HasValidInfo =>
            LastTitles.Count > 0 ||
            LastSummaries.Count > 0 ||
            !string.IsNullOrWhiteSpace(CharacterStates) ||
            !string.IsNullOrWhiteSpace(Foreshadowings);

        public string ToContextString()
        {
            if (!HasValidInfo)
                return string.Empty;

            var parts = new List<string>();

            if (LastTitles.Count > 0)
            {
                parts.Add($"【上一批次标题】\n{string.Join("\n", LastTitles)}");
            }

            if (LastSummaries.Count > 0)
            {
                parts.Add($"【上一批次摘要】\n{string.Join("\n", LastSummaries)}");
            }

            if (!string.IsNullOrWhiteSpace(CharacterStates))
            {
                parts.Add($"【人物/势力状态】\n{CharacterStates}");
            }

            if (!string.IsNullOrWhiteSpace(Foreshadowings))
            {
                parts.Add($"【已埋伏笔】\n{Foreshadowings}");
            }

            return string.Join("\n\n", parts);
        }
    }
}
