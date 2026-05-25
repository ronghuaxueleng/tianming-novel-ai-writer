using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Metadata;

namespace TM.Modules.Generate.Elements.VolumeDesign
{
    public partial class VolumeDesignViewModel
    {
        private static readonly System.Text.RegularExpressions.Regex VolumeTitlePrefixRegex = new(@"^第\s*\d+\s*卷(?:\s*[：:]\s*)?", System.Text.RegularExpressions.RegexOptions.Compiled);

        private List<VolumeChapterRange>? _batchPreCalculatedRanges;
        private List<VolumeChapterRange>? _batchAllRanges;
        private int _batchExpectedTotalVolumes;
        private int _batchRangeIndex;

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return new AIGenerationConfig
            {
                Category = "小说创作者",
                ActiveModuleHint = "分卷设计",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = ResponseFormat.Json,
                MessagePrefix = "分卷设计",
                ProgressMessage = "正在设计分卷...",
                CompleteMessage = "分卷设计完成",
                InputVariables = new()
                {
                    ["大纲名称"] = () => FormVolumeNumber > 0
                        ? $"第{FormVolumeNumber}卷：{FormVolumeTitle}"
                        : FormVolumeTitle,
                    ["章节标题"] = () => string.Empty,
                    ["场景标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["卷序号"] = v => FormVolumeNumber = SafeParseInt(v),
                    ["卷标题"] = v => FormVolumeTitle = VolumeTitlePrefixRegex.Replace(v.Trim(), string.Empty),
                    ["卷主题"] = v => FormVolumeTheme = v,
                    ["卷阶段目标"] = v => FormStageGoal = v,

                    ["卷主冲突"] = v => FormMainConflict = v,
                    ["压力来源"] = v => FormPressureSource = v,
                    ["关键转折"] = v => FormKeyEvents = v,
                    ["卷开篇状态"] = v => FormOpeningState = v,
                    ["卷收束状态"] = v => FormEndingState = v,

                    ["章节分配总览"] = v => { if (string.IsNullOrWhiteSpace(FormChapterAllocationOverview)) FormChapterAllocationOverview = v; },
                    ["剧情分配"] = v => { if (string.IsNullOrWhiteSpace(FormPlotAllocation)) FormPlotAllocation = v; },
                    ["章节生成提示"] = v => { if (string.IsNullOrWhiteSpace(FormChapterGenerationHints)) FormChapterGenerationHints = v; },

                    ["出场角色"] = v => FormReferencedCharacterNames = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及势力"] = v => FormReferencedFactionNames = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["涉及地点"] = v => FormReferencedLocationNames = FilterToCandidatesOrRaw(v, AvailableLocations),
                },
                OutputFieldGetters = new()
                {
                    ["卷序号"] = () => FormVolumeNumber.ToString(),
                    ["卷标题"] = () => FormVolumeTitle,
                    ["卷主题"] = () => FormVolumeTheme,
                    ["卷阶段目标"] = () => FormStageGoal,

                    ["卷主冲突"] = () => FormMainConflict,
                    ["压力来源"] = () => FormPressureSource,
                    ["关键转折"] = () => FormKeyEvents,
                    ["卷开篇状态"] = () => FormOpeningState,
                    ["卷收束状态"] = () => FormEndingState,

                    ["章节分配总览"] = () => FormChapterAllocationOverview,
                    ["剧情分配"] = () => FormPlotAllocation,
                    ["章节生成提示"] = () => FormChapterGenerationHints,

                    ["出场角色"] = () => FormReferencedCharacterNames,
                    ["涉及势力"] = () => FormReferencedFactionNames,
                    ["涉及地点"] = () => FormReferencedLocationNames,
                },
                ContextProvider = async () =>
                {
                    RefreshEntityOptions();

                    int volNum = 0, startChapter = 0, endChapter = 0, targetCount = 0;
                    if (_batchPreCalculatedRanges != null && _batchRangeIndex < _batchPreCalculatedRanges.Count)
                    {
                        var r = _batchPreCalculatedRanges[_batchRangeIndex];
                        volNum = r.VolumeNumber;
                        startChapter = r.StartChapter;
                        endChapter = r.EndChapter;
                        targetCount = r.TargetChapterCount;
                    }
                    else if (FormStartChapter > 0 && FormEndChapter > 0)
                    {
                        volNum = FormVolumeNumber;
                        startChapter = FormStartChapter;
                        endChapter = FormEndChapter;
                        targetCount = FormEndChapter - FormStartChapter + 1;
                    }

                    var volumeKey = volNum > 0 ? $"第{volNum}卷" : null;
                    var sb = new System.Text.StringBuilder();
                    var baseContext = await _contextService.GetVolumeDesignStructureContextAsync(volumeKey);
                    if (!string.IsNullOrWhiteSpace(baseContext))
                    {
                        sb.AppendLine(baseContext);
                        sb.AppendLine();
                    }

                    if (startChapter > 0 && endChapter > 0)
                    {
                        sb.AppendLine($"<section name=\"volume_chapter_range\" locked=\"true\">");
                        sb.AppendLine($"- 本卷为第{volNum}卷，共 {targetCount} 章");
                        sb.AppendLine($"- 章节范围：第 {startChapter} 章 ～ 第 {endChapter} 章");
                        sb.AppendLine($"- 「章节分配总览」「剧情分配」「章节生成提示」中所有章节编号必须严格在此范围内（{startChapter}～{endChapter}），不得使用此范围之外的任何章节编号。");
                        sb.AppendLine("</section>");
                        sb.AppendLine();
                    }

                    if (AvailableCharacters.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选角色", AvailableCharacters, "「出场角色」必须从以下列表中选择，不得编造"));
                    if (AvailableFactions.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选势力", AvailableFactions, "「涉及势力」必须从以下列表中选择，不得编造"));
                    if (AvailableLocations.Count > 0)
                        sb.Append(EntityReferencePromptHelper.BuildCandidateSection("可选地点", AvailableLocations, "「涉及地点」必须从以下列表中选择，不得编造"));

                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「卷序号」必须为整数（仅数字）。");
                    sb.AppendLine("2. 「章节分配总览」「剧情分配」「章节生成提示」如有多条，请在字符串内用换行分条。");
                    sb.AppendLine("3. 「起始章节」「结束章节」「目标章节数」由系统自动分配，无需在JSON中输出这三个字段。");
                    sb.AppendLine("4. 「出场角色」「涉及势力」「涉及地点」必须从上方候选列表中精确选取，以字符串形式输出（不要用JSON数组）。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                SequenceFieldName = "VolumeNumber",
                GetCurrentMaxSequence = (categoryName) => Service.GetAllVolumeDesigns()
                    .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Select(c => c.VolumeNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = EntityFieldMeta.GetFieldKeyMap("volumedesign"),
                BatchIndexFields = new() { "VolumeNumber", "VolumeTheme", "StageGoal" }
            };
        }

        private static int SafeParseInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (int.TryParse(text.Trim(), out var v)) return v;

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out v)) return v;
            return 0;
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override bool RequiresBatchSlotCompletion =>
            _batchPreCalculatedRanges != null && _batchPreCalculatedRanges.Count > 0;

        protected override async Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prompt)
                && _batchPreCalculatedRanges != null
                && _batchRangeIndex < _batchPreCalculatedRanges.Count)
            {
                var r = _batchPreCalculatedRanges[_batchRangeIndex];
                if (r.StartChapter > 0 && r.EndChapter > 0)
                {
                    var sb = new System.Text.StringBuilder(prompt);
                    sb.AppendLine();
                    sb.AppendLine($"<volume_chapter_range mandatory=\"true\">");
                    sb.AppendLine($"- 本卷为第{r.VolumeNumber}卷，共 {r.TargetChapterCount} 章");
                    sb.AppendLine($"- 章节范围：第 {r.StartChapter} 章 ～ 第 {r.EndChapter} 章");
                    sb.AppendLine($"- 「章节分配总览」「剧情分配」「章节生成提示」中所有章节编号必须严格在此范围内（{r.StartChapter}～{r.EndChapter}），不得使用此范围之外的任何章节编号。");
                    sb.AppendLine("</volume_chapter_range>");
                    return sb.ToString();
                }
            }
            return prompt;
        }

