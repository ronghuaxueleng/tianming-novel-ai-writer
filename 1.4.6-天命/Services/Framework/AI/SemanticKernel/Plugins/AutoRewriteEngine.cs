using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Generation;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public class AutoRewriteEngine
    {
        #region 常量

        public const int MaxRewriteAttempts = 2;

        public const int MaxFailureReasonsPerRewrite = 5;

        #endregion

        #region 润色配置

        private int GetPolishMode(CreativeSpec? spec) => spec?.PolishMode ?? 0;

        #endregion

        #region 构造函数

        public AutoRewriteEngine() { }

        #endregion

        #region 公开方法

        public async Task<GenerationResult> GenerateWithRewriteAsync(
            string chapterId,
            ContentTaskContext taskContext,
            FactSnapshot factSnapshot,
            CreativeSpec? spec,
            CancellationToken ct = default)
        {
            var result = new GenerationResult { ChapterId = chapterId };
            List<string> previousFailures = new();
            bool hadAnyGateFailure = false;

            TM.App.Log($"[AutoRewriteEngine] 开始生成章节: {chapterId}");
            GenerationProgressHub.Report($"开始生成章节 {chapterId}...");

            var sessionKey = $"rewrite_{chapterId}_{DateTime.Now.Ticks}";
            var aiService = ServiceLocator.Get<Core.AIService>();

            try
            {

            var designElements = BuildDesignElementNames(taskContext);

            for (int attempt = 0; attempt <= MaxRewriteAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var isRewrite = attempt > 0;
                var isFullRetry = isRewrite && previousFailures.Count == 0;
                TM.App.Log($"[AutoRewriteEngine] 第{attempt + 1}次生成{(isFullRetry ? "（重新生成）" : isRewrite ? "（重写）" : "")}");
                GenerationProgressHub.Report(isFullRetry ? $"AI未返回内容，第{attempt + 1}次重新生成..."
                    : isRewrite ? $"校验未通过，开始第{attempt + 1}次重写..."
                    : "正在调用AI生成内容...");

                string userPrompt;
                if (!isRewrite || previousFailures.Count == 0)
                {
                        userPrompt = BuildPromptWithFailures(taskContext, factSnapshot, spec, previousFailures, false);
                }
                else
                {
                        userPrompt = BuildRewriteFeedback(previousFailures, factSnapshot);
                }

                var aiResult = await aiService.GenerateInBusinessSessionAsync(
                    sessionKey,
                    () => Task.FromResult(BuildSystemPromptWithSpec(spec)),
                    userPrompt,
                    ct,
                    isNavigationGuarded: false);
                int emptyRetries = 0;
                while ((!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content)) && emptyRetries < MaxRewriteAttempts)
                {
                    ct.ThrowIfCancellationRequested();
                    emptyRetries++;
                    TM.App.Log($"[AutoRewriteEngine] AI空返回，内部重试 {emptyRetries}/{MaxRewriteAttempts}");
                    GenerationProgressHub.Report($"⚠ AI未返回内容，重试中（{emptyRetries}/{MaxRewriteAttempts}）...");
                    aiService.EndBusinessSession(sessionKey);
                    sessionKey = $"rewrite_{chapterId}_{DateTime.Now.Ticks}";
                    userPrompt = BuildPromptWithFailures(taskContext, factSnapshot, spec, new List<string>(), false);
                    aiResult = await aiService.GenerateInBusinessSessionAsync(
                        sessionKey,
                        () => Task.FromResult(BuildSystemPromptWithSpec(spec)),
                        userPrompt,
                        ct,
                        isNavigationGuarded: false);
                }
                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    var errorMsg = string.IsNullOrWhiteSpace(aiResult.ErrorMessage)
                        ? "AI未返回任何内容"
                        : aiResult.ErrorMessage;
                    result.AddAttempt(attempt, false, $"AI连续{emptyRetries + 1}次空返回: {errorMsg}", new List<string> { errorMsg });
                    TM.App.Log($"[AutoRewriteEngine] AI连续空返回，attempt {attempt} 失败");
                    previousFailures = new List<string>();
                    continue;
                }

                GenerationProgressHub.Report("AI生成完成，正在校验...");
                var gateResult = await ServiceLocator.Get<GenerationGate>().ValidateAsync(
                    chapterId,
                    aiResult.Content!,
                    factSnapshot,
                    designElements: designElements);

                if (gateResult.Success)
                {
                    var finalContent = aiResult.Content!;
                    var finalGateResult = gateResult;

                    var polishMode = GetPolishMode(spec);
                    if (polishMode > 0)
                    {
                        var polisher = ServiceLocator.Get<ContentPolisher>();
                        var totalPolishRounds = polishMode;

                        TM.App.Log($"[AutoRewriteEngine] 开始润色（共{totalPolishRounds}轮）...");
                        GenerationProgressHub.Report(polishMode == 2
                            ? "校验通过，开始第1次润色..."
                            : "校验通过，开始润色...");

                        var polishResult = await polisher.PolishAsync(aiResult.Content!, ct);

                        if (polishMode >= 2 && polishResult.Success && !string.IsNullOrWhiteSpace(polishResult.PolishedContent))
                        {
                            TM.App.Log($"[AutoRewriteEngine] 开始第2次润色...");
                            GenerationProgressHub.Report("第1次润色完成，开始第2次润色...");
                            var polish2 = await polisher.PolishAsync(polishResult.PolishedContent, ct);

                            if (polish2.Success && !string.IsNullOrWhiteSpace(polish2.ContentWithoutChanges))
                            {
                                polishResult = polish2;
                                TM.App.Log($"[AutoRewriteEngine] 使用第2次润色结果");
                            }
                            else
                            {
                                TM.App.Log($"[AutoRewriteEngine] 第2次润色失败，使用第1次结果");
                            }
                        }

                        if (polishResult.Success && !string.IsNullOrWhiteSpace(polishResult.ContentWithoutChanges))
                        {
                            var polishGateResult = await ServiceLocator.Get<GenerationGate>().ValidateAsync(
                                chapterId,
                                polishResult.PolishedContent,
                                factSnapshot,
                                designElements);

                            if (polishGateResult.Success)
                            {
                                finalContent = polishResult.PolishedContent;
                                finalGateResult = polishGateResult;
                                TM.App.Log($"[AutoRewriteEngine] 润色完成并通过校验（共{totalPolishRounds}轮）");
                                GenerationProgressHub.Report($"✓ 润色完成，校验通过（{totalPolishRounds}轮）");
                            }
                            else
                            {
                                TM.App.Log($"[AutoRewriteEngine] 润色后校验失败，使用原文");
                                GenerationProgressHub.Report("⚠ 润色后校验失败，使用原文");
                            }
                        }
                        else
                        {
                            TM.App.Log($"[AutoRewriteEngine] 润色失败，使用原文: {polishResult.ErrorMessage}");
                            GenerationProgressHub.Report("⚠ 润色失败，使用原文");
                        }
                    }

                    var bpCheckContent = finalGateResult.ContentWithoutChanges
                        ?? StripChangesSection(finalContent);
                    var missingItems = CheckBlueprintCompliance(bpCheckContent, taskContext);
                    if (missingItems.Count > 0)
                    {
                        var totalBpEntities = CountBlueprintEntities(taskContext);
                        var bpThreshold = Math.Max(3, totalBpEntities / 3);
                        if (missingItems.Count <= bpThreshold)
                        {
                            TM.App.Log($"[AutoRewriteEngine] 蓝图合规 warn: {missingItems.Count}/{totalBpEntities} 缺席 (threshold={bpThreshold}, pass)");
                        }
                        else
                        {
                            if (!ReferenceEquals(finalContent, aiResult.Content!))
                            {
                                var origBpContent = gateResult.ContentWithoutChanges ?? StripChangesSection(aiResult.Content!);
                                var origMissing = CheckBlueprintCompliance(origBpContent, taskContext);
                                if (origMissing.Count <= bpThreshold)
                                {
                                    finalContent = aiResult.Content!;
                                    finalGateResult = gateResult;
                                    TM.App.Log($"[AutoRewriteEngine] 润色导致蓝图合规退步({missingItems.Count}→{origMissing.Count})，回退到原文");
                                    GenerationProgressHub.Report("⚠ 润色导致蓝图退步，使用原文");
                                    goto bp_passed;
                                }
                            }

                            var msg = $"蓝图要求的以下角色/地点/势力在正文中未出现，请在重写时自然融入：【{string.Join("、", missingItems)}】";
                            previousFailures = new List<string> { msg };
                            result.AddAttempt(attempt, false, msg, previousFailures);
                            TM.App.Log($"[AutoRewriteEngine] 蓝图合规检查失败: {msg}");
                            GenerationProgressHub.Report($"⚠ 蓝图合规: {missingItems.Count}/{totalBpEntities} 个实体未出场，重写中...");
                            continue;
                        }
                    }

                    bp_passed:
                    result.Success = true;
                    result.Content = finalContent;
                    result.ParsedChanges = finalGateResult.ParsedChanges;
                    result.GateResult = finalGateResult;
                    result.DesignElements = designElements;
                    result.AddAttempt(attempt, true, "校验通过");

                    TM.App.Log($"[AutoRewriteEngine] 第{attempt + 1}次生成成功");
                    GenerationProgressHub.Report($"✓ 章节 {chapterId} 生成完成");

                    ServiceLocator.Get<GenerationStatisticsService>().RecordGeneration(result);

                    return result;
                }

                hadAnyGateFailure = true;
                previousFailures = gateResult.GetHumanReadableFailures(MaxFailureReasonsPerRewrite);
                result.AddAttempt(attempt, false, string.Join("; ", previousFailures), previousFailures);

                foreach (var failure in gateResult.Failures)
                {
                    if (failure.Type == FailureType.Consistency)
                    {
                        foreach (var error in failure.Errors)
                        {
                            if (error.Contains("PayoffBeforeSetup"))
                                ServiceLocator.Get<GenerationStatisticsService>().RecordConsistencyIssue("PayoffBeforeSetup");
                            else if (error.Contains("ForeshadowingRollback"))
                                ServiceLocator.Get<GenerationStatisticsService>().RecordConsistencyIssue("ForeshadowingRollback");
                            else if (error.Contains("ConflictStatusSkip"))
                                ServiceLocator.Get<GenerationStatisticsService>().RecordConsistencyIssue("ConflictStatusSkip");
                            else if (error.Contains("CharacterNotInvolved"))
                                ServiceLocator.Get<GenerationStatisticsService>().RecordConsistencyIssue("CharacterNotInvolved");
                        }
                    }
                }

                TM.App.Log($"[AutoRewriteEngine] 第{attempt + 1}次生成校验失败: {string.Join("; ", previousFailures.Take(3))}");
                var progressSummary = SummarizeFailuresForProgress(previousFailures);
                GenerationProgressHub.Report($"⚠ 校验失败：{progressSummary}，开始第{attempt + 1}次重写...");
            }

            result.Success = false;
            result.RequiresManualIntervention = true;
            bool exhaustedByEmpty = !hadAnyGateFailure && previousFailures.Count == 0;
            result.InterventionHint = exhaustedByEmpty
                ? $"AI连续返回空内容（共{result.TotalAttempts}次），请检查网络连接或减少章节字数要求后重试"
                : $"已达到最大重写次数（{MaxRewriteAttempts + 1}次），请调整快照/规则/章节任务后重试";
            result.ErrorMessage = exhaustedByEmpty
                ? $"章节生成失败，AI未返回任何内容（共尝试{result.TotalAttempts}次）"
                : $"章节生成失败，共尝试{result.TotalAttempts}次。最后失败原因：{string.Join("; ", previousFailures)}";

            TM.App.Log($"[AutoRewriteEngine] 达到最大重写次数，需要人工介入: {chapterId}");

            ServiceLocator.Get<GenerationStatisticsService>().RecordGeneration(result);

            return result;

            }
            finally
            {
                aiService.EndBusinessSession(sessionKey);
            }
        }

        public string BuildPromptWithFailures(
            ContentTaskContext taskContext,
            FactSnapshot factSnapshot,
            CreativeSpec? spec,
            List<string> previousFailures,
            bool isRewrite)
        {
            var basePrompt = ServiceLocator.Get<LayeredPromptBuilder>().BuildLayeredPrompt(taskContext, factSnapshot, spec);

            if (isRewrite && previousFailures.Count > 0)
            {
                basePrompt = AppendFailureReasons(basePrompt, previousFailures);
            }

            if (!string.IsNullOrWhiteSpace(taskContext.RepairHints))
            {
                basePrompt = basePrompt + "\n\n" + taskContext.RepairHints;
            }

            return basePrompt;
        }

        #endregion

        #region 私有方法

        private string BuildRewriteFeedback(List<string> failures, FactSnapshot? factSnapshot = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<rewrite_feedback reason=\"validation_failure\">");
            sb.AppendLine("你上次生成的内容未通过校验，请根据以下问题重新生成完整章节：");
            sb.AppendLine();

            var reasonsToAppend = failures.Take(MaxFailureReasonsPerRewrite).ToList();
            for (int i = 0; i < reasonsToAppend.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {reasonsToAppend[i]}");
            }

            if (factSnapshot != null)
            {
                if (failures.Any(f => f.Contains("FromLocation", StringComparison.OrdinalIgnoreCase)
                                      || f.Contains("FromHolder", StringComparison.OrdinalIgnoreCase)
                                      || f.Contains("路径不连续", StringComparison.OrdinalIgnoreCase)))
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"changes_consistency\">");
                    sb.AppendLine("以下为账本基线（必须严格对齐）：");
                    sb.AppendLine("1) 若输出 CharacterMovements：CharacterId 使用角色 ShortId；FromLocation 必须等于该角色当前位置的地点 ShortId；");
                    sb.AppendLine("2) 若输出 ItemTransfers：FromHolder/ToHolder 使用角色 ShortId；FromHolder 必须等于该物品当前持有者 ShortId。\n");

                    if (factSnapshot.CharacterLocations != null && factSnapshot.CharacterLocations.Count > 0)
                    {
                        sb.AppendLine("【角色当前位置】格式: 角色名（CharacterId）: 地点名（LocationId）");
                        var locMap = factSnapshot.LocationDescriptions ?? new System.Collections.Generic.Dictionary<string, LocationCoreDescription>();
                        foreach (var loc in factSnapshot.CharacterLocations)
                        {
                            var charName = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                            if (string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                            string locDisplay;
                            if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(loc.CurrentLocation)
                                && locMap.TryGetValue(loc.CurrentLocation, out var lDesc))
                                locDisplay = $"{lDesc.Name}（{loc.CurrentLocation}）";
                            else
                                locDisplay = loc.CurrentLocation;
                            sb.AppendLine($"- {charName}（{loc.CharacterId}）: {locDisplay}");
                        }
                        sb.AppendLine();
                    }

                    if (factSnapshot.ItemStates != null && factSnapshot.ItemStates.Count > 0)
                    {
                        sb.AppendLine("【物品持有者】格式: 物品名（ItemId）: 持有者名（CharacterId）");
                        var charMap = factSnapshot.CharacterDescriptions ?? new System.Collections.Generic.Dictionary<string, CharacterCoreDescription>();
                        foreach (var item in factSnapshot.ItemStates)
                        {
                            if (string.IsNullOrWhiteSpace(item.Name)) continue;
                            string holderDisplay;
                            if (string.IsNullOrWhiteSpace(item.CurrentHolder))
                                holderDisplay = "无人持有";
                            else if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(item.CurrentHolder)
                                     && charMap.TryGetValue(item.CurrentHolder, out var cDesc))
                                holderDisplay = $"{cDesc.Name}（{item.CurrentHolder}）";
                            else
                                holderDisplay = item.CurrentHolder;
                            var idPart = string.IsNullOrWhiteSpace(item.Id) ? string.Empty : $"（{item.Id}）";
                            sb.AppendLine($"- {item.Name}{idPart}: {holderDisplay}");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("</hard_baseline>");
                }

                AppendValidEntityHint(sb, failures, "CharacterId", "不在本章涉及角色列表", "CharacterId",
                    GetValidEntityHints(factSnapshot.CharacterStates?.Select(s => ((string?)s.Name, (string?)s.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "RelationshipChanges", "RelationshipChanges.key", "CharacterId（关系变化对象）",
                    GetValidEntityHints(factSnapshot.CharacterStates?.Select(s => ((string?)s.Name, (string?)s.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "ConflictId", "NewStatus", "ConflictId",
                    GetValidEntityHints(factSnapshot.ConflictProgress?.Select(c => ((string?)c.Name, (string?)c.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "ForeshadowId", "埋设", "ForeshadowId",
                    GetValidEntityHints(factSnapshot.ForeshadowingStatus?.Select(f => ((string?)f.Name, (string?)f.Id)).ToList()));
                bool hasForeshadowErr = failures.Any(f => f.Contains("伏笔", StringComparison.OrdinalIgnoreCase)
                                                       || f.Contains("ForeshadowId", StringComparison.OrdinalIgnoreCase)
                                                       || f.Contains("埋设", StringComparison.OrdinalIgnoreCase));
                if (hasForeshadowErr && factSnapshot.ForeshadowingStatus?.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ **伏笔当前状态（重写时必须严格遵守，不可违反）**：");
                    foreach (var fs in factSnapshot.ForeshadowingStatus)
                    {
                        var st = fs.IsResolved ? "已揭示（禁止再埋设/揭示）"
                               : fs.IsSetup   ? "已埋设未揭示（可揭示，禁止再埋设）"
                                              : "未埋设（可埋设，禁止揭示）";
                        sb.AppendLine($"  - {fs.Name}（{fs.Id}）：{st}");
                    }
                }
                AppendValidEntityHint(sb, failures, "LocationId", "FromLocation", "LocationId",
                    GetValidEntityHints(factSnapshot.LocationStates?.Select(l => ((string?)l.Name, (string?)l.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "ToLocation", "终点位置", "LocationId",
                    GetValidEntityHints(factSnapshot.LocationStates?.Select(l => ((string?)l.Name, (string?)l.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "势力", "FactionId", "FactionId",
                    GetValidEntityHints(factSnapshot.FactionStates?.Select(f => ((string?)f.Name, (string?)f.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "InvolvedCharacters", "CharacterMovements", "CharacterId",
                    GetValidEntityHints(factSnapshot.CharacterStates?.Select(s => ((string?)s.Name, (string?)s.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "ItemId", "ItemTransfers", "ItemId",
                    GetValidEntityHints(factSnapshot.ItemStates?.Select(i => ((string?)i.Name, (string?)i.Id)).ToList()));
                AppendValidEntityHint(sb, failures, "FromHolder", "ToHolder", "CharacterId（物品持有者）",
                    GetValidEntityHints(factSnapshot.CharacterStates?.Select(s => ((string?)s.Name, (string?)s.Id)).ToList()));

                bool charLedgerEmpty     = factSnapshot.CharacterStates?.All(s  => string.IsNullOrWhiteSpace(s.Id))  != false;
                bool conflictLedgerEmpty = factSnapshot.ConflictProgress?.All(c  => string.IsNullOrWhiteSpace(c.Id))  != false;
                bool fsLedgerEmpty       = factSnapshot.ForeshadowingStatus?.All(f => string.IsNullOrWhiteSpace(f.Id)) != false;
                bool locLedgerEmpty      = factSnapshot.LocationStates?.All(l  => string.IsNullOrWhiteSpace(l.Id))  != false;
                bool facLedgerEmpty      = factSnapshot.FactionStates?.All(f   => string.IsNullOrWhiteSpace(f.Id))  != false;
                bool itemLedgerEmpty     = factSnapshot.ItemStates?.All(i      => string.IsNullOrWhiteSpace(i.Id))  != false;

                bool needCharEmpty    = charLedgerEmpty     && failures.Any(f => f.Contains("CharacterId",      StringComparison.OrdinalIgnoreCase) || f.Contains("InvolvedCharacters", StringComparison.OrdinalIgnoreCase) || f.Contains("CharacterMovements", StringComparison.OrdinalIgnoreCase) || f.Contains("FromHolder", StringComparison.OrdinalIgnoreCase) || f.Contains("ToHolder", StringComparison.OrdinalIgnoreCase));
                bool needConflictEmpty= conflictLedgerEmpty && failures.Any(f => f.Contains("ConflictId",       StringComparison.OrdinalIgnoreCase));
                bool needFsEmpty      = fsLedgerEmpty       && failures.Any(f => f.Contains("ForeshadowId",     StringComparison.OrdinalIgnoreCase));
                bool needLocEmpty     = locLedgerEmpty      && failures.Any(f => f.Contains("LocationId",       StringComparison.OrdinalIgnoreCase) || f.Contains("FromLocation", StringComparison.OrdinalIgnoreCase) || f.Contains("ToLocation", StringComparison.OrdinalIgnoreCase));
                bool needFacEmpty     = facLedgerEmpty      && failures.Any(f => f.Contains("FactionId",        StringComparison.OrdinalIgnoreCase));
                bool needItemEmpty    = itemLedgerEmpty      && failures.Any(f => f.Contains("ItemId",          StringComparison.OrdinalIgnoreCase));

                if (needCharEmpty || needConflictEmpty || needFsEmpty || needLocEmpty || needFacEmpty || needItemEmpty)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"empty_ledger_shortid\">");
                    sb.AppendLine("⚠ 以下实体类型在账本中无已追踪记录（无可用ShortId），对应CHANGES字段必须输出空数组，禁止填写任何名称/拼音/自造标识符（此指令优先于上方关于ShortId的要求）：");
                    if (needCharEmpty)
                        sb.AppendLine("  角色账本为空 → CharacterStateChanges=[] | CharacterMovements=[] | RelationshipChanges={} | InvolvedCharacters（NewPlotPoints中）=[] | ItemTransfers中 FromHolder/ToHolder均留空字符串");
                    if (needConflictEmpty)
                        sb.AppendLine("  冲突账本为空 → ConflictProgress=[]");
                    if (needFsEmpty)
                        sb.AppendLine("  伏笔账本为空 → ForeshadowingActions=[]");
                    if (needLocEmpty)
                        sb.AppendLine("  地点账本为空 → LocationStateChanges=[]");
                    if (needFacEmpty)
                        sb.AppendLine("  势力账本为空 → FactionStateChanges=[]");
                    if (needItemEmpty)
                        sb.AppendLine("  物品账本为空 → ItemTransfers=[]（没有已追踪物品，ItemId无合法ShortId，禁止填写）");
                    sb.AppendLine("</hard_baseline>");
                }

                var missingChars = failures
                    .Where(f => f.Contains("指定角色未在正文出现:") || f.Contains("剧情关键角色未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingFactions = failures
                    .Where(f => f.Contains("指定势力未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingLocs = failures
                    .Where(f => f.Contains("指定地点未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingBpEntities = failures
                    .Where(f => f.Contains("蓝图要求") || f.Contains("自然融入"))
                    .SelectMany(f =>
                    {
                        var s = f.IndexOf('【'); var e = f.IndexOf('】');
                        if (s < 0 || e <= s) return Enumerable.Empty<string>();
                        return f.Substring(s + 1, e - s - 1)
                                .Split('、', StringSplitOptions.RemoveEmptyEntries)
                                .Select(n => n.Trim()).Where(n => n.Length >= 2);
                    })
                    .Where(n => !missingChars.Contains(n, StringComparer.OrdinalIgnoreCase)
                             && !missingFactions.Contains(n, StringComparer.OrdinalIgnoreCase)
                             && !missingLocs.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (missingChars.Count > 0 || missingFactions.Count > 0 || missingLocs.Count > 0 || missingBpEntities.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"design_element_presence\">");
                    sb.AppendLine("以下实体是本章任务的必要组成部分，重写时**必须**在正文中安排有实质戏份（对话/动作/事件均可）：");
                    if (missingChars.Count > 0)
                        sb.AppendLine($"- 必须出场的角色：【{string.Join("、", missingChars)}】");
                    if (missingFactions.Count > 0)
                        sb.AppendLine($"- 必须提及的势力：【{string.Join("、", missingFactions)}】");
                    if (missingLocs.Count > 0)
                        sb.AppendLine($"- 必须出现的地点：【{string.Join("、", missingLocs)}】");
                    if (missingBpEntities.Count > 0)
                        sb.AppendLine($"- 必须出现的实体：【{string.Join("、", missingBpEntities)}】");
                    sb.AppendLine("请在重写时围绕本章主线情节，自然地安排上述实体出场，不可省略或跳过。");
                    sb.AppendLine("</hard_baseline>");
                }
            }

            bool hasBlueprintFailure = failures.Any(f => f.Contains("蓝图要求") || f.Contains("自然融入"));
            if (hasBlueprintFailure && factSnapshot != null)
            {
                var charHints  = GetValidEntityHints(factSnapshot.CharacterStates?.Select(s  => ((string?)s.Name,  (string?)s.Id)).ToList());
                var conflHints = GetValidEntityHints(factSnapshot.ConflictProgress?.Select(c  => ((string?)c.Name,  (string?)c.Id)).ToList());
                var fsHints    = GetValidEntityHints(factSnapshot.ForeshadowingStatus?.Select(f => ((string?)f.Name,  (string?)f.Id)).ToList());
                var locHints   = GetValidEntityHints(factSnapshot.LocationStates?.Select(l  => ((string?)l.Name,  (string?)l.Id)).ToList());
                var facHints   = GetValidEntityHints(factSnapshot.FactionStates?.Select(f   => ((string?)f.Name,  (string?)f.Id)).ToList());
                if (charHints.Count > 0 || conflHints.Count > 0 || fsHints.Count > 0 || locHints.Count > 0 || facHints.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<shortid_reference mandatory=\"true\" reason=\"blueprint_rewrite\">");
                    sb.AppendLine("重写时 CHANGES 中所有 Id 字段必须使用以下括号内的 ShortId，禁止使用名称、拼音或自造标识符：");
                    if (charHints.Count > 0)  sb.AppendLine($"角色: {string.Join("、", charHints)}");
                    if (conflHints.Count > 0) sb.AppendLine($"冲突: {string.Join("、", conflHints)}");
                    if (fsHints.Count > 0)    sb.AppendLine($"伏笔: {string.Join("、", fsHints)}");
                    if (locHints.Count > 0)   sb.AppendLine($"地点: {string.Join("、", locHints)}");
                    if (facHints.Count > 0)   sb.AppendLine($"势力: {string.Join("、", facHints)}");
                    sb.AppendLine("</shortid_reference>");
                }
            }

            sb.AppendLine();
            sb.AppendLine("请保持原有的写作要求和格式规范（包含 ---CHANGES--- 分隔符及完整JSON），修复以上问题后重新输出完整内容。");
            sb.AppendLine("</rewrite_feedback>");

            return sb.ToString();
        }

        private static void AppendValidEntityHint(
            StringBuilder sb,
            List<string> failures,
            string keyword1,
            string keyword2,
            string fieldName,
            List<string>? validEntries)
        {
            if (validEntries == null || validEntries.Count == 0) return;
            bool hasError = failures.Any(f => f.Contains(keyword1, StringComparison.OrdinalIgnoreCase) || f.Contains(keyword2, StringComparison.OrdinalIgnoreCase));
            if (!hasError) return;

            var distinct = validEntries.Distinct().ToList();
            sb.AppendLine();
            sb.AppendLine($"⚠ **{fieldName}格式纠正**：本章合法实体列表（必须填写**括号内的 ShortId**，禁止填写名称文字）：");
            sb.AppendLine($"  {string.Join("、", distinct)}");
            var exampleId = ExtractIdFromHint(distinct.First());
            if (!string.IsNullOrWhiteSpace(exampleId))
                sb.AppendLine($"  例如：\"{fieldName}\": \"{exampleId}\"");
        }

        private static string? ExtractIdFromHint(string hint)
        {
            var m = System.Text.RegularExpressions.Regex.Match(hint, @"（([^\uff09]+)）$");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static List<string> GetValidEntityHints(List<(string? name, string? id)>? pairs)
        {
            if (pairs == null) return new List<string>();
            return pairs
                .Where(p => !string.IsNullOrWhiteSpace(p.name) && !string.IsNullOrWhiteSpace(p.id))
                .Select(p => $"{p.name}（{p.id}）")
                .Distinct()
                .ToList();
        }

        private static readonly char[] _bpSeparators = { ',', '，', '、', ';', '；' };

        private static int CountBlueprintEntities(ContentTaskContext ctx)
        {
            if (ctx.Blueprints == null || ctx.Blueprints.Count == 0) return 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bp in ctx.Blueprints)
            {
                foreach (var sep in new[] { bp.Cast, bp.Locations, bp.Factions })
                {
                    if (string.IsNullOrWhiteSpace(sep)) continue;
                    foreach (var p in sep.Split(_bpSeparators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var n = p.Trim();
                        if (n.Length >= 2) names.Add(n);
                    }
                }
                if (!string.IsNullOrWhiteSpace(bp.PovCharacter)) names.Add(bp.PovCharacter.Trim());
            }
            return names.Count;
        }

        private static string SummarizeFailuresForProgress(List<string> failures)
        {
            if (failures.Count == 0) return "未知原因";
            var cleaned = failures
                .Take(2)
                .Select(f => System.Text.RegularExpressions.Regex.Replace(
                    f, @"（反馈了名称'[^']+'）", string.Empty).Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();
            var summary = string.Join("；", cleaned);
            if (failures.Count > 2) summary += $"（共{failures.Count}项）";
            return summary;
        }

        private static List<string> CheckBlueprintCompliance(string content, ContentTaskContext ctx)
        {
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(content) || ctx.Blueprints == null || ctx.Blueprints.Count == 0)
                return new List<string>();

            foreach (var bp in ctx.Blueprints)
            {
                ExtractBlueprintMissing(content, bp.Cast, missing);
                ExtractBlueprintMissing(content, bp.Locations, missing);
                ExtractBlueprintMissing(content, bp.Factions, missing);
                if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                {
                    var name = bp.PovCharacter.Trim();
                    if (name.Length >= 2 && !content.Contains(name, StringComparison.OrdinalIgnoreCase))
                        missing.Add(name);
                }
            }
            return missing.ToList();
        }

        private static void ExtractBlueprintMissing(string content, string? raw, HashSet<string> missing)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var part in raw.Split(_bpSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length < 2) continue;
                if (content.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                var shortName = EntityNameNormalizeHelper.StripBracketAnnotation(name);
                if (!string.IsNullOrWhiteSpace(shortName) && shortName != name
                    && shortName.Length >= 2 && content.Contains(shortName, StringComparison.OrdinalIgnoreCase)) continue;
                missing.Add(name);
            }
        }

        private static string StripChangesSection(string content)
        {
            var idx = content.IndexOf("---CHANGES---", StringComparison.Ordinal);
            return idx > 0 ? content[..idx].TrimEnd() : content;
        }

        private string AppendFailureReasons(string prompt, List<string> failures)
        {
            if (failures.Count == 0) return prompt;

            var sb = new StringBuilder(prompt);
            sb.AppendLine();
            sb.AppendLine("<rewrite_instruction reason=\"validation_failure\">");
            sb.AppendLine("上次生成失败，请修复以下问题：");
            sb.AppendLine();

            var reasonsToAppend = failures.Take(MaxFailureReasonsPerRewrite).ToList();
            for (int i = 0; i < reasonsToAppend.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {reasonsToAppend[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("请仔细检查并修正上述问题，确保输出符合要求。");
            sb.AppendLine("</rewrite_instruction>");

            return sb.ToString();
        }

        internal static DesignElementNames BuildDesignElementNames(ContentTaskContext ctx)
        {
            var names = new DesignElementNames();

            if (ctx.Characters != null)
            {
                foreach (var c in ctx.Characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name))
                        names.CharacterNames.Add(c.Name);
                }
            }

            if (ctx.Locations != null)
            {
                foreach (var loc in ctx.Locations)
                {
                    if (!string.IsNullOrWhiteSpace(loc.Name))
                        names.LocationNames.Add(loc.Name);
                }
            }

            if (ctx.ExpandedCharacters != null)
            {
                foreach (var c in ctx.ExpandedCharacters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && !names.CharacterNames.Contains(c.Name))
                        names.CharacterNames.Add(c.Name);
                }
            }

            if (ctx.Blueprints != null)
            {
                foreach (var bp in ctx.Blueprints)
                {
                    if (!string.IsNullOrWhiteSpace(bp.PovCharacter))
                    {
                        var pov = bp.PovCharacter.Trim();
                        if (!names.CharacterNames.Contains(pov))
                            names.CharacterNames.Add(pov);
                        if (!names.PovCharacterNames.Contains(pov))
                            names.PovCharacterNames.Add(pov);
                    }
                    AddBlueprintTextEntities(bp.Cast, names.CharacterNames);
                    AddBlueprintTextEntities(bp.Locations, names.LocationNames);
                    AddBlueprintTextEntities(bp.Factions, names.FactionNames);
                }
            }

            return names;
        }

        private static readonly char[] _bpNameSeparators = { ',', '\uff0c', '\u3001', ';', '\uff1b' };

        private static void AddBlueprintTextEntities(string? raw, List<string> target)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var part in raw.Split(_bpNameSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length >= 2 && !target.Contains(name))
                    target.Add(name);
            }
        }

        private static string BuildSystemPromptWithSpec(CreativeSpec? spec)
        {
            var sb = new StringBuilder();

            if (spec != null && !string.IsNullOrEmpty(spec.TemplateName))
            {
                try
                {
                    var repo = ServiceLocator.Get<IPromptRepository>();
                    var specTemplate = repo.GetAllTemplates()
                        .FirstOrDefault(t => t.Name == spec.TemplateName
                            && t.Tags != null && t.Tags.Contains("Spec"));
                    if (specTemplate != null && !string.IsNullOrWhiteSpace(specTemplate.SystemPrompt))
                    {
                        sb.AppendLine("<genre_spec priority=\"highest\" source=\"prompt_library\">");
                        sb.AppendLine(specTemplate.SystemPrompt);
                        sb.AppendLine("</genre_spec>");
                        sb.AppendLine();
                        TM.App.Log($"[AutoRewriteEngine] 已注入Spec模板原文: {specTemplate.Name}");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AutoRewriteEngine] 加载Spec模板失败: {ex.Message}");
                }
            }

            sb.Append(GetEnabledBusinessPrompt());

            return sb.ToString();
        }

        private static string GetEnabledBusinessPrompt()
        {
            try
            {
                var repo = ServiceLocator.Get<IPromptRepository>();
                var templates = repo.GetTemplatesByCategory("业务提示词");
                var enabled = templates
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .FirstOrDefault();
                if (enabled != null)
                {
                    TM.App.Log($"[AutoRewriteEngine] 使用业务提示词模板: {enabled.Name} ({enabled.Id})");
                    return enabled.SystemPrompt;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoRewriteEngine] prompt repo err, fallback: {ex.Message}");
            }

            TM.App.Log("[AutoRewriteEngine] no template, fallback");
            return Prompts.Business.BusinessPromptProvider.GenerationBusinessPrompt;
        }

        #endregion
    }
}
