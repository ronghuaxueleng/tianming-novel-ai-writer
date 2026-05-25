using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Models;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Metadata;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Modules.Generate.Elements.Blueprint
{
    public partial class BlueprintViewModel
    {
        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "小说创作者",
                ActiveModuleHint = "章节蓝图",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "蓝图设计",
                ProgressMessage = "正在设计章节蓝图...",
                CompleteMessage = "蓝图设计完成",
                InputVariables = new()
                {
                    ["场景标题"] = () => FormSceneTitle,
                    ["大纲名称"] = () => string.Empty,
                    ["章节标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["场景标题"] = v => FormSceneTitle = v,
                    ["一句话结构"] = v => { if (string.IsNullOrWhiteSpace(FormOneLineStructure)) FormOneLineStructure = v; },
                    ["节奏曲线"] = v => { if (string.IsNullOrWhiteSpace(FormPacingCurve)) FormPacingCurve = v; },
                    ["视点角色"] = v => FormPovCharacter = FilterToCandidateOrRaw(v, AvailableCharacters),
                    ["开场"] = v => { if (string.IsNullOrWhiteSpace(FormOpening)) FormOpening = v; },
                    ["发展"] = v => { if (string.IsNullOrWhiteSpace(FormDevelopment)) FormDevelopment = v; },
                    ["转折"] = v => { if (string.IsNullOrWhiteSpace(FormTurning)) FormTurning = v; },
                    ["结尾"] = v => { if (string.IsNullOrWhiteSpace(FormEnding)) FormEnding = v; },
                    ["信息释放"] = v => { if (string.IsNullOrWhiteSpace(FormInfoDrop)) FormInfoDrop = v; },
                    ["物品线索"] = v => { if (string.IsNullOrWhiteSpace(FormItemsClues)) FormItemsClues = v; },
                    ["出场角色"] = v => FormCast = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及地点"] = v => FormLocations = FilterToCandidatesOrRaw(v, AvailableLocations),
                    ["涉及势力"] = v => FormFactions = FilterToCandidatesOrRaw(v, AvailableFactions),
                },
                OutputFieldGetters = new()
                {
                    ["场景标题"] = () => FormSceneTitle,
                    ["一句话结构"] = () => FormOneLineStructure,
                    ["节奏曲线"] = () => FormPacingCurve,
                    ["视点角色"] = () => FormPovCharacter,
                    ["开场"] = () => FormOpening,
                    ["发展"] = () => FormDevelopment,
                    ["转折"] = () => FormTurning,
                    ["结尾"] = () => FormEnding,
                    ["信息释放"] = () => FormInfoDrop,
                    ["物品线索"] = () => FormItemsClues,
                    ["出场角色"] = () => FormCast,
                    ["涉及地点"] = () => FormLocations,
                    ["涉及势力"] = () => FormFactions,
                },
                ContextProvider = async () => await GetEnhancedBlueprintContextAsync(),
                SequenceFieldName = "SceneNumber",
                GetCurrentMaxSequence = (categoryName) => Service.GetAllBlueprints()
                    .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Select(c => c.SceneNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = EntityFieldMeta.GetFieldKeyMap("blueprint"),
                BatchIndexFields = new() { "SceneNumber", "SceneTitle", "OneLineStructure" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override void OnBatchGenerationFailed(int failedCount)
        {
            if (_currentBatchChapterIds?.Count > 0)
            {
                _batchChapterIdIndex = Math.Max(0, _batchChapterIdIndex - _currentBatchChapterIds.Count);
                TM.App.Log($"[BlueprintViewModel] 批次失败，回退章节ID索引至 {_batchChapterIdIndex}");
            }
        }

        protected override bool RequiresBatchSlotCompletion =>
            _batchPreCalculatedChapterIds != null && _batchPreCalculatedChapterIds.Count > 0;

        protected override void OnBatchRetrySlotTrimmed(int filledSoFar)
        {
            if (_currentBatchChapterIds != null && filledSoFar < _currentBatchChapterIds.Count)
            {
                _currentBatchChapterIds = _currentBatchChapterIds.Skip(filledSoFar).ToList();
                TM.App.Log($"[BlueprintViewModel] 槽位缩减：已完成 {filledSoFar} 个，剩余 {_currentBatchChapterIds.Count} 个 ChapterId 待生成");
            }
        }

        private List<string>? _batchFullChapterIds;
        private List<string>? _batchPreCalculatedChapterIds;
        private int _batchChapterIdIndex;
        private List<string>? _currentBatchChapterIds;
        private List<string>? _currentBatchChapterIdsAll;
        private const string SystemChapterIdKey = "_SystemChapterId";

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> GenerateBatchAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var result = await base.GenerateBatchAsync(categoryName, count, cancellationToken);

            if (_currentBatchChapterIdsAll != null && result != null)
            {
                for (int i = 0; i < result.Count && i < _currentBatchChapterIdsAll.Count; i++)
                {
                    result[i][SystemChapterIdKey] = _currentBatchChapterIdsAll[i];
                }
            }

            return result ?? new List<Dictionary<string, object>>();
        }
        private int ResolveSceneNumberForChapterId(string chapterId)
        {
            if (!string.IsNullOrWhiteSpace(chapterId) && _batchFullChapterIds != null && _batchFullChapterIds.Count > 0)
            {
                var idx = _batchFullChapterIds.FindIndex(id => string.Equals(id, chapterId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx + 1;
            }
            var parsed = TryParseChapterNumberFromChapterId(chapterId);
            return parsed > 0 ? parsed : 0;
        }

        protected override GenerationRange? GetNextGenerationRange(string categoryName, int requestedCount)
        {
            if (_batchPreCalculatedChapterIds != null && _batchPreCalculatedChapterIds.Count > 0)
            {
                var take = Math.Min(requestedCount, _batchPreCalculatedChapterIds.Count - _batchChapterIdIndex);
                if (take > 0)
                {
                    _currentBatchChapterIds = _batchPreCalculatedChapterIds
                        .Skip(_batchChapterIdIndex)
                        .Take(take)
                        .ToList();
                    _currentBatchChapterIdsAll = _currentBatchChapterIds.ToList();
                    _batchChapterIdIndex += take;
                }
                else
                {
                    _currentBatchChapterIds = null;
                    _currentBatchChapterIdsAll = null;
                }
                return null;
            }
            _currentBatchChapterIds = null;
            _currentBatchChapterIdsAll = null;
            return base.GetNextGenerationRange(categoryName, requestedCount);
        }

        protected override async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (string.IsNullOrWhiteSpace(prompt)) return prompt;

            var sb = new System.Text.StringBuilder(prompt);

            var allChapterIds = AvailableChapterIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (allChapterIds.Count > 0)
            {
                var usedChapterIds = GetUsedChapterIdsInCategory(categoryName);
                var unusedChapterIds = allChapterIds.Where(id => !usedChapterIds.Contains(id)).ToList();
                sb.AppendLine();
                sb.AppendLine("<blueprint_allocation_state>");
                sb.AppendLine($"- 该卷可用章节ID：{string.Join("、", allChapterIds)}");
                if (usedChapterIds.Count > 0)
                    sb.AppendLine($"- 已有蓝图的章节：{string.Join("、", usedChapterIds)}");
                if (unusedChapterIds.Count > 0)
                    sb.AppendLine($"- 待生成蓝图的章节：{string.Join("、", unusedChapterIds)}");
                sb.AppendLine("- 说明：批量重建时「关联章节ID」由系统按章节列表依次分配，AI不应生成此字段");
                sb.AppendLine("</blueprint_allocation_state>");
            }

            if (_currentBatchChapterIds?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<blueprint_assignments mandatory=\"true\">");
                sb.AppendLine($"本批生成任务（输出数组长度必须 = {_currentBatchChapterIds.Count}，第i项对应第 i 个 ChapterId）：");
                sb.AppendLine(string.Join("、", _currentBatchChapterIds));
                sb.AppendLine("要求：本批每个对象的 Name/SceneTitle 必须互不重复，且需体现对应章节的场景特征（不要使用 vol/ch 编号前缀）。");
                sb.AppendLine("</blueprint_assignments>");
            }

            return sb.ToString();
        }

        protected override async System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            var outlineService = ServiceLocator.Get<OutlineService>();
            try
            {
                await System.Threading.Tasks.Task.WhenAll(
                    _volumeDesignService.InitializeAsync(),
                    _chapterService.InitializeAsync(),
                    outlineService.InitializeAsync());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 初始化服务失败: {ex.Message}");
            }

            int volNum = 1;
            VolumeDesignData? volume = null;
            try
            {
                volume = _volumeDesignService.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.IsEnabled
                        && (string.Equals((v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name), categoryName, StringComparison.Ordinal)
                            || string.Equals(v.Name, categoryName, StringComparison.Ordinal)));
                if (volume != null && volume.VolumeNumber > 0)
                {
                    volNum = volume.VolumeNumber;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 解析卷号失败: {ex.Message}");
            }

            if (volume == null || volume.VolumeNumber <= 0)
            {
                if (!_isPipelineExecution) StandardDialog.ShowWarning("未找到对应分卷设计，请确认分卷已启用并正确配置", "前置条件不满足");
                return null;
            }

            var (allocRanges, allocVolumeMap, allocError) = await ResolveVolumeAllocationAsync(outlineService);
            if (allocError != null)
            {
                if (!_isPipelineExecution) StandardDialog.ShowWarning(allocError, "章节范围校验失败");
                else TM.App.Log($"[BlueprintViewModel] Pipeline: 章节范围校验失败: {allocError}");
                return null;
            }

            var currentRange = allocRanges!.FirstOrDefault(r => r.VolumeNumber == volNum);
            if (currentRange == null)
            {
                if (!_isPipelineExecution) StandardDialog.ShowWarning($"大纲分配中未找到第{volNum}卷的章节范围，请检查大纲总章节数与分卷数配置", "前置条件不满足");
                else TM.App.Log($"[BlueprintViewModel] Pipeline: 大纲分配中未找到第{volNum}卷的章节范围");
                return null;
            }

            if (volNum > 1 && !_isPipelineExecution)
            {
                var prevIncomplete = GetIncompleteBlueprintVolumes(
                    allocRanges!.Where(r => r.VolumeNumber < volNum), allocVolumeMap!);
                if (prevIncomplete.Count > 0)
                {
                    var volList = string.Join("、", prevIncomplete.Select(n => $"第{n}卷"));
                    StandardDialog.ShowWarning(
                        $"以下分卷的蓝图设计尚未全部生成完毕：\n{volList}\n\n请先补全前置分卷，再生成第{volNum}卷的蓝图，以保证内容连贯性。",
                        "前置分卷未完成");
                    return null;
                }
            }

            _batchFullChapterIds = Enumerable.Range(currentRange.StartChapter, currentRange.TargetChapterCount)
                .Select(n => $"vol{volNum}_ch{n}")
                .ToList();

            var volCategoryName = volume.VolumeNumber > 0 ? $"第{volume.VolumeNumber}卷 {volume.VolumeTitle ?? string.Empty}".Trim() : volume.Name;
            var allChapters = _chapterService.GetAllChapters()
                .Where(c => c.IsEnabled)
                .ToList();
            var existingChapterNums = allChapters
                .Where(c => (string.Equals(c.CategoryId, volume.Id, StringComparison.Ordinal)
                             || string.Equals(c.Category, volCategoryName, StringComparison.Ordinal)
                             || string.Equals(c.Volume, volCategoryName, StringComparison.Ordinal)
                             || string.Equals(c.Category, volume.Name, StringComparison.Ordinal)
                             || string.Equals(c.Volume, volume.Name, StringComparison.Ordinal)
                             || string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                             || string.Equals(c.Volume, categoryName, StringComparison.Ordinal)))
                .Select(c => c.ChapterNumber)
                .ToHashSet();

            var expectedChapterNums = Enumerable.Range(currentRange.StartChapter, currentRange.TargetChapterCount).ToList();
            var missingChapterNums = expectedChapterNums.Where(n => !existingChapterNums.Contains(n)).ToList();
            if (missingChapterNums.Count > 0)
            {
                if (!_isPipelineExecution) StandardDialog.ShowWarning($"本卷章节设计未覆盖完整范围（第 {currentRange.StartChapter} 章 ~ 第 {currentRange.EndChapter} 章）。\n缺失章节：{string.Join("、", missingChapterNums.Take(30).Select(n => $"第{n}章"))}{(missingChapterNums.Count > 30 ? "……" : string.Empty)}\n\n请先在章节设计中补全缺失章节后再生成蓝图。", "前置条件不满足");
                else TM.App.Log($"[BlueprintViewModel] Pipeline: 本卷章节设计未覆盖完整范围, 缺失{missingChapterNums.Count}章");
                return null;
            }

            var expectedIdSet = new HashSet<string>(_batchFullChapterIds, StringComparer.OrdinalIgnoreCase);
            var blueprintCandidates = Service.GetAllBlueprints()
                .Where(b => b.IsEnabled
                            && (string.Equals(b.CategoryId, volume.Id, StringComparison.Ordinal)
                                || string.Equals(b.Category, volCategoryName, StringComparison.Ordinal)
                                || string.Equals(b.Category, volume.Name, StringComparison.Ordinal)));

            var existingWithContent = blueprintCandidates
                .Where(b => !string.IsNullOrWhiteSpace(b.OneLineStructure)
                            && !string.IsNullOrWhiteSpace(b.ChapterId)
                            && expectedIdSet.Contains(b.ChapterId))
                .Select(b => b.ChapterId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var alreadyCompleted = _batchFullChapterIds.Count(id => existingWithContent.Contains(id));
            _batchPreCalculatedChapterIds = _batchFullChapterIds
                .Where(id => !existingWithContent.Contains(id))
                .ToList();
            _batchChapterIdIndex = 0;

            if (_isPipelineResume && _batchPreCalculatedChapterIds.Count > 0 && alreadyCompleted > 0)
            {
                var toDelete = Service.GetAllBlueprints()
                    .Where(b => (!string.IsNullOrWhiteSpace(volume.Id) && string.Equals(b.CategoryId, volume.Id, StringComparison.Ordinal))
                                || string.Equals(b.Category, volCategoryName, StringComparison.Ordinal)
                                || string.Equals(b.Category, volume.Name, StringComparison.Ordinal))
                    .ToList();
                foreach (var b in toDelete)
                    Service.DeleteBlueprint(b.Id);

                TM.App.Log($"[BlueprintViewModel] 续传清卷: 第{volNum}卷清除 {toDelete.Count} 条已有蓝图（已完成{alreadyCompleted}），将完整重建");
                _batchPreCalculatedChapterIds = _batchFullChapterIds.ToList();
                _batchChapterIdIndex = 0;
                alreadyCompleted = 0;
            }

            if (_batchPreCalculatedChapterIds.Count == 0)
            {
                _batchFullChapterIds = null;
                _batchPreCalculatedChapterIds = null;

                if (_isPipelineExecution)
                {
                    TM.App.Log($"[BlueprintViewModel] Pipeline: 第{volNum}卷蓝图已全部完成({alreadyCompleted}个)，跳过");
                    return new BatchGenerationConfig { CategoryName = categoryName, TotalCount = 0, BatchSize = 1 };
                }

                StandardDialog.ShowInfo($"本卷 {currentRange.TargetChapterCount} 个蓝图均已有AI内容，无需重新生成", "已全部完成");
                return null;
            }

            if (_isPipelineExecution)
            {
                TM.App.Log($"[BlueprintViewModel] Pipeline: 第{volNum}卷 待生成{_batchPreCalculatedChapterIds.Count}个蓝图 (已完成{alreadyCompleted}个跳过)");
                return new BatchGenerationConfig
                {
                    CategoryName = categoryName,
                    TotalCount = _batchPreCalculatedChapterIds.Count,
                    BatchSize = GetDefaultBatchSize()
                };
            }

            var chapterRangeText = $"vol{volNum}_ch{currentRange.StartChapter} ~ vol{volNum}_ch{currentRange.EndChapter}";

            string msg;
            if (alreadyCompleted > 0)
            {
                msg = $"即将对「{categoryName}」继续执行 AI 批量重建章节蓝图：\n\n"
                    + $"• 章节范围：{chapterRangeText}\n"
                    + $"• 已完成：{alreadyCompleted} 个（跳过）\n"
                    + $"• 待生成：{_batchPreCalculatedChapterIds.Count} 个\n"
                    + "• 仅展示本卷起止章节，不逐章展开\n\n"
                    + "确认继续生成？";
            }
            else
            {
                msg = $"即将对「{categoryName}」执行 AI 批量重建章节蓝图：\n\n"
                    + $"• 蓝图数量：共 {currentRange.TargetChapterCount} 个（每章一蓝图）\n"
                    + $"• 章节范围：{chapterRangeText}\n"
                    + $"• 超出范围的旧蓝图数据将被自动清理\n\n"
                    + "确认开始生成？";
            }

            var confirmed = StandardDialog.ShowConfirm(msg, "批量重建章节蓝图");
            if (!confirmed)
            {
                _batchFullChapterIds = null;
                _batchPreCalculatedChapterIds = null;
                return null;
            }

            return new BatchGenerationConfig
            {
                CategoryName = categoryName,
                TotalCount = _batchPreCalculatedChapterIds.Count,
                BatchSize = GetDefaultBatchSize()
            };
        }

        private async System.Threading.Tasks.Task<(List<VolumeChapterRange>? Ranges, Dictionary<int, VolumeDesignData>? VolumeMap, string? ErrorMessage)> ResolveVolumeAllocationAsync(OutlineService? preInitializedOutlineService = null)
        {
            try
            {
                var enabledVolumes = _volumeDesignService.GetAllVolumeDesigns()
                    .Where(v => v.IsEnabled && v.VolumeNumber > 0)
                    .OrderBy(v => v.VolumeNumber)
                    .ToList();

                if (enabledVolumes.Count == 0)
                    return (null, null, "未找到已启用的分卷设计");

                var volumeNumbers = enabledVolumes.Select(v => v.VolumeNumber).Distinct().OrderBy(n => n).ToList();
                var duplicates = enabledVolumes.GroupBy(v => v.VolumeNumber).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(n => n).ToList();
                if (duplicates.Count > 0)
                    return (null, null, $"分卷编号重复（{string.Join("、", duplicates.Select(n => $"第{n}卷"))}），无法按大纲分配章节范围");

                var expectedNums = Enumerable.Range(1, volumeNumbers.Count).ToList();
                if (!volumeNumbers.SequenceEqual(expectedNums))
                    return (null, null, $"分卷编号不连续（{string.Join("、", volumeNumbers.Select(n => $"第{n}卷"))}），无法按大纲分配章节范围");

                var outlineService = preInitializedOutlineService ?? ServiceLocator.Get<OutlineService>();
                if (preInitializedOutlineService == null)
                    await outlineService.InitializeAsync();
                var outlinesForScope = outlineService.GetAllOutlines()
                    .Where(o => o.IsEnabled)
                    .ToList();

                var totalChaptersList = outlinesForScope
                    .Where(o => o.TotalChapterCount > 0)
                    .Select(o => o.TotalChapterCount)
                    .Distinct()
                    .ToList();

                if (totalChaptersList.Count == 0)
                    return (null, null, "大纲未配置总章节数，请先在大纲设计中填写总章节数");
                if (totalChaptersList.Count > 1)
                    return (null, null, $"大纲总章节数冲突（{string.Join("、", totalChaptersList)}），无法分配");

                var totalChapters = totalChaptersList[0];
                if (totalChapters < volumeNumbers.Count)
                    return (null, null, $"大纲总章节数({totalChapters})小于总卷数({volumeNumbers.Count})");

                var volumeMap = enabledVolumes.ToDictionary(v => v.VolumeNumber, v => v);
                var volumeDivision = outlinesForScope
                    .Where(o => !string.IsNullOrWhiteSpace(o.VolumeDivision))
                    .Select(o => o.VolumeDivision)
                    .FirstOrDefault();

                if (!ChapterAllocationHelper.TryParseVolumeDivision(volumeDivision, volumeNumbers.Count, totalChapters, out var parsedRanges))
                {
                    TM.App.Log($"[BlueprintViewModel] 大纲 VolumeDivision 解析失败，回退到算法分配");
                    parsedRanges = ChapterAllocationHelper.Allocate(volumeNumbers.Count, totalChapters);
                }

                return (parsedRanges, volumeMap, null);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 大纲分配解析异常: {ex.Message}");
                return (null, null, $"章节范围解析异常：{ex.Message}");
            }
        }

        private List<int> GetIncompleteBlueprintVolumes(
            IEnumerable<VolumeChapterRange> rangesToCheck,
            Dictionary<int, VolumeDesignData> volumeMap)
        {
            var allBlueprints = Service.GetAllBlueprints()
                .Where(b => b.IsEnabled)
                .ToList();

            var incompleteVolumes = new List<int>();
            foreach (var r in rangesToCheck)
            {
                if (!volumeMap.TryGetValue(r.VolumeNumber, out var vol))
                {
                    incompleteVolumes.Add(r.VolumeNumber);
                    continue;
                }

                var catName = vol.VolumeNumber > 0 ? $"第{vol.VolumeNumber}卷 {vol.VolumeTitle ?? string.Empty}".Trim() : vol.Name;
                var expectedNums = Enumerable.Range(r.StartChapter, r.TargetChapterCount).ToList();
                var expectedIds = expectedNums.Select(n => $"vol{r.VolumeNumber}_ch{n}").ToHashSet(StringComparer.OrdinalIgnoreCase);
                var completedCount = allBlueprints
                    .Where(b => (string.Equals(b.CategoryId, vol.Id, StringComparison.Ordinal)
                                 || string.Equals(b.Category, catName, StringComparison.Ordinal)
                                 || string.Equals(b.Category, vol.Name, StringComparison.Ordinal))
                                && !string.IsNullOrWhiteSpace(b.OneLineStructure)
                                && !string.IsNullOrWhiteSpace(b.ChapterId)
                                && expectedIds.Contains(b.ChapterId))
                    .Select(b =>
                    {
                        var idx = b.ChapterId.LastIndexOf("_ch", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0) return 0;
                        var s = b.ChapterId[(idx + 3)..];
                        return int.TryParse(s, out var n) ? n : 0;
                    })
                    .Where(n => n > 0)
                    .Distinct()
                    .Count();
                if (completedCount < expectedNums.Count)
                    incompleteVolumes.Add(vol.VolumeNumber);
            }
            return incompleteVolumes;
        }

        public override async System.Threading.Tasks.Task<List<string>> GetIncompletePrerequisiteCategoriesAsync(string categoryName)
        {
            try
            {
                var volume = _volumeDesignService.GetAllVolumeDesigns()
                    .FirstOrDefault(v => v.IsEnabled
                        && (string.Equals((v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name), categoryName, StringComparison.Ordinal)
                            || string.Equals(v.Name, categoryName, StringComparison.Ordinal)));

                if (volume == null || volume.VolumeNumber <= 1)
                    return new List<string>();

                var (allocRanges, allocVolumeMap, allocError) = await ResolveVolumeAllocationAsync();
                if (allocError != null || allocRanges == null || allocVolumeMap == null)
                    return new List<string>();

                var prevIncomplete = GetIncompleteBlueprintVolumes(
                    allocRanges.Where(r => r.VolumeNumber < volume.VolumeNumber), allocVolumeMap);

                if (prevIncomplete.Count == 0)
                    return new List<string>();

                var result = new List<string>();
                foreach (var volNum in prevIncomplete)
                {
                    if (allocVolumeMap.TryGetValue(volNum, out var vol))
                    {
                        var catName = vol.VolumeNumber > 0
                            ? $"第{vol.VolumeNumber}卷 {vol.VolumeTitle ?? string.Empty}".Trim()
                            : vol.Name;
                        result.Add(catName);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] GetIncompletePrerequisiteCategoriesAsync 异常: {ex.Message}");
                return new List<string>();
            }
        }

        protected override int GetBaseBatchSize() => 10;
        protected override int GetBatchSize64K() => 12;
        protected override int GetBatchSize128K() => 15;

    }
}

