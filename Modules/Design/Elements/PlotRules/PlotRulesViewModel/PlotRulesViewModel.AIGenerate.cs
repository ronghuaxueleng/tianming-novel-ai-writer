using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Metadata;

namespace TM.Modules.Design.Elements.PlotRules
{
    public partial class PlotRulesViewModel
    {
        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return new AIGenerationConfig
            {
                Category = "小说设计师",
                ActiveModuleHint = "剧情规则",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = ResponseFormat.Json,
                MessagePrefix = "剧情设计",
                ProgressMessage = "正在设计剧情规则...",
                CompleteMessage = "剧情设计完成",
                InputVariables = new()
                {
                    ["规则名称"] = () => FormName,
                },
                OutputFields = new()
                {
                    ["总卷数"] = v =>
                    {
                        if (string.IsNullOrWhiteSpace(FormTargetVolume))
                            FormTargetVolume = v;
                    },
                    ["所属卷"] = v => FormAssignedVolume = EntityNameNormalizeHelper.FilterToCandidate(v, AssignedVolumeOptions),
                    ["一句话简介"] = v => FormOneLineSummary = v,
                    ["事件类型"] = v => FormEventType = EntityNameNormalizeHelper.FilterToCandidate(v, EventTypeOptions),
                    ["所属阶段"] = v => FormStoryPhase = v,
                    ["前置条件触发"] = v => FormPrerequisitesTrigger = v,
                    ["主要角色"] = v => FormMainCharacters = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["关键NPC"] = v => FormKeyNpcs = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["地点"] = v => FormLocation = FilterToCandidateOrRaw(v, AvailableLocations),
                    ["时间跨度"] = v => FormTimeDuration = v,
                    ["步骤标题"] = v => FormStepTitle = v,
                    ["目标"] = v => FormGoal = v,
                    ["冲突"] = v => FormConflict = v,
                    ["结果"] = v => FormResult = v,
                    ["情绪曲线"] = v => FormEmotionCurve = v,
                    ["主线推进"] = v => FormMainPlotPush = v,
                    ["角色成长"] = v => FormCharacterGrowth = v,
                    ["世界观揭示"] = v => FormWorldReveal = v,
                    ["奖励线索"] = v => FormRewardsClues = v,
                },
                OutputFieldGetters = new()
                {
                    ["总卷数"] = () => FormTargetVolume,
                    ["所属卷"] = () => FormAssignedVolume,
                    ["一句话简介"] = () => FormOneLineSummary,
                    ["事件类型"] = () => FormEventType,
                    ["所属阶段"] = () => FormStoryPhase,
                    ["前置条件触发"] = () => FormPrerequisitesTrigger,
                    ["主要角色"] = () => FormMainCharacters,
                    ["关键NPC"] = () => FormKeyNpcs,
                    ["地点"] = () => FormLocation,
                    ["时间跨度"] = () => FormTimeDuration,
                    ["步骤标题"] = () => FormStepTitle,
                    ["目标"] = () => FormGoal,
                    ["冲突"] = () => FormConflict,
                    ["结果"] = () => FormResult,
                    ["情绪曲线"] = () => FormEmotionCurve,
                    ["主线推进"] = () => FormMainPlotPush,
                    ["角色成长"] = () => FormCharacterGrowth,
                    ["世界观揭示"] = () => FormWorldReveal,
                    ["奖励线索"] = () => FormRewardsClues,
                },
                ContextProvider = async () => await GetEnhancedPlotContextAsync(),
                BatchFieldKeyMap = CreateBatchFieldKeyMap(),
                BatchIndexFields = new() { "Name", "AssignedVolume", "EventType", "MainCharacters" }
            };
        }

        public static Dictionary<string, string> CreateBatchFieldKeyMap()
            => EntityFieldMeta.GetFieldKeyMap("plotrules");

        protected override ModuleNormalizationConfig? GetNormalizationConfig()
        {
            return new ModuleNormalizationConfig
            {
                ModuleName = nameof(PlotRulesViewModel),
                Rules = new List<FieldNormalizationRule>
                {
                    new()
                    {
                        FieldName = "MainCharacters",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableCharacters.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    },
                    new()
                    {
                        FieldName = "KeyNpcs",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableCharacters.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    },
                    new()
                    {
                        FieldName = "Location",
                        Type = NormalizationType.DynamicList,
                        DynamicOptionsProvider = () => AvailableLocations.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                        DefaultValue = string.Empty,
                        AllowEmpty = true
                    }
                }
            };
        }

        protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            var isValidTotalVolume = int.TryParse(FormTargetVolume?.Trim(), out var n) && n > 0;
            IsAIGenerateEnabled = hasSelection && isValidTotalVolume;
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (!base.CanExecuteAIGenerate()) return false;

