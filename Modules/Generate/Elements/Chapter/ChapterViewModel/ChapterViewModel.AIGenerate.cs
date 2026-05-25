using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
using TM.Framework.Common.ViewModels;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Metadata;

namespace TM.Modules.Generate.Elements.Chapter
{
    public partial class ChapterViewModel
    {
        private const string SystemChapterNumberKey = "_SystemChapterNumber";
        private List<int>? _batchFullChapterRange;
        private List<int>? _batchPreCalculatedChapterNumbers;
        private int _batchChapterIndex;
        private List<int>? _currentBatchChapterNumbers;
        private List<int>? _currentBatchChapterNumbersAll;

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return new AIGenerationConfig
            {
                Category = "小说创作者",
                ActiveModuleHint = "章节规划",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = ResponseFormat.Json,
                MessagePrefix = "章节规划",
                ProgressMessage = "正在规划章节...",
                CompleteMessage = "章节规划完成",
                InputVariables = new()
                {
                    ["章节标题"] = () => FormChapterTitle,
                    ["大纲名称"] = () => string.Empty,
                    ["场景标题"] = () => string.Empty,
                },
                OutputFields = new()
                {
                    ["章节标题"] = v => FormChapterTitle = v,
                    ["章节目标"] = v => { if (string.IsNullOrWhiteSpace(FormMainGoal)) FormMainGoal = v; },
                    ["章节主题"] = v => { if (string.IsNullOrWhiteSpace(FormChapterTheme)) FormChapterTheme = v; },
                    ["读者体验目标"] = v => { if (string.IsNullOrWhiteSpace(FormReaderExperienceGoal)) FormReaderExperienceGoal = v; },
                    ["阻力来源"] = v => { if (string.IsNullOrWhiteSpace(FormResistanceSource)) FormResistanceSource = v; },
                    ["关键转折"] = v => { if (string.IsNullOrWhiteSpace(FormKeyTurn)) FormKeyTurn = v; },
                    ["钩子"] = v => { if (string.IsNullOrWhiteSpace(FormHook)) FormHook = v; },
                    ["世界信息释放"] = v => { if (string.IsNullOrWhiteSpace(FormWorldInfoDrop)) FormWorldInfoDrop = v; },
                    ["角色弧推进"] = v => { if (string.IsNullOrWhiteSpace(FormCharacterArcProgress)) FormCharacterArcProgress = v; },
                    ["主线推进"] = v => { if (string.IsNullOrWhiteSpace(FormMainPlotProgress)) FormMainPlotProgress = v; },
                    ["伏笔"] = v => { if (string.IsNullOrWhiteSpace(FormForeshadowing)) FormForeshadowing = v; },
                    ["出场角色"] = v => FormReferencedCharacterNames = FilterToCandidatesOrRaw(v, AvailableCharacters),
                    ["涉及势力"] = v => FormReferencedFactionNames = FilterToCandidatesOrRaw(v, AvailableFactions),
                    ["涉及地点"] = v => FormReferencedLocationNames = FilterToCandidatesOrRaw(v, AvailableLocations),
                },
                OutputFieldGetters = new()
                {
                    ["章节标题"] = () => FormChapterTitle,
                    ["章节目标"] = () => FormMainGoal,
                    ["章节主题"] = () => FormChapterTheme,
                    ["读者体验目标"] = () => FormReaderExperienceGoal,
                    ["阻力来源"] = () => FormResistanceSource,
                    ["关键转折"] = () => FormKeyTurn,
                    ["钩子"] = () => FormHook,
                    ["世界信息释放"] = () => FormWorldInfoDrop,
                    ["角色弧推进"] = () => FormCharacterArcProgress,
                    ["主线推进"] = () => FormMainPlotProgress,
                    ["伏笔"] = () => FormForeshadowing,
                    ["出场角色"] = () => FormReferencedCharacterNames,
                    ["涉及势力"] = () => FormReferencedFactionNames,
                    ["涉及地点"] = () => FormReferencedLocationNames,
                },
                ContextProvider = async () =>
                {
                    var sb = new System.Text.StringBuilder();

                    if (!string.IsNullOrWhiteSpace(FormCategory))
                        RefreshEntityPool(FormCategory);

                    var baseContext = await _contextService.GetChapterContextWithVolumeLocatorAsync(FormCategory);

                    if (!string.IsNullOrWhiteSpace(baseContext))
                    {
                        sb.AppendLine(baseContext);
                        sb.AppendLine();
                    }

                    var promptCharacters = AvailableCharacters.Count > 0
                        ? (IEnumerable<string>)AvailableCharacters
                        : _characterService.GetAllCharacterRules().Where(c => c.IsEnabled).Select(c => c.Name);
                    var promptFactions = AvailableFactions.Count > 0
                        ? (IEnumerable<string>)AvailableFactions
                        : _factionService.GetAllFactionRules().Where(f => f.IsEnabled).Select(f => f.Name);
                    var promptLocations = AvailableLocations.Count > 0
                        ? (IEnumerable<string>)AvailableLocations
                        : _locationService.GetAllLocationRules().Where(l => l.IsEnabled).Select(l => l.Name);

                    var promptCharList = promptCharacters.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var promptFacList = promptFactions.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var promptLocList = promptLocations.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (promptCharList.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选角色", promptCharList, "「出场角色」必须从以下列表中选择，不得编造"));
                    if (promptFacList.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选势力", promptFacList, "「涉及势力」必须从以下列表中选择，不得编造"));
                    if (promptLocList.Count > 0)
                        sb.Append(TM.Framework.Common.Helpers.EntityReferencePromptHelper.BuildCandidateSection(
                            "可选地点", promptLocList, "「涉及地点」必须从以下列表中选择，不得编造"));

                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「章节编号」由系统按批次顺序分配（见 chapter_assignments），AI不应生成此字段。");
                    sb.AppendLine("2. 「章节标题」不要带\"第X章\"前缀（系统会自动规范化标题）。");
                    sb.AppendLine("3. 「关键转折」「伏笔」「世界信息释放」如有多条，请在字符串内用换行分条。");
                    sb.AppendLine("4. 「出场角色」「涉及势力」「涉及地点」必须从上方可选列表中选择，逗号/顿号分隔。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                SequenceFieldName = "ChapterNumber",
                GetCurrentMaxSequence = (categoryName) => Service.GetAllChapters()
                    .Where(c => string.IsNullOrEmpty(categoryName) || string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                    .Select(c => c.ChapterNumber)
                    .DefaultIfEmpty(0)
                    .Max(),
                BatchFieldKeyMap = EntityFieldMeta.GetFieldKeyMap("chapter"),
                BatchIndexFields = new() { "ChapterNumber", "ChapterTitle", "KeyTurn", "Hook" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override bool IsNameDedupEnabled() => false;

        protected override void OnBatchGenerationFailed(int failedCount)
        {
            if (_currentBatchChapterNumbers?.Count > 0)
            {
                _batchChapterIndex = Math.Max(0, _batchChapterIndex - _currentBatchChapterNumbers.Count);
                TM.App.Log($"[ChapterViewModel] 批次失败，回退章节索引至 {_batchChapterIndex}");
            }
        }

        protected override bool RequiresBatchSlotCompletion =>
            _batchPreCalculatedChapterNumbers != null && _batchPreCalculatedChapterNumbers.Count > 0;

        protected override void OnBatchRetrySlotTrimmed(int filledSoFar)
        {
            if (_currentBatchChapterNumbers != null && filledSoFar < _currentBatchChapterNumbers.Count)
            {
                _currentBatchChapterNumbers = _currentBatchChapterNumbers.Skip(filledSoFar).ToList();
                TM.App.Log($"[ChapterViewModel] 槽位缩减：已完成 {filledSoFar} 个，剩余 {_currentBatchChapterNumbers.Count} 个章节号待生成");
            }
        }

        protected override async Task<List<Dictionary<string, object>>> GenerateBatchAsync(
            string categoryName, int count, CancellationToken cancellationToken)
        {
            var result = await base.GenerateBatchAsync(categoryName, count, cancellationToken);

            if (_currentBatchChapterNumbersAll != null && result != null)
            {
                for (int i = 0; i < result.Count && i < _currentBatchChapterNumbersAll.Count; i++)
                {
                    result[i][SystemChapterNumberKey] = _currentBatchChapterNumbersAll[i];
                }
            }

            return result ?? new List<Dictionary<string, object>>();
        }

        protected override GenerationRange? GetNextGenerationRange(string categoryName, int requestedCount)
        {
            if (_batchPreCalculatedChapterNumbers != null && _batchPreCalculatedChapterNumbers.Count > 0)
            {
                var take = Math.Min(requestedCount, _batchPreCalculatedChapterNumbers.Count - _batchChapterIndex);
                if (take > 0)
                {
                    _currentBatchChapterNumbers = _batchPreCalculatedChapterNumbers
                        .Skip(_batchChapterIndex)
                        .Take(take)
                        .ToList();
                    _currentBatchChapterNumbersAll = _currentBatchChapterNumbers.ToList();
                    _batchChapterIndex += take;
                }
                else
                {
                    _currentBatchChapterNumbers = null;
                    _currentBatchChapterNumbersAll = null;
                }
                return null;
            }
            _currentBatchChapterNumbers = null;
            _currentBatchChapterNumbersAll = null;
            return base.GetNextGenerationRange(categoryName, requestedCount);
        }

        protected override async Task<string> BuildBatchGenerationPromptAsync(
            string categoryName, int count, CancellationToken cancellationToken)
        {
            var prompt = await base.BuildBatchGenerationPromptAsync(categoryName, count, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prompt) && _currentBatchChapterNumbers?.Count > 0)
            {
                var sb = new System.Text.StringBuilder(prompt);
                sb.AppendLine();
                sb.AppendLine("<chapter_assignments mandatory=\"true\">");
                sb.AppendLine($"本批生成任务（输出数组长度必须 = {_currentBatchChapterNumbers.Count}，第i项对应第 i 个章节号）：");
                sb.AppendLine(string.Join("、", _currentBatchChapterNumbers.Select(n => $"第{n}章")));
                sb.AppendLine("要求：本批每个对象的 Name/ChapterTitle 必须各不相同，且标题需体现本章核心事件，禁止使用\"第X章\"前缀。");

                var allExisting = Service.GetAllChapters()
                    .Where(c => c.IsEnabled
                        && (string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                            || string.Equals(c.Volume, categoryName, StringComparison.Ordinal))
                        && !string.IsNullOrWhiteSpace(c.ChapterTheme)
                        && HasRealTitleContent(c.ChapterTitle))
                    .OrderBy(c => c.ChapterNumber)
                    .ToList();

                if (allExisting.Count > 0)
                {
                    var minBatch = _currentBatchChapterNumbers.Min();
                    var maxBatch = _currentBatchChapterNumbers.Max();

                    var preceding = allExisting.Where(c => c.ChapterNumber < minBatch).TakeLast(3).ToList();
                    var following = allExisting.Where(c => c.ChapterNumber > maxBatch).Take(3).ToList();

                    if (preceding.Count > 0 || following.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("<continuity_anchor note=\"本批为补全/续接生成，必须与前后已有章节保持叙事连贯\">");
                        if (preceding.Count > 0)
                        {
                            sb.AppendLine("【前接章节（本批之前）】");
                            foreach (var ch in preceding)
                                sb.AppendLine($"  第{ch.ChapterNumber}章「{NormalizeChapterTitle(ch.ChapterTitle)}」主题={ch.ChapterTheme}，钩子={ch.Hook ?? "无"}");
                        }
                        if (following.Count > 0)
                        {
                            sb.AppendLine("【后续章节（本批之后）】");
                            foreach (var ch in following)
                                sb.AppendLine($"  第{ch.ChapterNumber}章「{NormalizeChapterTitle(ch.ChapterTitle)}」主题={ch.ChapterTheme}");
                        }
                        sb.AppendLine("要求：本批章节必须承接前接章节的钩子/伏笔，并为后续章节做好铺垫，确保叙事无断裂。");
                        sb.AppendLine("</continuity_anchor>");
                    }
                }

                sb.AppendLine("</chapter_assignments>");
                return sb.ToString();
            }
            return prompt;
        }

        protected override async Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(
            string categoryName, bool singleMode = false)
        {
            var outlineService = ServiceLocator.Get<OutlineService>();
            try
            {
                await Task.WhenAll(
                    _volumeDesignService.InitializeAsync(),
                    outlineService.InitializeAsync());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 初始化服务失败: {ex.Message}");
            }

            var volume = _volumeDesignService.GetAllVolumeDesigns()
                .FirstOrDefault(v => v.IsEnabled
                    && (string.Equals((v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle ?? string.Empty}".Trim() : v.Name), categoryName, StringComparison.Ordinal)
                        || string.Equals(v.Name, categoryName, StringComparison.Ordinal)));

            var (allocRanges, allocVolumeMap, allocError) = await ResolveVolumeAllocationAsync(outlineService);

            if (allocError != null)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("缺少配置", allocError);
                else TM.App.Log($"[ChapterViewModel] Pipeline: {allocError}");
                return null;
            }

            if (allocRanges == null || allocVolumeMap == null)
                return null;

            VolumeChapterRange? currentRange = null;
            if (volume != null)
                currentRange = allocRanges.FirstOrDefault(r => r.VolumeNumber == volume.VolumeNumber);

            if (currentRange == null)
            {
                if (!_isPipelineExecution) GlobalToast.Warning("无匹配分配", $"未找到卷 '{categoryName}' 的章节范围");
                else TM.App.Log($"[ChapterViewModel] Pipeline: 未找到分卷范围 '{categoryName}'");
                return null;
            }

            if (volume != null && volume.VolumeNumber > 1 && !_isPipelineExecution)
            {
                var prevIncomplete = GetIncompleteChapterVolumes(
                    allocRanges.Where(r => r.VolumeNumber < volume.VolumeNumber), allocVolumeMap);
                if (prevIncomplete.Count > 0)
                {
                    var volList = string.Join("、", prevIncomplete.Select(n => $"第{n}卷"));
                    StandardDialog.ShowWarning(
                        $"以下分卷的章节设计尚未全部生成完毕：\n{volList}\n\n请先补全前置分卷，再生成第{volume.VolumeNumber}卷的章节，以保证内容连贯性。",
                        "前置分卷未完成");
                    return null;
                }
            }

            var allChapters = Service.GetAllChapters();
            var existingNums = allChapters
                .Where(c => (string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                            || string.Equals(c.Volume, categoryName, StringComparison.Ordinal))
                            && c.IsEnabled
                            && !string.IsNullOrWhiteSpace(c.ChapterTheme)
                            && HasRealTitleContent(c.ChapterTitle))
                .Select(c => c.ChapterNumber)
                .ToHashSet();

            var fullRange = Enumerable.Range(currentRange.StartChapter, currentRange.TargetChapterCount).ToList();
            var missingNums = fullRange.Where(n => !existingNums.Contains(n)).ToList();

            _batchFullChapterRange = fullRange;

            if (missingNums.Count == 0)
            {
                _batchPreCalculatedChapterNumbers = null;
                if (_isPipelineExecution)
                {
                    TM.App.Log($"[ChapterViewModel] Pipeline: 本分卷 {currentRange.TargetChapterCount} 章均已完成，跳过");
                    return new BatchGenerationConfig { CategoryName = categoryName, TotalCount = 0, BatchSize = GetDefaultBatchSize() };
                }
                GlobalToast.Info("已全部完成", $"本分卷 {currentRange.TargetChapterCount} 章均已有内容，无需重新生成");
                return null;
            }

            _batchPreCalculatedChapterNumbers = missingNums;
            _batchChapterIndex = 0;

            if (_isPipelineResume && missingNums.Count > 0 && existingNums.Count > 0)
            {
                var chaptersToDelete = allChapters
                    .Where(c => string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                                || string.Equals(c.Volume, categoryName, StringComparison.Ordinal))
                    .ToList();
                foreach (var ch in chaptersToDelete)
                    Service.DeleteChapter(ch.Id);

                TM.App.Log($"[ChapterViewModel] 续传清卷: '{categoryName}' 清除 {chaptersToDelete.Count} 条已有章节（已完成{existingNums.Count}），将完整重建");
                _batchPreCalculatedChapterNumbers = fullRange.ToList();
                _batchChapterIndex = 0;
            }

            if (_isPipelineExecution)
            {
                return new BatchGenerationConfig
                {
                    CategoryName = categoryName,
                    TotalCount = _batchPreCalculatedChapterNumbers.Count,
                    BatchSize = GetDefaultBatchSize()
                };
            }

            var alreadyDone = fullRange.Count - missingNums.Count;
            string msg;
            if (alreadyDone > 0)
            {
                msg = $"即将对「{categoryName}」继续执行 AI 批量重建章节设计：\n\n"
                    + $"• 章节数量：共 {currentRange.TargetChapterCount} 章\n"
                    + $"• 已完成：{alreadyDone} 章（跳过）\n"
                    + $"• 待生成：{missingNums.Count} 章\n\n"
                    + "确认继续生成？";
            }
            else
            {
                msg = $"即将对「{categoryName}」执行 AI 批量重建章节设计：\n\n"
                    + $"• 章节数量：共 {currentRange.TargetChapterCount} 章\n"
                    + $"• 章节范围：第 {currentRange.StartChapter} 章 ~ 第 {currentRange.EndChapter} 章\n"
                    + "• 仅展示本卷起止章节，不逐章展开\n"
                    + $"• 超出范围的旧章节数据将被自动清理\n\n"
                    + "确认开始生成？";
            }

            var confirmed = StandardDialog.ShowConfirm(msg, "批量重建章节设计");
            if (!confirmed)
            {
                _batchFullChapterRange = null;
                _batchPreCalculatedChapterNumbers = null;
                return null;
            }

            return new BatchGenerationConfig
            {
                CategoryName = categoryName,
                TotalCount = _batchPreCalculatedChapterNumbers.Count,
                BatchSize = GetDefaultBatchSize()
            };
        }

        protected override async Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            var fullRange = _batchFullChapterRange;
            if (fullRange == null || fullRange.Count == 0) return;

            var validSet = new HashSet<int>(fullRange);

            var tail = Service.GetAllChapters()
                .Where(c => string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal))
                .Where(c => !validSet.Contains(c.ChapterNumber))
                .ToList();

            foreach (var c in tail)
            {
                Service.DeleteChapter(c.Id);
                TM.App.Log($"[ChapterViewModel] 清尾: 删除第{c.ChapterNumber}章（不在有效范围内）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[ChapterViewModel] 清尾完成: 删除 {tail.Count} 个旧章节");

            if (_lastBatchStoppedBySlotExhausted)
            {
                TM.App.Log("[ChapterViewModel] 批量生成已因槽位重试耗尽而停止：跳过补缺占位，等待下次续跑");
            }
            else if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllChapters()
                    .Where(c => string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal)
                        && string.IsNullOrWhiteSpace(NormalizeChapterTitle(c.ChapterTitle))
                        && string.IsNullOrWhiteSpace(c.ChapterTheme)
                        && string.IsNullOrWhiteSpace(c.ReaderExperienceGoal)
                        && string.IsNullOrWhiteSpace(c.MainGoal)
                        && string.IsNullOrWhiteSpace(c.ResistanceSource)
                        && string.IsNullOrWhiteSpace(c.KeyTurn)
                        && string.IsNullOrWhiteSpace(c.Hook)
                        && string.IsNullOrWhiteSpace(c.WorldInfoDrop)
                        && string.IsNullOrWhiteSpace(c.CharacterArcProgress)
                        && string.IsNullOrWhiteSpace(c.MainPlotProgress)
                        && string.IsNullOrWhiteSpace(c.Foreshadowing))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteChapter(shell.Id);
                    TM.App.Log($"[ChapterViewModel] 取消清理: 删除空壳第{shell.ChapterNumber}章");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳章节，下次批量生成会按需续接");
            }
            else
            {
                var placeholderCreated = 0;
                foreach (var chNum in fullRange)
                {
                    var existing = Service.GetAllChapters()
                        .FirstOrDefault(c => c.ChapterNumber == chNum
                            && string.Equals(c.Category, config.CategoryName, StringComparison.Ordinal));

                    if (existing == null)
                    {
                        var data = new ChapterData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = $"第{chNum}章",
                            Category = config.CategoryName,
                            Volume = config.CategoryName,
                            IsEnabled = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterNumber = chNum,
                            ChapterTitle = $"第{chNum}章",
                        };
                        await Service.AddChapterAsync(data);
                        placeholderCreated++;
                        if (TM.App.IsDebugMode)
                            TM.App.Log($"[ChapterViewModel] 补缺: 第{chNum}章（AI未生成，创建占位）");
                    }
                }

                if (!TM.App.IsDebugMode && placeholderCreated > 0)
                    TM.App.Log($"[ChapterViewModel] 补缺完成: 已创建占位 {placeholderCreated} 个（AI未生成）");
            }

            _batchFullChapterRange = null;
            _batchPreCalculatedChapterNumbers = null;
            _currentBatchChapterNumbers = null;
            _currentBatchChapterNumbersAll = null;

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
                        var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);

                        int chapterNumber;
                        if (entity.TryGetValue(SystemChapterNumberKey, out var sysChNum) && sysChNum is int stampedNum && stampedNum > 0)
                        {
                            chapterNumber = stampedNum;
                        }
                        else
                        {
                            chapterNumber = reader.GetInt("ChapterNumber");
                        }

                        var existing = Service.GetAllChapters()
                            .FirstOrDefault(c => c.ChapterNumber == chapterNumber
                                && string.Equals(c.Category, categoryName, StringComparison.Ordinal));

                        var (mergedChars, mergedLocs, mergedFacs) = NormalizeChapterReferences(
                            categoryName,
                            reader.GetString("ReferencedCharacterNames"),
                            reader.GetString("ReferencedLocationNames"),
                            reader.GetString("ReferencedFactionNames"));

                        if (existing != null)
                        {
                            var aiName = reader.GetString("Name");
                            var aiTitle = reader.GetString("ChapterTitle");
                            var normalizedTitle = NormalizeChapterTitle(aiTitle);
                            var normalizedName = NormalizeChapterTitle(aiName);
                            if (!string.IsNullOrWhiteSpace(normalizedTitle)) existing.ChapterTitle = normalizedTitle;
                            else if (!string.IsNullOrWhiteSpace(normalizedName)) existing.ChapterTitle = normalizedName;
                            if (!string.IsNullOrWhiteSpace(normalizedName)) existing.Name = normalizedName;
                            else if (!string.IsNullOrWhiteSpace(normalizedTitle)) existing.Name = normalizedTitle;
                            var aiTheme = reader.GetString("ChapterTheme");
                            if (!string.IsNullOrWhiteSpace(aiTheme)) existing.ChapterTheme = aiTheme;
                            var aiReg = reader.GetString("ReaderExperienceGoal");
                            if (!string.IsNullOrWhiteSpace(aiReg)) existing.ReaderExperienceGoal = aiReg;
                            var aiGoal = reader.GetString("MainGoal");
                            if (!string.IsNullOrWhiteSpace(aiGoal)) existing.MainGoal = aiGoal;
                            var aiRes = reader.GetString("ResistanceSource");
                            if (!string.IsNullOrWhiteSpace(aiRes)) existing.ResistanceSource = aiRes;
                            var aiKey = reader.GetString("KeyTurn");
                            if (!string.IsNullOrWhiteSpace(aiKey)) existing.KeyTurn = aiKey;
                            var aiHook = reader.GetString("Hook");
                            if (!string.IsNullOrWhiteSpace(aiHook)) existing.Hook = aiHook;
                            var aiWid = reader.GetString("WorldInfoDrop");
                            if (!string.IsNullOrWhiteSpace(aiWid)) existing.WorldInfoDrop = aiWid;
                            var aiCap = reader.GetString("CharacterArcProgress");
                            if (!string.IsNullOrWhiteSpace(aiCap)) existing.CharacterArcProgress = aiCap;
                            var aiMpp = reader.GetString("MainPlotProgress");
                            if (!string.IsNullOrWhiteSpace(aiMpp)) existing.MainPlotProgress = aiMpp;
                            var aiFsh = reader.GetString("Foreshadowing");
                            if (!string.IsNullOrWhiteSpace(aiFsh)) existing.Foreshadowing = aiFsh;
                            if (!string.IsNullOrWhiteSpace(mergedChars) && existing.ReferencedCharacterNames.Count == 0)
                                existing.ReferencedCharacterNames = FromCommaSeparated(mergedChars);
                            if (!string.IsNullOrWhiteSpace(mergedLocs) && existing.ReferencedLocationNames.Count == 0)
                                existing.ReferencedLocationNames = FromCommaSeparated(mergedLocs);
                            if (!string.IsNullOrWhiteSpace(mergedFacs) && existing.ReferencedFactionNames.Count == 0)
                                existing.ReferencedFactionNames = FromCommaSeparated(mergedFacs);

                            existing.ChapterNumber = chapterNumber;
                            existing.Volume = categoryName;
                            existing.DependencyModuleVersions = versionSnapshot ?? new();
                            existing.UpdatedAt = DateTime.Now;
                            await Service.UpdateChapterAsync(existing);
                            entity["ChapterNumber"] = chapterNumber;
                            TM.App.Log($"[ChapterViewModel] Upsert更新: 第{chapterNumber}章");
                        }
                        else
                        {
                            var name = reader.GetString("Name");
                            if (string.IsNullOrWhiteSpace(name)) name = $"第{chapterNumber}章";
                            var title = reader.GetString("ChapterTitle");
                            if (string.IsNullOrWhiteSpace(title)) title = name;
                            var normalizedTitle = NormalizeChapterTitle(title);
                            var normalizedName = NormalizeChapterTitle(name);
                            var finalTitle = !string.IsNullOrWhiteSpace(normalizedTitle) ? normalizedTitle : normalizedName;
                            var finalName = !string.IsNullOrWhiteSpace(normalizedName) ? normalizedName : normalizedTitle;

                            var data = new ChapterData
                            {
                                Id = ShortIdGenerator.New("D"),
                                Name = finalName,
                                Category = categoryName,
                                IsEnabled = true,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                                ChapterNumber = chapterNumber,
                                Volume = categoryName,
                                ChapterTitle = finalTitle,
                                ChapterTheme = reader.GetString("ChapterTheme"),
                                ReaderExperienceGoal = reader.GetString("ReaderExperienceGoal"),
                                MainGoal = reader.GetString("MainGoal"),
                                ResistanceSource = reader.GetString("ResistanceSource"),
                                KeyTurn = reader.GetString("KeyTurn"),
                                Hook = reader.GetString("Hook"),
                                WorldInfoDrop = reader.GetString("WorldInfoDrop"),
                                CharacterArcProgress = reader.GetString("CharacterArcProgress"),
                                MainPlotProgress = reader.GetString("MainPlotProgress"),
                                Foreshadowing = reader.GetString("Foreshadowing"),
                                ReferencedCharacterNames = FromCommaSeparated(mergedChars),
                                ReferencedFactionNames = FromCommaSeparated(mergedFacs),
                                ReferencedLocationNames = FromCommaSeparated(mergedLocs),
                                DependencyModuleVersions = versionSnapshot ?? new()
                            };
                            entity["ChapterNumber"] = chapterNumber;
                            entity["ChapterTitle"] = finalTitle;
                            await Service.AddChapterAsync(data);
                            TM.App.Log($"[ChapterViewModel] Upsert新建: 第{chapterNumber}章");
                        }

                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ChapterViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[ChapterViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }

        protected override int GetBaseBatchSize() => 10;
        protected override int GetBatchSize64K() => 12;
        protected override int GetBatchSize128K() => 15;

        private async Task<(List<VolumeChapterRange>? Ranges, Dictionary<int, VolumeDesignData>? VolumeMap, string? ErrorMessage)> ResolveVolumeAllocationAsync(OutlineService? preInitializedOutlineService = null)
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

                if (!TM.Framework.Common.Helpers.ChapterAllocationHelper.TryParseVolumeDivision(volumeDivision, volumeNumbers.Count, totalChapters, out var parsedRanges))
                {
                    TM.App.Log($"[ChapterViewModel] 大纲 VolumeDivision 解析失败，回退到算法分配");
                    parsedRanges = TM.Framework.Common.Helpers.ChapterAllocationHelper.Allocate(volumeNumbers.Count, totalChapters);
                }

                return (parsedRanges, volumeMap, null);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] 大纲分配解析异常: {ex.Message}");
                return (null, null, $"章节范围解析异常：{ex.Message}");
            }
        }

