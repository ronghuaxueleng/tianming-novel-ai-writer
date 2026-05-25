using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public enum FailureType
    {
        Protocol,

        Consistency
    }

    public class GateFailure
    {
        [JsonPropertyName("Type")] public FailureType Type { get; set; }

        [JsonPropertyName("Errors")] public List<string> Errors { get; set; } = new();
    }

    public class GateResult
    {
        [JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [JsonPropertyName("Success")] public bool Success { get; set; }
        [JsonPropertyName("Failures")] public List<GateFailure> Failures { get; set; } = new();
        [JsonPropertyName("ParsedChanges")] public ChapterChanges? ParsedChanges { get; set; }
        [JsonPropertyName("ContentWithoutChanges")] public string? ContentWithoutChanges { get; set; }

        public void AddFailure(FailureType type, IEnumerable<string> errors)
        {
            Failures.Add(new GateFailure 
            { 
                Type = type, 
                Errors = errors.ToList() 
            });
        }

        public void AddFailure(FailureType type, string error)
        {
            Failures.Add(new GateFailure 
            { 
                Type = type, 
                Errors = new List<string> { error } 
            });
        }

        public List<string> GetTopFailures(int count)
        {
            return Failures
                .SelectMany(f => f.Errors.Select(e => $"[{f.Type}] {e}"))
                .Take(count)
                .ToList();
        }

        public List<string> GetAllFailures()
        {
            return Failures
                .SelectMany(f => f.Errors.Select(e => $"[{f.Type}] {e}"))
                .ToList();
        }

        public List<string> GetHumanReadableFailures(int count)
        {
            return Failures
                .SelectMany(f => f.Errors.Select(e => HumanizeError(f.Type, e)))
                .Take(count)
                .ToList();
        }

        private static string HumanizeError(FailureType type, string error)
        {
            if (type == FailureType.Protocol)
            {
                if (error.Contains("未找到CHANGES分隔符") || error.Contains("未识别到CHANGES区域"))
                    return "正文末尾缺少 ---CHANGES--- 分隔符或末尾JSON变更块，请在正文结尾添加 ---CHANGES--- 并紧跟JSON对象";
                if (error.Contains("JSON解析失败") || error.Contains("JSON格式无法解析"))
                    return "CHANGES段的JSON格式错误，请检查JSON语法（括号、逗号、引号）";
                if (error.Contains("缺少必要字段") || error.Contains("CHANGES缺失必需字段"))
                    return "CHANGES的JSON缺少必需字段，请确保显式包含9个顶级字段：CharacterStateChanges、ConflictProgress、ForeshadowingActions、NewPlotPoints、LocationStateChanges、FactionStateChanges、TimeProgression、CharacterMovements、ItemTransfers（可为空数组/空对象）";
                if (error.Contains("CHANGES只允许JSON格式"))
                    return "请不要使用 ### CHANGES 标题或 Markdown 格式，必须输出 ---CHANGES--- 分隔符 + JSON对象";
                if (error.Contains("CHANGES协议违规："))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(error, @"CHANGES协议违规：(.+?)必须为 ShortId");
                    var field = m.Success ? m.Groups[1].Value.Trim() : "实体ID字段";
                    var vm = System.Text.RegularExpressions.Regex.Match(error, @"收到(?:非Id值|非法值)'([^']+)'");
                    var val = vm.Success ? vm.Groups[1].Value : "";
                    var valHint = string.IsNullOrWhiteSpace(val) ? string.Empty : $"（反馈了名称'{val}'）";
                    return $"{field} 必须填写事实账本括号内的 ShortId{valHint}，请查阅事实账本中该实体对应的 ShortId 并原样复制";
                }
                return $"协议格式问题：{error}";
            }

            if (error.Contains("MovementStartLocationMismatch"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                var m = System.Text.RegularExpressions.Regex.Match(error, @"期望:\s*从已知位置\s*([^\s,]+)\s*出发");
                var rawLoc = m.Success ? m.Groups[1].Value : string.Empty;
                var expectedLoc = !string.IsNullOrWhiteSpace(rawLoc) ? EntityNameResolver.Resolve(rawLoc) : "账本记录位置";
                return $"角色'{entityName}'的出发地点必须是账本当前位置：{expectedLoc}。请修正CHANGES的FromLocation，或不要为该角色申报移动";
            }

            if (error.Contains("MovementChainBreak"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'本章多次移动时路径不连续：上一次ToLocation必须等于下一次FromLocation。请修正移动链或减少移动次数";
            }

            if (error.Contains("ItemOwnershipMismatch"))
            {
                var entityId = ExtractEntityId(error);
                var itemName = entityId;
                var m = System.Text.RegularExpressions.Regex.Match(error, @"期望:\s*物品由当前持有者\s*([^\s,]+)\s*转让");
                var rawHolder = m.Success ? m.Groups[1].Value : string.Empty;
                var expectedHolder = !string.IsNullOrWhiteSpace(rawHolder) ? EntityNameResolver.ResolveCharacter(rawHolder) : "账本持有者";
                return $"物品'{itemName}'的转让起点不符：当前持有者应为'{expectedHolder}'。请修正CHANGES的FromHolder，或不要申报该物品转让";
            }

            if (error.Contains("RelationshipContradiction"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'与他人关系申报矛盾：同一章内同一对角色不能同时声明盟友与仇敌。请统一关系结论并只保留一种";
            }

            if (error.Contains("LevelRegression"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'等级/阶段出现回退。除非正文明确发生失去/降级事件，否则不要让NewLevel低于账本记录";
            }

            if (error.Contains("AbilityLossWithoutEvent"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'声明失去能力但缺少原因。若LostAbilities非空，KeyEvent必须写清楚失去原因（封印/废除/代价等）";
            }

            if (error.Contains("TrustDeltaExceedsLimit"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'与他人的信任值变化幅度过大。请把TrustDelta控制在合理范围（默认±30以内），并用KeyEvent说明关系变化原因";
            }

            if (error.Contains("PayoffBeforeSetup"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveForeshadowing(entityId);
                return $"伏笔'{entityName}'尚未埋设，不能在本章揭示。请先在正文中自然埋设该伏笔，或移除揭示动作";
            }
            if (error.Contains("ForeshadowingRollback"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveForeshadowing(entityId);
                return $"伏笔'{entityName}'已揭示，不能重新埋设。请移除该伏笔的埋设动作";
            }

            if (error.Contains("ConflictStatusSkip"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveConflict(entityId);
                return $"冲突'{entityName}'的状态不能回退。请检查NewStatus是否正确，冲突只能向前推进";
            }

            if (error.Contains("CharacterNotInvolved"))
            {
                var entityId = ExtractEntityId(error);
                var entityName = EntityNameResolver.ResolveCharacter(entityId);
                return $"角色'{entityName}'不在本章涉及角色列表中，但出现在CHANGES里。请让该角色在正文中登场，或从CHANGES中移除";
            }

            if (error.Contains("正文引入未登记实体"))
            {
                var entity = error;
                var colonIdx = entity.IndexOf(':');
                if (colonIdx >= 0 && colonIdx < entity.Length - 1)
                {
                    entity = entity[(colonIdx + 1)..].Trim();
                }
                entity = entity
                    .Replace("正文引入未登记实体(有剧情作用)", string.Empty)
                    .Replace("正文引入未登记实体(龙套)", string.Empty)
                    .Replace("正文引入未登记实体", string.Empty)
                    .Trim();
                return $"正文中出现了未在设定中登记的实体'{entity}'。请使用已登记的角色/地点名称，或移除该实体";
            }

            if (error.Contains("描述矛盾") || error.Contains("描述不一致"))
            {
                return $"正文描述与设定不符：{error}。请修改正文使其与角色/地点设定一致";
            }

            if (error.Contains("违反世界观规则") || error.Contains("硬约束"))
            {
                return $"违反世界观设定：{error}。请修改正文使其符合世界观规则";
            }

            return error;
        }

        private static string ExtractEntityId(string error)
        {
            var match = System.Text.RegularExpressions.Regex.Match(error, @"实体:\s*([^,]+)");
            if (match.Success)
                return match.Groups[1].Value.Trim();

            match = System.Text.RegularExpressions.Regex.Match(error, @"EntityId[:\s]+([^\s,\]]+)");
            if (match.Success)
                return match.Groups[1].Value;

            match = System.Text.RegularExpressions.Regex.Match(error, @"角色\s+(\S+)\s+不在");
            if (match.Success)
                return match.Groups[1].Value;

            var colonIdx = error.IndexOf(':');
            if (colonIdx > 0 && colonIdx < error.Length - 1)
                return error.Substring(colonIdx + 1).Trim().Split(new[] { ' ', ',' })[0];

            return "未知实体";
        }
    }

    public class DesignElementNames
    {
        [JsonPropertyName("CharacterNames")] public List<string> CharacterNames { get; set; } = new();
        [JsonPropertyName("FactionNames")] public List<string> FactionNames { get; set; } = new();
        [JsonPropertyName("LocationNames")] public List<string> LocationNames { get; set; } = new();
        [JsonPropertyName("PlotKeyNames")] public List<string> PlotKeyNames { get; set; } = new();
        [JsonPropertyName("PovCharacterNames")] public List<string> PovCharacterNames { get; set; } = new();
    }
}
