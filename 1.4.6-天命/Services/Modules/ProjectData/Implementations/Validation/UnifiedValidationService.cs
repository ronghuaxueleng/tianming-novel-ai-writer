using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class UnifiedValidationService : IUnifiedValidationService
    {
        private readonly IContextService _contextService;
        private readonly IGeneratedContentService _contentService;
        private readonly IPublishService _publishService;
        private readonly IValidationSummaryService _validationSummaryService;
        private readonly TM.Services.Modules.VersionTracking.VersionTrackingService _versionTrackingService;
        private readonly AIService _aiService;
        private readonly IWorkScopeService _workScopeService;
        private readonly GuideContextService _guideContextService;
        private readonly GenerationGate _generationGate;
        private readonly string _ruleSignature;

        private const string SystemModuleName = "System";
        private const int ChapterPreviewLength = 1000;

        private const int ValidationBatchSize = 2;

        public UnifiedValidationService(
            IContextService contextService,
            IGeneratedContentService contentService,
            IPublishService publishService,
            IValidationSummaryService validationSummaryService,
            TM.Services.Modules.VersionTracking.VersionTrackingService versionTrackingService,
            AIService aiService,
            IWorkScopeService workScopeService,
            GuideContextService guideContextService,
            GenerationGate generationGate)
        {
            _contextService = contextService;
            _contentService = contentService;
            _publishService = publishService;
            _validationSummaryService = validationSummaryService;
            _versionTrackingService = versionTrackingService;
            _aiService = aiService;
            _workScopeService = workScopeService;
            _guideContextService = guideContextService;
            _generationGate = generationGate;
            _ruleSignature = BuildRulesSignature();

            TM.App.Log("[UnifiedValidationService] 初始化完成");
        }

        #region 公开方法

        public Task<bool> NeedsRepublishAsync()
        {
            var status = _publishService.GetPublishStatus();
            return Task.FromResult(status.NeedsRepublish);
        }

        public async Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default)
        {
            var chapterContent = await _contentService.GetChapterAsync(chapterId);
            return await ValidateChapterInternalAsync(chapterId, chapterContent, ct);
        }

        public Task<ChapterValidationResult> ValidateChapterWithContentAsync(string chapterId, string chapterContent, CancellationToken ct = default)
        {
            return ValidateChapterInternalAsync(chapterId, chapterContent, ct);
        }

        private async Task<ChapterValidationResult> ValidateChapterInternalAsync(string chapterId, string? chapterContent, CancellationToken ct = default)
        {
            TM.App.Log($"[UnifiedValidationService] 开始校验章节: {chapterId}");

            EnsurePackagedDataOrThrow();

            var (volumeNumber, chapterNumber) = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
            if (volumeNumber == 0 || chapterNumber == 0)
            {
                TM.App.Log($"[UnifiedValidationService] 无法解析章节ID: {chapterId}");
                return CreateErrorResult(chapterId, "无法解析章节ID");
            }

            if (string.IsNullOrEmpty(chapterContent))
            {
                var volumeNameForError = await GetVolumeNameAsync(volumeNumber);
                TM.App.Log($"[UnifiedValidationService] 章节正文不存在: {chapterId}");
                return CreateErrorResult(chapterId, "章节正文不存在", volumeNumber, chapterNumber, volumeNameForError);
            }

            var volumeName = await GetVolumeNameAsync(volumeNumber);

            var chapterTitle = ExtractChapterTitle(chapterContent);

            var context = await _contextService.GetValidationContextAsync(chapterId);
            if (context == null)
            {
                TM.App.Log($"[UnifiedValidationService] 无法加载校验上下文: {chapterId}");
                return CreateErrorResult(chapterId, "无法加载校验上下文，请先执行打包", volumeNumber, chapterNumber, volumeName);
            }

            var contentGuide = await _guideContextService.GetContentGuideAsync();
            if (contentGuide?.Chapters?.TryGetValue(chapterId, out var guideEntry) != true || guideEntry == null)
            {
                var errorMsg = $"ContextIds 缺失：章节 {chapterId} 未写入指导文件，请重新打包/更新。";
                TM.App.Log($"[UnifiedValidationService] {errorMsg}");
                return CreateErrorResult(chapterId, errorMsg, volumeNumber, chapterNumber, volumeName);
            }

            var contextIdsValidation = await _guideContextService.ValidateContextIdsAsync(guideEntry.ContextIds);
            if (!contextIdsValidation.IsValid)
            {
                var errorMsg = $"ContextIds 解析失败，索引与本体不一致，请重新打包/更新。{Environment.NewLine}{contextIdsValidation.GetErrorSummary()}";
                TM.App.Log($"[UnifiedValidationService] {errorMsg}");
                return CreateErrorResult(chapterId, errorMsg, volumeNumber, chapterNumber, volumeName);
            }

            var result = new ChapterValidationResult
            {
                ChapterId = chapterId,
                ChapterTitle = chapterTitle,
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                VolumeName = volumeName,
                ValidatedTime = DateTime.Now
            };

            ct.ThrowIfCancellationRequested();

            await RunGateChecksAsync(result, chapterId, chapterContent, guideEntry.ContextIds);

            ct.ThrowIfCancellationRequested();

            await ExecuteValidationsAsync(result, context, chapterContent, ct);

            result.OverallResult = DetermineOverallResult(result);

            TM.App.Log($"[UnifiedValidationService] 章节校验完成: {chapterId}, 结果: {result.OverallResult}, 问题数: {result.TotalIssueCount}");
            return result;
        }

        public async Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default)
        {
            TM.App.Log($"[UnifiedValidationService] 开始校验第{volumeNumber}卷");

            EnsurePackagedDataOrThrow();

            var volumeName = await GetVolumeNameAsync(volumeNumber);
            var result = new VolumeValidationResult
            {
                VolumeNumber = volumeNumber,
                VolumeName = volumeName,
                ValidatedTime = DateTime.Now
            };

            var chapters = await _contentService.GetGeneratedChaptersAsync();
            var volumeChapters = chapters.Where(c => c.VolumeNumber == volumeNumber)
                                         .OrderBy(c => c.ChapterNumber)
                                         .ToList();

            TM.App.Log($"[UnifiedValidationService] 第{volumeNumber}卷共{volumeChapters.Count}个章节");

            if (volumeChapters.Count == 0)
            {
                TM.App.Log($"[UnifiedValidationService] 第{volumeNumber}卷没有章节，跳过校验");
                return result;
            }

            var sampleCount = CalculateSampleCount(volumeChapters.Count);
            var sampledChapters = SampleChapters(volumeChapters, sampleCount);
            TM.App.Log($"[UnifiedValidationService] 卷章节总数: {volumeChapters.Count}, 抽样章节数: {sampledChapters.Count}");

            const int maxConcurrency = 8;
            using var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency, maxConcurrency);

            var batches = sampledChapters
                .Select((ch, i) => new { ch, i })
                .GroupBy(x => x.i / ValidationBatchSize)
                .Select(g => g.Select(x => x.ch).ToList())
                .ToList();

            var batchTasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (batch.Count == 1)
                        return new[] { await ValidateChapterAsync(batch[0].Id, ct) };
                    return await ValidateChapterBatchAsync(batch, volumeName, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[UnifiedValidationService] 批次校验失败: {string.Join(",", batch.Select(c => c.Id))}, {ex.Message}");
                    return batch.Select(c => CreateErrorResult(c.Id, $"校验异常: {ex.Message}", c.VolumeNumber, c.ChapterNumber, volumeName)).ToArray();
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var chapterResults = (await Task.WhenAll(batchTasks))
                .SelectMany(r => r)
                .OrderBy(r => r.ChapterNumber)
                .ToList();

            foreach (var r in chapterResults)
            {
                result.ChapterResults.Add(r);
            }

            var summaryData = AggregateToVolumeSummary(volumeNumber, volumeName, sampledChapters, chapterResults);

            _validationSummaryService.SaveVolumeValidation(volumeNumber, summaryData);

            TM.App.Log($"[UnifiedValidationService] 卷校验完成: 第{volumeNumber}卷, 抽样: {sampledChapters.Count}章, 结果: {summaryData.OverallResult}");
            return result;
        }

        private async Task<ChapterValidationResult[]> ValidateChapterBatchAsync(List<ChapterInfo> batch, string volumeName, CancellationToken ct = default)
        {
            var results = new List<ChapterValidationResult>();
            var contents = new List<string?>();

            foreach (var chapter in batch)
            {
                var content = await _contentService.GetChapterAsync(chapter.Id);

                if (string.IsNullOrEmpty(content))
                {
                    results.Add(CreateErrorResult(chapter.Id, "章节正文不存在", chapter.VolumeNumber, chapter.ChapterNumber, volumeName));
                    contents.Add(null);
                    continue;
                }

                var chapterTitle = ExtractChapterTitle(content);
                var r = new ChapterValidationResult
                {
                    ChapterId = chapter.Id,
                    ChapterTitle = chapterTitle,
                    VolumeNumber = chapter.VolumeNumber,
                    ChapterNumber = chapter.ChapterNumber,
                    VolumeName = volumeName,
                    ValidatedTime = DateTime.Now
                };
                results.Add(r);
                contents.Add(content);
            }

            var pendingIndices = contents
                .Select((c, i) => (c, i))
                .Where(x => x.c != null)
                .Select(x => x.i)
                .ToList();

            if (pendingIndices.Count == 0)
                return results.ToArray();

            ct.ThrowIfCancellationRequested();

            if (pendingIndices.Count == 1)
            {
                var idx = pendingIndices[0];
                await ExecuteValidationsAsync(results[idx], await _contextService.GetValidationContextAsync(batch[idx].Id), contents[idx]!, ct);
                results[idx].OverallResult = DetermineOverallResult(results[idx]);
                TM.App.Log($"[UnifiedValidationService] 单章校验完成: {batch[idx].Id}, 结果: {results[idx].OverallResult}");
                return results.ToArray();
            }

            var batchPrompt = await BuildBatchValidationPromptAsync(batch, pendingIndices, contents, results);
            var aiResult = await _aiService.GenerateAsync(batchPrompt, ct);

            if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
            {
                TM.App.Log($"[UnifiedValidationService] 批量AI校验失败，降级逐章: {aiResult.ErrorMessage}");
                foreach (var idx in pendingIndices)
                    await ExecuteValidationsAsync(results[idx], await _contextService.GetValidationContextAsync(batch[idx].Id), contents[idx]!, ct);
            }
            else
            {
                ParseBatchAIValidationResult(pendingIndices.Select(i => results[i]).ToList(), batch, aiResult.Content);
            }

            foreach (var idx in pendingIndices)
            {
                results[idx].OverallResult = DetermineOverallResult(results[idx]);
                TM.App.Log($"[UnifiedValidationService] 批量校验完成: {batch[idx].Id}, 结果: {results[idx].OverallResult}");
            }

            return results.ToArray();
        }

        private async Task<string> BuildBatchValidationPromptAsync(
            List<ChapterInfo> batch, List<int> pendingIndices,
            List<string?> contents, List<ChapterValidationResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<batch_validation_task>");
            sb.AppendLine($"<batch_size>{pendingIndices.Count}</batch_size>");
            sb.AppendLine("请对以下每个章节分别执行校验，返回JSON数组，数组长度必须严格等于 batch_size，第i项对应第i个章节。");
            sb.AppendLine();

            for (int seq = 0; seq < pendingIndices.Count; seq++)
            {
                var idx = pendingIndices[seq];
                var r = results[idx];
                var content = contents[idx]!;
                sb.AppendLine($"<chapter index=\"{seq + 1}\">");
                sb.AppendLine($"<chapter_id>{r.ChapterId}</chapter_id>");
                sb.AppendLine($"<chapter_info>标题={r.ChapterTitle}, 卷={r.VolumeNumber}, 章={r.ChapterNumber}</chapter_info>");

                var contentGuide = await _guideContextService.GetContentGuideAsync();
                contentGuide.Chapters.TryGetValue(r.ChapterId, out var guideEntry);
                var contextIds = guideEntry?.ContextIds;
                if (contextIds != null)
                {
                    if (contextIds.Characters?.Count > 0)
                    {
                        var chars = await _guideContextService.ExtractCharactersAsync(contextIds.Characters);
                        sb.AppendLine($"<characters>{string.Join("; ", chars.Take(5).Select(c => $"{c.Name}({c.Identity})"))}</characters>");
                    }
                    if (contextIds.Factions?.Count > 0)
                    {
                        var factions = await _guideContextService.ExtractFactionsAsync(contextIds.Factions);
                        sb.AppendLine($"<factions>{string.Join("; ", factions.Take(5).Select(f => f.Name))}</factions>");
                    }
                    if (contextIds.PlotRules?.Count > 0)
                    {
                        var plots = await _guideContextService.ExtractPlotRulesAsync(contextIds.PlotRules);
                        sb.AppendLine($"<plot_rules>{string.Join("; ", plots.Take(3).Select(p => $"{p.Name}:{TruncateString(p.Goal, 30)}"))}</plot_rules>");
                    }
                }
                sb.AppendLine($"<正文内容>{(content.Length > ChapterPreviewLength ? content.Substring(0, ChapterPreviewLength) + "..." : content)}</正文内容>");
                sb.AppendLine("</chapter>");
                sb.AppendLine();
            }

            sb.AppendLine("<校验要求>");
            sb.AppendLine($"对每个章节执行{ValidationRules.TotalRuleCount}条校验规则，返回JSON数组，数组长度={pendingIndices.Count}，顺序与输入章节一致：");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"chapterId\": \"章节ID\",");
            sb.AppendLine("    \"overallResult\": \"通过|警告|失败|未校验\",");
            sb.AppendLine("    \"moduleResults\": " + BuildJsonTemplateForPrompt().Replace("\n", "\n    ").TrimEnd());
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine($"每个对象的 moduleResults 必须包含全部 {ValidationRules.TotalRuleCount} 条规则，moduleName 必须与模板一致，不得为 null 或省略。");
            sb.AppendLine($"重要：summary、reason、suggestion 字段中不得引用提示词中的标签名称（如正文内容、缺失数据说明等），只描述内容本身。");
            sb.AppendLine("</校验要求>");
            sb.AppendLine("</batch_validation_task>");
            return sb.ToString();
        }

        private void ParseBatchAIValidationResult(List<ChapterValidationResult> results, List<ChapterInfo> batch, string aiContent)
        {
            try
            {
                var arrStart = aiContent.IndexOf('[');
                var arrEnd = aiContent.LastIndexOf(']');
                if (arrStart < 0 || arrEnd <= arrStart)
                {
                    TM.App.Log("[UnifiedValidationService] 批量校验：AI返回中未找到JSON数组，降级逐项处理");
                    foreach (var r in results)
                        AddProtocolErrorIssue(r, "批量校验AI返回格式错误");
                    return;
                }

                var jsonStr = aiContent.Substring(arrStart, arrEnd - arrStart + 1);
                var arr = JsonDocument.Parse(jsonStr).RootElement;
                var elements = arr.EnumerateArray().ToList();

                for (int i = 0; i < results.Count; i++)
                {
                    if (i >= elements.Count)
                    {
                        AddProtocolErrorIssue(results[i], "批量校验AI返回数组长度不足");
                        continue;
                    }
                    var elem = elements[i];
                    if (elem.TryGetProperty("moduleResults", out var moduleResultsArray))
                        ParseNewProtocolResult(results[i], moduleResultsArray);
                    else
                        AddProtocolErrorIssue(results[i], "批量校验结果缺少moduleResults");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 批量校验结果解析失败: {ex.Message}");
                foreach (var r in results)
                    AddProtocolErrorIssue(r, $"批量解析失败: {ex.Message}");
            }
        }

        #endregion

        #region 抽样算法

        internal static int CalculateSampleCount(int totalCount)
        {
            var sample = (int)Math.Ceiling(totalCount / 5.0);
            return Math.Max(3, Math.Min(50, sample));
        }

        internal List<ChapterInfo> SampleChapters(List<ChapterInfo> chapters, int maxCount)
        {
            if (chapters == null || chapters.Count == 0)
                return new List<ChapterInfo>();

            if (chapters.Count <= maxCount)
                return chapters.ToList();

            var sampled = new List<ChapterInfo>();
            var totalCount = chapters.Count;

            var step = (double)(totalCount - 1) / (maxCount - 1);

            for (int i = 0; i < maxCount; i++)
            {
                var index = (int)Math.Round(i * step);
                index = Math.Min(index, totalCount - 1);

                if (!sampled.Contains(chapters[index]))
                {
                    sampled.Add(chapters[index]);
                }
            }

            if (!sampled.Contains(chapters[0]))
            {
                sampled.Insert(0, chapters[0]);
                if (sampled.Count > maxCount)
                    sampled.RemoveAt(sampled.Count - 1);
            }

            if (!sampled.Contains(chapters[totalCount - 1]))
            {
                if (sampled.Count >= maxCount)
                    sampled.RemoveAt(sampled.Count - 1);
                sampled.Add(chapters[totalCount - 1]);
            }

            return sampled.OrderBy(c => c.ChapterNumber).ToList();
        }

        #endregion

        #region 结果聚合

        private ValidationSummaryData AggregateToVolumeSummary(
            int volumeNumber,
            string volumeName,
            List<ChapterInfo> sampledChapters,
            List<ChapterValidationResult> chapterResults)
        {
            var moduleResults = new List<ModuleValidationResult>();

            foreach (var moduleName in ValidationRules.AllModuleNames)
            {
                var result = AggregateModuleResult(moduleName, chapterResults);
                moduleResults.Add(result);
            }

            var overallResult = CalculateOverallResult(moduleResults);

            return new ValidationSummaryData
            {
                Id = ShortIdGenerator.New("D"),
                Name = $"第{volumeNumber}卷校验",
                Icon = GetOverallResultIcon(overallResult),
                Category = $"第{volumeNumber}卷",
                TargetVolumeNumber = volumeNumber,
                TargetVolumeName = volumeName,
                SampledChapterCount = sampledChapters.Count,
                SampledChapterIds = sampledChapters.Select(c => c.Id).ToList(),
                LastValidatedTime = DateTime.Now,
                OverallResult = overallResult,
                ModuleResults = moduleResults,
                DependencyModuleVersions = GetCurrentDependencyVersions()
            };
        }

        private ModuleValidationResult AggregateModuleResult(
            string moduleName,
            List<ChapterValidationResult> chapterResults)
        {
            var allIssues = chapterResults
                .Where(c => c.IssuesByModule.ContainsKey(moduleName))
                .SelectMany(c => c.IssuesByModule[moduleName])
                .ToList();

            string aggregatedResult;
            if (allIssues.Any(i => i.Severity == "Error"))
                aggregatedResult = "失败";
            else if (allIssues.Any(i => i.Severity == "Warning"))
                aggregatedResult = "警告";
            else if (allIssues.Count == 0)
                aggregatedResult = "通过";
            else
                aggregatedResult = "警告";

            var problemItems = chapterResults
                .Where(c => c.IssuesByModule.ContainsKey(moduleName))
                .SelectMany(c => c.IssuesByModule[moduleName].Select(issue => new ProblemItem
                {
                    Summary = issue.Message,
                    Reason = issue.Type,
                    Details = !string.IsNullOrEmpty(issue.EntityName) ? $"相关实体: {issue.EntityName}" : null,
                    Suggestion = !string.IsNullOrEmpty(issue.Suggestion) ? issue.Suggestion : null,
                    ChapterId = c.ChapterId,
                    ChapterTitle = c.ChapterTitle
                }))
                .ToList();

            var extendedData = GenerateExtendedData(moduleName);

            return new ModuleValidationResult
            {
                ModuleName = moduleName,
                DisplayName = ValidationRules.GetDisplayName(moduleName),
                VerificationType = GetVerificationType(moduleName),
                Result = aggregatedResult,
                IssueDescription = GenerateIssueDescription(allIssues),
                FixSuggestion = GenerateFixSuggestion(allIssues),
                ExtendedDataJson = JsonSerializer.Serialize(extendedData),
                ProblemItemsJson = JsonSerializer.Serialize(problemItems)
            };
        }

        private string CalculateOverallResult(List<ModuleValidationResult> moduleResults)
        {
            if (moduleResults.Any(m => m.Result == "失败"))
                return "失败";
            if (moduleResults.Any(m => m.Result == "警告"))
                return "警告";
            if (moduleResults.All(m => m.Result == "通过"))
                return "通过";
            return "未校验";
        }

        private string GetOverallResultIcon(string overallResult)
        {
            return overallResult switch
            {
                "通过" => "✅",
                "警告" => "⚠️",
                "失败" => "❌",
                _ => "⏳"
            };
        }

        private string GenerateIssueDescription(List<ValidationIssue> issues)
        {
            if (issues.Count == 0)
                return string.Empty;

            var descriptions = issues
                .Select(i => i.Message)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .Take(3);

            return string.Join("; ", descriptions);
        }

        private string GenerateFixSuggestion(List<ValidationIssue> issues)
        {
            var suggestions = issues
                .Select(i => i.Suggestion)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Take(3);

            return string.Join("; ", suggestions);
        }

        private Dictionary<string, string> GenerateExtendedData(string moduleName)
        {
            var schema = ValidationRules.GetExtendedDataSchema(moduleName);
            var extendedData = new Dictionary<string, string>();

            foreach (var fieldName in schema)
            {
                var camelCaseName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
                extendedData[camelCaseName] = string.Empty;
            }

            return extendedData;
        }

        private string GetVerificationType(string moduleName)
        {
            return moduleName switch
            {
                "StyleConsistency" => "文风",
                "WorldviewConsistency" => "世界观",
                "CharacterConsistency" => "角色",
                "FactionConsistency" => "势力",
                "LocationConsistency" => "地点",
                "PlotConsistency" => "剧情",
                "OutlineConsistency" => "大纲",
                "ChapterPlanConsistency" => "章节规划",
                "BlueprintConsistency" => "章节蓝图",
                "VolumeDesignConsistency" => "分卷设计",
                _ => "通用"
            };
        }

        private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines, int max = 8)
        {
            var list = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(max)
                .ToList();
            if (list.Count == 0) return;

            sb.AppendLine($"<section name=\"{title}\">");
            foreach (var line in list)
            {
                sb.AppendLine($"- {line}");
            }
            sb.AppendLine($"</section>");
            sb.AppendLine();
        }

        private static string TruncateString(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private static Models.Generate.StrategicOutline.OutlineData? ResolveOutline(
            ValidationContext context,
            string? outlineId)
        {
            if (context.Generate?.Outline?.Outlines == null) return null;
            if (string.IsNullOrWhiteSpace(outlineId))
            {
                return null;
            }
            return context.Generate.Outline.Outlines.FirstOrDefault(o => o.Id == outlineId);
        }

        private static IEnumerable<string> BuildChapterPlanLines(
            ValidationContext context,
            Models.Guides.ContextIdCollection? contextIds)
        {
            if (contextIds == null || string.IsNullOrWhiteSpace(contextIds.ChapterPlanId))
            {
                return Enumerable.Empty<string>();
            }

            var chapterPlan = context.Generate?.Planning?.Chapters
                ?.FirstOrDefault(c => string.Equals(c.Id, contextIds.ChapterPlanId, StringComparison.Ordinal));
            if (chapterPlan == null)
            {
                return Enumerable.Empty<string>();
            }

            return new[]
            {
                $"标题={chapterPlan.ChapterTitle}",
                $"主题={TruncateString(chapterPlan.ChapterTheme, 60)}",
                $"主目标={TruncateString(chapterPlan.MainGoal, 60)}",
                $"关键转折={TruncateString(chapterPlan.KeyTurn, 60)}",
                $"结尾钩子={TruncateString(chapterPlan.Hook, 60)}",
                $"伏笔={TruncateString(chapterPlan.Foreshadowing, 60)}"
            };
        }

        private static IEnumerable<string> ResolveBlueprintItems(
            ValidationContext context,
            List<string>? blueprintIds)
        {
            if (context.Generate?.Blueprint?.Blueprints == null || blueprintIds == null || blueprintIds.Count == 0)
                return Enumerable.Empty<string>();

            var blueprintIdSet = new HashSet<string>(blueprintIds);
            return context.Generate.Blueprint.Blueprints
                .Where(b => blueprintIdSet.Contains(b.Id))
                .Take(5)
                .Select(b => $"结构={TruncateString(b.OneLineStructure, 60)}, 节奏={TruncateString(b.PacingCurve, 40)}, 角色={TruncateString(b.Cast, 40)}, 地点={TruncateString(b.Locations, 40)}");
        }

        private static Models.Generate.VolumeDesign.VolumeDesignData? ResolveVolumeDesign(
            ValidationContext context,
            string? volumeDesignId)
        {
            if (context.Generate?.VolumeDesign?.VolumeDesigns == null || string.IsNullOrWhiteSpace(volumeDesignId))
                return null;
            return context.Generate.VolumeDesign.VolumeDesigns
                .FirstOrDefault(v => string.Equals(v.Id, volumeDesignId, StringComparison.Ordinal));
        }

        private Dictionary<string, int> GetCurrentDependencyVersions()
        {
            return new Dictionary<string, int>
            {
                ["Design"] = _versionTrackingService.GetModuleVersion("Design"),
                ["Generate"] = _versionTrackingService.GetModuleVersion("Generate")
            };
        }

        #endregion

        #region 规则校验层（无AI，确定性）

        private const string StructuralModuleName = "StructuralConsistency";

        private async Task RunGateChecksAsync(
            ChapterValidationResult result,
            string chapterId,
            string chapterContent,
            TM.Services.Modules.ProjectData.Models.Guides.ContextIdCollection contextIds)
        {
            try
            {
                var protocol = _generationGate.ValidateChangesProtocol(chapterContent);
                if (!protocol.Success || protocol.Changes == null)
                {
                    TM.App.Log($"[UnifiedValidationService] {chapterId} 无 CHANGES 块，跳过规则层校验");
                    return;
                }

                var issues = new List<ValidationIssue>();

                var structResult = _generationGate.ValidateStructuralOnly(protocol.Changes);
                if (!structResult.Success)
                {
                    foreach (var desc in structResult.GetIssueDescriptions())
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = "StructuralRule",
                            Severity = "Error",
                            Message = desc
                        });
                    }
                    TM.App.Log($"[UnifiedValidationService] {chapterId} 结构性规则问题: {issues.Count} 条");
                }

                try
                {
                    var factSnapshot = await _guideContextService.ExtractFactSnapshotForChapterAsync(chapterId, contextIds);
                    if (factSnapshot != null)
                    {
                        var gateResult = await _generationGate.ValidateAsync(chapterId, chapterContent, factSnapshot);
                        if (!gateResult.Success)
                        {
                            var allFailures = gateResult.GetAllFailures();
                            foreach (var msg in allFailures)
                            {
                                if (issues.Any(i => i.Message == msg)) continue;
                                issues.Add(new ValidationIssue
                                {
                                    Type = "GateRule",
                                    Severity = "Warning",
                                    Message = msg
                                });
                            }
                            TM.App.Log($"[UnifiedValidationService] {chapterId} 门禁规则问题: {allFailures.Count} 条");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[UnifiedValidationService] {chapterId} FactSnapshot 加载失败（不影响 AI 校验）: {ex.Message}");
                }

                if (issues.Count > 0)
                    result.IssuesByModule[StructuralModuleName] = issues;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 规则层校验异常: {chapterId}, {ex.Message}");
            }
        }

        #endregion

        #region 校验逻辑

        private async Task ExecuteValidationsAsync(ChapterValidationResult result, ValidationContext context, string chapterContent, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                result.IssuesByModule.TryGetValue(StructuralModuleName, out var knownStructuralIssues);
                var prompt = await BuildValidationPromptAsync(result, context, chapterContent, knownStructuralIssues);
                var aiResult = await _aiService.GenerateAsync(prompt, ct);

                if (aiResult.Success && !string.IsNullOrEmpty(aiResult.Content))
                {
                    ParseAIValidationResult(result, aiResult.Content);
                    TM.App.Log($"[UnifiedValidationService] AI校验完成: {result.ChapterId}");
                }
                else
                {
                    TM.App.Log($"[UnifiedValidationService] AI校验失败: {aiResult.ErrorMessage}");
                    result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Type = "AIValidationFailed",
                            Severity = "Warning",
                            Message = "AI校验失败，未执行校验。"
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] AI校验异常: {ex.Message}");
                result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>
                {
                    new ValidationIssue
                    {
                        Type = "AIValidationException",
                        Severity = "Warning",
                        Message = $"AI校验异常：{ex.Message}，未执行校验。"
                    }
                };
            }
        }

        private async Task<string> BuildValidationPromptAsync(
            ChapterValidationResult result,
            ValidationContext context,
            string chapterContent,
            List<ValidationIssue>? knownStructuralIssues = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<validation_task>");
            sb.AppendLine();
            sb.AppendLine("<chapter_info>");
            sb.AppendLine($"- 章节ID: {result.ChapterId}");
            sb.AppendLine($"- 章节标题: {result.ChapterTitle}");
            sb.AppendLine($"- 卷号: {result.VolumeNumber}");
            sb.AppendLine($"- 章节号: {result.ChapterNumber}");
            sb.AppendLine($"- 卷名: {result.VolumeName}");
            sb.AppendLine("</chapter_info>");
            sb.AppendLine();

            var contentGuide = await _guideContextService.GetContentGuideAsync();
            contentGuide.Chapters.TryGetValue(result.ChapterId, out var guideEntry);
            var contextIds = guideEntry?.ContextIds;

            var templateItems = new List<string>();
            if (contextIds?.TemplateIds?.Count > 0)
            {
                var templates = await _guideContextService.ExtractTemplatesAsync(contextIds.TemplateIds);
                templateItems = templates
                    .Take(3)
                    .Select(t => $"{t.Name}: 类型={t.Genre}, 构思={TruncateString(t.OverallIdea, 60)}, 世界观构建={TruncateString(t.WorldBuildingMethod, 40)}, 主角塑造={TruncateString(t.ProtagonistDesign, 40)}")
                    .ToList();
            }
            AppendSection(sb, "创作模板（文风约束）", templateItems);

            var worldItems = new List<string>();
            if (contextIds?.WorldRuleIds?.Count > 0)
            {
                var worldRules = await _guideContextService.ExtractWorldRulesAsync(contextIds.WorldRuleIds);
                worldItems = worldRules
                    .Take(5)
                    .Select(w => $"{w.Name}: 硬规则={TruncateString(w.HardRules, 60)}, 力量体系={TruncateString(w.PowerSystem, 40)}")
                    .ToList();
            }
            AppendSection(sb, "世界观规则", worldItems);

            var characterItems = new List<string>();
            var chapterCharacters = new List<Models.Design.Characters.CharacterRulesData>();
            if (contextIds?.Characters?.Count > 0)
            {
                var characters = await _guideContextService.ExtractCharactersAsync(contextIds.Characters);
                chapterCharacters = characters;
                characterItems = characters
                    .Take(10)
                    .Select(c => $"{c.Name}: 身份={c.Identity}, 种族={c.Race}, 核心缺陷={TruncateString(c.FlawBelief, 30)}, 外在目标={TruncateString(c.Want, 30)}, 成长路径={TruncateString(c.GrowthPath, 30)}")
                    .ToList();
                AppendSection(sb, "角色设定（本章相关）", characterItems);
            }

            var factionItems = new List<string>();
            if (contextIds?.Factions?.Count > 0)
            {
                var factions = await _guideContextService.ExtractFactionsAsync(contextIds.Factions);
                var characterIdToName = chapterCharacters
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

                factionItems = factions
                    .Take(8)
                    .Select(f => $"{f.Name}: 类型={f.FactionType}, 目标={TruncateString(f.Goal, 40)}, 领袖={(string.IsNullOrWhiteSpace(f.Leader) ? string.Empty : (characterIdToName.TryGetValue(f.Leader, out var n) ? n : f.Leader))}")
                    .ToList();
                AppendSection(sb, "势力设定（本章相关）", factionItems);
            }

            var locationItems = new List<string>();
            if (contextIds?.Locations?.Count > 0)
            {
                var locations = await _guideContextService.ExtractLocationsAsync(contextIds.Locations);
                locationItems = locations
                    .Take(8)
                    .Select(l => $"{l.Name}: 类型={l.LocationType}, 描述={TruncateString(l.Description, 40)}, 地形={TruncateString(l.Terrain, 30)}")
                    .ToList();
                AppendSection(sb, "地点设定（本章相关）", locationItems);
            }

            var plotItems = new List<string>();
            if (contextIds?.PlotRules?.Count > 0)
            {
                var plotRules = await _guideContextService.ExtractPlotRulesAsync(contextIds.PlotRules);
                plotItems = plotRules
                    .Take(8)
                    .Select(p => $"{p.Name}: 阶段={p.StoryPhase}, 目标={TruncateString(p.Goal, 40)}, 冲突={TruncateString(p.Conflict, 40)}, 结果={TruncateString(p.Result, 40)}")
                    .ToList();
                AppendSection(sb, "剧情规则（本章相关）", plotItems);
            }

            var outline = ResolveOutline(context, contextIds?.VolumeOutline);
            if (outline != null)
            {
                AppendSection(sb, "全书大纲", new[]
                {
                    $"一句话大纲={TruncateString(outline.OneLineOutline, 80)}",
                    $"核心冲突={TruncateString(outline.CoreConflict, 60)}",
                    $"主题={TruncateString(outline.Theme, 60)}",
                    $"结局状态={TruncateString(outline.EndingState, 60)}"
                });
            }

            var chapterPlanLines = BuildChapterPlanLines(context, contextIds).ToList();
            AppendSection(sb, "章节规划", chapterPlanLines);

            var blueprintItems = ResolveBlueprintItems(context, contextIds?.BlueprintIds).ToList();
            AppendSection(sb, "章节蓝图", blueprintItems);

            var volumeDesign = ResolveVolumeDesign(context, contextIds?.VolumeDesignId);
            if (volumeDesign != null)
            {
                AppendSection(sb, "分卷设计", new[]
                {
                    $"卷标题={volumeDesign.VolumeTitle}",
                    $"卷主题={TruncateString(volumeDesign.VolumeTheme, 60)}",
                    $"阶段目标={TruncateString(volumeDesign.StageGoal, 60)}",
                    $"主冲突={TruncateString(volumeDesign.MainConflict, 60)}",
                    $"关键事件={TruncateString(volumeDesign.KeyEvents, 60)}"
                });
            }

            var missingRules = new List<string>();
            if (templateItems.Count == 0) missingRules.Add("StyleConsistency");
            if (worldItems.Count == 0) missingRules.Add("WorldviewConsistency");
            if (characterItems.Count == 0) missingRules.Add("CharacterConsistency");
            if (factionItems.Count == 0) missingRules.Add("FactionConsistency");
            if (locationItems.Count == 0) missingRules.Add("LocationConsistency");
            if (plotItems.Count == 0) missingRules.Add("PlotConsistency");
            if (outline == null) missingRules.Add("OutlineConsistency");
            if (chapterPlanLines.Count == 0) missingRules.Add("ChapterPlanConsistency");
            if (blueprintItems.Count == 0) missingRules.Add("BlueprintConsistency");
            if (volumeDesign == null) missingRules.Add("VolumeDesignConsistency");

            if (missingRules.Count > 0)
            {
                sb.AppendLine("<缺失数据说明>");
                sb.AppendLine("以下规则缺少对应数据，请将 result 填写为\"未校验\"（系统按警告处理），problemItems 可为空：");
                foreach (var rule in missingRules.Distinct())
                {
                    sb.AppendLine($"- {rule}（{ValidationRules.GetDisplayName(rule)}）");
                }
                sb.AppendLine("</缺失数据说明>");
                sb.AppendLine();
            }

            sb.AppendLine("<正文内容>");
            sb.AppendLine(chapterContent.Length > ChapterPreviewLength
                ? chapterContent.Substring(0, ChapterPreviewLength) + "..."
                : chapterContent);
            sb.AppendLine("</正文内容>");
            sb.AppendLine();

            if (knownStructuralIssues != null && knownStructuralIssues.Count > 0)
            {
                sb.AppendLine("<已确认结构性问题>");
                foreach (var issue in knownStructuralIssues)
                    sb.AppendLine($"- [{issue.Type}] {issue.Message}");
                sb.AppendLine("</已确认结构性问题>");
                sb.AppendLine("以上结构性问题已由规则层确认。你的任务是专注于设计数据的语义一致性（10条规则），不要重复检查上述已确认问题。");
                sb.AppendLine();
            }

            sb.AppendLine("<校验要求>");
            sb.AppendLine($"请对章节执行{ValidationRules.TotalRuleCount}条校验规则，返回JSON格式的校验结果。");
            sb.AppendLine($"1. moduleResults必须输出完整规则清单（{ValidationRules.TotalRuleCount}项），缺失项视为协议错误");
            sb.AppendLine("2. extendedData为每个规则的差异字段容器，内容允许为空但不允许缺字段名");
            sb.AppendLine("3. 当result为警告/失败/未校验时，problemItems至少1条（未校验可说明原因）");
            sb.AppendLine("4. 当result为通过时，problemItems允许为空数组");
            sb.AppendLine("5. 重要：summary、reason、suggestion 字段中不得引用提示词中的标签名称（如 正文内容、缺失数据说明、已确认结构性问题、校验要求 等），只描述内容本身。");
            sb.AppendLine();
            sb.AppendLine("返回JSON格式：");
            sb.AppendLine("```json");
            sb.AppendLine(BuildJsonTemplateForPrompt());
            sb.AppendLine("```");
            sb.AppendLine("</校验要求>");
            sb.AppendLine();
            sb.AppendLine("<validation_rules_description>");
            sb.AppendLine(BuildRulesDescription());
            sb.AppendLine("</validation_rules_description>");
            sb.AppendLine();
            sb.AppendLine("</validation_task>");

            return sb.ToString();
        }

        private string BuildJsonTemplateForPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"overallResult\": \"通过|警告|失败|未校验\",");
            sb.AppendLine("  \"moduleResults\": [");

            for (int i = 0; i < ValidationRules.AllModuleNames.Length; i++)
            {
                var moduleName = ValidationRules.AllModuleNames[i];
                var displayName = ValidationRules.GetDisplayName(moduleName);
                var verificationType = GetVerificationType(moduleName);
                var fields = ValidationRules.GetExtendedDataSchema(moduleName);

                sb.AppendLine("    {");
                sb.AppendLine($"      \"moduleName\": \"{moduleName}\",");
                sb.AppendLine($"      \"displayName\": \"{displayName}\",");
                sb.AppendLine($"      \"verificationType\": \"{verificationType}\",");
                sb.AppendLine("      \"result\": \"通过|警告|失败|未校验\",");
                sb.AppendLine("      \"issueDescription\": \"问题描述（可空）\",");
                sb.AppendLine("      \"fixSuggestion\": \"修复建议（可空）\",");
                sb.AppendLine("      \"extendedData\": {");

                for (int f = 0; f < fields.Length; f++)
                {
                    var field = fields[f];
                    var camel = char.ToLowerInvariant(field[0]) + field.Substring(1);
                    var suffix = f == fields.Length - 1 ? string.Empty : ",";
                    sb.AppendLine($"        \"{camel}\": \"\"{suffix}");
                }

                sb.AppendLine("      },");
                sb.AppendLine("      \"problemItems\": [");
                sb.AppendLine("        {");
                sb.AppendLine("          \"summary\": \"问题简述\",");
                sb.AppendLine("          \"reason\": \"原因依据\",");
                sb.AppendLine("          \"details\": \"补充详情（可选）\",");
                sb.AppendLine("          \"suggestion\": \"修复建议（可选）\"");
                sb.AppendLine("        }");
                sb.AppendLine("      ]");
                sb.Append("    }");
                sb.AppendLine(i == ValidationRules.AllModuleNames.Length - 1 ? string.Empty : ",");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string BuildRulesDescription()
        {
            var sb = new StringBuilder();
            sb.AppendLine("1. StyleConsistency（文风模板一致性）：对齐创作模板文风/类型/构思");
            sb.AppendLine("   - extendedData: templateName, genre, overallIdea, styleHint");
            sb.AppendLine("2. WorldviewConsistency（世界观一致性）：对齐硬规则/力量体系/特殊法则");
            sb.AppendLine("   - extendedData: worldRuleName, hardRules, powerSystem, specialLaws");
            sb.AppendLine("3. CharacterConsistency（角色设定一致性）：对齐身份/特质/弧光目标");
            sb.AppendLine("   - extendedData: characterName, identity, coreTraits, arcGoal");
            sb.AppendLine("4. FactionConsistency（势力设定一致性）：对齐势力类型/目标/领袖");
            sb.AppendLine("   - extendedData: factionName, factionType, goal, leader");
            sb.AppendLine("5. LocationConsistency（地点设定一致性）：对齐地点类型/描述/地形");
            sb.AppendLine("   - extendedData: locationName, locationType, description, terrain");
            sb.AppendLine("6. PlotConsistency（剧情规则一致性）：对齐剧情阶段/目标/冲突/结果");
            sb.AppendLine("   - extendedData: plotName, storyPhase, goal, conflict, result");
            sb.AppendLine("7. OutlineConsistency（大纲一致性）：对齐一句话大纲/核心冲突/主题/结局");
            sb.AppendLine("   - extendedData: oneLineOutline, coreConflict, theme, endingState");
            sb.AppendLine("8. ChapterPlanConsistency（章节规划一致性）：对齐本章目标/转折/伏笔");
            sb.AppendLine("   - extendedData: chapterTitle, mainGoal, keyTurn, hook, foreshadowing");
            sb.AppendLine("9. BlueprintConsistency（章节蓝图一致性）：对齐结构/节奏/角色地点清单");
            sb.AppendLine("   - extendedData: chapterId, oneLineStructure, pacingCurve, cast, locations");
            sb.AppendLine("10. VolumeDesignConsistency（分卷设计一致性）：对齐卷主题/阶段目标/主冲突/关键事件");
            sb.AppendLine("   - extendedData: volumeTitle, volumeTheme, stageGoal, mainConflict, keyEvents");

            return sb.ToString();
        }

        private void ParseAIValidationResult(ChapterValidationResult result, string aiContent)
        {
            try
            {
                var jsonStart = aiContent.IndexOf('{');
                var jsonEnd = aiContent.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                {
                    TM.App.Log("[UnifiedValidationService] AI返回内容中未找到有效JSON");
                    AddProtocolErrorIssue(result, "AI返回内容中未找到有效JSON");
                    return;
                }

                var jsonStr = aiContent.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var doc = JsonDocument.Parse(jsonStr);

                if (!doc.RootElement.TryGetProperty("moduleResults", out var moduleResultsArray))
                {
                    TM.App.Log("[UnifiedValidationService] AI返回JSON中未找到moduleResults字段");
                    AddProtocolErrorIssue(result, "AI返回JSON中未找到moduleResults字段");
                    return;
                }

                var moduleCount = moduleResultsArray.GetArrayLength();
                if (moduleCount != ValidationRules.TotalRuleCount)
                {
                    TM.App.Log($"[UnifiedValidationService] AI协议错误：moduleResults应为{ValidationRules.TotalRuleCount}项，实际为{moduleCount}项");
                    AddProtocolErrorIssue(result, $"moduleResults应为{ValidationRules.TotalRuleCount}项，实际为{moduleCount}项");
                }

                ParseNewProtocolResult(result, moduleResultsArray);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 解析AI校验结果失败: {ex.Message}");
                AddProtocolErrorIssue(result, $"解析AI校验结果失败: {ex.Message}");
            }
        }

        private void AddProtocolErrorIssue(ChapterValidationResult result, string message)
        {
            if (!result.IssuesByModule.ContainsKey(SystemModuleName))
            {
                result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>();
            }

            result.IssuesByModule[SystemModuleName].Add(new ValidationIssue
            {
                Type = "ProtocolError",
                Severity = "Warning",
                Message = $"AI协议错误：{message}"
            });
        }

        private void ParseNewProtocolResult(ChapterValidationResult result, JsonElement moduleResultsArray)
        {
            var parsedModuleNames = new HashSet<string>();

            foreach (var moduleElement in moduleResultsArray.EnumerateArray())
            {
                var moduleName = moduleElement.TryGetProperty("moduleName", out var mn) 
                    ? mn.GetString() ?? "Unknown" 
                    : "Unknown";

                if (!ValidationRules.AllModuleNames.Contains(moduleName))
                {
                    TM.App.Log($"[UnifiedValidationService] AI协议错误：未知的moduleName: {moduleName}");
                    AddProtocolErrorIssue(result, $"未知的moduleName: {moduleName}");
                    continue;
                }

                parsedModuleNames.Add(moduleName);

                var moduleResult = moduleElement.TryGetProperty("result", out var r) 
                    ? r.GetString() ?? "未校验" 
                    : "未校验";

                if (moduleResult != "通过")
                {
                    var issues = new List<ValidationIssue>();
                    var severity = moduleResult == "失败" ? "Error" : "Warning";

                    if (moduleElement.TryGetProperty("problemItems", out var problemItems))
                    {
                        foreach (var item in problemItems.EnumerateArray())
                        {
                            var issue = new ValidationIssue
                            {
                                Type = item.TryGetProperty("reason", out var reason) 
                                    ? reason.GetString() ?? "" 
                                    : "",
                                Severity = severity,
                                Message = item.TryGetProperty("summary", out var summary) 
                                    ? summary.GetString() ?? "" 
                                    : "",
                                Suggestion = item.TryGetProperty("suggestion", out var sug) 
                                    ? sug.GetString() ?? "" 
                                    : "",
                                EntityName = ""
                            };
                            issues.Add(issue);
                        }
                    }

                    if (issues.Count == 0)
                    {
                        var issueDesc = moduleElement.TryGetProperty("issueDescription", out var desc) 
                            ? desc.GetString() ?? "" 
                            : "";
                        var fixSug = moduleElement.TryGetProperty("fixSuggestion", out var fix) 
                            ? fix.GetString() ?? "" 
                            : "";

                        var defaultMessage = moduleResult == "未校验"
                            ? $"规则未校验：{ValidationRules.GetDisplayName(moduleName)}"
                            : !string.IsNullOrEmpty(issueDesc) ? issueDesc : $"{moduleName}校验{moduleResult}";

                        issues.Add(new ValidationIssue
                        {
                            Type = moduleResult == "未校验" ? "UnvalidatedRule" : "ValidationIssue",
                            Severity = severity,
                            Message = defaultMessage,
                            Suggestion = string.IsNullOrWhiteSpace(fixSug)
                                ? (moduleResult == "未校验" ? "补齐对应数据后再执行校验" : string.Empty)
                                : fixSug
                        });
                    }

                    result.IssuesByModule[moduleName] = issues;
                }
            }

            var missingModules = ValidationRules.AllModuleNames.Except(parsedModuleNames).ToList();
            if (missingModules.Count > 0)
            {
                TM.App.Log($"[UnifiedValidationService] AI协议错误：缺失模块: {string.Join(", ", missingModules)}");
                AddProtocolErrorIssue(result, $"缺失模块: {string.Join(", ", missingModules)}");
            }
        }

        #endregion

        #region 辅助方法

        private string BuildRulesSignature()
        {
            var sb = new StringBuilder();
            foreach (var moduleName in ValidationRules.AllModuleNames)
            {
                sb.Append(moduleName).Append(':');
                foreach (var field in ValidationRules.GetExtendedDataSchema(moduleName))
                {
                    sb.Append(field).Append(',');
                }
                sb.Append('|');
            }

            return ComputeHash(sb.ToString());
        }

        private static string ComputeHash(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash)[..16];
        }

        private void EnsurePackagedDataOrThrow()
        {
            var manifest = _publishService.GetManifest();
            if (manifest == null)
            {
                throw new InvalidOperationException("未找到打包数据，请先执行打包");
            }

            var currentSourceBookId = _workScopeService.CurrentSourceBookId;
            if (!string.IsNullOrEmpty(manifest.SourceBookId)
                && !string.IsNullOrEmpty(currentSourceBookId)
                && !string.Equals(manifest.SourceBookId, currentSourceBookId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"当前Scope与打包数据不一致：current={currentSourceBookId}, packaged={manifest.SourceBookId}。请切换到对应来源拆书或重新打包。");
            }

            var designPath = StoragePathHelper.GetProjectConfigPath("Design");
            var generatePath = StoragePathHelper.GetProjectConfigPath("Generate");

            var hasAnyDesign = Directory.Exists(designPath) && Directory.EnumerateFiles(designPath, "*.json", SearchOption.TopDirectoryOnly).Any();
            var hasAnyGenerate = Directory.Exists(generatePath) && Directory.EnumerateFiles(generatePath, "*.json", SearchOption.TopDirectoryOnly).Any();

            if (!hasAnyDesign && !hasAnyGenerate)
            {
                throw new InvalidOperationException("当前没有用于校验的数据，请进行打包");
            }
        }

        private async Task<string> GetVolumeNameAsync(int volumeNumber)
        {
            var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
            await volumeService.InitializeAsync();
            var volume = volumeService.GetAllVolumeDesigns().FirstOrDefault(v => v.VolumeNumber == volumeNumber);
            var name = volumeNumber > 0
                ? $"第{volumeNumber}卷 {volume?.VolumeTitle}".Trim()
                : volume?.Name;
            return string.IsNullOrWhiteSpace(name) ? $"第{volumeNumber}卷" : name;
        }

        private string ExtractChapterTitle(string content)
        {
            if (string.IsNullOrEmpty(content)) return "未命名章节";

            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# "))
                    return trimmed.Substring(2).Trim();
                if (trimmed.StartsWith("## "))
                    return trimmed.Substring(3).Trim();
            }

            return "未命名章节";
        }

        private string DetermineOverallResult(ChapterValidationResult result)
        {
            if (result.HasErrors) return "失败";
            if (result.HasWarnings) return "警告";
            if (result.TotalIssueCount > 0) return "警告";
            return "通过";
        }

        private ChapterValidationResult CreateErrorResult(string chapterId, string message, int volumeNumber = 0, int chapterNumber = 0, string volumeName = "")
        {
            return new ChapterValidationResult
            {
                ChapterId = chapterId,
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                VolumeName = volumeName,
                OverallResult = "失败",
                ValidatedTime = DateTime.Now,
                IssuesByModule = new Dictionary<string, List<ValidationIssue>>
                {
                    ["System"] = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Type = "SystemError",
                            Severity = "Error",
                            Message = message
                        }
                    }
                }
            };
        }

        private string FormatIssues(List<ValidationIssue> issues)
        {
            if (!issues.Any()) return string.Empty;
            return string.Join("\n", issues.Select(i => $"[{i.Severity}] {i.Message}"));
        }

        private string FormatSuggestions(List<ValidationIssue> issues)
        {
            var suggestions = issues.Where(i => !string.IsNullOrEmpty(i.Suggestion))
                                    .Select(i => i.Suggestion)
                                    .Distinct();
            return string.Join("\n", suggestions);
        }

        #endregion
    }
}