        private List<int> GetIncompleteChapterVolumes(
            IEnumerable<VolumeChapterRange> rangesToCheck,
            Dictionary<int, VolumeDesignData> volumeMap)
        {
            var allChapters = Service.GetAllChapters()
                .Where(c => c.IsEnabled)
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
                var completedCount = allChapters
                    .Where(c => (string.Equals(c.CategoryId, vol.Id, StringComparison.Ordinal)
                                 || string.Equals(c.Category, catName, StringComparison.Ordinal)
                                 || string.Equals(c.Volume, catName, StringComparison.Ordinal)
                                 || string.Equals(c.Category, vol.Name, StringComparison.Ordinal)
                                 || string.Equals(c.Volume, vol.Name, StringComparison.Ordinal))
                                && expectedNums.Contains(c.ChapterNumber)
                                && !string.IsNullOrWhiteSpace(c.ChapterTheme)
                                && HasRealTitleContent(c.ChapterTitle))
                    .Select(c => c.ChapterNumber)
                    .Distinct()
                    .Count();
                if (completedCount < expectedNums.Count)
                    incompleteVolumes.Add(vol.VolumeNumber);
            }
            return incompleteVolumes;
        }

        public override async Task<List<string>> GetIncompletePrerequisiteCategoriesAsync(string categoryName)
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

                var prevIncomplete = GetIncompleteChapterVolumes(
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
                TM.App.Log($"[ChapterViewModel] GetIncompletePrerequisiteCategoriesAsync 异常: {ex.Message}");
                return new List<string>();
            }
        }

        protected override PreviousBatchInfo? GetPreviousBatchInfo(string categoryName)
        {
            try
            {
                var existingChapters = Service.GetAllChapters()
                    .Where(c => c.IsEnabled
                        && (string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                            || string.Equals(c.Volume, categoryName, StringComparison.Ordinal)))
                    .Where(c => !string.IsNullOrWhiteSpace(c.ChapterTheme) && HasRealTitleContent(c.ChapterTitle))
                    .OrderBy(c => c.ChapterNumber)
                    .ToList();

                if (existingChapters.Count == 0)
                    return null;

                var info = new PreviousBatchInfo();

                info.LastTitles = existingChapters
                    .TakeLast(10)
                    .Select(c => $"第{c.ChapterNumber}章 {NormalizeChapterTitle(c.ChapterTitle)}：{c.ChapterTheme}")
                    .ToList();

                info.LastSummaries = existingChapters
                    .Where(c => !string.IsNullOrWhiteSpace(c.MainGoal))
                    .TakeLast(10)
                    .Select(c => $"第{c.ChapterNumber}章目标：{(c.MainGoal.Length > 60 ? c.MainGoal.Substring(0, 60) + "…" : c.MainGoal)}")
                    .ToList();

                var foreshadowings = existingChapters
                    .Where(c => !string.IsNullOrWhiteSpace(c.Foreshadowing))
                    .TakeLast(10)
                    .Select(c => $"第{c.ChapterNumber}章伏笔：{c.Foreshadowing}")
                    .ToList();
                if (foreshadowings.Count > 0)
                    info.Foreshadowings = string.Join("\n", foreshadowings);

                var lastHooks = existingChapters
                    .Where(c => !string.IsNullOrWhiteSpace(c.Hook))
                    .TakeLast(5)
                    .Select(c => $"第{c.ChapterNumber}章钩子：{c.Hook}")
                    .ToList();
                if (lastHooks.Count > 0)
                    info.CharacterStates = "【末尾章节钩子（供衔接参考）】\n" + string.Join("\n", lastHooks);

                return info.HasValidInfo ? info : null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterViewModel] GetPreviousBatchInfo 失败: {ex.Message}");
                return null;
            }
        }
    }
}