            return int.TryParse(FormTargetVolume?.Trim(), out var n) && n > 0;
        }

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllPlotRules().Select(r => r.Name);
        protected override int GetBaseBatchSize() => 10;
        protected override int GetBatchSize64K() => 12;
        protected override int GetBatchSize128K() => 15;

        protected override async Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllPlotRules().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Service.BeginBatchSave();
            try
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name))
                            name = $"剧情_{DateTime.Now:HHmmss}_{result.Count + 1}";

                        var baseName = name;

                        if (dbNames.Contains(baseName))
                        {
                            TM.App.Log($"[PlotRulesViewModel] 跳过已存在剧情: {baseName}");
                            continue;
                        }

                        int suffix = 1;
                        while (batchNames.Contains(name))
                        {
                            name = $"{baseName}_{suffix++}";
                        }
                        batchNames.Add(name);
                        dbNames.Add(name);

                        var assignedVolume = EntityNameNormalizeHelper.FilterToCandidate(
                            reader.GetString("AssignedVolume"),
                            BuildAssignedVolumeOptions(string.IsNullOrWhiteSpace(FormTargetVolume)
                                ? reader.GetString("TargetVolume")
                                : FormTargetVolume));
                        var eventType = EntityNameNormalizeHelper.FilterToCandidate(reader.GetString("EventType"), EventTypeOptions);
                        var (mainCharacters, keyNpcs, location) = NormalizePlotReferences(
                            reader.GetString("MainCharacters"),
                            reader.GetString("KeyNpcs"),
                            reader.GetString("Location"));
                        var data = new PlotRulesData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = name,
                            Category = categoryName,
                            IsEnabled = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            TargetVolume = string.IsNullOrWhiteSpace(FormTargetVolume)
                                ? reader.GetString("TargetVolume")
                                : FormTargetVolume,
                            AssignedVolume = assignedVolume,
                            OneLineSummary = reader.GetString("OneLineSummary"),
                            EventType = eventType,
                            StoryPhase = reader.GetString("StoryPhase"),
                            PrerequisitesTrigger = reader.GetString("PrerequisitesTrigger"),
                            MainCharacters = mainCharacters,
                            KeyNpcs = keyNpcs,
                            Location = location,
                            TimeDuration = reader.GetString("TimeDuration"),
                            StepTitle = reader.GetString("StepTitle"),
                            Goal = reader.GetString("Goal"),
                            Conflict = reader.GetString("Conflict"),
                            Result = reader.GetString("Result"),
                            EmotionCurve = reader.GetString("EmotionCurve"),
                            MainPlotPush = reader.GetString("MainPlotPush"),
                            CharacterGrowth = reader.GetString("CharacterGrowth"),
                            WorldReveal = reader.GetString("WorldReveal"),
                            RewardsClues = reader.GetString("RewardsClues"),
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };

                        entity["Name"] = name;
                        entity["AssignedVolume"] = assignedVolume;
                        entity["EventType"] = eventType;
                        entity["MainCharacters"] = mainCharacters;
                        await Service.AddPlotRuleAsync(data);
                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[PlotRulesViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[PlotRulesViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }

        private async Task<string> GetEnhancedPlotContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            var baseContext = await _contextService.GetPlotContextStringAsync();
            if (!string.IsNullOrWhiteSpace(baseContext))
            {
                sb.AppendLine(baseContext);
                sb.AppendLine();
            }

            var availableChars = AvailableCharacters.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (availableChars.Count > 0)
            {
                sb.AppendLine("<section name=\"available_characters\">");
                sb.AppendLine("主要角色/关键NPC必须从以下列表中选择");
                sb.AppendLine(string.Join("、", availableChars));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            var availableLocs = AvailableLocations.Where(l => !string.IsNullOrEmpty(l)).ToList();
            if (availableLocs.Count > 0)
            {
                sb.AppendLine("<section name=\"available_locations\">");
                sb.AppendLine("地点必须从以下列表中选择");
                sb.AppendLine(string.Join("、", availableLocs));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「主要角色」「关键NPC」为多选字段，请只填写角色名称；如有多名，请在字符串内用换行分条；无则填写「暂无」。");
            sb.AppendLine("2. 「地点」请只填写位置名称；有则填写，无则填写「暂无」。");
            sb.AppendLine("3. 「所属卷」必须为「全局」或「第N卷」。");
            sb.AppendLine($"4. 「事件类型」必须从以下选项中选择：{string.Join("、", EventTypeOptions)}");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            if (int.TryParse(FormTargetVolume?.Trim(), out var totalVolumes) && totalVolumes > 0)
            {
                sb.AppendLine($"<volume_count_constraint count=\"{totalVolumes}\">");
                sb.AppendLine($"批量生成时，每条剧情事件的「所属卷」字段必须填写为「全局」或「第1卷」~「第{totalVolumes}卷」之一，合理分配确保各卷均有覆盖。");
                sb.AppendLine($"事件类型必须从以下选项中选择：主线剧情、卷主线、支线剧情、过渡剧情、伏笔埋设、伏笔揭示");
                sb.AppendLine("</volume_count_constraint>");
                sb.AppendLine();
            }

            var existingPlots = Service.GetAllPlotRules()
                .Where(p => p.IsEnabled && p.Id != _currentEditingData?.Id)
                .Select(p => $"- **{p.Name}**（{p.AssignedVolume}）：{p.OneLineSummary}")
                .ToList();
            if (existingPlots.Count > 0)
            {
                sb.AppendLine("<section name=\"existing_plot_events\">");
                sb.AppendLine("以下剧情事件已存在，批量生成时请避免语义重复，并保持叙事逻辑连贯：");
                sb.AppendLine(string.Join("\n", existingPlots));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
