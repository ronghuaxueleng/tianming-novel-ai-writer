using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Contexts.Aggregates;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Contexts.Generate;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Common;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ContextService : IContextService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private const string PackagedCacheLayer = "PackagedContext";

        private readonly SessionContextCache _sessionCache;
        private readonly IWorkScopeService _workScopeService;

        private bool IsInCurrentScope(ISourceBookBound item)
        {
            var scopeId = _workScopeService.CurrentSourceBookId;
            if (string.IsNullOrWhiteSpace(scopeId))
                return true;
            if (item == null)
                return false;
            if (string.IsNullOrWhiteSpace(item.SourceBookId))
                return true;
            return string.Equals(item.SourceBookId, scopeId, StringComparison.Ordinal);
        }

        public ContextService(SessionContextCache sessionCache, IWorkScopeService workScopeService)
        {
            _sessionCache = sessionCache;
            _workScopeService = workScopeService;

            _workScopeService.ScopeChanged += (_, _) => _sessionCache.InvalidateLayer(PackagedCacheLayer);
            GuideContextService.CacheInvalidated += (_, _) => _sessionCache.InvalidateLayer(PackagedCacheLayer);

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => _sessionCache.InvalidateLayer(PackagedCacheLayer);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        #region Design

        public async Task<SmartParsingContext> GetSmartParsingContextAsync()
        {
            TM.App.Log("[ContextService] 构建SmartParsingContext");

            var context = new SmartParsingContext
            {
                BookAnalyses = await LoadFunctionDataAsync<BookAnalysisData>("BookAnalysis")
            };

            TM.App.Log($"[ContextService] SmartParsingContext构建完成: BookAnalyses={context.BookAnalyses.Count}");
            return context;
        }

        public async Task<TemplatesContext> GetTemplatesContextAsync()
        {
            TM.App.Log("[ContextService] 构建TemplatesContext");

            var context = new TemplatesContext
            {
                CreativeMaterials = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials")
            };

            TM.App.Log($"[ContextService] TemplatesContext构建完成: CreativeMaterials={context.CreativeMaterials.Count}");
            return context;
        }

        public async Task<WorldviewContext> GetWorldviewContextAsync()
        {
            TM.App.Log("[ContextService] 构建WorldviewContext");

            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var context = new WorldviewContext
            {
                Templates = templates,
                WorldRules = worldRulesData
            };

            TM.App.Log($"[ContextService] WorldviewContext构建完成: WorldRules={context.WorldRules.Count}");
            return context;
        }

        public async Task<CharacterContext> GetCharacterContextAsync()
        {
            TM.App.Log("[ContextService] 构建CharacterContext");

            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesData = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = worldRulesData };
            var context = new CharacterContext
            {
                Templates = templates,
                Worldview = worldview,
                CharacterRules = characterRulesData
            };

            TM.App.Log($"[ContextService] CharacterContext构建完成");
            return context;
        }

        public async Task<FactionsContext> GetFactionsContextAsync()
        {
            TM.App.Log("[ContextService] 构建FactionsContext");

            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesData = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesData = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = worldRulesData };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = characterRulesData };
            var context = new FactionsContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                FactionRules = factionRulesData
            };

            TM.App.Log($"[ContextService] FactionsContext构建完成");
            return context;
        }

        public async Task<LocationContext> GetLocationsContextAsync()
        {
            TM.App.Log("[ContextService] 构建LocationContext");

            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesData = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesData = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locationRulesData = await LoadFunctionDataAsync<LocationRulesData>("LocationRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = worldRulesData };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = characterRulesData };
            var factions = new FactionsContext { Templates = templates, Worldview = worldview, Characters = characters, FactionRules = factionRulesData };
            var context = new LocationContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                LocationRules = locationRulesData
            };

            TM.App.Log($"[ContextService] LocationContext构建完成: LocationRules={context.LocationRules.Count}");
            return context;
        }

        public async Task<PlotContext> GetPlotContextAsync()
        {
            TM.App.Log("[ContextService] 构建PlotContext");

            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesData = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesData = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locationRulesData = await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            var plotRulesData = await LoadFunctionDataAsync<PlotRulesData>("PlotRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = worldRulesData };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = characterRulesData };
            var factions = new FactionsContext { Templates = templates, Worldview = worldview, Characters = characters, FactionRules = factionRulesData };
            var locations = new LocationContext { Templates = templates, Worldview = worldview, Characters = characters, Factions = factions, LocationRules = locationRulesData };
            var context = new PlotContext
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                Locations = locations,
                PlotRules = plotRulesData
            };

            TM.App.Log($"[ContextService] PlotContext构建完成");
            return context;
        }

        public async Task<DesignData> GetDesignContextAsync()
        {
            TM.App.Log("[ContextService] 构建DesignContext");
            var designData = await BuildDesignDataAsync();
            TM.App.Log($"[ContextService] DesignContext构建完成");
            return designData;
        }

        #endregion

        #region Generate

        public async Task<OutlineContext> GetOutlineContextAsync()
        {
            TM.App.Log("[ContextService] 构建OutlineContext");

            var context = new OutlineContext
            {
                Design = await BuildDesignDataAsync(),
                Outlines = await LoadFunctionDataAsync<Models.Generate.StrategicOutline.OutlineData>("Outline")
            };

            TM.App.Log($"[ContextService] OutlineContext构建完成");
            return context;
        }

        public async Task<PlanningContext> GetPlanningContextAsync()
        {
            TM.App.Log("[ContextService] 构建PlanningContext");

            var context = new PlanningContext
            {
                Design = await BuildDesignDataAsync(),
                Outline = await BuildOutlineDataAsync(),
                Chapters = await LoadFunctionDataAsync<Models.Generate.ChapterPlanning.ChapterData>("Chapter")
            };

            TM.App.Log($"[ContextService] PlanningContext构建完成");
            return context;
        }

        public async Task<BlueprintContext> GetBlueprintContextAsync()
        {
            TM.App.Log("[ContextService] 构建BlueprintContext");

            var context = new BlueprintContext
            {
                Design = await BuildDesignDataAsync(),
                Outline = await BuildOutlineDataAsync(),
                Planning = await BuildPlanningDataAsync(),
                Blueprints = await LoadFunctionDataAsync<Models.Generate.ChapterBlueprint.BlueprintData>("Blueprint")
            };

            TM.App.Log($"[ContextService] BlueprintContext构建完成");
            return context;
        }

        #endregion

        #region Validate

        public async Task<ValidationContext> GetValidationContextAsync(string chapterId)
        {
            TM.App.Log($"[ContextService] 构建ValidationContext: chapterId={chapterId}");

            var context = new ValidationContext
            {
                ChapterId = chapterId,
                Design = await LoadPackagedDesignDataAsync(),
                Generate = await LoadPackagedGenerateDataAsync(),
                Rules = new ValidateRules()
            };

            if (TryParseChapterId(chapterId, out int vol, out int ch))
            {
                context.VolumeNumber = vol;
                context.ChapterNumber = ch;
                context.GeneratedContent = await LoadGeneratedContentAsync(vol, ch);
            }

            TM.App.Log($"[ContextService] ValidationContext构建完成");
            return context;
        }

        #endregion

        #region DataAggregation

        private async Task<DesignData> BuildDesignDataAsync()
        {
            var templatesData = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
            var worldRulesData = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
            var characterRulesData = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factionRulesData = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locationRulesData = await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            var plotRulesData = await LoadFunctionDataAsync<PlotRulesData>("PlotRules");

            var templates = new TemplatesContext { CreativeMaterials = templatesData };
            var worldview = new WorldviewContext { Templates = templates, WorldRules = worldRulesData };
            var characters = new CharacterContext { Templates = templates, Worldview = worldview, CharacterRules = characterRulesData };
            var factions = new FactionsContext { Templates = templates, Worldview = worldview, Characters = characters, FactionRules = factionRulesData };
            var locations = new LocationContext { Templates = templates, Worldview = worldview, Characters = characters, Factions = factions, LocationRules = locationRulesData };
            var plot = new PlotContext { Templates = templates, Worldview = worldview, Characters = characters, Factions = factions, Locations = locations, PlotRules = plotRulesData };

            return new DesignData
            {
                Templates = templates,
                Worldview = worldview,
                Characters = characters,
                Factions = factions,
                Locations = locations,
                Plot = plot
            };
        }

        private async Task<OutlineDataAggregate> BuildOutlineDataAsync()
        {
            return new OutlineDataAggregate
            {
                Outlines = await LoadDataListAsync<Models.Generate.StrategicOutline.OutlineData>(
                    "Modules/Generate/GlobalSettings/Outline", "outline_data.json")
            };
        }

        private async Task<PlanningData> BuildPlanningDataAsync()
        {
            return new PlanningData
            {
                Chapters = await LoadDataListAsync<Models.Generate.ChapterPlanning.ChapterData>(
                    "Modules/Generate/Elements/Chapter", "chapter_data.json")
            };
        }

        #endregion

        #region PackagedData

        private async Task<DesignData> LoadPackagedDesignDataAsync()
        {
            var cacheKey = BuildPackagedCacheKey("Design");
            var cached = await _sessionCache.GetOrLoadAsync(cacheKey, async () =>
            {
                var designData = new DesignData();

                try
                {
                    var configPath = GetProjectConfigPath("Design");

                    designData.Templates = new TemplatesContext();

                    var worldRules = await LoadPackagedDataAsync<WorldRulesData>(configPath, "globalsettings.json", "worldrules");
                    designData.Worldview = new WorldviewContext { WorldRules = worldRules };

                    var characterRules = await LoadPackagedDataAsync<CharacterRulesData>(configPath, "elements.json", "characterrules");
                    designData.Characters = new CharacterContext { CharacterRules = characterRules };

                    var factionRules = await LoadPackagedDataAsync<FactionRulesData>(configPath, "elements.json", "factionrules");
                    designData.Factions = new FactionsContext { FactionRules = factionRules };

                    var locationRules = await LoadPackagedDataAsync<LocationRulesData>(configPath, "elements.json", "locationrules");
                    designData.Locations = new LocationContext { LocationRules = locationRules };

                    var plotRules = await LoadPackagedDataAsync<PlotRulesData>(configPath, "elements.json", "plotrules");
                    designData.Plot = new PlotContext { PlotRules = plotRules };
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContextService] 加载打包Design数据失败: {ex.Message}");
                }

                return designData;
            });

            return cached ?? new DesignData();
        }

        private async Task<GenerateData> LoadPackagedGenerateDataAsync()
        {
            var cacheKey = BuildPackagedCacheKey("Generate");
            var cached = await _sessionCache.GetOrLoadAsync(cacheKey, async () =>
            {
                var generateData = new GenerateData();

                try
                {
                    var configPath = GetProjectConfigPath("Generate");

                    var outlineData = await LoadPackagedDataAsync<Models.Generate.StrategicOutline.OutlineData>(configPath, "globalsettings.json", "outline");
                    generateData.Outline = new OutlineDataAggregate { Outlines = outlineData };

                    var chapterData = await LoadPackagedDataAsync<Models.Generate.ChapterPlanning.ChapterData>(configPath, "elements.json", "chapter");
                    generateData.Planning = new PlanningData { Chapters = chapterData };

                    var blueprintData = await LoadPackagedDataAsync<Models.Generate.ChapterBlueprint.BlueprintData>(configPath, "elements.json", "blueprint");
                    generateData.Blueprint = new BlueprintDataAggregate { Blueprints = blueprintData };

                    var volumeDesignData = await LoadPackagedDataAsync<Models.Generate.VolumeDesign.VolumeDesignData>(configPath, "elements.json", "volumedesign");
                    generateData.VolumeDesign = new VolumeDesignDataAggregate { VolumeDesigns = volumeDesignData };
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContextService] 加载打包Generate数据失败: {ex.Message}");
                }

                return generateData;
            });

            return cached ?? new GenerateData();
        }

        private string BuildPackagedCacheKey(string section)
        {
            var scopeId = _workScopeService.CurrentSourceBookId ?? "default";
            var projectName = StoragePathHelper.CurrentProjectName;
            return $"{PackagedCacheLayer}_{projectName}_{scopeId}_{section}";
        }

        private async Task<string> LoadGeneratedContentAsync(int volumeNumber, int chapterNumber)
        {
            try
            {
                var filePath = Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"vol{volumeNumber}_ch{chapterNumber}.md");
                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载生成内容失败: {ex.Message}");
            }

            return string.Empty;
        }

        #endregion

        #region Helpers

        private async Task<List<T>> LoadDataListAsync<T>(string relativePath, string fileName)
        {
            try
            {
                var filePath = Path.Combine(StoragePathHelper.GetStorageRoot(), relativePath, fileName);

                if (!File.Exists(filePath))
                {
                    TM.App.Log($"[ContextService] 文件不存在: {filePath}");
                    return new List<T>();
                }

                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
                return data ?? new List<T>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载数据失败 [{relativePath}/{fileName}]: {ex.Message}");
                return new List<T>();
            }
        }

        private async Task<T?> LoadPackagedFileAsync<T>(string configPath, string fileName) where T : class
        {
            try
            {
                var filePath = Path.Combine(configPath, fileName);

                if (!File.Exists(filePath))
                {
                    TM.App.Log($"[ContextService] 打包文件不存在: {filePath}");
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载打包文件失败 [{fileName}]: {ex.Message}");
                return null;
            }
        }

        private async Task<List<T>> LoadPackagedDataAsync<T>(string configPath, string fileName, string dataKey)
        {
            var items = new List<T>();
            try
            {
                var filePath = Path.Combine(configPath, fileName);

                if (!File.Exists(filePath))
                {
                    return items;
                }

                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.TryGetProperty(dataKey, out var keyProp))
                    {
                        if (keyProp.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var fileProp in keyProp.EnumerateObject())
                            {
                                if (string.Equals(fileProp.Name, "categories", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (fileProp.Value.ValueKind == JsonValueKind.Array)
                                {
                                    var arrayJson = fileProp.Value.GetRawText();
                                    var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                                    if (arrayItems != null) items.AddRange(arrayItems);
                                }
                            }
                        }
                        else if (keyProp.ValueKind == JsonValueKind.Array)
                        {
                            var arrayJson = keyProp.GetRawText();
                            var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                            if (arrayItems != null) items.AddRange(arrayItems);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载打包数据失败 [{fileName}/{dataKey}]: {ex.Message}");
            }

            return items;
        }

        private string GetProjectConfigPath(string moduleName)
        {
            return StoragePathHelper.GetProjectConfigPath(moduleName);
        }

        private string GetFunctionPath(string functionName)
        {
            var path = NavigationConfigParser.GetStoragePath(functionName);
            if (string.IsNullOrEmpty(path))
            {
                TM.App.Log($"[ContextService] ⚠️ 未找到功能路径: {functionName}");
            }
            return path;
        }

        private string GetDataFileName(string functionName)
        {
            var snakeCase = ToSnakeCase(functionName);

            if (functionName is "Outline" or "Chapter" or "Blueprint" or "VolumeDesign")
            {
                return $"{snakeCase}_data.json";
            }

            return $"{snakeCase}.json";
        }

        public async Task<string> GetVolumeDesignContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"volume_design\">");
            sb.Append(await GetCoreDesignContextAsync());
            sb.AppendLine();
            sb.Append(await BuildOutlineStringAsync());
            sb.AppendLine();
            sb.Append(await BuildVolumeDesignStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        private string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c) && i > 0)
                {
                    result.Append('_');
                }
                result.Append(char.ToLower(c));
            }
            return result.ToString();
        }

        private async Task<List<T>> LoadFunctionDataAsync<T>(string functionName)
        {
            var path = GetFunctionPath(functionName);
            var fileName = GetDataFileName(functionName);
            return await LoadDataListAsync<T>(path, fileName);
        }

        private bool TryParseChapterId(string chapterId, out int volumeNumber, out int chapterNumber)
        {
            volumeNumber = 0;
            chapterNumber = 0;

            if (string.IsNullOrEmpty(chapterId))
                return false;

            if (chapterId.StartsWith("vol") && chapterId.Contains("_ch"))
            {
                var parts = chapterId.Replace("vol", "").Split("_ch");
                if (parts.Length == 2 && 
                    int.TryParse(parts[0], out volumeNumber) && 
                    int.TryParse(parts[1], out chapterNumber))
                {
                    return true;
                }
            }

            if (chapterId.Contains("-"))
            {
                var parts = chapterId.Split('-');
                if (parts.Length == 2 && 
                    int.TryParse(parts[0], out volumeNumber) && 
                    int.TryParse(parts[1], out chapterNumber))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region FullContext

        private enum MaterialScope { Worldview, Character, Faction, Location, Plot }

        public async Task<string> GetCreativeMaterialsContextAsync()
        {
            TM.App.Log("[ContextService] 构建CreativeMaterialsContext");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<creative_materials_catalog>");

            try
            {
                var items = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");

                foreach (var item in items.Where(i => i.IsEnabled && IsInCurrentScope(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    sb.AppendLine($"分类：{item.Category}");
                    if (!string.IsNullOrWhiteSpace(item.Genre))
                        sb.AppendLine($"题材类型：{item.Genre}");
                    if (!string.IsNullOrWhiteSpace(item.OverallIdea))
                        sb.AppendLine($"整体构思：{item.OverallIdea}");
                    if (!string.IsNullOrWhiteSpace(item.WorldBuildingMethod))
                        sb.AppendLine($"世界观素材-构建手法：{item.WorldBuildingMethod}");
                    if (!string.IsNullOrWhiteSpace(item.PowerSystemDesign))
                        sb.AppendLine($"世界观素材-力量体系：{item.PowerSystemDesign}");
                    if (!string.IsNullOrWhiteSpace(item.EnvironmentDescription))
                        sb.AppendLine($"世界观素材-环境描写：{item.EnvironmentDescription}");
                    if (!string.IsNullOrWhiteSpace(item.FactionDesign))
                        sb.AppendLine($"世界观素材-势力设计：{item.FactionDesign}");
                    if (!string.IsNullOrWhiteSpace(item.WorldviewHighlights))
                        sb.AppendLine($"世界观素材-亮点：{item.WorldviewHighlights}");
                    if (!string.IsNullOrWhiteSpace(item.ProtagonistDesign))
                        sb.AppendLine($"角色素材-主角塑造：{item.ProtagonistDesign}");
                    if (!string.IsNullOrWhiteSpace(item.SupportingRoles))
                        sb.AppendLine($"角色素材-配角设计：{item.SupportingRoles}");
                    if (!string.IsNullOrWhiteSpace(item.CharacterRelations))
                        sb.AppendLine($"角色素材-人物关系：{item.CharacterRelations}");
                    if (!string.IsNullOrWhiteSpace(item.GoldenFingerDesign))
                        sb.AppendLine($"角色素材-金手指：{item.GoldenFingerDesign}");
                    if (!string.IsNullOrWhiteSpace(item.CharacterHighlights))
                        sb.AppendLine($"角色素材-角色亮点：{item.CharacterHighlights}");
                    if (!string.IsNullOrWhiteSpace(item.PlotStructure))
                        sb.AppendLine($"剧情素材-情节结构：{item.PlotStructure}");
                    if (!string.IsNullOrWhiteSpace(item.ConflictDesign))
                        sb.AppendLine($"剧情素材-冲突设计：{item.ConflictDesign}");
                    if (!string.IsNullOrWhiteSpace(item.ClimaxArrangement))
                        sb.AppendLine($"剧情素材-高潮布局：{item.ClimaxArrangement}");
                    if (!string.IsNullOrWhiteSpace(item.ForeshadowingTechnique))
                        sb.AppendLine($"剧情素材-伏笔设计：{item.ForeshadowingTechnique}");
                    if (!string.IsNullOrWhiteSpace(item.PlotHighlights))
                        sb.AppendLine($"剧情素材-剧情亮点：{item.PlotHighlights}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetCreativeMaterialsContextAsync失败: {ex.Message}");
            }

            sb.AppendLine("</creative_materials_catalog>");
            return sb.ToString();
        }

        private async Task<string> BuildCreativeMaterialsStringAsync(MaterialScope scope)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<creative_materials_catalog>");
            try
            {
                var items = await LoadFunctionDataAsync<CreativeMaterialData>("CreativeMaterials");
                foreach (var item in items.Where(i => i.IsEnabled && IsInCurrentScope(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    sb.AppendLine($"分类：{item.Category}");
                    if (!string.IsNullOrWhiteSpace(item.Genre))
                        sb.AppendLine($"题材类型：{item.Genre}");
                    if (!string.IsNullOrWhiteSpace(item.OverallIdea))
                        sb.AppendLine($"整体构思：{item.OverallIdea}");

                    bool needWorldBuilding = scope == MaterialScope.Worldview || scope == MaterialScope.Location;
                    bool needPowerSystem   = scope != MaterialScope.Location;
                    bool needEnvironment   = scope == MaterialScope.Worldview || scope == MaterialScope.Location;
                    bool needFactionDesign = scope == MaterialScope.Worldview || scope == MaterialScope.Faction;

                    if (needWorldBuilding && !string.IsNullOrWhiteSpace(item.WorldBuildingMethod))
                        sb.AppendLine($"世界观素材-构建手法：{item.WorldBuildingMethod}");
                    if (needPowerSystem && !string.IsNullOrWhiteSpace(item.PowerSystemDesign))
                        sb.AppendLine($"世界观素材-力量体系：{item.PowerSystemDesign}");
                    if (needEnvironment && !string.IsNullOrWhiteSpace(item.EnvironmentDescription))
                        sb.AppendLine($"世界观素材-环境描写：{item.EnvironmentDescription}");
                    if (needFactionDesign && !string.IsNullOrWhiteSpace(item.FactionDesign))
                        sb.AppendLine($"世界观素材-势力设计：{item.FactionDesign}");
                    if (!string.IsNullOrWhiteSpace(item.WorldviewHighlights))
                        sb.AppendLine($"世界观素材-亮点：{item.WorldviewHighlights}");

                    bool needCharMaterials = scope == MaterialScope.Character || scope == MaterialScope.Plot;
                    if (needCharMaterials)
                    {
                        if (!string.IsNullOrWhiteSpace(item.ProtagonistDesign))
                            sb.AppendLine($"角色素材-主角塑造：{item.ProtagonistDesign}");
                        if (!string.IsNullOrWhiteSpace(item.SupportingRoles))
                            sb.AppendLine($"角色素材-配角设计：{item.SupportingRoles}");
                        if (!string.IsNullOrWhiteSpace(item.CharacterRelations))
                            sb.AppendLine($"角色素材-人物关系：{item.CharacterRelations}");
                        if (!string.IsNullOrWhiteSpace(item.GoldenFingerDesign))
                            sb.AppendLine($"角色素材-金手指：{item.GoldenFingerDesign}");
                        if (!string.IsNullOrWhiteSpace(item.CharacterHighlights))
                            sb.AppendLine($"角色素材-角色亮点：{item.CharacterHighlights}");
                    }

                    if (scope == MaterialScope.Plot)
                    {
                        if (!string.IsNullOrWhiteSpace(item.PlotStructure))
                            sb.AppendLine($"剧情素材-情节结构：{item.PlotStructure}");
                        if (!string.IsNullOrWhiteSpace(item.ConflictDesign))
                            sb.AppendLine($"剧情素材-冲突设计：{item.ConflictDesign}");
                        if (!string.IsNullOrWhiteSpace(item.ClimaxArrangement))
                            sb.AppendLine($"剧情素材-高潮布局：{item.ClimaxArrangement}");
                        if (!string.IsNullOrWhiteSpace(item.ForeshadowingTechnique))
                            sb.AppendLine($"剧情素材-伏笔设计：{item.ForeshadowingTechnique}");
                        if (!string.IsNullOrWhiteSpace(item.PlotHighlights))
                            sb.AppendLine($"剧情素材-剧情亮点：{item.PlotHighlights}");
                    }

                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCreativeMaterialsStringAsync(scope={scope})失败: {ex.Message}"); }
            sb.AppendLine("</creative_materials_catalog>");
            return sb.ToString();
        }

        public async Task<string> GetWorldviewContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"worldview_rules\">");
            sb.Append(await BuildCreativeMaterialsStringAsync(MaterialScope.Worldview));
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetCharacterContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"character_rules\">");
            sb.Append(await BuildCreativeMaterialsStringAsync(MaterialScope.Character));
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetFactionContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"faction_rules\">");
            sb.Append(await BuildCreativeMaterialsStringAsync(MaterialScope.Faction));
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.AppendLine();
            sb.Append(await BuildCharacterSummaryStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetLocationContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"location_rules\">");
            sb.Append(await BuildCreativeMaterialsStringAsync(MaterialScope.Location));
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.AppendLine();
            sb.Append(await BuildCharacterMinimalStringAsync());
            sb.AppendLine();
            sb.Append(await BuildFactionSummaryStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetPlotContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"plot_rules\">");
            sb.Append(await BuildCreativeMaterialsStringAsync(MaterialScope.Plot));
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.AppendLine();
            sb.Append(await BuildCharacterArcStringAsync());
            sb.AppendLine();
            sb.Append(await BuildFactionSummaryStringAsync());
            sb.AppendLine();
            sb.Append(await BuildLocationSummaryStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetOutlineContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"outline\">");
            sb.Append(await GetCoreDesignContextAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetChapterContextStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"chapter_planning\">");
            sb.Append(await GetCoreDesignContextAsync());
            sb.AppendLine();
            sb.Append(await BuildOutlineStringAsync());
            sb.AppendLine();
            sb.Append(await BuildVolumeDesignStringAsync());
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetChapterContextWithVolumeLocatorAsync(string categoryKey)
        {
            var sb = new System.Text.StringBuilder();

            var volume = await GetVolumeDesignByCategoryAsync(categoryKey);
            var volumeFilter = volume != null && volume.VolumeNumber > 0 ? $"第{volume.VolumeNumber}卷" : null;

            var isVolumeName = !string.IsNullOrWhiteSpace(categoryKey)
                               && categoryKey.StartsWith("第", StringComparison.Ordinal)
                               && categoryKey.EndsWith("卷", StringComparison.Ordinal);

            var effectiveVolumeFilter = volumeFilter ?? (isVolumeName ? categoryKey : null);

            if (!string.IsNullOrWhiteSpace(effectiveVolumeFilter))
            {
                sb.Append(await GetCoreDesignContextForVolumeAsync(effectiveVolumeFilter));
            }
            else
            {
                sb.Append(await GetCoreDesignContextAsync());
            }
            sb.AppendLine();
            sb.Append(await BuildOutlineStringAsync());

            if (volume != null)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"<context_block type=\"volume_locator\" title=\"{volume.VolumeTitle}\">");
                sb.AppendLine($"- 卷序号：第{volume.VolumeNumber}卷");
                if (volume.StartChapter > 0 && volume.EndChapter > 0)
                    sb.AppendLine($"- 章节范围：第{volume.StartChapter}-{volume.EndChapter}章");
                if (volume.TargetChapterCount > 0)
                    sb.AppendLine($"- 目标章节数：{volume.TargetChapterCount}");
                if (!string.IsNullOrWhiteSpace(volume.VolumeTheme))
                    sb.AppendLine($"- 卷主题：{volume.VolumeTheme}");
                if (!string.IsNullOrWhiteSpace(volume.StageGoal))
                    sb.AppendLine($"- 阶段目标：{volume.StageGoal}");
                if (!string.IsNullOrWhiteSpace(volume.EstimatedWordCount))
                    sb.AppendLine($"- 预计字数：{volume.EstimatedWordCount}");
                if (!string.IsNullOrWhiteSpace(volume.MainConflict))
                    sb.AppendLine($"- 卷主冲突：{volume.MainConflict}");
                if (!string.IsNullOrWhiteSpace(volume.PressureSource))
                    sb.AppendLine($"- 压力来源：{volume.PressureSource}");
                if (!string.IsNullOrWhiteSpace(volume.KeyEvents))
                    sb.AppendLine($"- 关键转折：{volume.KeyEvents}");
                if (!string.IsNullOrWhiteSpace(volume.OpeningState))
                    sb.AppendLine($"- 卷开篇状态：{volume.OpeningState}");
                if (!string.IsNullOrWhiteSpace(volume.EndingState))
                    sb.AppendLine($"- 卷收束状态：{volume.EndingState}");
                if (!string.IsNullOrWhiteSpace(volume.ChapterAllocationOverview))
                    sb.AppendLine($"- 章节分配总览：{volume.ChapterAllocationOverview}");
                if (!string.IsNullOrWhiteSpace(volume.PlotAllocation))
                    sb.AppendLine($"- 剧情分配：{volume.PlotAllocation}");
                if (!string.IsNullOrWhiteSpace(volume.ChapterGenerationHints))
                    sb.AppendLine($"- 章节生成提示：{volume.ChapterGenerationHints}");
                sb.AppendLine("</context_block>");
            }
            else
            {
                sb.AppendLine();
                sb.Append(await BuildVolumeDesignStringAsync());
            }

            return sb.ToString();
        }

        public async Task<string> GetBlueprintContextWithChapterLocatorAsync(string chapterId)
        {
            var sb = new System.Text.StringBuilder();

            var chapter = await GetChapterDataByChapterIdAsync(chapterId);
            string? volumeFilter = null;
            VolumeDesignData? volumeData = null;
            if (chapter != null)
            {
                var vKey = !string.IsNullOrWhiteSpace(chapter.CategoryId) ? chapter.CategoryId : chapter.Category;
                volumeData = await GetVolumeDesignByCategoryAsync(vKey);
                if (volumeData != null && volumeData.VolumeNumber > 0)
                    volumeFilter = $"第{volumeData.VolumeNumber}卷";
            }

            if (!string.IsNullOrWhiteSpace(volumeFilter))
            {
                sb.Append(await GetCoreDesignContextForVolumeAsync(volumeFilter));
            }
            else
            {
                sb.Append(await GetCoreDesignContextAsync());
            }
            sb.AppendLine();
            sb.Append(await BuildOutlineStringAsync());
            sb.AppendLine();

            if (volumeData == null)
            {
                sb.Append(await BuildVolumeDesignStringAsync());
                sb.AppendLine();
            }

            var planningVolumeKey = chapter != null
                ? (!string.IsNullOrWhiteSpace(chapter.CategoryId) ? chapter.CategoryId : chapter.Category)
                : null;
            sb.Append(await BuildPlanningStringForVolumeAsync(planningVolumeKey));

            if (chapter != null)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine($"<context_block type=\"chapter_locator\" title=\"{chapter.ChapterTitle}\">");
                sb.AppendLine($"- 章节序号：第{chapter.ChapterNumber}章");
                sb.AppendLine($"- 所属卷：{chapter.Category}");
                if (!string.IsNullOrWhiteSpace(chapter.ChapterTheme))
                    sb.AppendLine($"- 章节主题：{chapter.ChapterTheme}");
                if (!string.IsNullOrWhiteSpace(chapter.MainGoal))
                    sb.AppendLine($"- 本章目标：{chapter.MainGoal}");
                if (!string.IsNullOrWhiteSpace(chapter.ResistanceSource))
                    sb.AppendLine($"- 阻力来源：{chapter.ResistanceSource}");
                if (!string.IsNullOrWhiteSpace(chapter.KeyTurn))
                    sb.AppendLine($"- 关键转折：{chapter.KeyTurn}");
                if (!string.IsNullOrWhiteSpace(chapter.Hook))
                    sb.AppendLine($"- 结尾钉子：{chapter.Hook}");
                if (!string.IsNullOrWhiteSpace(chapter.WorldInfoDrop))
                    sb.AppendLine($"- 世界观投放：{chapter.WorldInfoDrop}");
                if (!string.IsNullOrWhiteSpace(chapter.CharacterArcProgress))
                    sb.AppendLine($"- 角色弧光推进：{chapter.CharacterArcProgress}");
                sb.AppendLine("</context_block>");

                if (volumeData != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<context_block type=\"parent_volume\" title=\"{volumeData.VolumeTitle}\">");
                    sb.AppendLine($"- 卷序号：第{volumeData.VolumeNumber}卷");
                    if (volumeData.StartChapter > 0 && volumeData.EndChapter > 0)
                        sb.AppendLine($"- 章节范围：第{volumeData.StartChapter}-{volumeData.EndChapter}章");
                    if (volumeData.TargetChapterCount > 0)
                        sb.AppendLine($"- 目标章节数：{volumeData.TargetChapterCount}");
                    if (!string.IsNullOrWhiteSpace(volumeData.VolumeTheme))
                        sb.AppendLine($"- 卷主题：{volumeData.VolumeTheme}");
                    if (!string.IsNullOrWhiteSpace(volumeData.StageGoal))
                        sb.AppendLine($"- 阶段目标：{volumeData.StageGoal}");
                    if (!string.IsNullOrWhiteSpace(volumeData.EstimatedWordCount))
                        sb.AppendLine($"- 预计字数：{volumeData.EstimatedWordCount}");
                    if (!string.IsNullOrWhiteSpace(volumeData.MainConflict))
                        sb.AppendLine($"- 卷主冲突：{volumeData.MainConflict}");
                    if (!string.IsNullOrWhiteSpace(volumeData.PressureSource))
                        sb.AppendLine($"- 压力来源：{volumeData.PressureSource}");
                    if (!string.IsNullOrWhiteSpace(volumeData.KeyEvents))
                        sb.AppendLine($"- 关键转折：{volumeData.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(volumeData.OpeningState))
                        sb.AppendLine($"- 卷开篇状态：{volumeData.OpeningState}");
                    if (!string.IsNullOrWhiteSpace(volumeData.EndingState))
                        sb.AppendLine($"- 卷收束状态：{volumeData.EndingState}");
                    if (!string.IsNullOrWhiteSpace(volumeData.ChapterAllocationOverview))
                        sb.AppendLine($"- 章节分配总览：{volumeData.ChapterAllocationOverview}");
                    if (!string.IsNullOrWhiteSpace(volumeData.PlotAllocation))
                        sb.AppendLine($"- 剧情分配：{volumeData.PlotAllocation}");
                    if (!string.IsNullOrWhiteSpace(volumeData.ChapterGenerationHints))
                        sb.AppendLine($"- 章节生成提示：{volumeData.ChapterGenerationHints}");
                    sb.AppendLine("</context_block>");
                }
            }

            return sb.ToString();
        }

        private async Task<VolumeDesignData?> GetVolumeDesignByCategoryAsync(string categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey)) return null;

            var key = categoryKey.Trim();

            try
            {
                var volumeDesigns = await LoadFunctionDataAsync<VolumeDesignData>("VolumeDesign");

                var candidates = volumeDesigns
                    .Where(v => v.IsEnabled)
                    .Where(IsInCurrentScope)
                    .ToList();

                var exact = candidates.FirstOrDefault(v =>
                    (!string.IsNullOrWhiteSpace(v.CategoryId) && string.Equals(v.CategoryId, key, StringComparison.Ordinal)) ||
                    string.Equals(v.Category, key, StringComparison.Ordinal) ||
                    string.Equals(v.Id, key, StringComparison.Ordinal) ||
                    string.Equals(v.Name, key, StringComparison.Ordinal) ||
                    string.Equals(v.VolumeTitle, key, StringComparison.Ordinal));
                if (exact != null) return exact;

                int volNum = 0;
                var match = System.Text.RegularExpressions.Regex.Match(key, @"(?:第\s*)?(\d+)\s*卷", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int.TryParse(match.Groups[1].Value, out volNum);
                }
                else
                {
                    var m2 = System.Text.RegularExpressions.Regex.Match(key, @"^vol(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m2.Success)
                        int.TryParse(m2.Groups[1].Value, out volNum);
                    else
                        int.TryParse(new string(key.Where(char.IsDigit).ToArray()), out volNum);
                }
                if (volNum > 0)
                {
                    var byNum = candidates.FirstOrDefault(v => v.VolumeNumber == volNum);
                    if (byNum != null) return byNum;
                }

                var fuzzy = candidates.FirstOrDefault(v =>
                    (!string.IsNullOrWhiteSpace(v.Name) && v.Name.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(v.VolumeTitle) && v.VolumeTitle.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(v.Category) && v.Category.Contains(key, StringComparison.OrdinalIgnoreCase)));
                return fuzzy;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetVolumeDesignByCategoryAsync失败: {ex.Message}");
                return null;
            }
        }

        private async Task<ChapterData?> GetChapterDataByChapterIdAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId)) return null;

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(chapterId, @"vol(\d+)_ch(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success) return null;

                var volumeNumber = int.Parse(match.Groups[1].Value);
                var chapterNumber = int.Parse(match.Groups[2].Value);
                var chapters = await LoadFunctionDataAsync<ChapterData>("Chapter");
                var enabled = chapters.Where(c => c.IsEnabled && IsInCurrentScope(c)).ToList();

                var volumePrefix = $"第{volumeNumber}卷";
                return enabled.FirstOrDefault(c =>
                    c.ChapterNumber == chapterNumber &&
                    (
                        (!string.IsNullOrEmpty(c.Volume) &&
                            (string.Equals(c.Volume, volumePrefix, StringComparison.Ordinal) ||
                             c.Volume.StartsWith(volumePrefix + " ", StringComparison.Ordinal))) ||
                        (!string.IsNullOrEmpty(c.Category) &&
                            (string.Equals(c.Category, volumePrefix, StringComparison.Ordinal) ||
                             c.Category.StartsWith(volumePrefix + " ", StringComparison.Ordinal))) ||
                        (!string.IsNullOrWhiteSpace(c.CategoryId) &&
                            (string.Equals(c.CategoryId, volumePrefix, StringComparison.Ordinal) ||
                             c.CategoryId.StartsWith(volumePrefix + " ", StringComparison.Ordinal)))
                    ));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] GetChapterDataByChapterIdAsync失败: {ex.Message}");
                return null;
            }
        }

        private async Task<string> BuildLocationStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<LocationRulesData>? preloaded = null,
            Dictionary<string, string>? factionIdToName = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildLocationStringAsync(preloaded, factionIdToName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<location_rules>");
            try
            {
                if (factionIdToName == null)
                {
                    var fr = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
                    factionIdToName = fr.Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
                        .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
                }
                var all = preloaded ?? await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
                var enabledAll = all.Where(i => i.IsEnabled && IsInCurrentScope(i)).ToList();
                var filtered = enabledAll.Where(i => HasLocationContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();

                if (filtered.Count == 0)
                    return await BuildLocationStringAsync();

                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.LocationType)) sb.AppendLine($"类型：{item.LocationType}");
                    if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"描述：{item.Description}");
                    if (!string.IsNullOrWhiteSpace(item.Scale)) sb.AppendLine($"规模：{item.Scale}");
                    if (!string.IsNullOrWhiteSpace(item.Terrain)) sb.AppendLine($"地形环境：{item.Terrain}");
                    if (!string.IsNullOrWhiteSpace(item.Climate)) sb.AppendLine($"气候特征：{item.Climate}");
                    if (item.Landmarks.Count > 0) sb.AppendLine($"标志地标：{string.Join("、", item.Landmarks)}");
                    if (item.Resources.Count > 0) sb.AppendLine($"特产资源：{string.Join("、", item.Resources)}");
                    if (!string.IsNullOrWhiteSpace(item.HistoricalSignificance)) sb.AppendLine($"历史意义：{item.HistoricalSignificance}");
                    if (item.Dangers.Count > 0) sb.AppendLine($"危险/禁忌：{string.Join("、", item.Dangers)}");
                    if (!string.IsNullOrWhiteSpace(item.FactionId))
                    {
                        var factionName = factionIdToName.TryGetValue(item.FactionId, out var n) ? n : item.FactionId;
                        sb.AppendLine($"所属势力：{factionName}");
                    }
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未涉及地点已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildLocationStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</location_rules>");
            return sb.ToString();
        }

        public Task<string> GetVolumeDesignListAsync() => BuildVolumeDesignStringAsync();

        private async Task<string> BuildVolumeDesignStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<volume_designs>");
            try
            {
                var volumes = await LoadFunctionDataAsync<Models.Generate.VolumeDesign.VolumeDesignData>("VolumeDesign");
                foreach (var item in volumes.Where(i => i.IsEnabled && IsInCurrentScope(i)))
                {
                    sb.AppendLine($"<item name=\"第{item.VolumeNumber}卷 {item.VolumeTitle}\">");
                    if (!string.IsNullOrWhiteSpace(item.VolumeTheme))
                        sb.AppendLine($"卷主题：{item.VolumeTheme}");
                    if (!string.IsNullOrWhiteSpace(item.StageGoal))
                        sb.AppendLine($"阶段目标：{item.StageGoal}");
                    if (!string.IsNullOrWhiteSpace(item.EstimatedWordCount))
                        sb.AppendLine($"预计字数：{item.EstimatedWordCount}");
                    if (item.TargetChapterCount > 0)
                        sb.AppendLine($"目标章节数：{item.TargetChapterCount}");
                    if (item.StartChapter > 0 && item.EndChapter > 0)
                        sb.AppendLine($"章节范围：第{item.StartChapter}章-第{item.EndChapter}章");
                    if (!string.IsNullOrWhiteSpace(item.MainConflict))
                        sb.AppendLine($"卷主冲突：{item.MainConflict}");
                    if (!string.IsNullOrWhiteSpace(item.PressureSource))
                        sb.AppendLine($"压力来源：{item.PressureSource}");
                    if (!string.IsNullOrWhiteSpace(item.KeyEvents))
                        sb.AppendLine($"关键转折：{item.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(item.OpeningState))
                        sb.AppendLine($"卷开篇状态：{item.OpeningState}");
                    if (!string.IsNullOrWhiteSpace(item.EndingState))
                        sb.AppendLine($"卷收束状态：{item.EndingState}");
                    if (!string.IsNullOrWhiteSpace(item.ChapterAllocationOverview))
                        sb.AppendLine($"章节分配总览：{item.ChapterAllocationOverview}");
                    if (!string.IsNullOrWhiteSpace(item.PlotAllocation))
                        sb.AppendLine($"剧情分配：{item.PlotAllocation}");
                    if (!string.IsNullOrWhiteSpace(item.ChapterGenerationHints))
                        sb.AppendLine($"章节生成提示：{item.ChapterGenerationHints}");
                    if (item.ReferencedCharacterNames.Count > 0)
                        sb.AppendLine($"本卷出场角色：{string.Join("、", item.ReferencedCharacterNames)}");
                    if (item.ReferencedFactionNames.Count > 0)
                        sb.AppendLine($"本卷涉及势力：{string.Join("、", item.ReferencedFactionNames)}");
                    if (item.ReferencedLocationNames.Count > 0)
                        sb.AppendLine($"本卷涉及地点：{string.Join("、", item.ReferencedLocationNames)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildVolumeDesignStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</volume_designs>");
            return sb.ToString();
        }

        private async Task<string> BuildWorldviewStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<worldview_rules>");
            try
            {
                var worldRules = await LoadFunctionDataAsync<WorldRulesData>("WorldRules");
                foreach (var item in worldRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasWorldRulesContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.OneLineSummary))
                        sb.AppendLine($"简介：{item.OneLineSummary}");
                    if (!string.IsNullOrWhiteSpace(item.PowerSystem))
                        sb.AppendLine($"力量体系：{item.PowerSystem}");
                    if (!string.IsNullOrWhiteSpace(item.Cosmology))
                        sb.AppendLine($"宇宙观：{item.Cosmology}");
                    if (!string.IsNullOrWhiteSpace(item.SpecialLaws))
                        sb.AppendLine($"特殊法则：{item.SpecialLaws}");
                    if (!string.IsNullOrWhiteSpace(item.HardRules))
                        sb.AppendLine($"硬规则：{item.HardRules}");
                    if (!string.IsNullOrWhiteSpace(item.SoftRules))
                        sb.AppendLine($"软规则：{item.SoftRules}");
                    if (!string.IsNullOrWhiteSpace(item.AncientEra))
                        sb.AppendLine($"创世/古代纪元：{item.AncientEra}");
                    if (!string.IsNullOrWhiteSpace(item.KeyEvents))
                        sb.AppendLine($"关键历史事件：{item.KeyEvents}");
                    if (!string.IsNullOrWhiteSpace(item.ModernHistory))
                        sb.AppendLine($"近代史：{item.ModernHistory}");
                    if (!string.IsNullOrWhiteSpace(item.StatusQuo))
                        sb.AppendLine($"故事开始前现状：{item.StatusQuo}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildWorldviewStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</worldview_rules>");
            return sb.ToString();
        }

        private static string ResolveId(string? idOrName, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            return map.TryGetValue(idOrName, out var n) ? n : idOrName;
        }

        private static string ResolveIds(string? ids, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
            return string.Join("、", ids.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => ResolveId(s, map)).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private async Task<(Dictionary<string, string> charIdToName,
                            Dictionary<string, string> factionIdToName,
                            Dictionary<string, string> locIdToName,
                            List<CharacterRulesData>   chars,
                            List<FactionRulesData>     factions,
                            List<LocationRulesData>    locations)> LoadEntityMapsAsync()
        {
            var chars    = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
            var factions = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
            var locs     = await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
            var charMap    = chars.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                                  .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
            var factionMap = factions.Where(f => !string.IsNullOrWhiteSpace(f.Id))
                                     .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
            var locMap     = locs.Where(l => !string.IsNullOrWhiteSpace(l.Id))
                                 .ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);
            return (charMap, factionMap, locMap, chars, factions, locs);
        }

        private async Task<string> BuildLocationStringAsync(
            List<LocationRulesData>? preloaded = null,
            Dictionary<string, string>? factionIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<location_rules>");
            try
            {
                if (factionIdToName == null)
                {
                    var factionRules = await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
                    factionIdToName = factionRules
                        .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
                        .ToDictionary(f => f.Id, f => f.Name, StringComparer.OrdinalIgnoreCase);
                }
                var locationRules = preloaded ?? await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
                foreach (var item in locationRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasLocationContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.LocationType))
                        sb.AppendLine($"类型：{item.LocationType}");
                    if (!string.IsNullOrWhiteSpace(item.Description))
                        sb.AppendLine($"描述：{item.Description}");
                    if (!string.IsNullOrWhiteSpace(item.Scale))
                        sb.AppendLine($"规模：{item.Scale}");
                    if (!string.IsNullOrWhiteSpace(item.Terrain))
                        sb.AppendLine($"地形环境：{item.Terrain}");
                    if (!string.IsNullOrWhiteSpace(item.Climate))
                        sb.AppendLine($"气候特征：{item.Climate}");
                    if (item.Landmarks.Count > 0)
                        sb.AppendLine($"标志地标：{string.Join("、", item.Landmarks)}");
                    if (item.Resources.Count > 0)
                        sb.AppendLine($"特产资源：{string.Join("、", item.Resources)}");
                    if (!string.IsNullOrWhiteSpace(item.HistoricalSignificance))
                        sb.AppendLine($"历史意义：{item.HistoricalSignificance}");
                    if (item.Dangers.Count > 0)
                        sb.AppendLine($"危险/禁忌：{string.Join("、", item.Dangers)}");
                    if (!string.IsNullOrWhiteSpace(item.FactionId))
                    {
                        var factionName = factionIdToName.TryGetValue(item.FactionId, out var n) ? n : item.FactionId;
                        sb.AppendLine($"所属势力：{factionName}");
                    }
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildLocationStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</location_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterSummaryStringAsync(List<CharacterRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                foreach (var item in characterRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterArcStringAsync(
            List<CharacterRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                charIdToName ??= characterRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in characterRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    if (!string.IsNullOrWhiteSpace(item.Need)) sb.AppendLine($"内在需求：{item.Need}");
                    if (!string.IsNullOrWhiteSpace(item.FlawBelief)) sb.AppendLine($"致命缺点：{item.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(item.GrowthPath)) sb.AppendLine($"成长路径：{item.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(item.TargetCharacterName))
                    {
                        var tn = ResolveId(item.TargetCharacterName, charIdToName);
                        if (!string.IsNullOrWhiteSpace(tn)) sb.AppendLine($"关联角色：{tn}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.RelationshipType)) sb.AppendLine($"关系类型：{item.RelationshipType}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionDynamic)) sb.AppendLine($"情感动态：{item.EmotionDynamic}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterArcStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterMinimalStringAsync(List<CharacterRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var characterRules = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                foreach (var item in characterRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasCharacterContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterMinimalStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildFactionSummaryStringAsync(
            List<FactionRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<faction_rules>");
            try
            {
                var factionRules = preloaded ?? await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
                if (charIdToName == null)
                {
                    var charRules = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                    charIdToName = charRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                foreach (var item in factionRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasFactionContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.FactionType)) sb.AppendLine($"类型：{item.FactionType}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"理念目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.Leader)) sb.AppendLine($"领袖：{ResolveId(item.Leader, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.StrengthTerritory)) sb.AppendLine($"实力/地盘：{item.StrengthTerritory}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildFactionSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</faction_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildLocationSummaryStringAsync(List<LocationRulesData>? preloaded = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<location_rules>");
            try
            {
                var locationRules = preloaded ?? await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
                foreach (var item in locationRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasLocationContent(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.LocationType)) sb.AppendLine($"类型：{item.LocationType}");
                    if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"描述：{item.Description}");
                    if (item.Dangers.Count > 0) sb.AppendLine($"危险/禁忌：{string.Join("、", item.Dangers)}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildLocationSummaryStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</location_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildCharacterArcStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<CharacterRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildCharacterArcStringAsync(preloaded, charIdToName);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<character_rules>");
            try
            {
                var all = preloaded ?? await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                charIdToName ??= all.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                var enabledAll = all.Where(i => i.IsEnabled && IsInCurrentScope(i)).ToList();
                var filtered = enabledAll.Where(i => HasCharacterContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count == 0)
                    return await BuildCharacterArcStringAsync(all, charIdToName);
                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.CharacterType)) sb.AppendLine($"角色类型：{item.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(item.Identity)) sb.AppendLine($"身份：{item.Identity}");
                    if (!string.IsNullOrWhiteSpace(item.Want)) sb.AppendLine($"外在目标：{item.Want}");
                    if (!string.IsNullOrWhiteSpace(item.Need)) sb.AppendLine($"内在需求：{item.Need}");
                    if (!string.IsNullOrWhiteSpace(item.FlawBelief)) sb.AppendLine($"致命缺点：{item.FlawBelief}");
                    if (!string.IsNullOrWhiteSpace(item.GrowthPath)) sb.AppendLine($"成长路径：{item.GrowthPath}");
                    if (!string.IsNullOrWhiteSpace(item.TargetCharacterName))
                    {
                        var tn = ResolveId(item.TargetCharacterName, charIdToName);
                        if (!string.IsNullOrWhiteSpace(tn)) sb.AppendLine($"关联角色：{tn}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.RelationshipType)) sb.AppendLine($"关系类型：{item.RelationshipType}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionDynamic)) sb.AppendLine($"情感动态：{item.EmotionDynamic}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未出场角色已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildCharacterArcStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</character_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildFactionSummaryStringForVolumeAsync(
            IReadOnlyCollection<string> nameFilter,
            List<FactionRulesData>? preloaded = null,
            Dictionary<string, string>? charIdToName = null)
        {
            if (nameFilter == null || nameFilter.Count == 0)
                return await BuildFactionSummaryStringAsync(preloaded, charIdToName);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<faction_rules>");
            try
            {
                var all = preloaded ?? await LoadFunctionDataAsync<FactionRulesData>("FactionRules");
                if (charIdToName == null)
                {
                    var charRules = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                    charIdToName = charRules.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                var enabledAll = all.Where(i => i.IsEnabled && IsInCurrentScope(i)).ToList();
                var filtered = enabledAll.Where(i => HasFactionContent(i) &&
                    nameFilter.Any(n =>
                        string.Equals(n, i.Name, StringComparison.OrdinalIgnoreCase) ||
                        i.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                        n.Contains(i.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count == 0)
                    return await BuildFactionSummaryStringAsync(all, charIdToName);
                foreach (var item in filtered)
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (!string.IsNullOrWhiteSpace(item.FactionType)) sb.AppendLine($"类型：{item.FactionType}");
                    if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"理念目标：{item.Goal}");
                    if (!string.IsNullOrWhiteSpace(item.Leader)) sb.AppendLine($"领袖：{ResolveId(item.Leader, charIdToName)}");
                    if (!string.IsNullOrWhiteSpace(item.StrengthTerritory)) sb.AppendLine($"实力/地盘：{item.StrengthTerritory}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
                if (filtered.Count < enabledAll.Count)
                    sb.AppendLine($"（另有 {enabledAll.Count - filtered.Count} 个未出场势力已过滤）");
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildFactionSummaryStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</faction_rules>");
            return sb.ToString();
        }

        private async Task<string> BuildOutlineStringAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<strategic_outline>");
            try
            {
                var outlines = await LoadFunctionDataAsync<Models.Generate.StrategicOutline.OutlineData>("Outline");
                foreach (var item in outlines.Where(i => i.IsEnabled && IsInCurrentScope(i)))
                {
                    sb.AppendLine($"<item name=\"{item.Name}\">");
                    if (item.TotalChapterCount > 0)
                        sb.AppendLine($"全书总章节数：{item.TotalChapterCount}");
                    if (!string.IsNullOrWhiteSpace(item.EstimatedWordCount))
                        sb.AppendLine($"预计总字数：{item.EstimatedWordCount}");
                    if (!string.IsNullOrWhiteSpace(item.OneLineOutline))
                        sb.AppendLine($"一句话大纲：{item.OneLineOutline}");
                    if (!string.IsNullOrWhiteSpace(item.EmotionalTone))
                        sb.AppendLine($"情感基调：{item.EmotionalTone}");
                    if (!string.IsNullOrWhiteSpace(item.PhilosophicalMotif))
                        sb.AppendLine($"哲学母题：{item.PhilosophicalMotif}");
                    if (!string.IsNullOrWhiteSpace(item.Theme))
                        sb.AppendLine($"主题思想：{item.Theme}");
                    if (!string.IsNullOrWhiteSpace(item.CoreConflict))
                        sb.AppendLine($"核心冲突：{item.CoreConflict}");
                    if (!string.IsNullOrWhiteSpace(item.EndingState))
                        sb.AppendLine($"结局/目标状态：{item.EndingState}");
                    if (!string.IsNullOrWhiteSpace(item.VolumeDivision))
                        sb.AppendLine($"卷/幕划分：{item.VolumeDivision}");
                    if (!string.IsNullOrWhiteSpace(item.OutlineOverview))
                        sb.AppendLine($"大纲总览：{item.OutlineOverview}");
                    sb.AppendLine("</item>");
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildOutlineStringAsync失败: {ex.Message}"); }
            sb.AppendLine("</strategic_outline>");
            return sb.ToString();
        }

        private static void AppendChapterFull(System.Text.StringBuilder sb, Models.Generate.ChapterPlanning.ChapterData item)
        {
            sb.AppendLine($"<item name=\"第{item.ChapterNumber}章 {item.ChapterTitle}\">");
            if (!string.IsNullOrWhiteSpace(item.Volume))
                sb.AppendLine($"所属卷：{item.Volume}");
            if (!string.IsNullOrWhiteSpace(item.EstimatedWordCount))
                sb.AppendLine($"预计字数：{item.EstimatedWordCount}");
            if (!string.IsNullOrWhiteSpace(item.ChapterTheme))
                sb.AppendLine($"章节主题：{item.ChapterTheme}");
            if (!string.IsNullOrWhiteSpace(item.ReaderExperienceGoal))
                sb.AppendLine($"读者体验目标：{item.ReaderExperienceGoal}");
            if (!string.IsNullOrWhiteSpace(item.MainGoal))
                sb.AppendLine($"章节主目标：{item.MainGoal}");
            if (!string.IsNullOrWhiteSpace(item.ResistanceSource))
                sb.AppendLine($"阻力来源：{item.ResistanceSource}");
            if (!string.IsNullOrWhiteSpace(item.KeyTurn))
                sb.AppendLine($"关键转折：{item.KeyTurn}");
            if (!string.IsNullOrWhiteSpace(item.Hook))
                sb.AppendLine($"结尾钉子：{item.Hook}");
            if (!string.IsNullOrWhiteSpace(item.WorldInfoDrop))
                sb.AppendLine($"世界观投放：{item.WorldInfoDrop}");
            if (!string.IsNullOrWhiteSpace(item.CharacterArcProgress))
                sb.AppendLine($"角色弧光推进：{item.CharacterArcProgress}");
            if (!string.IsNullOrWhiteSpace(item.MainPlotProgress))
                sb.AppendLine($"主线推进点：{item.MainPlotProgress}");
            if (!string.IsNullOrWhiteSpace(item.Foreshadowing))
                sb.AppendLine($"伏笔埋设/回收：{item.Foreshadowing}");
            sb.AppendLine("</item>");
            sb.AppendLine();
        }

        private async Task<string> BuildPlanningStringForVolumeAsync(string? volumeCategory)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var chapters = await LoadDataListAsync<Models.Generate.ChapterPlanning.ChapterData>(
                    "Modules/Generate/Elements/Chapter", "chapter_data.json");
                chapters = chapters.Where(IsInCurrentScope).ToList();

                IEnumerable<Models.Generate.ChapterPlanning.ChapterData> filtered;
                if (string.IsNullOrWhiteSpace(volumeCategory))
                {
                    filtered = chapters;
                    sb.AppendLine("<chapter_plan>");
                }
                else
                {
                    var key = volumeCategory.Trim();
                    var keyVolNum = ExtractVolumeNumberFromText(key);
                    var volumeChapters = chapters.Where(c =>
                        string.Equals(c.Category, key, StringComparison.Ordinal) ||
                        (!string.IsNullOrWhiteSpace(c.CategoryId) && string.Equals(c.CategoryId, key, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(c.Volume) && (
                            string.Equals(c.Volume, key, StringComparison.Ordinal) ||
                            c.Volume.StartsWith(key + " ", StringComparison.Ordinal) ||
                            (keyVolNum > 0 && ExtractVolumeNumberFromText(c.Volume) == keyVolNum))))
                        .ToList();
                    filtered = volumeChapters;

                    sb.AppendLine($"<chapter_plan volume=\"{volumeCategory}\" count=\"{volumeChapters.Count}/{chapters.Count}\">");

                    TM.App.Log($"[ContextService] 章节规划按卷过滤: 当前卷={volumeCategory}, 注入={volumeChapters.Count}, 总数={chapters.Count}");
                }

                foreach (var item in filtered)
                    AppendChapterFull(sb, item);
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] BuildPlanningStringForVolumeAsync失败: {ex.Message}"); }
            sb.AppendLine("</chapter_plan>");
            return sb.ToString();
        }

        public async Task<string> GetCoreDesignContextAsync()
        {
            TM.App.Log("[ContextService] 构建CoreDesignContext");
            var (charMap, factionMap, locMap, chars, factions, locs) = await LoadEntityMapsAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_data>");
            sb.Append(await GetCreativeMaterialsContextAsync());
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());
            sb.Append(await BuildCharacterArcStringAsync(chars, charMap));
            sb.Append(await BuildFactionSummaryStringAsync(factions, charMap));
            sb.Append(await BuildLocationStringAsync(locs, factionMap));
            sb.Append(await BuildPlotRulesStringAsync(null, charMap, locMap));
            sb.AppendLine("</design_data>");
            return sb.ToString();
        }

        public async Task<string> GetCoreDesignContextForVolumeAsync(string volumeKey)
        {
            TM.App.Log($"[ContextService] 构建CoreDesignContext（按卷过滤：{volumeKey}）");
            var (charMap, factionMap, locMap, chars, factions, locs) = await LoadEntityMapsAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_data>");
            sb.Append(await GetCreativeMaterialsContextAsync());
            sb.AppendLine();
            sb.Append(await BuildWorldviewStringAsync());

            var volume = await GetVolumeDesignByCategoryAsync(volumeKey);
            if (volume != null)
            {
                sb.Append(volume.ReferencedCharacterNames.Count > 0
                    ? await BuildCharacterArcStringForVolumeAsync(volume.ReferencedCharacterNames, chars, charMap)
                    : await BuildCharacterArcStringAsync(chars, charMap));
                sb.Append(volume.ReferencedFactionNames.Count > 0
                    ? await BuildFactionSummaryStringForVolumeAsync(volume.ReferencedFactionNames, factions, charMap)
                    : await BuildFactionSummaryStringAsync(factions, charMap));
                sb.Append(volume.ReferencedLocationNames.Count > 0
                    ? await BuildLocationStringForVolumeAsync(volume.ReferencedLocationNames, locs, factionMap)
                    : await BuildLocationStringAsync(locs, factionMap));
                TM.App.Log($"[ContextService] 实体注入（按卷）: 角色名单={volume.ReferencedCharacterNames.Count}, 势力名单={volume.ReferencedFactionNames.Count}, 地点名单={volume.ReferencedLocationNames.Count}");
            }
            else
            {
                sb.Append(await BuildCharacterArcStringAsync(chars, charMap));
                sb.Append(await BuildFactionSummaryStringAsync(factions, charMap));
                sb.Append(await BuildLocationStringAsync(locs, factionMap));
            }

            sb.Append(await BuildPlotRulesStringAsync(volume != null ? volumeKey : null, charMap, locMap));
            sb.AppendLine("</design_data>");
            return sb.ToString();
        }

        private async Task<string> BuildPlotRulesStringAsync(
            string? volumeFilter,
            Dictionary<string, string>? charIdToName = null,
            Dictionary<string, string>? locIdToName = null)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var plotRules = await LoadFunctionDataAsync<PlotRulesData>("PlotRules");
                var enabledRules = plotRules.Where(i => i.IsEnabled && IsInCurrentScope(i) && HasPlotContent(i)).ToList();
                if (charIdToName == null)
                {
                    var cr = await LoadFunctionDataAsync<CharacterRulesData>("CharacterRules");
                    charIdToName = cr.Where(c => !string.IsNullOrWhiteSpace(c.Id))
                        .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
                }
                if (locIdToName == null)
                {
                    var lr = await LoadFunctionDataAsync<LocationRulesData>("LocationRules");
                    locIdToName = lr.Where(l => !string.IsNullOrWhiteSpace(l.Id))
                        .ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);
                }
                if (string.IsNullOrWhiteSpace(volumeFilter))
                {
                    sb.AppendLine("<plot_rules>");
                    foreach (var item in enabledRules)
                        AppendPlotRuleFull(sb, item, charIdToName, locIdToName);
                }
                else
                {
                    var filterVolNum = ExtractVolumeNumberFromText(volumeFilter);
                    var injected = enabledRules.Where(r =>
                        r.EventType == "主线剧情" ||
                        r.AssignedVolume == "全局" ||
                        string.IsNullOrWhiteSpace(r.AssignedVolume) ||
                        string.Equals(r.AssignedVolume, volumeFilter, StringComparison.OrdinalIgnoreCase) ||
                        (filterVolNum > 0 && ExtractVolumeNumberFromText(r.AssignedVolume) == filterVolNum)
                    ).ToList();
                    var skipped = enabledRules.Count - injected.Count;
                    sb.AppendLine($"<plot_rules volume=\"{volumeFilter}\">");
                    foreach (var item in injected)
                        AppendPlotRuleFull(sb, item, charIdToName, locIdToName);
                    if (skipped > 0) sb.AppendLine($"（另有 {skipped} 条其他卷剧情未注入）");
                    TM.App.Log($"[ContextService] 剧情按卷过滤: 注入={injected.Count}, 跳过={skipped}");
                }
            }
            catch (Exception ex) { TM.App.Log($"[ContextService] 加载PlotRules失败: {ex.Message}"); }
            sb.AppendLine("</plot_rules>");
            return sb.ToString();
        }

        private void AppendPlotRuleFull(
            System.Text.StringBuilder sb,
            PlotRulesData item,
            Dictionary<string, string> charIdToName,
            Dictionary<string, string> locIdToName)
        {
            sb.AppendLine($"<item name=\"{item.Name}\">");
            if (!string.IsNullOrWhiteSpace(item.AssignedVolume)) sb.AppendLine($"所属卷：{item.AssignedVolume}");
            if (!string.IsNullOrWhiteSpace(item.OneLineSummary)) sb.AppendLine($"简介：{item.OneLineSummary}");
            if (!string.IsNullOrWhiteSpace(item.EventType)) sb.AppendLine($"事件类型：{item.EventType}");
            if (!string.IsNullOrWhiteSpace(item.StoryPhase)) sb.AppendLine($"所属阶段：{item.StoryPhase}");
            if (!string.IsNullOrWhiteSpace(item.PrerequisitesTrigger)) sb.AppendLine($"前置条件：{item.PrerequisitesTrigger}");
            if (!string.IsNullOrWhiteSpace(item.MainCharacters)) sb.AppendLine($"主要角色：{ResolveIds(item.MainCharacters, charIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.KeyNpcs)) sb.AppendLine($"关键NPC：{ResolveIds(item.KeyNpcs, charIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.Location)) sb.AppendLine($"地点：{ResolveId(item.Location, locIdToName)}");
            if (!string.IsNullOrWhiteSpace(item.TimeDuration)) sb.AppendLine($"时间跨度：{item.TimeDuration}");
            if (!string.IsNullOrWhiteSpace(item.StepTitle)) sb.AppendLine($"步骤标题：{item.StepTitle}");
            if (!string.IsNullOrWhiteSpace(item.Goal)) sb.AppendLine($"目标：{item.Goal}");
            if (!string.IsNullOrWhiteSpace(item.Conflict)) sb.AppendLine($"冲突：{item.Conflict}");
            if (!string.IsNullOrWhiteSpace(item.Result)) sb.AppendLine($"结果：{item.Result}");
            if (!string.IsNullOrWhiteSpace(item.EmotionCurve)) sb.AppendLine($"情绪曲线：{item.EmotionCurve}");
            if (!string.IsNullOrWhiteSpace(item.MainPlotPush)) sb.AppendLine($"主线推动：{item.MainPlotPush}");
            if (!string.IsNullOrWhiteSpace(item.CharacterGrowth)) sb.AppendLine($"角色成长：{item.CharacterGrowth}");
            if (!string.IsNullOrWhiteSpace(item.WorldReveal)) sb.AppendLine($"世界观揭示：{item.WorldReveal}");
            if (!string.IsNullOrWhiteSpace(item.RewardsClues)) sb.AppendLine($"奖励/线索：{item.RewardsClues}");
            sb.AppendLine("</item>");
            sb.AppendLine();
        }

        #endregion

        #region ContentCheck

        private static int ExtractVolumeNumberFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var digitMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"(?:第\s*|[Vv]ol[_\s]?)(\d+)\s*卷?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (digitMatch.Success && int.TryParse(digitMatch.Groups[1].Value, out var n) && n > 0)
                return n;

            var chineseMap = new Dictionary<char, int>
            {
                ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4, ['五'] = 5,
                ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9, ['十'] = 10
            };
            var chineseMatch = System.Text.RegularExpressions.Regex.Match(text, @"第([一二三四五六七八九十]+)卷");
            if (chineseMatch.Success)
            {
                var chStr = chineseMatch.Groups[1].Value;
                if (chStr.Length == 1 && chineseMap.TryGetValue(chStr[0], out var cv))
                    return cv;
                if (chStr.Length == 2)
                {
                    if (chStr[0] == '十' && chineseMap.TryGetValue(chStr[1], out var cv2))
                        return 10 + cv2;
                    if (chStr[1] == '十' && chineseMap.TryGetValue(chStr[0], out var cv3))
                        return cv3 * 10;
                }
                if (chStr.Length == 3 && chStr[1] == '十'
                    && chineseMap.TryGetValue(chStr[0], out var tens)
                    && chineseMap.TryGetValue(chStr[2], out var ones))
                    return tens * 10 + ones;
            }
            return 0;
        }

        private static bool HasWorldRulesContent(WorldRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.PowerSystem) ||
                   !string.IsNullOrWhiteSpace(data.HardRules);
        }

        private static bool HasCharacterContent(CharacterRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Identity) ||
                   !string.IsNullOrWhiteSpace(data.Want) ||
                   !string.IsNullOrWhiteSpace(data.Need);
        }

        private static bool HasFactionContent(FactionRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Goal) ||
                   !string.IsNullOrWhiteSpace(data.FactionType);
        }

        private static bool HasLocationContent(LocationRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.Description) ||
                   !string.IsNullOrWhiteSpace(data.Terrain);
        }

        private static bool HasPlotContent(PlotRulesData data)
        {
            return !string.IsNullOrWhiteSpace(data.Name) ||
                   !string.IsNullOrWhiteSpace(data.OneLineSummary) ||
                   !string.IsNullOrWhiteSpace(data.Goal);
        }

        #endregion
    }
}
