using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;

namespace TM.Services.Modules.ProjectData.Models.Design.Factions
{
    public class FactionRulesData : BusinessDataBase, Worldview.ICoreRuleSummaryProvider, IContextStringProvider
    {
        #region Tab1: 基本信息（Identity）

        [JsonPropertyName("FactionType")]
        public string FactionType { get; set; } = string.Empty;

        [JsonPropertyName("Goal")]
        public string Goal { get; set; } = string.Empty;

        [JsonPropertyName("StrengthTerritory")]
        public string StrengthTerritory { get; set; } = string.Empty;

        #endregion

        #region Tab2: 核心成员（Members）

        [JsonPropertyName("Leader")]
        public string Leader { get; set; } = string.Empty;

        [JsonPropertyName("CoreMembers")]
        public string CoreMembers { get; set; } = string.Empty;

        [JsonPropertyName("MemberTraits")]
        public string MemberTraits { get; set; } = string.Empty;

        #endregion

        #region Tab3: 对外关系（Relations）

        [JsonPropertyName("Allies")]
        public string Allies { get; set; } = string.Empty;

        [JsonPropertyName("Enemies")]
        public string Enemies { get; set; } = string.Empty;

        [JsonPropertyName("NeutralCompetitors")]
        public string NeutralCompetitors { get; set; } = string.Empty;

        #endregion

        #region 摘要与描述

        public string Description => string.IsNullOrWhiteSpace(Goal) ? Name : $"{Name}: {Goal}";

        public string GetDeepSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Name)) parts.Add(Name);
            if (!string.IsNullOrWhiteSpace(FactionType)) parts.Add($"类型: {FactionType}");
            if (!string.IsNullOrWhiteSpace(Goal)) parts.Add($"目标: {Goal}");
            return string.Join("; ", parts);
        }

        #endregion

        #region ICoreRuleSummaryProvider 实现

        public string GetCoreSummary()
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;
            if (!string.IsNullOrWhiteSpace(Goal))
                return Goal;
            return Name;
        }

        public int GetImportanceWeight()
        {
            if (!string.IsNullOrWhiteSpace(Goal) && !string.IsNullOrWhiteSpace(Leader)) return 80;
            if (!string.IsNullOrWhiteSpace(Name)) return 60;
            return 40;
        }

        #endregion

        #region IContextStringProvider 实现

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<item name=\"{Name}\">");
            if (!string.IsNullOrWhiteSpace(FactionType))
                sb.AppendLine($"类型：{FactionType}");
            if (!string.IsNullOrWhiteSpace(Goal))
                sb.AppendLine($"目标：{Goal}");
            if (!string.IsNullOrWhiteSpace(StrengthTerritory))
                sb.AppendLine($"实力/地盘：{StrengthTerritory}");
            sb.AppendLine("</item>");
            return sb.ToString();
        }

        public string ToBriefContextString()
        {
            return $"- **{Name}**：{GetCoreSummary()}";
        }

        #endregion
    }
}
