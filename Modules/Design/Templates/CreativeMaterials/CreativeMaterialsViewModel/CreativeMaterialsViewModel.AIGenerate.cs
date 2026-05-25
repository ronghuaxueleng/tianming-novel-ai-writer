using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Models;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Metadata;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    public partial class CreativeMaterialsViewModel
    {
        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        private AIGenerationConfig? _cachedConfig;
        private int _multiGenreSavedCountInCurrentCall;
        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return _cachedConfig ??= new AIGenerationConfig
            {
                Category = "素材设计师",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = ResponseFormat.Json,
                MessagePrefix = "生成素材",
                ProgressMessage = "正在生成三维度素材，请稍候...",
                CompleteMessage = "AI素材已生成，请查看并编辑",
                InputVariables = new()
                {
                    ["素材名称"] = () => FormName,
                    ["题材类型"] = () => FindGenreInfo(FormGenre)?.ToPromptString() ?? FormGenre,
                    ["来源拆书"] = () => FormSourceBookName,
                },
                OutputFields = new()
                {
                    ["小说简介"] = v => FormNovelSynopsis = v,
                    ["整体构思"] = v => FormOverallIdea = v,
                    ["世界观素材-构建手法"] = v => FormWorldBuildingMethod = v,
                    ["世界观素材-力量体系"] = v => FormPowerSystemDesign = v,
                    ["世界观素材-环境描写"] = v => FormEnvironmentDescription = v,
                    ["世界观素材-势力设计"] = v => FormFactionDesign = v,
                    ["世界观素材-亮点"] = v => FormWorldviewHighlights = v,
                    ["角色素材-主角塑造"] = v => FormProtagonistDesign = v,
                    ["角色素材-配角设计"] = v => FormSupportingRoles = v,
                    ["角色素材-人物关系"] = v => FormCharacterRelations = v,
                    ["角色素材-金手指"] = v => FormGoldenFingerDesign = v,
                    ["角色素材-角色亮点"] = v => FormCharacterHighlights = v,
                    ["剧情素材-情节结构"] = v => FormPlotStructure = v,
                    ["剧情素材-冲突设计"] = v => FormConflictDesign = v,
                    ["剧情素材-高潮布局"] = v => FormClimaxArrangement = v,
                    ["剧情素材-伏笔设计"] = v => FormForeshadowingTechnique = v,
                    ["剧情素材-剧情亮点"] = v => FormPlotHighlights = v,
                },
                OutputFieldGetters = new()
                {
                    ["小说简介"] = () => FormNovelSynopsis,
                    ["整体构思"] = () => FormOverallIdea,
                    ["世界观素材-构建手法"] = () => FormWorldBuildingMethod,
                    ["世界观素材-力量体系"] = () => FormPowerSystemDesign,
                    ["世界观素材-环境描写"] = () => FormEnvironmentDescription,
                    ["世界观素材-势力设计"] = () => FormFactionDesign,
                    ["世界观素材-亮点"] = () => FormWorldviewHighlights,
                    ["角色素材-主角塑造"] = () => FormProtagonistDesign,
                    ["角色素材-配角设计"] = () => FormSupportingRoles,
                    ["角色素材-人物关系"] = () => FormCharacterRelations,
                    ["角色素材-金手指"] = () => FormGoldenFingerDesign,
                    ["角色素材-角色亮点"] = () => FormCharacterHighlights,
                    ["剧情素材-情节结构"] = () => FormPlotStructure,
                    ["剧情素材-冲突设计"] = () => FormConflictDesign,
                    ["剧情素材-高潮布局"] = () => FormClimaxArrangement,
                    ["剧情素材-伏笔设计"] = () => FormForeshadowingTechnique,
                    ["剧情素材-剧情亮点"] = () => FormPlotHighlights,
                },
                ContextProvider = async () =>
                {
                    var sb = new System.Text.StringBuilder();

                    var focusId = _currentEditingData?.Id ?? string.Empty;
                    var context = await _focusContextService.GetDesignContextAsync(focusId, "Templates");

                    if (!string.IsNullOrWhiteSpace(FormGenre) && _promptRepository != null)
                    {
                        var specTemplates = _promptRepository.GetTemplatesByCategory(FormGenre);
                        var specTemplate = specTemplates?
                            .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                            .OrderByDescending(t => t.IsDefault)
                            .ThenByDescending(t => t.IsBuiltIn)
                            .FirstOrDefault();
                        if (specTemplate != null)
                        {
                            sb.AppendLine("<genre_spec priority=\"highest\" source=\"prompt_library\">");
                            sb.AppendLine(ExtractDesignSpec(specTemplate.SystemPrompt));
                            sb.AppendLine();
                            sb.AppendLine("以上规格约束具有最高优先级。后续拆书数据仅供借鉴写作技法，所有设计内容的题材风格、世界观元素、避免事项必须严格遵守此规格。");
                            sb.AppendLine("</genre_spec>");
                            sb.AppendLine();
                        }
                    }

                    if (!string.IsNullOrEmpty(context.GlobalSummary?.StorySummary))
                    {
                        sb.AppendLine("<section name=\"story_theme\">");
                        sb.AppendLine(context.GlobalSummary.StorySummary);
                        sb.AppendLine("</section>");
                        sb.AppendLine();
                    }

                    var sourceBook = !string.IsNullOrWhiteSpace(FormBookAnalysisId)
                        ? _bookAnalysisService.GetAllAnalysis().FirstOrDefault(b => b.Id == FormBookAnalysisId)
                        : _bookAnalysisService.GetAllAnalysis().FirstOrDefault(b => b.IsEnabled);
                    if (sourceBook != null)
                    {
                        sb.AppendLine("<section name=\"source_book\">");
                        sb.AppendLine($"条目: {sourceBook.Name}");
                        sb.AppendLine();

                        if (!string.IsNullOrWhiteSpace(sourceBook.WorldBuildingMethod)
                            || !string.IsNullOrWhiteSpace(sourceBook.PowerSystemDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.EnvironmentDescription)
                            || !string.IsNullOrWhiteSpace(sourceBook.FactionDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.WorldviewHighlights))
                        {
                            sb.AppendLine("<section name=\"worldview_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.WorldBuildingMethod)) sb.AppendLine(sourceBook.WorldBuildingMethod);
                            if (!string.IsNullOrWhiteSpace(sourceBook.PowerSystemDesign)) sb.AppendLine(sourceBook.PowerSystemDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.EnvironmentDescription)) sb.AppendLine(sourceBook.EnvironmentDescription);
                            if (!string.IsNullOrWhiteSpace(sourceBook.FactionDesign)) sb.AppendLine(sourceBook.FactionDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.WorldviewHighlights)) sb.AppendLine(sourceBook.WorldviewHighlights);
                            sb.AppendLine("</section>");
                            sb.AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(sourceBook.ProtagonistDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.SupportingRoles)
                            || !string.IsNullOrWhiteSpace(sourceBook.CharacterRelations)
                            || !string.IsNullOrWhiteSpace(sourceBook.GoldenFingerDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.CharacterHighlights))
                        {
                            sb.AppendLine("<section name=\"character_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.ProtagonistDesign)) sb.AppendLine(sourceBook.ProtagonistDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.SupportingRoles)) sb.AppendLine(sourceBook.SupportingRoles);
                            if (!string.IsNullOrWhiteSpace(sourceBook.CharacterRelations)) sb.AppendLine(sourceBook.CharacterRelations);
                            if (!string.IsNullOrWhiteSpace(sourceBook.GoldenFingerDesign)) sb.AppendLine(sourceBook.GoldenFingerDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.CharacterHighlights)) sb.AppendLine(sourceBook.CharacterHighlights);
                            sb.AppendLine("</section>");
                            sb.AppendLine();
                        }

                        if (!string.IsNullOrWhiteSpace(sourceBook.PlotStructure)
                            || !string.IsNullOrWhiteSpace(sourceBook.ConflictDesign)
                            || !string.IsNullOrWhiteSpace(sourceBook.ClimaxArrangement)
                            || !string.IsNullOrWhiteSpace(sourceBook.ForeshadowingTechnique)
                            || !string.IsNullOrWhiteSpace(sourceBook.PlotHighlights))
                        {
                            sb.AppendLine("<section name=\"plot_materials\">");
                            if (!string.IsNullOrWhiteSpace(sourceBook.PlotStructure)) sb.AppendLine(sourceBook.PlotStructure);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ConflictDesign)) sb.AppendLine(sourceBook.ConflictDesign);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ClimaxArrangement)) sb.AppendLine(sourceBook.ClimaxArrangement);
                            if (!string.IsNullOrWhiteSpace(sourceBook.ForeshadowingTechnique)) sb.AppendLine(sourceBook.ForeshadowingTechnique);
                            if (!string.IsNullOrWhiteSpace(sourceBook.PlotHighlights)) sb.AppendLine(sourceBook.PlotHighlights);
                            sb.AppendLine("</section>");
                        }

                        sb.AppendLine("</section>");
                    }

                    sb.AppendLine();
                    sb.AppendLine("<field_constraints mandatory=\"true\">");
                    sb.AppendLine("1. 「角色素材-主角塑造」第一行必须使用固定格式：主角姓名：XXX（仅一个姓名，不要附加解释）。");
                    sb.AppendLine("2. 「角色素材-配角设计」中出现的配角尽量给出明确姓名，并保持前后一致。");
                    sb.AppendLine("3. 「角色素材-人物关系」「剧情素材」等字段涉及角色引用时，优先使用已出现的主角/配角姓名，避免出现无名或临时新角色。");
                    sb.AppendLine("</field_constraints>");
                    sb.AppendLine();

                    return sb.ToString();
                },
                BatchFieldKeyMap = EntityFieldMeta.GetFieldKeyMapWithName("templates"),
                BatchIndexFields = new() { "Name", "OverallIdea", "WorldBuildingMethod" }
            };
        }

        protected override bool CanExecuteAIGenerate() => base.CanExecuteAIGenerate();

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllMaterials().Select(r => r.Name);

        protected override bool SupportsBatch(TreeNodeItem categoryNode) => false;

        protected override Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(string categoryName, bool singleMode = false)
        {
            if (IsMultiGenreMode)
            {
                var genres = GetSelectedGenresForGeneration();
                if (genres.Count == 0)
                {
                    GlobalToast.Warning("请选择题材", "多选模式下请至少选择 1 个题材");
                    return Task.FromResult<BatchGenerationConfig?>(null);
                }

                if (!_isPipelineExecution)
                {
                    var confirmed = StandardDialog.ShowConfirm(
                        $"将按选中的 {genres.Count} 个题材依次生成 {genres.Count} 个素材，每个题材使用全新独立会话（用完即销毁），是否继续？",
                        "多题材批量生成");
                    if (!confirmed) return Task.FromResult<BatchGenerationConfig?>(null);
                }

                return Task.FromResult<BatchGenerationConfig?>(new BatchGenerationConfig
                {
                    CategoryName = categoryName,
                    TotalCount = 1,
                    BatchSize = 1,
                });
            }
            return base.ShowBatchGenerationDialogAsync(categoryName, singleMode);
        }

        protected override async Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            if (!IsMultiGenreMode)
            {
                await base.ExecuteBatchAIGenerateAsync(config);
                return;
            }

            var genres = GetSelectedGenresForGeneration();
            if (genres.Count == 0)
            {
                GlobalToast.Warning("请选择题材", "多选模式下请至少选择 1 个题材");
                return;
            }

            var total = genres.Count;
            var prevGenre = FormGenre;
            var prevGenreManual = _genreManuallySet;
            int success = 0;
            int failed = 0;
            var failedGenres = new List<string>();

            TM.App.Log($"[CreativeMaterialsViewModel] 多题材批量启动: 共 {total} 个题材");

            try
            {
                for (int i = 0; i < total; i++)
                {
                    var genre = genres[i];
                    if (genre == null || string.IsNullOrWhiteSpace(genre.Name))
                    {
                        TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 跳过：题材为空");
                        failed++;
                        continue;
                    }

                    _suppressGenreManualMark = true;
                    _suppressSelectedGenresSync = true;
                    try { FormGenre = genre.Name; }
                    finally
                    {
                        _suppressSelectedGenresSync = false;
                        _suppressGenreManualMark = false;
                    }

                    try { ConfirmAndEndAISessionForPipeline(); } catch { }

                    BatchProgressText = $"多题材生成 ({i + 1}/{total} · {genre.Name})...";
                    TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 开始生成: {genre.Name}");

                    try
                    {
                        var oneConfig = new BatchGenerationConfig
                        {
                            CategoryName = config.CategoryName,
                            TotalCount = 1,
                            BatchSize = 1,
                        };
                        _multiGenreSavedCountInCurrentCall = 0;
                        var prevPipelineExecution = _isPipelineExecution;
                        try
                        {
                            _isPipelineExecution = true;
                            await base.ExecuteBatchAIGenerateAsync(oneConfig);
                        }
                        finally
                        {
                            _isPipelineExecution = prevPipelineExecution;
                        }

                        if (_lastBatchWasCancelled)
                        {
                            failed++;
                            failedGenres.Add(genre.Name);
                            TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 已取消: {genre.Name}");
                            break;
                        }

                        if (_lastBatchKeyExhausted)
                        {
                            failed++;
                            failedGenres.Add(genre.Name);
                            TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] AI模型或密钥不可用，停止后续题材");
                            break;
                        }

                        if (_multiGenreSavedCountInCurrentCall > 0)
                        {
                            success++;
                            TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 完成: {genre.Name}");
                        }
                        else
                        {
                            failed++;
                            failedGenres.Add(genre.Name);
                            TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 未生成有效素材: {genre.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failedGenres.Add(genre.Name);
                        TM.App.Log($"[CreativeMaterialsViewModel] 多题材 [{i + 1}/{total}] 失败 ({genre.Name}): {ex.Message}");
                    }
                    finally
                    {
                        try { ConfirmAndEndAISessionForPipeline(); } catch { }
                    }
                }
            }
            finally
            {
                try { ConfirmAndEndAISessionForPipeline(); } catch { }

                _suppressGenreManualMark = true;
                _suppressSelectedGenresSync = true;
                try { FormGenre = prevGenre; }
                finally
                {
                    _suppressSelectedGenresSync = false;
                    _suppressGenreManualMark = false;
                }
                _genreManuallySet = prevGenreManual;

                BatchProgressText = string.Empty;
            }

            if (success > 0 && failed == 0)
            {
                GlobalToast.Success("多题材生成完成", $"已成功生成 {success} 个素材");
            }
            else if (success > 0 && failed > 0)
            {
                GlobalToast.Info("部分完成", $"成功 {success} 个，失败 {failed} 个：{string.Join("、", failedGenres)}");
            }
            else
            {
                GlobalToast.Warning("生成失败", $"全部 {total} 个题材均未生成成功");
            }
        }

        private List<GenreInfo> GetSelectedGenresForGeneration()
        {
            return FormSelectedGenres
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(5)
                .ToList();
        }

        protected override async Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(
                Service.GetAllMaterials().Select(m => m.Name),
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
                            name = $"素材_{DateTime.Now:HHmmss}_{result.Count + 1}";

                        var baseName = name;

                        if (dbNames.Contains(baseName))
                        {
                            TM.App.Log($"[CreativeMaterialsViewModel] 跳过已存在素材: {baseName}");
                            continue;
                        }

                        int suffix = 1;
                        while (batchNames.Contains(name))
                        {
                            name = $"{baseName}_{suffix++}";
                        }
                        batchNames.Add(name);
                        dbNames.Add(name);

                        var data = new CreativeMaterialData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = name,
                            Category = categoryName,
                            Icon = DefaultDataIcon,
                            IsEnabled = true,
                            CreatedTime = DateTime.Now,
                            ModifiedTime = DateTime.Now,
                            SourceBookName = FormSourceBookName,
                            Genre = FormGenre,
                            NovelSynopsis = reader.GetString("NovelSynopsis"),
                            OverallIdea = reader.GetString("OverallIdea"),
                            WorldBuildingMethod = reader.GetString("WorldBuildingMethod"),
                            PowerSystemDesign = reader.GetString("PowerSystemDesign"),
                            EnvironmentDescription = reader.GetString("EnvironmentDescription"),
                            FactionDesign = reader.GetString("FactionDesign"),
                            WorldviewHighlights = reader.GetString("WorldviewHighlights"),
                            ProtagonistDesign = reader.GetString("ProtagonistDesign"),
                            SupportingRoles = reader.GetString("SupportingRoles"),
                            CharacterRelations = reader.GetString("CharacterRelations"),
                            GoldenFingerDesign = reader.GetString("GoldenFingerDesign"),
                            CharacterHighlights = reader.GetString("CharacterHighlights"),
                            PlotStructure = reader.GetString("PlotStructure"),
                            ConflictDesign = reader.GetString("ConflictDesign"),
                            ClimaxArrangement = reader.GetString("ClimaxArrangement"),
                            ForeshadowingTechnique = reader.GetString("ForeshadowingTechnique"),
                            PlotHighlights = reader.GetString("PlotHighlights"),
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };

                        entity["Name"] = name;
                        await Service.AddMaterialAsync(data);
                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: 成功保存 {result.Count}/{entities.Count} 个实体");
                _multiGenreSavedCountInCurrentCall += result.Count;
                if (result.Count > 0)
                {
                    var genreToSync = FormGenre;
                    if (string.IsNullOrWhiteSpace(genreToSync))
                    {
                        var categoryNames = CollectCategoryAndChildrenNames(categoryName);
                        genreToSync = Service.GetAllMaterials()
                            .Where(m => categoryNames.Contains(m.Category) && m.IsEnabled && !string.IsNullOrWhiteSpace(m.Genre))
                            .OrderByDescending(m => m.ModifiedTime)
                            .FirstOrDefault()?.Genre ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(genreToSync))
                            TM.App.Log($"[CreativeMaterialsViewModel] SaveBatchEntitiesAsync: FormGenre为空，从子分类继承题材 → {genreToSync}");
                    }
                    if (!string.IsNullOrWhiteSpace(genreToSync))
                        await SyncSpecWithGenreAsync(genreToSync);
                }
                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }

        protected override void OnAfterDeleteAll(int deletedCount)
        {
            if (deletedCount > 0)
                _ = ClearSpecAsync();
        }

        private static readonly HashSet<string> _designExcludedTags = new()
        {
            "目标字数", "段落长度", "对话比例", "节奏要求", "叙述视角"
        };

        private static string ExtractDesignSpec(string systemPrompt)
        {
            var lines = systemPrompt.Split('\n');
            return string.Join("\n", lines.Where(line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^【([^】]+)】");
                return !m.Success || !_designExcludedTags.Contains(m.Groups[1].Value);
            }));
        }

        private async Task SyncSpecWithGenreAsync(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre) || _promptRepository == null) return;
            try
            {
                var specTemplates = _promptRepository.GetTemplatesByCategory(genre);
                var specTemplate = specTemplates?
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .FirstOrDefault();
                if (specTemplate == null) return;

                var spec = SpecTemplateParser.Parse(specTemplate.SystemPrompt, specTemplate.Name);
                await _specLoader.SaveProjectSpecAsync(spec);
                _specLoader.InvalidateCache();
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 已同步为题材: {genre} → {specTemplate.Name}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 同步失败: {ex.Message}");
            }
        }

        private async Task ClearSpecAsync()
        {
            try
            {
                var current = await _specLoader.LoadProjectSpecAsync() ?? new CreativeSpec();
                current.TemplateName = null;
                await _specLoader.SaveProjectSpecAsync(current);
                _specLoader.InvalidateCache();
                TM.App.Log("[CreativeMaterialsViewModel] Spec 已清空模板选择（创作模板已删除）");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterialsViewModel] Spec 清空失败: {ex.Message}");
            }
        }

    }
}