        protected override async Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            PlotRulesService plotRulesService;
            try { plotRulesService = TM.Framework.Common.Services.ServiceLocator.Get<PlotRulesService>(); }
            catch { if (!_isPipelineExecution) GlobalToast.Error("服务错误", "无法获取剧情规则服务"); return null; }

            try { await plotRulesService.InitializeAsync(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 初始化剧情规则服务失败: {ex.Message}"); }

            var validVolumes = plotRulesService.GetAllPlotRules()
                .Where(p => p.IsEnabled
                       && int.TryParse(p.TargetVolume?.Trim(), out var _v) && _v > 0)
                .Select(p => { int.TryParse(p.TargetVolume!.Trim(), out var n); return n; })
                .Distinct().ToList();

            if (validVolumes.Count == 0)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("缺少父约束", "请先在剧情规则中填写总卷数，再执行分卷批量生成");
                else TM.App.Log($"[VolumeDesignViewModel] Pipeline: 剧情规则中未找到总卷数");
                return null;
            }
            if (validVolumes.Count > 1)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("总卷数冲突", $"剧情规则中存在多个不同总卷数（{string.Join("、", validVolumes)}），请先统一后再生成");
                else TM.App.Log($"[VolumeDesignViewModel] Pipeline: 总卷数冲突: {string.Join(",", validVolumes)}");
                return null;
            }
            var totalVolumes = validVolumes[0];

