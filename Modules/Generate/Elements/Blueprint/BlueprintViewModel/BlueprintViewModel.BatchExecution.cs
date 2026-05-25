using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;

namespace TM.Modules.Generate.Elements.Blueprint
{
    public partial class BlueprintViewModel
    {
        protected override async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            await base.ExecuteBatchAIGenerateAsync(config);

            var fullIds = _batchFullChapterIds;
            if (fullIds == null || fullIds.Count == 0) return;

            var validSet = new HashSet<string>(fullIds, StringComparer.OrdinalIgnoreCase);

            var tail = Service.GetAllBlueprints()
                .Where(b => string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal))
                .Where(b => !validSet.Contains(b.ChapterId))
                .ToList();

            foreach (var b in tail)
            {
                Service.DeleteBlueprint(b.Id);
                TM.App.Log($"[BlueprintViewModel] 清尾: 删除蓝图 {b.ChapterId}（不在有效集合内）");
            }
            if (tail.Count > 0)
                TM.App.Log($"[BlueprintViewModel] 清尾完成: 删除 {tail.Count} 个旧蓝图");

            if (_lastBatchStoppedBySlotExhausted)
            {
                TM.App.Log("[BlueprintViewModel] 批量生成已因重试耗尽或连续失败而停止：跳过补缺占位，等待下次续跑");
            }
            else if (_lastBatchWasCancelled)
            {
                var shells = Service.GetAllBlueprints()
                    .Where(b => string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal)
                        && string.IsNullOrWhiteSpace(CleanBlueprintSceneTitle(b.SceneTitle))
                        && string.IsNullOrWhiteSpace(b.OneLineStructure)
                        && string.IsNullOrWhiteSpace(b.PacingCurve)
                        && string.IsNullOrWhiteSpace(b.PovCharacter)
                        && string.IsNullOrWhiteSpace(b.Opening)
                        && string.IsNullOrWhiteSpace(b.Development)
                        && string.IsNullOrWhiteSpace(b.Turning)
                        && string.IsNullOrWhiteSpace(b.Ending)
                        && string.IsNullOrWhiteSpace(b.InfoDrop)
                        && string.IsNullOrWhiteSpace(b.ItemsClues)
                        && string.IsNullOrWhiteSpace(b.Cast)
                        && string.IsNullOrWhiteSpace(b.Locations)
                        && string.IsNullOrWhiteSpace(b.Factions))
                    .ToList();
                foreach (var shell in shells)
                {
                    Service.DeleteBlueprint(shell.Id);
                    TM.App.Log($"[BlueprintViewModel] 取消清理: 删除空壳蓝图 {shell.ChapterId}");
                }
                if (shells.Count > 0)
                    GlobalToast.Info("取消清理", $"已清理 {shells.Count} 个未完成的空壳蓝图，下次批量生成会按需续接");
            }
            else
            {
                var placeholderCreated = 0;
                foreach (var chId in fullIds)
                {
                    var existing = Service.GetAllBlueprints()
                        .FirstOrDefault(b => string.Equals(b.ChapterId, chId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(b.Category, config.CategoryName, StringComparison.Ordinal));

                    if (existing == null)
                    {
                        var data = new BlueprintData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = $"蓝图_{chId}",
                            Category = config.CategoryName,
                            IsEnabled = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ChapterId = chId,
                            SceneTitle = $"蓝图_{chId}",
                        };
                        await Service.AddBlueprintAsync(data);
                        placeholderCreated++;
                        if (TM.App.IsDebugMode)
                            TM.App.Log($"[BlueprintViewModel] 补缺: {chId}（AI未生成，创建占位）");
                    }
                }

                if (!TM.App.IsDebugMode && placeholderCreated > 0)
                    TM.App.Log($"[BlueprintViewModel] 补缺完成: 已创建占位 {placeholderCreated} 个（AI未生成）");
            }

            _batchFullChapterIds = null;
            _batchPreCalculatedChapterIds = null;
            _currentBatchChapterIds = null;
            _currentBatchChapterIdsAll = null;

            RefreshTreeData();
        }

        protected override async System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
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

                        string chapterId;
                        int sceneNumber;
                        if (entity.TryGetValue(SystemChapterIdKey, out var sysChId) && sysChId is string stampedId && !string.IsNullOrWhiteSpace(stampedId))
                        {
                            chapterId = stampedId;
                            sceneNumber = ResolveSceneNumberForChapterId(chapterId);
                        }
                        else
                        {
                            chapterId = MatchChapterId(reader.GetString("ChapterId"));
                            sceneNumber = reader.GetInt("SceneNumber");
                        }

                        var existing = Service.GetAllBlueprints()
                            .FirstOrDefault(b => string.Equals(b.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(b.Category, categoryName, StringComparison.Ordinal));

                        var (mergedCast, mergedLocs, mergedFacs, mergedPov) = await NormalizeBlueprintEntitiesAsync(
                            chapterId, categoryName,
                            reader.GetString("Cast"),
                            reader.GetString("Locations"),
                            reader.GetString("Factions"),
                            reader.GetString("PovCharacter"));

                        if (existing != null)
                        {
                            var aiName = reader.GetString("Name");
                            var aiSt = reader.GetString("SceneTitle");
                            var cleanedSceneTitle = CleanBlueprintSceneTitle(aiSt);
                            var cleanedName = CleanBlueprintSceneTitle(aiName);
                            if (!string.IsNullOrWhiteSpace(cleanedSceneTitle))
                                existing.SceneTitle = cleanedSceneTitle;
                            else if (!string.IsNullOrWhiteSpace(cleanedName))
                                existing.SceneTitle = cleanedName;
                            if (!string.IsNullOrWhiteSpace(cleanedName))
                                existing.Name = cleanedName;
                            else if (!string.IsNullOrWhiteSpace(cleanedSceneTitle))
                                existing.Name = cleanedSceneTitle;
                            var aiOls = reader.GetString("OneLineStructure");
                            if (!string.IsNullOrWhiteSpace(aiOls)) existing.OneLineStructure = aiOls;
                            var aiPc = reader.GetString("PacingCurve");
                            if (!string.IsNullOrWhiteSpace(aiPc)) existing.PacingCurve = aiPc;
                            if (!string.IsNullOrWhiteSpace(mergedPov)) existing.PovCharacter = mergedPov;
                            var aiOp = reader.GetString("Opening");
                            if (!string.IsNullOrWhiteSpace(aiOp)) existing.Opening = aiOp;
                            var aiDev = reader.GetString("Development");
                            if (!string.IsNullOrWhiteSpace(aiDev)) existing.Development = aiDev;
                            var aiTrn = reader.GetString("Turning");
                            if (!string.IsNullOrWhiteSpace(aiTrn)) existing.Turning = aiTrn;
                            var aiEnd = reader.GetString("Ending");
                            if (!string.IsNullOrWhiteSpace(aiEnd)) existing.Ending = aiEnd;
                            var aiInf = reader.GetString("InfoDrop");
                            if (!string.IsNullOrWhiteSpace(aiInf)) existing.InfoDrop = aiInf;
                            var aiItm = reader.GetString("ItemsClues");
                            if (!string.IsNullOrWhiteSpace(aiItm)) existing.ItemsClues = aiItm;
                            if (!string.IsNullOrWhiteSpace(mergedCast)) existing.Cast = mergedCast;
                            if (!string.IsNullOrWhiteSpace(mergedLocs)) existing.Locations = mergedLocs;
                            if (!string.IsNullOrWhiteSpace(mergedFacs)) existing.Factions = mergedFacs;

                            var chFallbackMatch = VolChIdRegex.Match(chapterId);
                            if (chFallbackMatch.Success)
                            {
                                var fbVolNum = int.Parse(chFallbackMatch.Groups[1].Value);
                                var fbChNum = int.Parse(chFallbackMatch.Groups[2].Value);
                                var chapterForFallback = _chapterService.GetAllChapters()
                                    .FirstOrDefault(c => c.IsEnabled && c.ChapterNumber == fbChNum
                                        && ExtractVolumeNumber(c.Category) == fbVolNum);
                                if (chapterForFallback != null)
                                {
                                    if (string.IsNullOrWhiteSpace(existing.PovCharacter) && chapterForFallback.ReferencedCharacterNames.Count > 0)
                                        existing.PovCharacter = await BlueprintResolveCharacterAsync(chapterForFallback.ReferencedCharacterNames[0]);
                                    if (string.IsNullOrWhiteSpace(existing.Cast) && chapterForFallback.ReferencedCharacterNames.Count > 0)
                                        existing.Cast = await BlueprintResolveCharactersAsync(string.Join("、", chapterForFallback.ReferencedCharacterNames));
                                    if (string.IsNullOrWhiteSpace(existing.Locations) && chapterForFallback.ReferencedLocationNames.Count > 0)
                                        existing.Locations = await BlueprintResolveLocationsAsync(string.Join("、", chapterForFallback.ReferencedLocationNames));
                                    if (string.IsNullOrWhiteSpace(existing.Factions) && chapterForFallback.ReferencedFactionNames.Count > 0)
                                        existing.Factions = await BlueprintResolveFactionsAsync(string.Join("、", chapterForFallback.ReferencedFactionNames));
                                }
                            }

                            existing.ChapterId = chapterId;
                            existing.SceneNumber = sceneNumber;
                            existing.DependencyModuleVersions = versionSnapshot ?? new();
                            existing.UpdatedAt = DateTime.Now;
                            await Service.UpdateBlueprintAsync(existing);
                            entity["SceneNumber"] = sceneNumber;
                            TM.App.Log($"[BlueprintViewModel] Upsert更新: {chapterId}");
                        }
                        else
                        {
                            var name = reader.GetString("Name");
                            if (string.IsNullOrWhiteSpace(name)) name = $"蓝图_{chapterId}";
                            var title = reader.GetString("SceneTitle");
                            if (string.IsNullOrWhiteSpace(title)) title = name;

                            var data = new BlueprintData
                            {
                                Id = ShortIdGenerator.New("D"),
                                Name = CleanBlueprintSceneTitle(name),
                                Category = categoryName,
                                IsEnabled = true,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                                ChapterId = chapterId,
                                SceneNumber = sceneNumber,
                                OneLineStructure = reader.GetString("OneLineStructure"),
                                PacingCurve = reader.GetString("PacingCurve"),
                                SceneTitle = CleanBlueprintSceneTitle(title),
                                PovCharacter = mergedPov,
                                Opening = reader.GetString("Opening"),
                                Development = reader.GetString("Development"),
                                Turning = reader.GetString("Turning"),
                                Ending = reader.GetString("Ending"),
                                InfoDrop = reader.GetString("InfoDrop"),
                                Cast = mergedCast,
                                Locations = mergedLocs,
                                Factions = mergedFacs,
                                ItemsClues = reader.GetString("ItemsClues"),
                                DependencyModuleVersions = versionSnapshot ?? new()
                            };

                            var chNewFbMatch = VolChIdRegex.Match(chapterId);
                            if (chNewFbMatch.Success)
                            {
                                var newFbVolNum = int.Parse(chNewFbMatch.Groups[1].Value);
                                var newFbChNum = int.Parse(chNewFbMatch.Groups[2].Value);
                                var chapterFallback = _chapterService.GetAllChapters()
                                    .FirstOrDefault(c => c.IsEnabled && c.ChapterNumber == newFbChNum
                                        && ExtractVolumeNumber(c.Category) == newFbVolNum);
                                if (chapterFallback != null)
                                {
                                    if (string.IsNullOrWhiteSpace(data.PovCharacter) && chapterFallback.ReferencedCharacterNames.Count > 0)
                                        data.PovCharacter = await BlueprintResolveCharacterAsync(chapterFallback.ReferencedCharacterNames[0]);
                                    if (string.IsNullOrWhiteSpace(data.Cast) && chapterFallback.ReferencedCharacterNames.Count > 0)
                                        data.Cast = await BlueprintResolveCharactersAsync(string.Join("、", chapterFallback.ReferencedCharacterNames));
                                    if (string.IsNullOrWhiteSpace(data.Locations) && chapterFallback.ReferencedLocationNames.Count > 0)
                                        data.Locations = await BlueprintResolveLocationsAsync(string.Join("、", chapterFallback.ReferencedLocationNames));
                                    if (string.IsNullOrWhiteSpace(data.Factions) && chapterFallback.ReferencedFactionNames.Count > 0)
                                        data.Factions = await BlueprintResolveFactionsAsync(string.Join("、", chapterFallback.ReferencedFactionNames));
                                }
                            }
                            entity["SceneNumber"] = sceneNumber;
                            entity["SceneTitle"] = CleanBlueprintSceneTitle(title);
                            await Service.AddBlueprintAsync(data);
                            TM.App.Log($"[BlueprintViewModel] Upsert新建: {chapterId}");
                        }

                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BlueprintViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[BlueprintViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }

        protected override void OnTreeDataRefreshed()
        {
            if (_chapterService == null)
                return;

            LoadAvailableEntities();

            if (string.IsNullOrWhiteSpace(FormChapterId))
            {
                FormChapterId = GetDefaultChapterId();
            }
            else
            {
                FormChapterId = MatchChapterId(FormChapterId);
            }
        }

        private string GetDefaultChapterId()
        {
            var first = AvailableChapterIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            return first ?? string.Empty;
        }

        private static int TryParseChapterNumberFromChapterId(string? chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId)) return 0;
            var m = ChapterSuffixNumRegex.Match(chapterId.Trim());
            if (!m.Success) return 0;
            return int.TryParse(m.Groups["ch"].Value, out var ch) ? ch : 0;
        }

        private string? TryResolveChapterTitle(int chapterNumber)
        {
            if (chapterNumber <= 0) return null;
            if (_chapterService == null) return null;
            try
            {
                var all = _chapterService.GetAllChapters()
                    .Where(c => c.IsEnabled)
                    .FirstOrDefault(c => c.ChapterNumber == chapterNumber);

                return all == null ? null : NormalizeChapterTitle(all.ChapterTitle);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeChapterTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = NormChVolArabicRegex.Replace(t, string.Empty);
            t = NormChVolChineseRegex.Replace(t, string.Empty);
            t = NormChArabicRegex.Replace(t, string.Empty);
            t = NormChChineseRegex.Replace(t, string.Empty);
            return t.Trim();
        }

        private static string CleanBlueprintSceneTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var t = title.Trim();
            t = CleanNumRangeRegex.Replace(t, string.Empty);
            t = CleanVolChStrRegex.Replace(t, string.Empty);
            t = CleanChPrefixRegex.Replace(t, string.Empty);
            t = CleanSceneBpRegex.Replace(t, string.Empty);
            t = CleanSceneNumRegex.Replace(t, string.Empty);
            t = CleanVolChArabicRegex.Replace(t, string.Empty);
            t = CleanVolChChineseRegex.Replace(t, string.Empty);
            t = CleanSceneRefRegex.Replace(t, " ");
            t = CleanVolRefRegex.Replace(t, " ");
            t = CleanVolNumRegex.Replace(t, " ");
            t = CleanChNumRegex.Replace(t, " ");
            t = t.Replace("__", " ").Replace("--", " ");
            t = t.Trim(' ', '-', '_');
            return t.Trim();
        }

        protected override void OnTreeAfterAction(string? action)
        {
            if (action == "Reorder")
            {
                return;
            }

            base.OnTreeAfterAction(action);
        }

        private void OnVolumeDataChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RefreshTreeAndCategorySelection();
                    UpdateBulkToggleState();
                    LoadAvailableEntities();

                    if (!string.IsNullOrWhiteSpace(FormCategory))
                    {
                        var categories = GetAllCategoriesFromService() ?? new List<BlueprintCategory>();
                        if (!categories.Any(c => string.Equals(c.Name, FormCategory, StringComparison.Ordinal)))
                        {
                            FormCategory = string.Empty;
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 同步分卷数据变更失败: {ex.Message}");
            }
        }

        private void OnChapterDataChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    LoadAvailableEntities();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 同步章节数据变更失败: {ex.Message}");
            }
        }

        private void ReloadAvailableChapterIds()
        {
            try
            {
                if (!_chapterService.IsInitialized)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try { await _chapterService.InitializeAsync().ConfigureAwait(false); } catch { }
                        if (_chapterService.IsInitialized)
                        {
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                ReloadAvailableChapterIds,
                                System.Windows.Threading.DispatcherPriority.Background);
                        }
                    });
                    return;
                }

                var chaptersQuery = _chapterService.GetAllChapters()
                    .Where(c => c.IsEnabled);

                if (!string.IsNullOrWhiteSpace(FormCategory))
                {
                    chaptersQuery = chaptersQuery.Where(c => string.Equals(c.Category, FormCategory, StringComparison.Ordinal));
                }

                var chapters = chaptersQuery.OrderBy(c => c.ChapterNumber);

                var newIds = new System.Collections.Generic.List<string> { string.Empty };
                foreach (var ch in chapters)
                {
                    var volNum = ExtractVolumeNumber(ch.Volume);
                    if (volNum <= 0)
                        volNum = ExtractVolumeNumber(ch.Category);
                    if (volNum <= 0)
                        continue;
                    var chapterId = $"vol{volNum}_ch{ch.ChapterNumber}";
                    if (!newIds.Contains(chapterId))
                        newIds.Add(chapterId);
                }

                for (int i = AvailableChapterIds.Count - 1; i >= 0; i--)
                {
                    if (!newIds.Contains(AvailableChapterIds[i]))
                        AvailableChapterIds.RemoveAt(i);
                }
                foreach (var id in newIds)
                {
                    if (!AvailableChapterIds.Contains(id))
                        AvailableChapterIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BlueprintViewModel] 刷新章节ID列表失败: {ex.Message}");
            }
        }

        protected override string GetModuleNameForVersionTracking() => "Blueprint";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
                Service.UpdateBlueprint(_currentEditingData);
        }

        private async System.Threading.Tasks.Task<string> GetEnhancedBlueprintContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrWhiteSpace(FormCategory))
                LoadAvailableEntities();

            if (IsBatchModeActive && !string.IsNullOrWhiteSpace(FormCategory))
            {
                var volumeContext = await _contextService.GetChapterContextWithVolumeLocatorAsync(FormCategory);
                if (!string.IsNullOrWhiteSpace(volumeContext))
                {
                    sb.AppendLine(volumeContext);
                    sb.AppendLine();
                }

                var chapterSummary = BuildVolumeChapterSummary(FormCategory);
                if (!string.IsNullOrWhiteSpace(chapterSummary))
                {
                    sb.AppendLine("<section name=\"volume_chapter_plan\">");
                    sb.AppendLine(chapterSummary);
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }

            }
            else
            {
                var baseContext = await _contextService.GetBlueprintContextWithChapterLocatorAsync(FormChapterId);
                if (!string.IsNullOrWhiteSpace(baseContext))
                {
                    sb.AppendLine(baseContext);
                    sb.AppendLine();
                }

                var chapterIds = AvailableChapterIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (chapterIds.Count > 0)
                {
                    sb.AppendLine("<section name=\"available_chapter_ids\">");
                    sb.AppendLine("关联章节ID必须从以下列表中选择，格式：vol{卷号}_ch{章号}");
                    sb.AppendLine(string.Join("、", chapterIds));
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }

            if (AvailableCharacters.Count > 0)
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选角色",
                    candidates: AvailableCharacters,
                    fieldHint: "「出场角色」必须从以下列表中选择，不得编造"));
            }

            if (AvailableLocations.Count > 0)
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选地点",
                    candidates: AvailableLocations,
                    fieldHint: "「涉及地点」必须从以下列表中选择，不得编造"));
            }

            if (AvailableFactions.Count > 0)
            {
                sb.Append(EntityReferencePromptHelper.BuildCandidateSection(
                    title: "可选势力",
                    candidates: AvailableFactions,
                    fieldHint: "「涉及势力」必须从以下列表中选择，不得编造"));
            }

            var povCharacters = AvailableCharacters.ToList();
            if (povCharacters.Count > 0)
            {
                sb.AppendLine("<section name=\"available_pov_characters\">");
                sb.AppendLine("视点角色必须从以下列表中选择");
                sb.AppendLine(string.Join("、", povCharacters));
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("<field_constraints mandatory=\"true\">");
            sb.AppendLine("1. 「关联章节ID」由系统自动分配，AI不应生成此字段。");
            sb.AppendLine("2. 「视点角色」必须从上方可选角色列表中选择，填写单个角色名称；无则填写「暂无」。");
            sb.AppendLine("3. 「出场角色」「涉及地点」「涉及势力」必须从上方对应候选列表中选择，多个名称使用顿号分隔；无则填写「暂无」。");
            sb.AppendLine("</field_constraints>");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(FormChapterId))
            {
                var volChMatch = VolChIdRegex.Match(FormChapterId);
                if (volChMatch.Success && int.TryParse(volChMatch.Groups[2].Value, out var chNum) && chNum >= 1 && chNum <= 3)
                {
                    if (await TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.LoadAsync())
                    {
                        sb.AppendLine("<golden_chapter_requirement priority=\"high\">");
                        sb.AppendLine($"当前是第{chNum}章（黄金三章阶段），蓝图设计必须额外满足：");
                        sb.AppendLine("【开场】前200字内直接切入冲突或悬念，禁止慢热铺垫和背景堆砌");
                        sb.AppendLine("【主角】本章必须有让读者立刻记住主角的高光时刻，展现核心特质");
                        sb.AppendLine("【结尾钉子】悬念强度必须高于普通章节，直接驱动读者翻下一章");
                        if (chNum == 1) sb.AppendLine("【第1章专项】2分钟内让读者明白：故事背景、主角处境、核心矛盾是什么");
                        if (chNum == 2) sb.AppendLine("【第2章专项】激化第1章冲突，主角遭遇真正阻力，情绪曲线向下走");
                        if (chNum == 3) sb.AppendLine("【第3章专项】推向前期第一个小高潮，结尾是全书前三章最强悬念节点");
                        sb.AppendLine("</golden_chapter_requirement>");
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        private string BuildVolumeChapterSummary(string categoryName)
        {
            _chapterService.EnsureInitialized();

            var chapters = _chapterService.GetAllChapters()
                .Where(c => c.IsEnabled && string.Equals(c.Category, categoryName, StringComparison.Ordinal))
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            if (chapters.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var ch in chapters)
            {
                var volNum = ExtractVolumeNumber(ch.Volume);
                if (volNum <= 0)
                    volNum = ExtractVolumeNumber(ch.Category);
                if (volNum <= 0)
                    continue;
                var chapterId = $"vol{volNum}_ch{ch.ChapterNumber}";
                sb.AppendLine($"<item name=\"{chapterId} - 第{ch.ChapterNumber}章：{ch.ChapterTitle}\">");
                if (!string.IsNullOrWhiteSpace(ch.MainGoal))
                    sb.AppendLine($"  章节主目标：{ch.MainGoal}");
                if (!string.IsNullOrWhiteSpace(ch.ResistanceSource))
                    sb.AppendLine($"  阻力来源：{ch.ResistanceSource}");
                if (!string.IsNullOrWhiteSpace(ch.KeyTurn))
                    sb.AppendLine($"  关键转折：{ch.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(ch.Hook))
                    sb.AppendLine($"  结尾钉子：{ch.Hook}");
                if (!string.IsNullOrWhiteSpace(ch.WorldInfoDrop))
                    sb.AppendLine($"  世界观投放：{ch.WorldInfoDrop}");
                if (!string.IsNullOrWhiteSpace(ch.CharacterArcProgress))
                    sb.AppendLine($"  角色弧光推进：{ch.CharacterArcProgress}");
                if (!string.IsNullOrWhiteSpace(ch.MainPlotProgress))
                    sb.AppendLine($"  主线推进点：{ch.MainPlotProgress}");
                sb.AppendLine("</item>");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private HashSet<string> GetUsedChapterIdsInCategory(string categoryName)
        {
            return Service.GetAllBlueprints()
                .Where(b => string.Equals(b.Category, categoryName, StringComparison.Ordinal))
                .Where(b => !string.IsNullOrWhiteSpace(b.ChapterId))
                .Select(b => b.ChapterId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private string MatchChapterId(string aiValue)
        {
            if (string.IsNullOrWhiteSpace(aiValue)) return string.Empty;

            var trimmed = aiValue.Trim();

            var exactMatch = AvailableChapterIds.FirstOrDefault(id =>
                string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactMatch)) return exactMatch;

            var volMatch = VolInputParseRegex.Match(trimmed);
            var chMatch = ChInputParseRegex.Match(trimmed);
            int? chapterNum = null;
            if (chMatch.Success)
            {
                var chNumText = chMatch.Groups[1].Success ? chMatch.Groups[1].Value :
                               (chMatch.Groups[2].Success ? chMatch.Groups[2].Value : chMatch.Groups[3].Value);
                if (int.TryParse(chNumText, out var parsed))
                    chapterNum = parsed;
            }

            if (volMatch.Success && chapterNum.HasValue)
            {
                var volNum = volMatch.Groups[1].Value;
                var constructedId = $"vol{volNum}_ch{chapterNum.Value}";
                if (AvailableChapterIds.Contains(constructedId)) return constructedId;
            }

            if (chapterNum.HasValue)
            {
                var categoryVolume = ExtractVolumeNumber(FormCategory);
                if (categoryVolume > 0)
                {
                    var categoryMatch = $"vol{categoryVolume}_ch{chapterNum.Value}";
                    if (AvailableChapterIds.Contains(categoryMatch)) return categoryMatch;
                }

                var suffixMatch = AvailableChapterIds.FirstOrDefault(id =>
                    id.EndsWith($"_ch{chapterNum.Value}", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(suffixMatch)) return suffixMatch;

                var fallbackId = $"vol1_ch{chapterNum.Value}";
                if (AvailableChapterIds.Contains(fallbackId)) return fallbackId;
            }

            if (int.TryParse(trimmed, out var numericChapter))
            {
                var fallbackId = $"vol1_ch{numericChapter}";
                if (AvailableChapterIds.Contains(fallbackId)) return fallbackId;
            }

            TM.App.Log($"[BlueprintViewModel] 章节ID匹配失败: {aiValue}");
            return trimmed;
        }

        public override void Dispose()
        {
            _volumeDesignService.DataChanged -= OnVolumeDataChanged;
            _chapterService.DataChanged -= OnChapterDataChanged;
            base.Dispose();
        }
    }
}

