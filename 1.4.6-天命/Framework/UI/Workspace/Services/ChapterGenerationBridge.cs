using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Framework.UI.Workspace.Services
{
    public class ChapterGenerationBridge
    {
        private readonly GuideContextService _guideContextService;
        private readonly IGeneratedContentService _contentService;

        public ChapterGenerationBridge(GuideContextService guideContextService, GeneratedContentService contentService)
        {
            _guideContextService = guideContextService;
            _contentService = contentService;
        }

        public async Task<string> GetGenerationPromptAsync(string chapterId)
        {
            try
            {
                var context = await _guideContextService.BuildContentContextAsync(chapterId);
                if (context == null)
                {
                    TM.App.Log($"[ChapterGenerationBridge] 获取上下文失败: {chapterId}，请确认已执行打包");
                    return string.Empty;
                }

                return BuildPromptFromGuide(context);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterGenerationBridge] 获取生成Prompt异常: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<ContentTaskContext?> GetContentContextAsync(string chapterId)
        {
            try
            {
                return await _guideContextService.BuildContentContextAsync(chapterId);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterGenerationBridge] 获取Context异常: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetChapterContentAsync(string chapterId)
        {
            return await _contentService.GetChapterAsync(chapterId);
        }

        private string BuildPromptFromGuide(ContentTaskContext ctx)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<chapter_generation_task>");
            sb.AppendLine();

            if (ctx.HistoricalMilestones?.Count > 0)
            {
                sb.AppendLine("<section name=\"historical_milestones\">");
                sb.AppendLine("> 以下为各前卷历史摘要，用于维持跨卷剧情连贯性");
                foreach (var milestone in ctx.HistoricalMilestones)
                {
                    if (string.IsNullOrWhiteSpace(milestone.Milestone)) continue;
                    sb.AppendLine($"<item name=\"第{milestone.VolumeNumber}卷\">");
                    sb.AppendLine(milestone.Milestone);
                    sb.AppendLine("</item>");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PreviousVolumeArchives?.Count > 0)
            {
                sb.AppendLine("<section name=\"previous_volume_baselines\">");
                sb.AppendLine("> 以下为前卷末结构化状态，请严格遵守角色状态/冲突进度等约束");
                foreach (var archive in ctx.PreviousVolumeArchives)
                {
                    sb.AppendLine($"<item name=\"第{archive.VolumeNumber}卷末（截至 {archive.LastChapterId}）\">");
                    if (archive.CharacterStates?.Count > 0)
                    {
                        sb.AppendLine("**角色状态**");
                        foreach (var cs in archive.CharacterStates)
                            sb.AppendLine($"- **{cs.Name}**：{cs.Stage}");
                    }
                    if (archive.ConflictProgress?.Count > 0)
                    {
                        sb.AppendLine("**冲突进度**");
                        foreach (var cp in archive.ConflictProgress)
                            sb.AppendLine($"- {cp.Name}：{cp.Status}");
                    }
                    var unresolved = archive.ForeshadowingStatus?.Where(f => f.IsSetup && !f.IsResolved).ToList();
                    if (unresolved?.Count > 0)
                    {
                        sb.AppendLine("**未解伏笔**");
                        foreach (var f in unresolved)
                            sb.AppendLine($"- {f.Name}（埋设于 {f.SetupChapterId}）");
                    }
                    if (archive.CharacterLocations?.Count > 0)
                    {
                        sb.AppendLine("**卷末角色位置**");
                        foreach (var cl in archive.CharacterLocations)
                            sb.AppendLine($"- {cl.CharacterName} → {cl.CurrentLocation}");
                    }
                    sb.AppendLine("</item>");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PreviousChapterSummaries?.Count > 0)
            {
                sb.AppendLine("<section name=\"previous_chapters\">");
                foreach (var summary in ctx.PreviousChapterSummaries)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章**：{summary.Summary}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else if (ctx.MdPreviousChapterSummaries?.Count > 0)
            {
                sb.AppendLine("<section name=\"previous_chapters\">");
                foreach (var summary in ctx.MdPreviousChapterSummaries)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章**：{summary.Summary}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.FactSnapshot != null)
            {
                var fs = ctx.FactSnapshot;
                sb.AppendLine("<section name=\"current_state\">");
                if (fs.CharacterStates?.Count > 0)
                {
                    sb.AppendLine("**角色当前状态**");
                    foreach (var cs in fs.CharacterStates)
                    {
                        sb.AppendLine($"- **{cs.Name}**：{cs.Stage}");
                        if (!string.IsNullOrWhiteSpace(cs.Abilities))
                            sb.AppendLine($"  - 能力：{TruncateText(cs.Abilities, 120)}");
                        if (!string.IsNullOrWhiteSpace(cs.Relationships))
                            sb.AppendLine($"  - 关系：{TruncateText(cs.Relationships, 100)}");
                    }
                }
                if (fs.ConflictProgress?.Count > 0)
                {
                    sb.AppendLine("**当前冲突进度**");
                    foreach (var cp in fs.ConflictProgress)
                    {
                        sb.AppendLine($"- {cp.Name}：{cp.Status}");
                        if (cp.RecentProgress != null && cp.RecentProgress.Count > 0)
                        {
                            var tail = cp.RecentProgress.Count <= 2
                                ? cp.RecentProgress
                                : cp.RecentProgress.TakeLast(2).ToList();
                            sb.AppendLine($"  - 最新进展：{string.Join("；", tail)}");
                        }
                    }
                }
                var pendingFow = fs.ForeshadowingStatus?.Where(f => f.IsSetup && !f.IsResolved).ToList();
                if (pendingFow?.Count > 0)
                {
                    sb.AppendLine("**悬而未决的伏笔**");
                    foreach (var f in pendingFow)
                        sb.AppendLine($"- {f.Name}（{(f.IsOverdue ? "⚠️逾期！" : "待回收")}，埋设于 {f.SetupChapterId}）");
                }
                if (fs.CharacterLocations?.Count > 0)
                {
                    sb.AppendLine("**角色当前位置**");
                    foreach (var cl in fs.CharacterLocations)
                        sb.AppendLine($"- {cl.CharacterName} → {cl.CurrentLocation}");
                }
                if (fs.Timeline?.Count > 0)
                {
                    sb.AppendLine("**故事时间线**");
                    foreach (var t in fs.Timeline.TakeLast(2))
                        sb.AppendLine($"- {t.TimePeriod}：{t.KeyTimeEvent}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VectorRecallFragments?.Count > 0)
            {
                sb.AppendLine("<section name=\"vector_recall\">");
                foreach (var frag in ctx.VectorRecallFragments)
                {
                    if (string.IsNullOrWhiteSpace(frag.Content)) continue;
                    sb.AppendLine($"[来自 {frag.ChapterId}]");
                    sb.AppendLine(TruncateText(frag.Content, 300));
                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(ctx.PreviousChapterTail))
            {
                sb.AppendLine("<section name=\"previous_chapter_tail\">");
                sb.AppendLine("```");
                sb.AppendLine(ctx.PreviousChapterTail);
                sb.AppendLine("```");
                sb.AppendLine("> 请自然衔接上文。");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.StateDivergenceWarnings?.Count > 0 || ctx.VectorRecallDegraded)
            {
                sb.AppendLine("<section name=\"generation_warnings\">");
                if (ctx.VectorRecallDegraded)
                    sb.AppendLine("> 🔴 向量召回不可用，远距离一致性精度下降，请特别注意跨章细节的自洽");
                foreach (var w in ctx.StateDivergenceWarnings!)
                    sb.AppendLine($"> ⚠️ {w}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            bool hasTaskLayer = !string.IsNullOrEmpty(ctx.Title) || ctx.Characters?.Count > 0;

            if (hasTaskLayer)
            {
                sb.AppendLine("<section name=\"chapter_task\">");
                sb.AppendLine($"- 章节：{ctx.ChapterId}");
                sb.AppendLine($"- 标题：{ctx.Title}");
                sb.AppendLine($"- 概要：{ctx.Summary}");
                if (ctx.Rhythm != null)
                {
                    sb.AppendLine($"- 节奏：{ctx.Rhythm.PaceType} / {ctx.Rhythm.Intensity} / {ctx.Rhythm.EmotionalTone}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("<section name=\"continuation_task\">");
                sb.AppendLine($"- 章节：{ctx.ChapterId}");
                sb.AppendLine("> 当前为纯续写模式，请根据上一章结尾自然续写。");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.WorldRules?.Count > 0)
            {
                sb.AppendLine($"<section name=\"world_rules\" count=\"{ctx.WorldRules.Count}\">世界观规则");
                foreach (var rule in ctx.WorldRules)
                {
                    sb.AppendLine($"- **{rule.Name}**");
                    if (!string.IsNullOrWhiteSpace(rule.OneLineSummary))
                        sb.AppendLine($"  - 简介：{rule.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(rule.HardRules))
                        sb.AppendLine($"  - 硬规则：{rule.HardRules}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeOutline != null)
            {
                sb.AppendLine("<section name=\"outline\">");
                sb.AppendLine($"- 名称：{ctx.VolumeOutline.Name}");
                sb.AppendLine($"- 主题：{ctx.VolumeOutline.Theme}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeDesign != null)
            {
                sb.AppendLine("<section name=\"volume_design\">");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTitle))
                    sb.AppendLine($"- 卷标题：{ctx.VolumeDesign.VolumeTitle}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTheme))
                    sb.AppendLine($"- 卷主题：{ctx.VolumeDesign.VolumeTheme}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.StageGoal))
                    sb.AppendLine($"- 阶段目标：{ctx.VolumeDesign.StageGoal}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.MainConflict))
                    sb.AppendLine($"- 主冲突：{ctx.VolumeDesign.MainConflict}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.ChapterPlan != null)
            {
                sb.AppendLine("<section name=\"chapter_plan\">");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTitle))
                    sb.AppendLine($"- 章节标题：{ctx.ChapterPlan.ChapterTitle}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTheme))
                    sb.AppendLine($"- 章节主题：{ctx.ChapterPlan.ChapterTheme}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainGoal))
                    sb.AppendLine($"- 主目标：{ctx.ChapterPlan.MainGoal}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.KeyTurn))
                    sb.AppendLine($"- 关键转折：{ctx.ChapterPlan.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Hook))
                    sb.AppendLine($"- 结尾钩子：{ctx.ChapterPlan.Hook}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Characters?.Count > 0)
            {
                sb.AppendLine($"<section name=\"characters\" count=\"{ctx.Characters.Count}\">本章角色");
                foreach (var c in ctx.Characters)
                {
                    sb.AppendLine($"- **{c.Name}**：{c.Identity}，{c.Race}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Factions?.Count > 0)
            {
                sb.AppendLine($"<section name=\"factions\" count=\"{ctx.Factions.Count}\">本章势力");
                foreach (var f in ctx.Factions)
                {
                    sb.AppendLine($"- **{f.Name}**：{TruncateText(f.Description, 100)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Locations?.Count > 0)
            {
                sb.AppendLine($"<section name=\"locations\" count=\"{ctx.Locations.Count}\">本章地点");
                foreach (var loc in ctx.Locations)
                {
                    sb.AppendLine($"- **{loc.Name}**：{TruncateText(loc.Description, 100)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.PlotRules?.Count > 0)
            {
                sb.AppendLine($"<section name=\"plot_rules\" count=\"{ctx.PlotRules.Count}\">本章剧情规则");
                foreach (var p in ctx.PlotRules)
                {
                    sb.AppendLine($"- **{p.Name}**：{p.Goal}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Blueprints?.Count > 0)
            {
                sb.AppendLine($"<section name=\"blueprints\" count=\"{ctx.Blueprints.Count}\">章节蓝图");
                foreach (var blueprint in ctx.Blueprints)
                {
                    var bpTitle = !string.IsNullOrWhiteSpace(blueprint.SceneTitle)
                        ? blueprint.SceneTitle : blueprint.Name;
                    sb.AppendLine($"- **{bpTitle}**");
                    if (!string.IsNullOrWhiteSpace(blueprint.OneLineStructure))
                        sb.AppendLine($"  - 结构：{blueprint.OneLineStructure}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Opening))
                        sb.AppendLine($"  - 起：{blueprint.Opening}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Turning))
                        sb.AppendLine($"  - 转：{blueprint.Turning}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Ending))
                        sb.AppendLine($"  - 合：{blueprint.Ending}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Scenes?.Count > 0)
            {
                sb.AppendLine($"<section name=\"scenes\" count=\"{ctx.Scenes.Count}\">场景规划");
                foreach (var s in ctx.Scenes)
                {
                    sb.AppendLine($"- 场景{s.SceneNumber}：{s.Purpose}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<requirements>");
            sb.AppendLine("请根据以上信息生成完整章节正文。");
            sb.AppendLine("</requirements>");
            sb.AppendLine("</chapter_generation_task>");

            return sb.ToString();
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }
    }
}