            OutlineService outlineService;
            try { outlineService = TM.Framework.Common.Services.ServiceLocator.Get<OutlineService>(); }
            catch { if (!_isPipelineExecution) GlobalToast.Error("服务错误", "无法获取大纲服务"); return null; }

            try { await outlineService.InitializeAsync(); }
            catch (Exception ex) { TM.App.Log($"[VolumeDesignViewModel] 初始化大纲服务失败: {ex.Message}"); }

            var validChapters = outlineService.GetAllOutlines()
                .Where(o => o.IsEnabled
                       && o.TotalChapterCount > 0)
                .Select(o => o.TotalChapterCount)
                .Distinct().ToList();

            if (validChapters.Count == 0)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("缺少父约束", "请先在大纲设计中填写总章节数，再执行分卷批量生成");
                else TM.App.Log($"[VolumeDesignViewModel] Pipeline: 大纲设计中未找到总章节数");
                return null;
            }
            if (validChapters.Count > 1)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("总章节数冲突", $"大纲设计中存在多个不同总章节数（{string.Join("、", validChapters)}），请先统一后再生成");
                else TM.App.Log($"[VolumeDesignViewModel] Pipeline: 总章节数冲突: {string.Join(",", validChapters)}");
                return null;
            }
            var totalChapters = validChapters[0];

            if (totalChapters < totalVolumes)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("参数无效", $"总章节数({totalChapters})不能少于总卷数({totalVolumes})，请检查大纲设计");
                else TM.App.Log($"[VolumeDesignViewModel] Pipeline: 总章节数({totalChapters})<总卷数({totalVolumes})");
                return null;
            }

            List<VolumeChapterRange> allRanges;
            var outlineVolumeDivision = outlineService.GetAllOutlines()
                .Where(o => o.IsEnabled
                       && !string.IsNullOrWhiteSpace(o.VolumeDivision))
                .Select(o => o.VolumeDivision)
                .FirstOrDefault();

            if (ChapterAllocationHelper.TryParseVolumeDivision(outlineVolumeDivision, totalVolumes, totalChapters, out var parsedRanges))
            {
                allRanges = parsedRanges;
                TM.App.Log($"[VolumeDesignViewModel] 从大纲 VolumeDivision 解析成功: {allRanges.Count}卷");
                foreach (var r in allRanges)
                    TM.App.Log($"[VolumeDesignViewModel] 大纲分配: 第{r.VolumeNumber}卷 {r.StartChapter}-{r.EndChapter} ({r.TargetChapterCount}章)");
            }
            else
            {
                TM.App.Log($"[VolumeDesignViewModel] 大纲 VolumeDivision 解析失败或不完整，回退到算法分配");
                if (!_isPipelineExecution) GlobalToast.Warning("兜底分配", "大纲未配置或无法解析卷划分，当前使用五幕式算法自动分配章节范围");
                try
                {
                    allRanges = ChapterAllocationHelper.Allocate(totalVolumes, totalChapters);
                    foreach (var r in allRanges)
                        TM.App.Log($"[VolumeDesignViewModel] 算法分配: 第{r.VolumeNumber}卷 {r.StartChapter}-{r.EndChapter} ({r.TargetChapterCount}章)");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[VolumeDesignViewModel] 章节范围计算失败: {ex.Message}");
                    if (!_isPipelineExecution) GlobalToast.Error("分配失败", $"分配失败：{ex.Message}");
                    return null;
                }
            }

            var existingWithContent = Service.GetAllVolumeDesigns()
                .Where(v => !string.IsNullOrWhiteSpace(v.VolumeTheme)
                       && !string.IsNullOrWhiteSpace(v.MainConflict)
                       && string.Equals(v.Category, categoryName, StringComparison.Ordinal))
                .Select(v => v.VolumeNumber)
                .ToHashSet();

            _batchPreCalculatedRanges = allRanges
                .Where(r => !existingWithContent.Contains(r.VolumeNumber))
                .ToList();
            _batchAllRanges = allRanges;
            _batchExpectedTotalVolumes = totalVolumes;
            _batchRangeIndex = 0;

            var alreadyCompleted = allRanges.Count - _batchPreCalculatedRanges.Count;
            if (_batchPreCalculatedRanges.Count == 0)
            {
                _batchPreCalculatedRanges = null;
                _batchAllRanges = null;
                _batchExpectedTotalVolumes = 0;

                if (_isPipelineExecution)
                {
                    TM.App.Log($"[VolumeDesignViewModel] Pipeline: 本分类 {totalVolumes} 卷均已完成，跳过");
                    return new BatchGenerationConfig { CategoryName = categoryName, TotalCount = 0, BatchSize = 1 };
                }

                GlobalToast.Info("已全部完成", $"本分类 {totalVolumes} 卷均已有AI内容，无需重新生成");
                return null;
            }

            TM.App.Log($"[VolumeDesignViewModel] 批量配置: 总卷数={totalVolumes}, 总章节数={totalChapters}, 待生成={_batchPreCalculatedRanges.Count}");

            if (_isPipelineExecution)
            {
                return new BatchGenerationConfig { CategoryName = categoryName, TotalCount = _batchPreCalculatedRanges.Count, BatchSize = 1 };
            }

            string confirmMessage;
            if (alreadyCompleted > 0)
            {
                confirmMessage = $"即将对「{categoryName}」继续执行 AI 批量重建分卷设计：\n\n" +
                                 $"• 分卷数量：共 {totalVolumes} 卷\n" +
                                 $"• 已完成：{alreadyCompleted} 卷（跳过）\n" +
                                 $"• 待生成：{_batchPreCalculatedRanges.Count} 卷\n\n" +
                                 $"确认继续生成？";
            }
            else
            {
                confirmMessage = $"即将对「{categoryName}」执行 AI 批量重建分卷设计：\n\n" +
                                 $"• 分卷数量：共 {totalVolumes} 卷\n" +
                                 $"• 总章节数：{totalChapters} 章\n" +
                                 $"• 超出卷数的旧分卷数据将被自动清理\n\n" +
                                 $"确认开始生成？";
            }
            if (!StandardDialog.ShowConfirm(confirmMessage, "AI 批量生成确认"))
            {
                _batchPreCalculatedRanges = null;
                _batchAllRanges = null;
                _batchExpectedTotalVolumes = 0;
                return null;
            }

            var config = new BatchGenerationConfig { CategoryName = categoryName, TotalCount = _batchPreCalculatedRanges.Count, BatchSize = 1 };
            return config;
        }

        protected override async Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            if (_batchAllRanges == null || _batchAllRanges.Count == 0) return;

            var totalVolumes = _batchExpectedTotalVolumes > 0 ? _batchExpectedTotalVolumes : _batchAllRanges.Count;
            var tail = Service.GetAllVolumeDesigns()
                .Where(v => v.VolumeNumber > totalVolumes
                       && string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal))
                .ToList();

            foreach (var v in tail)
            {
                Service.DeleteVolumeDesign(v.Id);
                TM.App.Log($"[VolumeDesignViewModel] 清尾: 删除第{v.VolumeNumber}卷（超出本次总卷数{totalVolumes}）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[VolumeDesignViewModel] 清尾完成: 删除 {tail.Count} 个旧分卷");

            if (_lastBatchStoppedBySlotExhausted)
            {
                TM.App.Log("[VolumeDesignViewModel] 批量生成已因连续失败或重试耗尽而停止：跳过补缺占位，等待下次续跑");
                _batchPreCalculatedRanges = null;
                _batchAllRanges = null;
                _batchExpectedTotalVolumes = 0;
            }
            else if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllVolumeDesigns()
                    .Where(v => string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal)
                           && string.IsNullOrWhiteSpace(v.VolumeTheme)
                           && string.IsNullOrWhiteSpace(v.MainConflict))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteVolumeDesign(shell.Id);
                    TM.App.Log($"[VolumeDesignViewModel] 取消清理: 删除空壳第{shell.VolumeNumber}卷");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳分卷，下次批量生成会按需续接");

                _batchPreCalculatedRanges = null;
                _batchAllRanges = null;
                _batchExpectedTotalVolumes = 0;
            }
            else
            {
                foreach (var range in _batchAllRanges)
                {
                    var existing = Service.GetAllVolumeDesigns()
                        .FirstOrDefault(v => v.VolumeNumber == range.VolumeNumber
                                       && string.Equals(v.Category, config.CategoryName, StringComparison.Ordinal));

                    if (existing != null)
                    {
                        existing.StartChapter = range.StartChapter;
                        existing.EndChapter = range.EndChapter;
                        existing.TargetChapterCount = range.TargetChapterCount;
                        await Service.UpdateVolumeDesignAsync(existing);
                        continue;
                    }

                    var data = new VolumeDesignData
                    {
                        Id = ShortIdGenerator.New("D"),
                        Name = $"第{range.VolumeNumber}卷",
                        Category = config.CategoryName,
                        IsEnabled = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        VolumeNumber = range.VolumeNumber,
                        VolumeTitle = $"第{range.VolumeNumber}卷",
                        StartChapter = range.StartChapter,
                        EndChapter = range.EndChapter,
                        TargetChapterCount = range.TargetChapterCount,
                    };
                    await Service.AddVolumeDesignAsync(data);
                    TM.App.Log($"[VolumeDesignViewModel] 补齐缺卷: 第{range.VolumeNumber}卷 {range.StartChapter}-{range.EndChapter}");
                }

                _batchPreCalculatedRanges = null;
                _batchAllRanges = null;
                _batchExpectedTotalVolumes = 0;
            }

            RefreshTreeData();
        }

        protected override async Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();

            Service.BeginBatchSave();
            try
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        VolumeChapterRange? range = null;
                        if (_batchPreCalculatedRanges != null && _batchRangeIndex < _batchPreCalculatedRanges.Count)
                        {
                            range = _batchPreCalculatedRanges[_batchRangeIndex];
                            _batchRangeIndex++;
                        }

                        var volumeNumber = range?.VolumeNumber ?? _batchRangeIndex;
                        entity["VolumeNumber"] = volumeNumber;
                        var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);

                        var existing = Service.GetAllVolumeDesigns()
                            .FirstOrDefault(v => v.VolumeNumber == volumeNumber
                                           && string.Equals(v.Category, categoryName, StringComparison.Ordinal));

                        if (existing != null)
                        {
                            var aiName = reader.GetString("Name");
                            if (!string.IsNullOrWhiteSpace(aiName)) existing.Name = aiName;

                            var volumeTitle = reader.GetString("VolumeTitle");
                            if (!string.IsNullOrWhiteSpace(volumeTitle)) existing.VolumeTitle = VolumeTitlePrefixRegex.Replace(volumeTitle.Trim(), string.Empty);
                            var volumeTheme = reader.GetString("VolumeTheme");
                            if (!string.IsNullOrWhiteSpace(volumeTheme)) existing.VolumeTheme = volumeTheme;
                            var stageGoal = reader.GetString("StageGoal");
                            if (!string.IsNullOrWhiteSpace(stageGoal)) existing.StageGoal = stageGoal;
                            var mainConflict = reader.GetString("MainConflict");
                            if (!string.IsNullOrWhiteSpace(mainConflict)) existing.MainConflict = mainConflict;
                            var pressureSource = reader.GetString("PressureSource");
                            if (!string.IsNullOrWhiteSpace(pressureSource)) existing.PressureSource = pressureSource;
                            var keyEvents = reader.GetString("KeyEvents");
                            if (!string.IsNullOrWhiteSpace(keyEvents)) existing.KeyEvents = keyEvents;
                            var openingState = reader.GetString("OpeningState");
                            if (!string.IsNullOrWhiteSpace(openingState)) existing.OpeningState = openingState;
                            var endingState = reader.GetString("EndingState");
                            if (!string.IsNullOrWhiteSpace(endingState)) existing.EndingState = endingState;
                            var chapterAllocationOverview = reader.GetString("ChapterAllocationOverview");
                            if (!string.IsNullOrWhiteSpace(chapterAllocationOverview)) existing.ChapterAllocationOverview = chapterAllocationOverview;
                            var plotAllocation = reader.GetString("PlotAllocation");
                            if (!string.IsNullOrWhiteSpace(plotAllocation)) existing.PlotAllocation = plotAllocation;
                            var chapterGenerationHints = reader.GetString("ChapterGenerationHints");
                            if (!string.IsNullOrWhiteSpace(chapterGenerationHints)) existing.ChapterGenerationHints = chapterGenerationHints;
                            var refCharsRaw = string.Join("、", reader.GetStringList("ReferencedCharacterNames"));
                            var refCharsResolved = await VolumeResolveNamesAsync(refCharsRaw, "character");
                            var refCharacters = SplitEntityNames(refCharsResolved);
                            if (refCharacters.Count > 0) existing.ReferencedCharacterNames = refCharacters;
                            var refFacsRaw = string.Join("、", reader.GetStringList("ReferencedFactionNames"));
                            var refFacsResolved = await VolumeResolveNamesAsync(refFacsRaw, "faction");
                            var refFactions = SplitEntityNames(refFacsResolved);
                            if (refFactions.Count > 0) existing.ReferencedFactionNames = refFactions;
                            var refLocsRaw = string.Join("、", reader.GetStringList("ReferencedLocationNames"));
                            var refLocsResolved = await VolumeResolveNamesAsync(refLocsRaw, "location");
                            var refLocations = SplitEntityNames(refLocsResolved);
                            if (refLocations.Count > 0) existing.ReferencedLocationNames = refLocations;

                            if (range != null)
                            {
                                existing.StartChapter = range.StartChapter;
                                existing.EndChapter = range.EndChapter;
                                existing.TargetChapterCount = range.TargetChapterCount;
                            }
                            existing.DependencyModuleVersions = versionSnapshot ?? new();
                            await Service.UpdateVolumeDesignAsync(existing);
                            TM.App.Log($"[VolumeDesignViewModel] Upsert更新: 第{volumeNumber}卷 {existing.StartChapter}-{existing.EndChapter}");
                        }
                        else
                        {
                            var name = reader.GetString("Name");
                            if (string.IsNullOrWhiteSpace(name)) name = $"第{volumeNumber}卷";
                            var data = new VolumeDesignData
                            {
                                Id = ShortIdGenerator.New("D"),
                                Name = name,
                                Category = categoryName,
                                IsEnabled = true,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                                VolumeNumber = volumeNumber,
                                VolumeTitle = VolumeTitlePrefixRegex.Replace((reader.GetString("VolumeTitle") ?? string.Empty).Trim(), string.Empty),
                                VolumeTheme = reader.GetString("VolumeTheme"),
                                StageGoal = reader.GetString("StageGoal"),
                                StartChapter = range?.StartChapter ?? 0,
                                EndChapter = range?.EndChapter ?? 0,
                                TargetChapterCount = range?.TargetChapterCount ?? 0,
                                MainConflict = reader.GetString("MainConflict"),
                                PressureSource = reader.GetString("PressureSource"),
                                KeyEvents = reader.GetString("KeyEvents"),
                                OpeningState = reader.GetString("OpeningState"),
                                EndingState = reader.GetString("EndingState"),
                                ChapterAllocationOverview = reader.GetString("ChapterAllocationOverview"),
                                PlotAllocation = reader.GetString("PlotAllocation"),
                                ChapterGenerationHints = reader.GetString("ChapterGenerationHints"),
                                ReferencedCharacterNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedCharacterNames")), "character")),
                                ReferencedFactionNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedFactionNames")), "faction")),
                                ReferencedLocationNames = SplitEntityNames(await VolumeResolveNamesAsync(string.Join("、", reader.GetStringList("ReferencedLocationNames")), "location")),
                                DependencyModuleVersions = versionSnapshot ?? new()
                            };
                            await Service.AddVolumeDesignAsync(data);
                            TM.App.Log($"[VolumeDesignViewModel] Upsert新建: 第{volumeNumber}卷 {data.StartChapter}-{data.EndChapter}");
                        }

                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[VolumeDesignViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[VolumeDesignViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }
    }
}
