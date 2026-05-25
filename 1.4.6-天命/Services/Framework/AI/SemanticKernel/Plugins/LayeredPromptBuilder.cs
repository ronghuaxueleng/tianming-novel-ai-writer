using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public class LayeredPromptBuilder
    {
        #region 常量

        #endregion

        #region 构造函数

        public LayeredPromptBuilder() { }

        #endregion

        #region 公开方法

        public string BuildLayeredPrompt(
            ContentTaskContext taskContext,
            FactSnapshot factSnapshot,
            CreativeSpec? spec)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<chapter_generation_task>");
            sb.AppendLine();
            AppendChangesFormatReminder(sb, spec);

            AppendFactLedgerSection(sb, factSnapshot);

            AppendVectorRecallSection(sb, taskContext.VectorRecallFragments);

            AppendTaskSection(sb, taskContext, spec);

            AppendChangesRequirement(sb);

            AppendTailEntityChecklist(sb, taskContext);

            AppendChangesIdQuickRef(sb, factSnapshot);

            sb.AppendLine("</chapter_generation_task>");

            return sb.ToString();
        }

        public string BuildLayeredPromptWithoutFact(
            ContentTaskContext taskContext,
            CreativeSpec? spec)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<chapter_generation_task>");
            sb.AppendLine();
            AppendChangesFormatReminder(sb, spec);

            AppendVectorRecallSection(sb, taskContext.VectorRecallFragments);

            AppendTaskSection(sb, taskContext, spec);
            AppendChangesRequirement(sb);
            AppendTailEntityChecklist(sb, taskContext);

            sb.AppendLine("</chapter_generation_task>");

            return sb.ToString();
        }

        #endregion

        #region 私有方法

        private static void AppendChangesFormatReminder(StringBuilder sb, CreativeSpec? spec = null)
        {
            sb.AppendLine("<format_reminder mandatory=\"true\">");
            sb.AppendLine("输出格式强制要求：正文末尾必须输出 `---CHANGES---` 分隔符（仅半角连字符），紧跟JSON变更摘要（含9个顶级字段）。所有ID字段必须使用事实账本中括号内的 **ShortId**（格式：13字符大写字母开头，如 `D7M3VT2K9P4N`），禁止填写名称文字。详见末尾「输出要求」。");
            if (spec?.TargetWordCount > 0)
            {
                sb.AppendLine($"字数要求（强制）：本章目标字数约 {spec.TargetWordCount} 字，请在正文完整覆盖章节蓝图所有场景后自然收束，不得因字数未达标而截断。");
            }
            sb.AppendLine("</format_reminder>");
            sb.AppendLine();
        }

        private void AppendFactLedgerSection(StringBuilder sb, FactSnapshot snapshot)
        {
            sb.AppendLine("<fact_ledger immutable=\"true\" override=\"never\">");
            sb.AppendLine("> 禁止推翻；变化必须发生在本章剧情中并记录。");
            sb.AppendLine();

            var factContent = FormatFactSnapshot(snapshot);
            sb.AppendLine(factContent);
            sb.AppendLine("</fact_ledger>");
            sb.AppendLine();
        }

        private string FormatFactSnapshot(FactSnapshot snapshot)
        {
            var sb = new StringBuilder();

            if (snapshot.WorldRuleConstraints != null && snapshot.WorldRuleConstraints.Count > 0)
            {
                sb.AppendLine("<section name=\"world_constraints\" role=\"hard_constraint\">");
                foreach (var rule in snapshot.WorldRuleConstraints)
                {
                    if (string.IsNullOrWhiteSpace(rule.RuleName) || string.IsNullOrWhiteSpace(rule.Constraint))
                        continue;
                    sb.AppendLine($"- **{rule.RuleName}**：{rule.Constraint}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.CharacterStates != null && snapshot.CharacterStates.Count > 0)
            {
                sb.AppendLine("<section name=\"character_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **以下括号内的 ShortId 即为CHANGES中 CharacterId / RelationshipChanges key 的唯一合法值，必须原样复制，禁止填写名称文字**");
                sb.AppendLine("> ⚠ **硬约束**：角色等级/阶段只升不降；若在CHANGES中声明能力失去或重大状态变化，必须在KeyEvent明确给出剧情原因；RelationshipChanges 的 TrustDelta 单章不要出现极端跳变（默认不应超过±30）。");
                foreach (var state in snapshot.CharacterStates)
                {
                    if (string.IsNullOrWhiteSpace(state.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(state.Id) ? "?" : state.Id;
                    sb.AppendLine($"- **{state.Name}**（{idLabel}）");
                    if (!string.IsNullOrWhiteSpace(state.Stage))
                        sb.AppendLine($"  - 阶段：{state.Stage}");
                    if (!string.IsNullOrWhiteSpace(state.Abilities))
                        sb.AppendLine($"  - 能力：{state.Abilities}");
                    if (!string.IsNullOrWhiteSpace(state.Relationships))
                        sb.AppendLine($"  - 关系：{state.Relationships}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.ConflictProgress != null && snapshot.ConflictProgress.Count > 0)
            {
                sb.AppendLine("<section name=\"conflict_progress\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **以下括号内的 ShortId 即为CHANGES中 ConflictId 的唯一合法值，必须原样复制**");
                sb.AppendLine("> ⚠ **硬约束**：冲突状态不可回退；若在CHANGES中更新 NewStatus，必须是对当前状态的推进或解决，不允许写回更早状态。");
                foreach (var conflict in snapshot.ConflictProgress)
                {
                    if (string.IsNullOrWhiteSpace(conflict.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(conflict.Id) ? "?" : conflict.Id;
                    sb.AppendLine($"- **{conflict.Name}**（{idLabel}）：{conflict.Status}");
                    if (conflict.RecentProgress != null)
                    {
                        foreach (var point in conflict.RecentProgress.Where(p => !string.IsNullOrWhiteSpace(p)))
                        {
                            sb.AppendLine($"  - {point}");
                        }
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.ForeshadowingStatus != null && snapshot.ForeshadowingStatus.Count > 0)
            {
                sb.AppendLine("<section name=\"foreshadowing\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **以下括号内的 ShortId 即为CHANGES中 ForeshadowId 的唯一合法值，必须原样复制**");
                sb.AppendLine("> ⚠ **硬约束**：未埋设不可揭示；已揭示不可重新埋设。若本章不涉及该伏笔流转，请不要在CHANGES中声明动作。");
                foreach (var f in snapshot.ForeshadowingStatus)
                {
                    if (string.IsNullOrWhiteSpace(f.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(f.Id) ? "?" : f.Id;
                    var status = f.IsResolved ? "已揭示" : (f.IsSetup ? "已埋设" : "未埋设");
                    var warning = f.IsOverdue ? " ⚠️逾期" : "";
                    sb.AppendLine($"- **{f.Name}**（{idLabel}）：{status}{warning}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.PlotPoints != null && snapshot.PlotPoints.Count > 0)
            {
                sb.AppendLine("<section name=\"plot_points\" role=\"narrative_baseline\">");
                var groups = snapshot.PlotPoints
                    .Where(p => !string.IsNullOrWhiteSpace(p.Summary))
                    .GroupBy(p => p.Storyline ?? "main")
                    .OrderByDescending(g => g.Key == "main" ? 2 : g.Key == "sub" ? 1 : 0);
                foreach (var group in groups)
                {
                    var label = group.Key switch
                    {
                        "main" => "主线",
                        "sub" => "支线",
                        "character_arc" => "人物弧光",
                        _ => group.Key
                    };
                    sb.AppendLine($"**{label}**：");
                    foreach (var point in group)
                    {
                        sb.AppendLine($"- {point.ChapterId}: {point.Summary}");
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.LocationStates != null && snapshot.LocationStates.Count > 0)
            {
                sb.AppendLine("<section name=\"location_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **以下括号内的 ShortId 即为CHANGES中 LocationId 的唯一合法值，必须原样复制**");
                foreach (var loc in snapshot.LocationStates)
                {
                    if (string.IsNullOrWhiteSpace(loc.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(loc.Id) ? "?" : loc.Id;
                    sb.AppendLine($"- **{loc.Name}**（{idLabel}）：{loc.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.FactionStates != null && snapshot.FactionStates.Count > 0)
            {
                sb.AppendLine("<section name=\"faction_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **以下括号内的 ShortId 即为CHANGES中 FactionId 的唯一合法值，必须原样复制**");
                foreach (var fac in snapshot.FactionStates)
                {
                    if (string.IsNullOrWhiteSpace(fac.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(fac.Id) ? "?" : fac.Id;
                    sb.AppendLine($"- **{fac.Name}**（{idLabel}）：{fac.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.Timeline != null && snapshot.Timeline.Count > 0)
            {
                sb.AppendLine("<section name=\"timeline\" role=\"temporal_baseline\">");
                foreach (var t in snapshot.Timeline)
                {
                    if (string.IsNullOrWhiteSpace(t.TimePeriod)) continue;
                    var elapsed = string.IsNullOrWhiteSpace(t.ElapsedTime) ? "" : $"（经过{t.ElapsedTime}）";
                    sb.AppendLine($"- {t.ChapterId}: {t.TimePeriod}{elapsed}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.CharacterLocations != null && snapshot.CharacterLocations.Count > 0)
            {
                sb.AppendLine("<section name=\"character_locations\" role=\"spatial_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：CharacterMovements.CharacterId 使用角色括号内 ShortId；FromLocation/ToLocation 使用地点括号内 ShortId；FromLocation 必须等于本表中该角色的地点 ShortId；同章内可多次移动（A→B→C），但每次 FromLocation = 上一次的 ToLocation。不得路径断裂。");
                var locDescMap = snapshot.LocationDescriptions ?? new Dictionary<string, LocationCoreDescription>();
                var locNameToId = locDescMap.Values
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(l.Id))
                    .ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var loc in snapshot.CharacterLocations)
                {
                    var charDisplayName = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                    if (string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                    string locationDisplay;
                    if (ShortIdGenerator.IsLikelyId(loc.CurrentLocation) && locDescMap.TryGetValue(loc.CurrentLocation, out var locDesc))
                        locationDisplay = $"{locDesc.Name}（{loc.CurrentLocation}）";
                    else if (locNameToId.TryGetValue(loc.CurrentLocation, out var locId))
                        locationDisplay = $"{loc.CurrentLocation}（{locId}）";
                    else
                        locationDisplay = loc.CurrentLocation;
                    sb.AppendLine($"- **{charDisplayName}**（{loc.CharacterId}）：{locationDisplay}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (snapshot.ItemStates != null && snapshot.ItemStates.Count > 0)
            {
                sb.AppendLine("<section name=\"item_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：ItemTransfers.ItemId 使用物品 ShortId；FromHolder/ToHolder 使用角色 ShortId；第一次转让的 FromHolder 必须等于本表该物品的持有者 ShortId；同章内可多次转手（A→B→C），但每次 FromHolder = 上一次的 ToHolder。");
                var charDescMap = snapshot.CharacterDescriptions ?? new Dictionary<string, CharacterCoreDescription>();
                var charNameToId = charDescMap.Values
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                    .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var item in snapshot.ItemStates)
                {
                    if (string.IsNullOrWhiteSpace(item.Name)) continue;
                    string holderDisplay;
                    if (string.IsNullOrWhiteSpace(item.CurrentHolder))
                        holderDisplay = "无人持有";
                    else if (ShortIdGenerator.IsLikelyId(item.CurrentHolder) && charDescMap.TryGetValue(item.CurrentHolder, out var cDesc))
                        holderDisplay = $"{cDesc.Name}（{item.CurrentHolder}）";
                    else if (charNameToId.TryGetValue(item.CurrentHolder, out var cId))
                        holderDisplay = $"{item.CurrentHolder}（{cId}）";
                    else
                        holderDisplay = item.CurrentHolder;
                    var itemIdPart = string.IsNullOrWhiteSpace(item.Id) ? string.Empty : $"（{item.Id}）";
                    sb.AppendLine($"- **{item.Name}**{itemIdPart}：{holderDisplay}，状态={item.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion

        #region 私有方法 - 长距离记忆召回

        private void AppendVectorRecallSection(StringBuilder sb, List<VectorRecallFragment>? fragments)
        {
            if (fragments == null || fragments.Count == 0)
                return;

            sb.AppendLine("<context_block source=\"vector_recall\">");
            sb.AppendLine("> 以下是从历史章节中检索到的与本章相关内容，请注意保持一致性。");
            sb.AppendLine();

            foreach (var fragment in fragments)
            {
                sb.AppendLine($"**来源: {fragment.ChapterId}**");
                sb.AppendLine(fragment.Content);
                sb.AppendLine();
            }
            sb.AppendLine("</context_block>");
            sb.AppendLine();
        }

        #endregion

        #region 私有方法 - 本章任务区块

        private void AppendTaskSection(StringBuilder sb, ContentTaskContext ctx, CreativeSpec? spec)
        {
            sb.AppendLine("<task_context>");
            sb.AppendLine("> 请将以下信息视为创作输入，目标是写出连贯自然的小说章节正文。");
            sb.AppendLine();

            static string ResolveId(string? id, Dictionary<string, string> map)
            {
                if (string.IsNullOrWhiteSpace(id)) return string.Empty;
                return map.TryGetValue(id, out var n) ? n : id;
            }

            static string ResolveIds(string? ids, Dictionary<string, string> map)
            {
                if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
                return string.Join("、", ids.Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => ResolveId(s, map)).Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            var characterIdToName = ctx.Characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
            var factionIdToName = ctx.Factions
                .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
                .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
            var locationIdToName = ctx.Locations
                .Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.Name))
                .ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);

            if (ctx.HistoricalMilestones != null && ctx.HistoricalMilestones.Count > 0)
            {
                sb.AppendLine("<section name=\"historical_milestones\" role=\"background_reference\" priority=\"normal\">");
                sb.AppendLine("> 以下是各前卷的浓缩历史，请确保本章内容与这些已发生事件保持一致。");
                sb.AppendLine();
                foreach (var milestone in ctx.HistoricalMilestones)
                {
                    sb.AppendLine(milestone.Milestone);
                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PreviousVolumeArchives != null && ctx.PreviousVolumeArchives.Count > 0)
            {
                sb.AppendLine("<section name=\"prev_volume_archive\" role=\"cross_volume_baseline\" priority=\"normal\">");
                sb.AppendLine("> 以下是各前卷末尾的结构化历史基线（快照时间：前卷结束时）。");
                sb.AppendLine("> ⚠ 当前卷内的最新状态以上方 `<fact_ledger>` 为准；两者冲突时 `<fact_ledger>` 优先。");
                sb.AppendLine();
                foreach (var archive in ctx.PreviousVolumeArchives)
                {
                    sb.AppendLine($"**第{archive.VolumeNumber}卷末**（{archive.LastChapterId}）");
                    foreach (var cs in archive.CharacterStates)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        if (!string.IsNullOrWhiteSpace(cs.Stage)) parts.Add($"修为·{cs.Stage}");
                        if (!string.IsNullOrWhiteSpace(cs.Abilities)) parts.Add($"能力·{cs.Abilities}");
                        if (!string.IsNullOrWhiteSpace(cs.Relationships)) parts.Add($"关系·{cs.Relationships}");
                        sb.AppendLine($"  {cs.Name}：{string.Join("；", parts)}");
                    }
                    foreach (var cf in archive.ConflictProgress)
                    {
                        if (!string.IsNullOrWhiteSpace(cf.Status))
                            sb.AppendLine($"  冲突[{cf.Name}]：{cf.Status}");
                    }

                    if (archive.Timeline != null && archive.Timeline.Count > 0)
                    {
                        foreach (var t in archive.Timeline)
                        {
                            if (string.IsNullOrWhiteSpace(t.TimePeriod)) continue;
                            var elapsed = string.IsNullOrWhiteSpace(t.ElapsedTime) ? string.Empty : $"（经过{t.ElapsedTime}）";
                            var timeEvent = string.IsNullOrWhiteSpace(t.KeyTimeEvent) ? string.Empty : $"，要点={t.KeyTimeEvent}";
                            sb.AppendLine($"  时间[{t.ChapterId}]：{TruncateLine(t.TimePeriod, 120)}{TruncateLine(elapsed, 80)}{TruncateLine(timeEvent, 120)}");
                        }
                    }

                    if (archive.CharacterLocations != null && archive.CharacterLocations.Count > 0)
                    {
                        foreach (var loc in archive.CharacterLocations)
                        {
                            if (string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                            var name = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                            sb.AppendLine($"  位置[{name}]：{TruncateLine(loc.CurrentLocation, 120)}");
                        }
                    }

                    if (archive.FactionStates != null && archive.FactionStates.Count > 0)
                    {
                        foreach (var fac in archive.FactionStates)
                        {
                            if (string.IsNullOrWhiteSpace(fac.Status)) continue;
                            sb.AppendLine($"  势力[{fac.Name}]：{TruncateLine(fac.Status, 120)}");
                        }
                    }

                    if (archive.LocationStates != null && archive.LocationStates.Count > 0)
                    {
                        foreach (var locState in archive.LocationStates)
                        {
                            if (string.IsNullOrWhiteSpace(locState.Status)) continue;
                            sb.AppendLine($"  地点[{locState.Name}]：{TruncateLine(locState.Status, 120)}");
                        }
                    }

                    if (archive.ItemStates != null && archive.ItemStates.Count > 0)
                    {
                        foreach (var item in archive.ItemStates)
                        {
                            if (string.IsNullOrWhiteSpace(item.Name)) continue;
                            var holder = string.IsNullOrWhiteSpace(item.CurrentHolder) ? string.Empty : $"，持有者={item.CurrentHolder}";
                            var status = string.IsNullOrWhiteSpace(item.Status) ? string.Empty : $"，状态={TruncateLine(item.Status, 80)}";
                            sb.AppendLine($"  物品[{item.Name}]{holder}{status}");
                        }
                    }

                    if (archive.ForeshadowingStatus != null && archive.ForeshadowingStatus.Count > 0)
                    {
                        foreach (var fs in archive.ForeshadowingStatus.Where(f => !f.IsResolved))
                        {
                            if (string.IsNullOrWhiteSpace(fs.Name)) continue;
                            var overdue = fs.IsOverdue ? "【逾期】" : string.Empty;
                            sb.AppendLine($"  伏笔[{fs.Name}]{overdue}：已埋设未揭示");
                        }
                    }

                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PreviousChapterSummaries != null && ctx.PreviousChapterSummaries.Count > 0)
            {
                sb.AppendLine("<section name=\"chapter_summaries\" role=\"recent_context\" priority=\"normal\">");
                foreach (var summary in ctx.PreviousChapterSummaries)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章**：{summary.Summary}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.MdPreviousChapterSummaries != null && ctx.MdPreviousChapterSummaries.Count > 0)
            {
                sb.AppendLine("<section name=\"md_summaries\" role=\"recent_context\" priority=\"normal\">");
                foreach (var summary in ctx.MdPreviousChapterSummaries)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章开头**：");
                    sb.AppendLine(summary.Summary);
                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(ctx.PreviousChapterTail))
            {
                sb.AppendLine("<section name=\"chapter_tail\" role=\"connection_anchor\" priority=\"high\">");
                sb.AppendLine("```");
                sb.AppendLine(ctx.PreviousChapterTail);
                sb.AppendLine("```");
                sb.AppendLine("> 请从此处自然衔接，不要复述提示词，不要解释你的写作过程。");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.StateDivergenceWarnings != null && ctx.StateDivergenceWarnings.Count > 0)
            {
                sb.AppendLine("<section name=\"consistency_warnings\" role=\"alert\" priority=\"high\">");
                sb.AppendLine("> 以下警告由系统检测生成，反映账本追踪可信度问题，请优先以FactSnapshot数据为准。");
                sb.AppendLine();
                foreach (var w in ctx.StateDivergenceWarnings)
                    sb.AppendLine($"- {w}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<section name=\"chapter_task\" role=\"primary_directive\" priority=\"highest\">");
            sb.AppendLine($"- 章节：{ctx.ChapterId}");
            sb.AppendLine($"- 标题：{ctx.Title}");
            if (!string.IsNullOrEmpty(ctx.Summary))
                sb.AppendLine($"- 概要：{ctx.Summary}");
            if (ctx.Rhythm != null)
                sb.AppendLine($"- 节奏：{ctx.Rhythm.PaceType} / {ctx.Rhythm.Intensity} / {ctx.Rhythm.EmotionalTone}");

            var mandatoryChars = ctx.Characters?.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => c.Name).ToList() ?? new System.Collections.Generic.List<string>();
            if (ctx.ExpandedCharacters != null)
                foreach (var ec in ctx.ExpandedCharacters)
                    if (!string.IsNullOrWhiteSpace(ec.Name) && !mandatoryChars.Contains(ec.Name))
                        mandatoryChars.Add(ec.Name);
            var mandatoryFactions = new System.Collections.Generic.List<string>();
            var mandatoryLocs = ctx.Locations?.Where(l => !string.IsNullOrWhiteSpace(l.Name)).Select(l => l.Name).ToList() ?? new System.Collections.Generic.List<string>();
            if (ctx.Blueprints != null)
            {
                char[] bpSep = { ',', '\uff0c', '\u3001', ';', '\uff1b' };
                foreach (var bp in ctx.Blueprints)
                {
                    if (!string.IsNullOrWhiteSpace(bp.PovCharacter) && !mandatoryChars.Contains(bp.PovCharacter.Trim()))
                        mandatoryChars.Add(bp.PovCharacter.Trim());
                    foreach (var p in (bp.Cast ?? string.Empty).Split(bpSep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !mandatoryChars.Contains(n)) mandatoryChars.Add(n); }
                    foreach (var p in (bp.Factions ?? string.Empty).Split(bpSep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !mandatoryFactions.Contains(n)) mandatoryFactions.Add(n); }
                    foreach (var p in (bp.Locations ?? string.Empty).Split(bpSep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !mandatoryLocs.Contains(n)) mandatoryLocs.Add(n); }
                }
            }
            if ((mandatoryChars.Count > 0) || (mandatoryFactions?.Count > 0) || (mandatoryLocs?.Count > 0))
            {
                sb.AppendLine("- \u26a0 **本章必须在正文中出场的实体（不可省略，须有实质戏份：对话/行动/情节参与，不能仅作背景一笔带过）**：");
                if (mandatoryChars?.Count > 0)
                    sb.AppendLine($"  - 角色：{string.Join("、", mandatoryChars)}");
                if (mandatoryFactions?.Count > 0)
                    sb.AppendLine($"  - 势力：{string.Join("、", mandatoryFactions)}");
                if (mandatoryLocs?.Count > 0)
                    sb.AppendLine($"  - 地点：{string.Join("、", mandatoryLocs)}");
            }

            bool hasTaskLayer = !string.IsNullOrEmpty(ctx.Title)
                               || !string.IsNullOrEmpty(ctx.Summary)
                               || (ctx.Characters != null && ctx.Characters.Count > 0)
                               || (ctx.WorldRules != null && ctx.WorldRules.Count > 0)
                               || (ctx.Templates != null && ctx.Templates.Count > 0)
                               || ctx.VolumeOutline != null
                               || ctx.VolumeDesign != null
                               || ctx.ChapterPlan != null
                               || (ctx.Blueprints != null && ctx.Blueprints.Count > 0)
                               || (ctx.Scenes != null && ctx.Scenes.Count > 0);
            if (!hasTaskLayer)
            {
                sb.AppendLine("> 当前为纯续写模式：请根据上一章结尾直接续写正文，保持人物口吻与叙事风格一致。");
            }
            sb.AppendLine("</section>");
            sb.AppendLine();

            if (ctx.Templates != null && ctx.Templates.Count > 0)
            {
                sb.AppendLine($"<section name=\"creative_materials\" role=\"style_reference\" priority=\"normal\" count=\"{ctx.Templates.Count}\">");
                foreach (var t in ctx.Templates)
                {
                    sb.AppendLine($"- **{t.Name}**");
                    if (!string.IsNullOrWhiteSpace(t.OverallIdea))
                        sb.AppendLine($"  - 整体构思：{t.OverallIdea}");
                    if (!string.IsNullOrWhiteSpace(t.WorldBuildingMethod))
                        sb.AppendLine($"  - 世界观构建手法：{t.WorldBuildingMethod}");
                    if (!string.IsNullOrWhiteSpace(t.PowerSystemDesign))
                        sb.AppendLine($"  - 力量体系：{t.PowerSystemDesign}");
                    if (!string.IsNullOrWhiteSpace(t.EnvironmentDescription))
                        sb.AppendLine($"  - 环境描写：{t.EnvironmentDescription}");
                    if (!string.IsNullOrWhiteSpace(t.PlotStructure))
                        sb.AppendLine($"  - 情节结构：{t.PlotStructure}");
                    if (!string.IsNullOrWhiteSpace(t.ProtagonistDesign))
                        sb.AppendLine($"  - 主角塑造：{t.ProtagonistDesign}");
                    if (!string.IsNullOrWhiteSpace(t.GoldenFingerDesign))
                        sb.AppendLine($"  - 金手指：{t.GoldenFingerDesign}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.WorldRules != null && ctx.WorldRules.Count > 0)
            {
                sb.AppendLine($"<section name=\"worldview_rules\" role=\"setting_constraint\" priority=\"normal\" count=\"{ctx.WorldRules.Count}\">");
                foreach (var rule in ctx.WorldRules)
                {
                    sb.AppendLine($"- **{rule.Name}**");
                    if (!string.IsNullOrWhiteSpace(rule.OneLineSummary))
                        sb.AppendLine($"  - 简介：{rule.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(rule.PowerSystem))
                        sb.AppendLine($"  - 力量体系：{rule.PowerSystem}");
                    if (!string.IsNullOrWhiteSpace(rule.Cosmology))
                        sb.AppendLine($"  - 宇宙观：{rule.Cosmology}");
                    if (!string.IsNullOrWhiteSpace(rule.SpecialLaws))
                        sb.AppendLine($"  - 特殊法则：{rule.SpecialLaws}");
                    if (!string.IsNullOrWhiteSpace(rule.HardRules))
                        sb.AppendLine($"  - 硬规则：{rule.HardRules}");
                    if (!string.IsNullOrWhiteSpace(rule.SoftRules))
                        sb.AppendLine($"  - 软规则：{rule.SoftRules}");
                    if (!string.IsNullOrWhiteSpace(rule.StatusQuo))
                        sb.AppendLine($"  - 故事开始前现状：{rule.StatusQuo}");
                    if (!string.IsNullOrWhiteSpace(rule.KeyEvents))
                        sb.AppendLine($"  - 关键历史事件：{rule.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(rule.AncientEra))
                        sb.AppendLine($"  - 创世/古代纪元：{rule.AncientEra}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeOutline != null)
            {
                sb.AppendLine("<section name=\"outline\" role=\"macro_reference\" priority=\"normal\">");
                sb.AppendLine($"- 名称：{ctx.VolumeOutline.Name}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.Theme))
                    sb.AppendLine($"- 主题：{ctx.VolumeOutline.Theme}");
                if (ctx.VolumeOutline.TotalChapterCount > 0)
                    sb.AppendLine($"- 全书总章节数：{ctx.VolumeOutline.TotalChapterCount}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.OneLineOutline))
                    sb.AppendLine($"- 一句话大纲：{ctx.VolumeOutline.OneLineOutline}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.EmotionalTone))
                    sb.AppendLine($"- 情感基调：{ctx.VolumeOutline.EmotionalTone}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.CoreConflict))
                    sb.AppendLine($"- 核心冲突：{ctx.VolumeOutline.CoreConflict}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.EndingState))
                    sb.AppendLine($"- 结局目标：{ctx.VolumeOutline.EndingState}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeDesign != null)
            {
                sb.AppendLine("<section name=\"volume_design\" role=\"structural_guide\" priority=\"normal\">");
                if (ctx.VolumeDesign.VolumeNumber > 0)
                    sb.AppendLine($"- 卷序号：{ctx.VolumeDesign.VolumeNumber}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTitle))
                    sb.AppendLine($"- 卷标题：{ctx.VolumeDesign.VolumeTitle}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTheme))
                    sb.AppendLine($"- 卷主题：{ctx.VolumeDesign.VolumeTheme}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.StageGoal))
                    sb.AppendLine($"- 阶段目标：{ctx.VolumeDesign.StageGoal}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.EstimatedWordCount))
                    sb.AppendLine($"- 预计字数：{ctx.VolumeDesign.EstimatedWordCount}");
                if (ctx.VolumeDesign.TargetChapterCount > 0)
                    sb.AppendLine($"- 目标章节数：{ctx.VolumeDesign.TargetChapterCount}");
                if (ctx.VolumeDesign.StartChapter > 0 || ctx.VolumeDesign.EndChapter > 0)
                    sb.AppendLine($"- 章节范围：{ctx.VolumeDesign.StartChapter} - {ctx.VolumeDesign.EndChapter}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.MainConflict))
                    sb.AppendLine($"- 主冲突：{ctx.VolumeDesign.MainConflict}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.PressureSource))
                    sb.AppendLine($"- 压力来源：{ctx.VolumeDesign.PressureSource}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.KeyEvents))
                    sb.AppendLine($"- 关键事件：{ctx.VolumeDesign.KeyEvents}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.OpeningState))
                    sb.AppendLine($"- 开篇状态：{ctx.VolumeDesign.OpeningState}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.EndingState))
                    sb.AppendLine($"- 收束状态：{ctx.VolumeDesign.EndingState}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.ChapterAllocationOverview))
                    sb.AppendLine($"- 章节分配总览：{ctx.VolumeDesign.ChapterAllocationOverview}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.PlotAllocation))
                    sb.AppendLine($"- 剧情分配：{ctx.VolumeDesign.PlotAllocation}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.ChapterGenerationHints))
                    sb.AppendLine($"- 章节生成提示：{ctx.VolumeDesign.ChapterGenerationHints}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.ChapterPlan != null)
            {
                sb.AppendLine("<section name=\"chapter_plan\" role=\"execution_guide\" priority=\"high\">");
                if (ctx.ChapterPlan.ChapterNumber > 0)
                    sb.AppendLine($"- 章节序号：{ctx.ChapterPlan.ChapterNumber}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTitle))
                    sb.AppendLine($"- 章节标题：{ctx.ChapterPlan.ChapterTitle}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Volume))
                    sb.AppendLine($"- 所属卷：{ctx.ChapterPlan.Volume}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.EstimatedWordCount))
                    sb.AppendLine($"- 预计字数：{ctx.ChapterPlan.EstimatedWordCount}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTheme))
                    sb.AppendLine($"- 章节主题：{ctx.ChapterPlan.ChapterTheme}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ReaderExperienceGoal))
                    sb.AppendLine($"- 读者体验目标：{ctx.ChapterPlan.ReaderExperienceGoal}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainGoal))
                    sb.AppendLine($"- 主目标：{ctx.ChapterPlan.MainGoal}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ResistanceSource))
                    sb.AppendLine($"- 阻力来源：{ctx.ChapterPlan.ResistanceSource}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.KeyTurn))
                    sb.AppendLine($"- 关键转折：{ctx.ChapterPlan.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Hook))
                    sb.AppendLine($"- 结尾钩子：{ctx.ChapterPlan.Hook}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.WorldInfoDrop))
                    sb.AppendLine($"- 世界观信息投放：{ctx.ChapterPlan.WorldInfoDrop}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.CharacterArcProgress))
                    sb.AppendLine($"- 角色弧光推进：{ctx.ChapterPlan.CharacterArcProgress}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainPlotProgress))
                    sb.AppendLine($"- 主线推进点：{ctx.ChapterPlan.MainPlotProgress}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Foreshadowing))
                    sb.AppendLine($"- 伏笔埋设/回收：{ctx.ChapterPlan.Foreshadowing}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Blueprints != null && ctx.Blueprints.Count > 0)
            {
                sb.AppendLine($"<section name=\"blueprints\" role=\"scene_directive\" priority=\"highest\" count=\"{ctx.Blueprints.Count}\">");
                foreach (var blueprint in ctx.Blueprints)
                {
                    var title = !string.IsNullOrWhiteSpace(blueprint.SceneTitle)
                        ? blueprint.SceneTitle
                        : blueprint.Name;
                    sb.AppendLine($"- **{title}**");
                    if (blueprint.SceneNumber > 0)
                        sb.AppendLine($"  - 场景序号：{blueprint.SceneNumber}");
                    if (!string.IsNullOrWhiteSpace(blueprint.OneLineStructure))
                        sb.AppendLine($"  - 结构：{blueprint.OneLineStructure}");
                    if (!string.IsNullOrWhiteSpace(blueprint.PacingCurve))
                        sb.AppendLine($"  - 节奏曲线：{blueprint.PacingCurve}");
                    if (!string.IsNullOrWhiteSpace(blueprint.PovCharacter))
                        sb.AppendLine($"  - 视角角色：{blueprint.PovCharacter}");
                    if (!string.IsNullOrWhiteSpace(blueprint.EstimatedWordCount))
                        sb.AppendLine($"  - 预计字数：{blueprint.EstimatedWordCount}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Opening))
                        sb.AppendLine($"  - 起：{blueprint.Opening}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Development))
                        sb.AppendLine($"  - 承：{blueprint.Development}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Turning))
                        sb.AppendLine($"  - 转：{blueprint.Turning}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Ending))
                        sb.AppendLine($"  - 合：{blueprint.Ending}");
                    if (!string.IsNullOrWhiteSpace(blueprint.InfoDrop))
                        sb.AppendLine($"  - 信息投放：{blueprint.InfoDrop}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Cast))
                        sb.AppendLine($"  - 角色：{blueprint.Cast}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Locations))
                        sb.AppendLine($"  - 地点：{blueprint.Locations}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Factions))
                        sb.AppendLine($"  - 势力：{blueprint.Factions}");
                    if (!string.IsNullOrWhiteSpace(blueprint.ItemsClues))
                        sb.AppendLine($"  - 道具/线索：{blueprint.ItemsClues}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Characters != null && ctx.Characters.Count > 0)
            {
                sb.AppendLine($"<section name=\"characters\" role=\"entity_reference\" priority=\"normal\" count=\"{ctx.Characters.Count}\">");
                foreach (var c in ctx.Characters)
                {
                    sb.AppendLine($"- **{c.Name}**");
                    if (!string.IsNullOrWhiteSpace(c.CharacterType))
                        sb.AppendLine($"  - 类型：{c.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(c.Identity))
                        sb.AppendLine($"  - 身份：{c.Identity}");
                    if (!string.IsNullOrWhiteSpace(c.Race))
                        sb.AppendLine($"  - 种族：{c.Race}");
                    if (!string.IsNullOrWhiteSpace(c.Appearance))
                        sb.AppendLine($"  - 外貌：{c.Appearance}");
                    if (!string.IsNullOrWhiteSpace(c.Want))
                        sb.AppendLine($"  - 外在目标：{c.Want}");
                    if (!string.IsNullOrWhiteSpace(c.Need))
                        sb.AppendLine($"  - 内在需求：{c.Need}");
                    if (!string.IsNullOrWhiteSpace(c.FlawBelief))
                        sb.AppendLine($"  - 致命缺点：{c.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(c.GrowthPath))
                        sb.AppendLine($"  - 成长路径：{c.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(c.SpecialAbilities))
                        sb.AppendLine($"  - 特殊能力：{c.SpecialAbilities}");
                    if (!string.IsNullOrWhiteSpace(c.NonCombatSkills))
                        sb.AppendLine($"  - 非战斗技能：{c.NonCombatSkills}");
                    if (!string.IsNullOrWhiteSpace(c.SignatureItems))
                        sb.AppendLine($"  - 标志性装备：{c.SignatureItems}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Factions != null && ctx.Factions.Count > 0)
            {
                sb.AppendLine($"<section name=\"factions\" role=\"entity_reference\" priority=\"normal\" count=\"{ctx.Factions.Count}\">");
                foreach (var f in ctx.Factions)
                {
                    sb.AppendLine($"- **{f.Name}**");
                    if (!string.IsNullOrWhiteSpace(f.FactionType))
                        sb.AppendLine($"  - 类型：{f.FactionType}");
                    if (!string.IsNullOrWhiteSpace(f.Goal))
                        sb.AppendLine($"  - 理念目标：{f.Goal}");
                    if (!string.IsNullOrWhiteSpace(f.Leader))
                        sb.AppendLine($"  - 领袖：{ResolveId(f.Leader, characterIdToName)}");
                    if (!string.IsNullOrWhiteSpace(f.StrengthTerritory))
                        sb.AppendLine($"  - 实力/地盘：{f.StrengthTerritory}");
                    if (!string.IsNullOrWhiteSpace(f.MemberTraits))
                        sb.AppendLine($"  - 成员特征：{f.MemberTraits}");
                    if (!string.IsNullOrWhiteSpace(f.Allies))
                        sb.AppendLine($"  - 盟友：{f.Allies}");
                    if (!string.IsNullOrWhiteSpace(f.Enemies))
                        sb.AppendLine($"  - 敌对：{f.Enemies}");
                    if (!string.IsNullOrWhiteSpace(f.NeutralCompetitors))
                        sb.AppendLine($"  - 中立/竞争：{f.NeutralCompetitors}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Locations != null && ctx.Locations.Count > 0)
            {
                sb.AppendLine($"<section name=\"locations\" role=\"entity_reference\" priority=\"normal\" count=\"{ctx.Locations.Count}\">");
                foreach (var loc in ctx.Locations)
                {
                    sb.AppendLine($"- **{loc.Name}**：{TruncateText(loc.Description, 100)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PlotRules != null && ctx.PlotRules.Count > 0)
            {
                sb.AppendLine($"<section name=\"plot_rules\" role=\"narrative_guide\" priority=\"normal\" count=\"{ctx.PlotRules.Count}\">");
                foreach (var p in ctx.PlotRules)
                {
                    sb.AppendLine($"- **{p.Name}**");
                    if (!string.IsNullOrWhiteSpace(p.OneLineSummary))
                        sb.AppendLine($"  - 简介：{p.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(p.Goal))
                        sb.AppendLine($"  - 目标：{p.Goal}");
                    if (!string.IsNullOrWhiteSpace(p.Conflict))
                        sb.AppendLine($"  - 冲突：{p.Conflict}");
                    if (!string.IsNullOrWhiteSpace(p.Result))
                        sb.AppendLine($"  - 结果：{p.Result}");
                    if (!string.IsNullOrWhiteSpace(p.EmotionCurve))
                        sb.AppendLine($"  - 情绪曲线：{p.EmotionCurve}");
                    if (!string.IsNullOrWhiteSpace(p.MainCharacters))
                        sb.AppendLine($"  - 主要角色：{ResolveIds(p.MainCharacters, characterIdToName)}");
                    if (!string.IsNullOrWhiteSpace(p.KeyNpcs))
                        sb.AppendLine($"  - 关键NPC：{ResolveIds(p.KeyNpcs, characterIdToName)}");
                    if (!string.IsNullOrWhiteSpace(p.Location))
                        sb.AppendLine($"  - 地点：{ResolveId(p.Location, locationIdToName)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.ExpandedCharacters != null && ctx.ExpandedCharacters.Count > 0)
            {
                sb.AppendLine($"<section name=\"expanded_characters\" role=\"entity_reference\" priority=\"normal\" count=\"{ctx.ExpandedCharacters.Count}\">");
                foreach (var c in ctx.ExpandedCharacters)
                {
                    sb.AppendLine($"- **{c.Name}**");
                    if (!string.IsNullOrWhiteSpace(c.CharacterType))
                        sb.AppendLine($"  - 类型：{c.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(c.Identity))
                        sb.AppendLine($"  - 身份：{c.Identity}");
                    if (!string.IsNullOrWhiteSpace(c.Want))
                        sb.AppendLine($"  - 外在目标：{c.Want}");
                    if (!string.IsNullOrWhiteSpace(c.FlawBelief))
                        sb.AppendLine($"  - 致命缺点：{c.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(c.SpecialAbilities))
                        sb.AppendLine($"  - 特殊能力：{c.SpecialAbilities}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Scenes != null && ctx.Scenes.Count > 0)
            {
                sb.AppendLine($"<section name=\"scenes\" role=\"scene_structure\" priority=\"normal\" count=\"{ctx.Scenes.Count}\">");
                foreach (var s in ctx.Scenes)
                {
                    sb.AppendLine($"- 场景{s.SceneNumber}：{s.Purpose}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (spec != null)
            {
                var specPrompt = spec.BuildPromptFragment();
                if (!string.IsNullOrWhiteSpace(specPrompt))
                {
                    sb.AppendLine("<section name=\"creative_spec\" role=\"style_constraint\" priority=\"highest\">");
                    sb.AppendLine(specPrompt);
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("</task_context>");
            sb.AppendLine();
        }

        #endregion

        #region 私有方法 - CHANGES输出要求

        private void AppendChangesRequirement(StringBuilder sb)
        {
            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine();
            sb.AppendLine("1. 请根据以上信息生成完整章节正文");
            sb.AppendLine("2. 直接输出小说正文内容，以章节标题（# 第X章：标题）开头");
            sb.AppendLine("3. 禁止输出任何自我介绍、AI身份说明、任务确认等非正文内容");
            sb.AppendLine("4. 禁止输出「好的」「我来生成」「以下是内容」等过渡语");
            sb.AppendLine("5. 只输出纯粹的小说章节正文");
            sb.AppendLine();
            sb.AppendLine("6. **正文末尾必须输出变更摘要**，格式如下：");
            sb.AppendLine();
            sb.AppendLine("<changes_protocol separator=\"---CHANGES---\" format=\"json\" fields=\"9\" mandatory=\"true\">");
            sb.AppendLine("**⚠ ID字段强制规则（违反将触发校验失败并强制重写）**：");
            sb.AppendLine("- 所有 ID 字段必须填写事实账本括号内的 **ShortId**（格式：13字符大写字母开头，如 `D7M3VT2K9P4N`），禁止填写名称文字或拼音");
            sb.AppendLine("- `CharacterId`（含 `CharacterMovements` 和 `RelationshipChanges` 的 key）→ 使用角色 ShortId");
            sb.AppendLine("- `ConflictId` → 使用冲突 ShortId");
            sb.AppendLine("- `ForeshadowId` → 使用伏笔 ShortId");
            sb.AppendLine("- `LocationId` → 使用地点 ShortId");
            sb.AppendLine("- `FactionId` → 使用势力 ShortId");
            sb.AppendLine("- `InvolvedCharacters`（NewPlotPoints中）→ 使用角色 ShortId 列表");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("---CHANGES---");
            sb.AppendLine("{");
            sb.AppendLine("  \"CharacterStateChanges\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"CharacterId\": \"\",");
            sb.AppendLine("      \"NewLevel\": \"\",");
            sb.AppendLine("      \"NewAbilities\": [],");
            sb.AppendLine("      \"LostAbilities\": [],");
            sb.AppendLine("      \"RelationshipChanges\": { ");
            sb.AppendLine("        \"<目标角色ShortId>\": { \"Relation\": \"\", \"TrustDelta\": 0 }");
            sb.AppendLine("      },");
            sb.AppendLine("      \"NewMentalState\": \"\",");
            sb.AppendLine("      \"KeyEvent\": \"\",");
            sb.AppendLine("      \"Importance\": \"normal\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"ConflictProgress\": [");
            sb.AppendLine("    { \"ConflictId\": \"\", \"NewStatus\": \"\", \"Event\": \"\", \"Importance\": \"normal\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"ForeshadowingActions\": [");
            sb.AppendLine("    { \"ForeshadowId\": \"\", \"Action\": \"setup\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"NewPlotPoints\": [");
            sb.AppendLine("    { \"Keywords\": [], \"Context\": \"\", \"InvolvedCharacters\": [], \"Importance\": \"normal\", \"Storyline\": \"main\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"LocationStateChanges\": [");
            sb.AppendLine("    { \"LocationId\": \"\", \"NewStatus\": \"\", \"Event\": \"\", \"Importance\": \"normal\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"FactionStateChanges\": [");
            sb.AppendLine("    { \"FactionId\": \"\", \"NewStatus\": \"\", \"Event\": \"\", \"Importance\": \"normal\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"TimeProgression\": {");
            sb.AppendLine("    \"TimePeriod\": \"\",");
            sb.AppendLine("    \"ElapsedTime\": \"\",");
            sb.AppendLine("    \"KeyTimeEvent\": \"\",");
            sb.AppendLine("    \"Importance\": \"normal\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"CharacterMovements\": [");
            sb.AppendLine("    { \"CharacterId\": \"\", \"FromLocation\": \"\", \"ToLocation\": \"\", \"Importance\": \"normal\" }");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"ItemTransfers\": [");
            sb.AppendLine("    { \"ItemId\": \"\", \"ItemName\": \"\", \"FromHolder\": \"\", \"ToHolder\": \"\", \"NewStatus\": \"active\", \"Event\": \"\", \"Importance\": \"normal\" }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("7. 分隔符必须原样输出为一整行：`---CHANGES---`（仅使用半角连字符 '-'，不得用 '—/–/−' 等其他破折号替换，不得加入额外字符）。");
            sb.AppendLine("8. JSON必须紧跟在分隔符之后（可用代码块包裹），且必须显式包含9个字段（即使为空数组/空对象）。");
            sb.AppendLine();
            sb.AppendLine("**CHANGES字段说明**：");
            sb.AppendLine("- `CharacterStateChanges`: 本章角色状态变化（阶段/能力/关系），无变化则为空数组。Importance标记事件重要性");
            sb.AppendLine("  - `NewAbilities`: 本章新获得的能力（增量，不含已有能力）");
            sb.AppendLine("  - `LostAbilities`: 本章失去的能力（需在KeyEvent中说明原因，如封印/废除）");
            sb.AppendLine("- `ConflictProgress`: 本章冲突进度变化（状态流转），无变化则为空数组。Importance标记事件重要性");
            sb.AppendLine("- `ForeshadowingActions`: 本章伏笔动作（setup/payoff），无动作则为空数组");
            sb.AppendLine("- `NewPlotPoints`: 本章新增关键情节，无新增则为空数组");
            sb.AppendLine("- `LocationStateChanges`: 本章地点状态变化（摧毁/改建/封锁等），无变化则为空数组。Importance标记事件重要性");
            sb.AppendLine("- `FactionStateChanges`: 本章势力状态变化（壮大/覆灭/联盟/分裂等），无变化则为空数组。Importance标记事件重要性");
            sb.AppendLine("- `TimeProgression`: 本章时间推进（时间段/经过时间/关键时间事件），无明确时间则留空字符串。Importance标记事件重要性");
            sb.AppendLine("- `CharacterMovements`: 本章角色位置移动（从哪到哪），无移动则为空数组。Importance标记事件重要性");
            sb.AppendLine("- `ItemTransfers`: 本章物品/道具流转（获得/失去/转交/毁坏/封印），无流转则为空数组。Importance标记事件重要性");
            sb.AppendLine();
            sb.AppendLine("**严格字段要求（必须使用以下字段名，不得改名）**：");
            sb.AppendLine("- CharacterStateChanges[]: CharacterId, NewLevel, NewAbilities, LostAbilities, RelationshipChanges, NewMentalState, KeyEvent, Importance（normal/important/critical）");
            sb.AppendLine("- ConflictProgress[]: ConflictId, NewStatus, Event, Importance（normal/important/critical）");
            sb.AppendLine("- ForeshadowingActions[]: ForeshadowId, Action（仅 setup 或 payoff）");
            sb.AppendLine("- NewPlotPoints[]: Keywords, Context, InvolvedCharacters, Importance（normal/important/critical）, Storyline（main/sub/character_arc）");
            sb.AppendLine("- LocationStateChanges[]: LocationId, NewStatus, Event, Importance（normal/important/critical）");
            sb.AppendLine("- FactionStateChanges[]: FactionId, NewStatus, Event, Importance（normal/important/critical）");
            sb.AppendLine("- TimeProgression{}: TimePeriod（如\"第三天黄昏\"）, ElapsedTime（如\"半天\"）, KeyTimeEvent, Importance（normal/important/critical）");
            sb.AppendLine("- CharacterMovements[]: CharacterId（角色ShortId）, FromLocation（地点ShortId）, ToLocation（地点ShortId）, Importance（normal/important/critical）");
            sb.AppendLine("- ItemTransfers[]: ItemId（物品ShortId）, ItemName（显示名称，如\"天命剑\"）, FromHolder（角色ShortId，无持有者则留空）, ToHolder（角色ShortId，毁坏/失去则留空）, NewStatus（active/lost/destroyed/sealed）, Event, Importance（normal/important/critical）");

            sb.AppendLine("**Importance使用规范**：");
            sb.AppendLine("- `normal`: 常规变化（默认值），历史记录可被裁剪压缩");
            sb.AppendLine("- `important`: 较重要变化，但可被压缩");
            sb.AppendLine("- `critical`: **永久保留，不会被裁剪**。仅用于对全书连贯性至关重要的事件，例如：角色死亡/断臂等不可逆变化、血誓/契约等长期约束、阵营叛变、重大时间跳跃等。请谨慎使用，每章最多标记1-2个critical");
            sb.AppendLine("</changes_protocol>");
            sb.AppendLine();
            sb.AppendLine("<final_checklist mandatory=\"true\">");
            sb.AppendLine("1. 分隔符是否为精确的 `---CHANGES---`（仅半角连字符`-`，共13个字符，不得用——/–/−替换）？");
            sb.AppendLine("2. JSON是否包含全部9个顶级字段（CharacterStateChanges/ConflictProgress/ForeshadowingActions/NewPlotPoints/LocationStateChanges/FactionStateChanges/TimeProgression/CharacterMovements/ItemTransfers）？");
            sb.AppendLine("3. 所有ID字段（CharacterId/ConflictId/ForeshadowId/LocationId/FactionId/ItemId/FromHolder/ToHolder/FromLocation/ToLocation/InvolvedCharacters中的元素）是否填写了事实账本括号内的 **ShortId**（格式：13字符大写字母开头，如`D7M3VT2K9P4N`）？禁止填写名称文字或拼音。");
            sb.AppendLine("4. 正文是否以章节标题开头，且不含任何AI过渡语（\"好的\"、\"我来生成\"等）？");
            sb.AppendLine("5. 无变化的字段是否仍保留为空数组`[]`或空对象`{}`，而非省略？");
            sb.AppendLine("</final_checklist>");
            sb.AppendLine("</output_requirements>");
        }

        #endregion

        private void AppendTailEntityChecklist(StringBuilder sb, ContentTaskContext ctx)
        {
            var chars = new List<string>();
            var factions = new List<string>();
            var locs = new List<string>();

            if (ctx.Characters != null)
                foreach (var c in ctx.Characters)
                    if (!string.IsNullOrWhiteSpace(c.Name)) chars.Add(c.Name);
            if (ctx.ExpandedCharacters != null)
                foreach (var c in ctx.ExpandedCharacters)
                    if (!string.IsNullOrWhiteSpace(c.Name) && !chars.Contains(c.Name)) chars.Add(c.Name);
            if (ctx.Locations != null)
                foreach (var l in ctx.Locations)
                    if (!string.IsNullOrWhiteSpace(l.Name)) locs.Add(l.Name);

            if (ctx.Blueprints != null)
            {
                char[] sep = { ',', '\uff0c', '\u3001', ';', '\uff1b' };
                foreach (var bp in ctx.Blueprints)
                {
                    if (!string.IsNullOrWhiteSpace(bp.PovCharacter) && !chars.Contains(bp.PovCharacter.Trim()))
                        chars.Add(bp.PovCharacter.Trim());
                    foreach (var p in (bp.Cast ?? "").Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !chars.Contains(n)) chars.Add(n); }
                    foreach (var p in (bp.Factions ?? "").Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !factions.Contains(n)) factions.Add(n); }
                    foreach (var p in (bp.Locations ?? "").Split(sep, StringSplitOptions.RemoveEmptyEntries))
                    { var n = p.Trim(); if (n.Length >= 2 && !locs.Contains(n)) locs.Add(n); }
                }
            }

            if (chars.Count == 0 && factions.Count == 0 && locs.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("<entity_checklist mandatory=\"true\" priority=\"critical\">");
            sb.AppendLine("**\u5199\u4f5c\u524d\u6700\u7ec8\u786e\u8ba4**\uff1a\u4ee5\u4e0b\u5b9e\u4f53\u5fc5\u987b\u5728\u6b63\u6587\u4e2d\u51fa\u73b0\uff08\u6709\u5bf9\u8bdd\u6216\u884c\u52a8\uff09\uff0c\u7f3a\u4efb\u4f55\u4e00\u4e2a\u5c06\u4e0d\u901a\u8fc7\u6821\u9a8c\uff1a");
            if (chars.Count > 0)
                sb.AppendLine($"  \u89d2\u8272\uff1a{string.Join(" / ", chars)}");
            if (factions.Count > 0)
                sb.AppendLine($"  \u52bf\u529b\uff1a{string.Join(" / ", factions)}");
            if (locs.Count > 0)
                sb.AppendLine($"  \u5730\u70b9\uff1a{string.Join(" / ", locs)}");
            sb.AppendLine("</entity_checklist>");
        }

        #region 私有方法 - CHANGES ID快速参考

        private static void AppendChangesIdQuickRef(StringBuilder sb, FactSnapshot snapshot)
        {
            var charIds    = snapshot.CharacterStates   .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            var conflictIds= snapshot.ConflictProgress  .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)).ToList();
            var fsIds      = snapshot.ForeshadowingStatus.Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name)).ToList();
            var locIds     = snapshot.LocationStates    .Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.Name)).ToList();
            var factionIds = snapshot.FactionStates     .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name)).ToList();

            sb.AppendLine();
            sb.AppendLine("<changes_id_ref mandatory=\"true\">");
            sb.AppendLine("CHANGES字段ID约束（按实体类型强制执行，禁止伪造ShortId）：");

            if (charIds.Count == 0)
                sb.AppendLine("  ⚠ 角色：账本无追踪记录 → CharacterStateChanges=[] CharacterMovements=[] RelationshipChanges={} InvolvedCharacters=[]");
            else
            {
                sb.Append("  角色CharacterId：");
                sb.AppendLine(string.Join(" | ", charIds.Select(s => $"{s.Name}={s.Id}")));
            }

            if (conflictIds.Count == 0)
                sb.AppendLine("  ⚠ 冲突：账本无追踪记录 → ConflictProgress=[]");
            else
            {
                sb.Append("  冲突ConflictId：");
                sb.AppendLine(string.Join(" | ", conflictIds.Select(c => $"{c.Name}={c.Id}")));
            }

            if (fsIds.Count == 0)
                sb.AppendLine("  ⚠ 伏笔：账本无追踪记录 → ForeshadowingActions=[]");
            else
            {
                sb.AppendLine("  伏笔ForeshadowId（含当前状态，Action只能是下表允许值）：");
                foreach (var f in fsIds)
                {
                    string state, allowed;
                    if (f.IsResolved)        { state = "已揭示"; allowed = "禁止任何Action，请从ForeshadowingActions中省略此条目"; }
                    else if (f.IsSetup)      { state = "已埋设"; allowed = "允许 payoff，禁止 setup"; }
                    else                     { state = "未埋设"; allowed = "允许 setup，禁止 payoff"; }
                    sb.AppendLine($"    {f.Name}={f.Id}（{state}，{allowed}）");
                }
            }

            if (locIds.Count == 0)
                sb.AppendLine("  ⚠ 地点：账本无追踪记录 → LocationStateChanges=[]");
            else
            {
                sb.Append("  地点LocationId：");
                sb.AppendLine(string.Join(" | ", locIds.Select(l => $"{l.Name}={l.Id}")));
            }

            if (factionIds.Count == 0)
                sb.AppendLine("  ⚠ 势力：账本无追踪记录 → FactionStateChanges=[]");
            else
            {
                sb.Append("  势力FactionId：");
                sb.AppendLine(string.Join(" | ", factionIds.Select(f => $"{f.Name}={f.Id}")));
            }

            var itemIds = snapshot.ItemStates?.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name)).ToList() ?? new();
            if (itemIds.Count == 0)
                sb.AppendLine("  ⚠ 物品：账本无追踪记录 → ItemTransfers=[]");
            else
            {
                sb.Append("  物品ItemId：");
                sb.AppendLine(string.Join(" | ", itemIds.Select(i => $"{i.Name}={i.Id}")));
            }

            sb.AppendLine("</changes_id_ref>");
        }

        #endregion

        #region 私有方法 - 辅助

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private static string TruncateLine(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private static int EstimateTokenCount(string text)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(text);

        #endregion
    }
}
