using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Contexts.Design;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Modules.Generate.Elements.VolumeDesign.Services;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class PublishService : IPublishService
    {
        private readonly IChangeDetectionService _changeDetectionService;
        private readonly IWorkScopeService _workScopeService;
        private readonly GuideContextService _guideContextService;
        private ManifestInfo? _cachedManifest;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static readonly Dictionary<string, (int Words, DateTime Modified)> _wordCountCache = new();
        private static readonly object _wordCountCacheLock = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[PublishService] {key}: {ex.Message}");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public PublishService(
            IChangeDetectionService changeDetectionService, 
            IWorkScopeService workScopeService,
            GuideContextService guideContextService)
        {
            _changeDetectionService = changeDetectionService;
            _workScopeService = workScopeService;
            _guideContextService = guideContextService;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _cachedManifest = null;
                    lock (_wordCountCacheLock) { _wordCountCache.Clear(); }
                    try
                    {
                        _guideContextService.ClearCache();
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static Dictionary<string, HashSet<string>> PackageSubModuleAllowlist => PackagingAllowlist.SubModules;

        private List<PackageMapping> GetPackageMappings()
        {
            var mappings = new List<PackageMapping>();

            foreach (var pair in PackageSubModuleAllowlist)
            {
                var moduleType = pair.Key;
                var allowlist = pair.Value;
                var subModules = NavigationConfigParser.GetSubModules(moduleType);

                foreach (var (subModule, displayName) in subModules)
                {
                    if (!allowlist.Contains(displayName))
                        continue;

                    var modulePath = $"{moduleType}/{subModule}";
                    var status = _changeDetectionService.GetStatus(modulePath);
                    if (!status.IsEnabled)
                        continue;

                    var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);
                    if (functions.Count == 0)
                    {
                        TM.App.Log($"[PublishService] 未找到功能: {moduleType}/{subModule}，跳过打包");
                        continue;
                    }
                    var functionNames = functions.Select(f => f.FunctionName).ToArray();

                    var targetFile = $"{subModule.ToLower()}.json";

                    mappings.Add(new PackageMapping(
                        moduleType,
                        subModule,
                        functionNames,
                        targetFile
                    ));
                }
            }

            return mappings;
        }

        #region 公共方法

        public async Task<PublishResult> PublishAllAsync()
        {
            TM.App.Log("[PublishService] 开始打包所有模块");

            var backupPath = string.Empty;
            var packagedModules = new List<string>();

            try
            {
                try
                {
                    var volumeService = ServiceLocator.Get<VolumeDesignService>();
                    await volumeService.InitializeAsync();
                    var _publishScopeId = await ServiceLocator.Get<IWorkScopeService>().GetCurrentScopeAsync();
                    var enabledVolumes = volumeService.GetAllVolumeDesigns()
                        .Where(v => v.IsEnabled && v.VolumeNumber > 0
                               && (string.IsNullOrEmpty(_publishScopeId) || string.Equals(v.SourceBookId, _publishScopeId, StringComparison.Ordinal)))
                        .ToList();
                    if (enabledVolumes.Count > 1)
                    {
                        var maxVolumeNumber = enabledVolumes.Max(v => v.VolumeNumber);
                        var unconfigured = enabledVolumes
                            .Where(v => v.EndChapter <= 0 && v.VolumeNumber < maxVolumeNumber)
                            .Select(v => v.VolumeNumber).ToList();
                        if (unconfigured.Count > 0)
                        {
                            var volList = string.Join("、", unconfigured.Select(n => $"第{n}卷"));
                            TM.App.Log($"[PublishService] L-004.1 阻断：{volList}未配置EndChapter，拒绝打包");
                            return PublishResult.Failed($"{volList}未配置\"结束章节\"", "跨卷角色状态基线存档将无法触发，可能导致剧情断裂。请在分卷设计中为每卷填写结束章节号后重新打包。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] L-004.1 EndChapter前置检查异常（非致命，允许继续）: {ex.Message}");
                }

                {
                    var allMappings = GetPackageMappings();
                    var storageRoot = StoragePathHelper.GetStorageRoot();
                    var missingFunctions = new List<string>();

                    foreach (var mapping in allMappings)
                    {
                        var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
                        foreach (var subDir in mapping.SubDirectories)
                        {
                            var subDirPath = Path.Combine(sourceBasePath, subDir);
                            var hasData = Directory.Exists(subDirPath)
                                          && Directory.GetFiles(subDirPath, "*.json").Length > 0;
                            if (!hasData)
                            {
                                var subModuleName = NavigationConfigParser.GetSubModuleDisplayName(mapping.SubModule);
                                var functionName = NavigationConfigParser.GetDisplayName(subDir);
                                missingFunctions.Add($"{subModuleName}/{functionName}");
                            }
                        }
                    }

                    if (missingFunctions.Count > 0)
                    {
                        var detail = string.Join("、", missingFunctions);
                        TM.App.Log($"[PublishService] 打包阻断：以下业务缺失构建数据: {detail}");
                        return PublishResult.Failed(
                            $"以下业务尚未构建数据，无法打包：{detail}。请先完成构建后重新打包。");
                    }
                }

                backupPath = await CreateBackupAsync();
                TM.App.Log($"[PublishService] 已创建备份: {backupPath}");

                EnsureDirectoriesExist();

                foreach (var mapping in GetPackageMappings())
                {
                    await PackageModuleAsync(mapping);
                    packagedModules.Add($"{mapping.ModuleType}/{mapping.SubModule}");
                }

                var version = await UpdateManifestAsync();
                TM.App.Log($"[PublishService] 已更新manifest，版本: {version}");

                await GenerateGuideFilesAsync();

                _changeDetectionService.MarkAllAsPackaged();

                var integrityResult = await ValidatePackagedDataIntegrityAsync();
                if (!integrityResult.IsValid)
                {
                    GlobalToast.Warning("打包警告", integrityResult.Message);
                }

                await RefreshServiceCachesAsync();

                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                    var _manifestBak = backupPath + ".manifest.json";
                    if (File.Exists(_manifestBak)) File.Delete(_manifestBak);
                }

                var completenessWarnings = await new ContextIdsCompletenessChecker(_guideContextService).RunAsync();
                NotifyCompletenessWarnings(completenessWarnings);

                TM.App.Log("[PublishService] 打包完成");
                return PublishResult.Success(version, packagedModules);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 打包失败: {ex.Message}");

                if (!string.IsNullOrEmpty(backupPath))
                {
                    try
                    {
                        await RestoreBackupAsync(backupPath);
                        TM.App.Log("[PublishService] 已回滚到备份");
                    }
                    catch (Exception rbEx)
                    {
                        TM.App.Log($"[PublishService] 回滚失败（非致命）: {rbEx.Message}");
                    }
                }

                _cachedManifest = null;

                try { ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict(); }
                catch { }

                try
                {
                    GuideContextService.RaiseCacheInvalidated();
                    await _guideContextService.InitializeCacheAsync();
                }
                catch (Exception cacheEx)
                {
                    TM.App.Log($"[PublishService] 回滚后预热缓存失败（非致命）: {cacheEx.Message}");
                }

                try { await _changeDetectionService.RefreshAllAsync(); } catch { }

                return PublishResult.Failed("打包失败", ex.Message);
            }
        }

        public async Task<PublishResult> PublishModuleAsync(string moduleName)
        {
            TM.App.Log($"[PublishService] 开始打包模块: {moduleName}");

            var packagedModules = new List<string>();
            var backupPath = string.Empty;

            try
            {
                {
                    var mappingsForModule = GetPackageMappings().Where(m => m.ModuleType == moduleName).ToList();
                    var storageRoot = StoragePathHelper.GetStorageRoot();
                    var missingFunctions = new List<string>();

                    foreach (var mapping in mappingsForModule)
                    {
                        var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
                        foreach (var subDir in mapping.SubDirectories)
                        {
                            var subDirPath = Path.Combine(sourceBasePath, subDir);
                            var hasData = Directory.Exists(subDirPath)
                                          && Directory.GetFiles(subDirPath, "*.json").Length > 0;
                            if (!hasData)
                            {
                                var subModuleName = NavigationConfigParser.GetSubModuleDisplayName(mapping.SubModule);
                                var functionName = NavigationConfigParser.GetDisplayName(subDir);
                                missingFunctions.Add($"{subModuleName}/{functionName}");
                            }
                        }
                    }

                    if (missingFunctions.Count > 0)
                    {
                        var detail = string.Join("、", missingFunctions);
                        TM.App.Log($"[PublishService] 单模块打包阻断：以下业务缺失构建数据: {detail}");
                        return PublishResult.Failed(
                            $"以下业务尚未构建数据，无法打包：{detail}。请先完成构建后重新打包。");
                    }
                }

                backupPath = await CreateBackupAsync();
                TM.App.Log($"[PublishService] 单模块打包已创建备份: {backupPath}");

                EnsureDirectoriesExist();

                var mappings = GetPackageMappings().Where(m => m.ModuleType == moduleName).ToList();

                foreach (var mapping in mappings)
                {
                    await PackageModuleAsync(mapping);
                    packagedModules.Add($"{mapping.ModuleType}/{mapping.SubModule}");
                    _changeDetectionService.MarkAsPackaged($"{mapping.ModuleType}/{mapping.SubModule}");
                }

                var version = await UpdateManifestAsync();

                await GenerateGuideFilesAsync();

                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                    var _manifestBak = backupPath + ".manifest.json";
                    if (File.Exists(_manifestBak)) File.Delete(_manifestBak);
                }

                try { await RefreshServiceCachesAsync(); }
                catch (Exception cacheEx) { TM.App.Log($"[PublishService] 单模块打包：刷新缓存失败（非致命）: {cacheEx.Message}"); }

                try
                {
                    var integrityResult = await ValidatePackagedDataIntegrityAsync();
                    if (!integrityResult.IsValid)
                        GlobalToast.Warning("打包警告", integrityResult.Message);
                }
                catch { }

                try
                {
                    var completenessWarnings = await new ContextIdsCompletenessChecker(_guideContextService).RunAsync();
                    NotifyCompletenessWarnings(completenessWarnings);
                }
                catch { }

                return PublishResult.Success(version, packagedModules);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 打包模块失败 [{moduleName}]: {ex.Message}");

                if (!string.IsNullOrEmpty(backupPath))
                {
                    try
                    {
                        await RestoreBackupAsync(backupPath);
                        TM.App.Log("[PublishService] 单模块打包已回滚到备份");
                    }
                    catch (Exception rbEx)
                    {
                        TM.App.Log($"[PublishService] 单模块打包回滚失败（非致命）: {rbEx.Message}");
                    }
                }

                _cachedManifest = null;
                try { ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict(); } catch { }
                try
                {
                    GuideContextService.RaiseCacheInvalidated();
                    await _guideContextService.InitializeCacheAsync();
                }
                catch { }
                try { await _changeDetectionService.RefreshAllAsync(); } catch { }

                return PublishResult.Failed($"打包{moduleName}失败", ex.Message);
            }
        }

        public PublishStatus GetPublishStatus()
        {
            var manifest = GetManifest();
            var changedModules = _changeDetectionService.GetChangedModules();

            return new PublishStatus
            {
                IsPublished = manifest != null,
                LastPublishTime = manifest?.PublishTime,
                CurrentVersion = manifest?.Version ?? 0,
                NeedsRepublish = changedModules.Count > 0,
                ChangedModuleCount = changedModules.Count
            };
        }

        public ManifestInfo? GetManifest()
        {
            if (_cachedManifest != null)
                return _cachedManifest;

            try
            {
                var manifestPath = GetManifestPath();
                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    _cachedManifest = JsonSerializer.Deserialize<ManifestInfo>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 读取manifest失败: {ex.Message}");
            }

            return _cachedManifest;
        }

        public bool NeedsRepublish()
        {
            return _changeDetectionService.GetChangedModules().Count > 0;
        }

        public void ClearCache()
        {
            _cachedManifest = null;
            TM.App.Log("[PublishService] 缓存已清除");
        }

        private async Task RefreshServiceCachesAsync()
        {
            try
            {
                GuideContextService.RaiseCacheInvalidated();

                _cachedManifest = null;
                EntityNameResolver.Invalidate();

                await _guideContextService.InitializeCacheAsync();

                TM.App.Log("[PublishService] 已刷新并预热缓存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 刷新缓存失败: {ex.Message}");
            }
        }

        private static void NotifyCompletenessWarnings(CompletenessWarnings? warnings)
        {
            if (warnings == null) return;

            var parts = new List<string>();
            if (warnings.EmptyContextWarnings.Count > 0)
                parts.Add($"{warnings.EmptyContextWarnings.Count}个章节蓝图缺少角色/地点/世界观规则关联");
            if (warnings.ForeshadowingWarnings.Count > 0)
                parts.Add($"{warnings.ForeshadowingWarnings.Count}条伏笔已埋设但无揭示计划");
            if (warnings.ConflictWarnings.Count > 0)
                parts.Add($"{warnings.ConflictWarnings.Count}条冲突活跃但无后续章节追踪");

            if (parts.Count > 0)
                GlobalToast.Warning("蓝图完整性提示", string.Join("；", parts), 5000);
        }

        private async Task<DataIntegrityResult> ValidatePackagedDataIntegrityAsync()
        {
            try
            {
                var configPath = StoragePathHelper.GetProjectConfigPath();

                var designGlobalPath = Path.Combine(configPath, "Design", "globalsettings.json");
                var designElementsPath = Path.Combine(configPath, "Design", "elements.json");

                if (!File.Exists(designGlobalPath))
                    return DataIntegrityResult.WithWarnings("Design/globalsettings.json 不存在");
                else if (!await ValidateJsonFileAsync(designGlobalPath, "worldrules"))
                    return DataIntegrityResult.WithWarnings("Design/globalsettings.json 解析失败或缺少 worldrules");

                if (!File.Exists(designElementsPath))
                    return DataIntegrityResult.WithWarnings("Design/elements.json 不存在");
                else
                {
                    var designKeys = new[] { "characterrules", "factionrules", "locationrules", "plotrules" };
                    foreach (var key in designKeys)
                    {
                        if (!await ValidateJsonFileAsync(designElementsPath, key))
                            return DataIntegrityResult.WithWarnings($"Design/elements.json 缺少或无法解析 {key}");
                    }
                }

                var generateGlobalPath = Path.Combine(configPath, "Generate", "globalsettings.json");
                var generateElementsPath = Path.Combine(configPath, "Generate", "elements.json");

                if (!File.Exists(generateGlobalPath))
                    return DataIntegrityResult.WithWarnings("Generate/globalsettings.json 不存在");
                else if (!await ValidateJsonFileAsync(generateGlobalPath, "outline"))
                    return DataIntegrityResult.WithWarnings("Generate/globalsettings.json 解析失败或缺少 outline");

                if (!File.Exists(generateElementsPath))
                    return DataIntegrityResult.WithWarnings("Generate/elements.json 不存在");
                else
                {
                    var generateKeys = new[] { "chapter", "blueprint", "volumedesign" };
                    foreach (var key in generateKeys)
                    {
                        if (!await ValidateJsonFileAsync(generateElementsPath, key))
                            return DataIntegrityResult.WithWarnings($"Generate/elements.json 缺少或无法解析 {key}");
                    }
                }

                var guidesDir = Path.Combine(configPath, "guides");
                var contentGuideShards = Directory.Exists(guidesDir)
                    ? Directory.GetFiles(guidesDir, "content_guide_vol*.json")
                    : Array.Empty<string>();
                if (contentGuideShards.Length == 0)
                    return DataIntegrityResult.WithWarnings(
                        "章节指导文件（content_guide）未生成，数据预览将无法显示任何章节");

                TM.App.Log("[PublishService] L-004: 数据检查通过");
                return DataIntegrityResult.Valid();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] L-004: 数据检查异常: {ex.Message}");
                return DataIntegrityResult.WithWarnings($"检查异常: {ex.Message}");
            }
        }

        private async Task<bool> ValidateJsonFileAsync(string filePath, string dataKey)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("data", out var dataProp))
                    return false;

                foreach (var prop in dataProp.EnumerateObject())
                {
                    if (string.Equals(prop.Name, dataKey, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 私有方法

        private async Task PackageModuleAsync(PackageMapping mapping)
        {
            var storageRoot = StoragePathHelper.GetStorageRoot();
            var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
            var targetPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(mapping.ModuleType), mapping.TargetFile);

            var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();

            var packageData = new Dictionary<string, object>
            {
                ["module"] = mapping.SubModule,
                ["publishTime"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["version"] = 1
            };

            var data = new Dictionary<string, object>();

            foreach (var subDir in mapping.SubDirectories)
            {
                var subDirPath = Path.Combine(sourceBasePath, subDir);
                if (Directory.Exists(subDirPath))
                {
                    var subData = await LoadSubDirectoryDataAsync(subDirPath, currentSourceBookId);
                    data[subDir.ToLowerInvariant()] = subData;
                }
            }

            packageData["data"] = data;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var json = JsonSerializer.Serialize(packageData, JsonOptions);
            var tmpPath = targetPath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, targetPath, overwrite: true);

            TM.App.Log($"[PublishService] 已打包: {mapping.ModuleType}/{mapping.SubModule} -> {mapping.TargetFile}");
        }

        private async Task<Dictionary<string, object>> LoadSubDirectoryDataAsync(string dirPath, string? sourceBookId)
        {
            var result = new Dictionary<string, object>();

            var jsonFiles = Directory.GetFiles(dirPath, "*.json");
            foreach (var file in jsonFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var node = JsonNode.Parse(json);

                    if (node is JsonArray arr)
                    {
                        var filtered = new JsonArray();
                        foreach (var item in arr)
                        {
                            if (item is JsonObject obj && obj["IsEnabled"] is JsonValue enabledValue)
                            {
                                try
                                {
                                    var isEnabled = enabledValue.GetValue<bool>();
                                    if (!isEnabled)
                                        continue;
                                }
                                catch (Exception ex)
                                {
                                    DebugLogOnce("LoadSubDirectoryData_IsEnabled", ex);
                                }
                            }

                            if (!string.IsNullOrEmpty(sourceBookId) && item is JsonObject scopedObj)
                            {
                                if (scopedObj["SourceBookId"] is JsonValue sbValue)
                                {
                                    try
                                    {
                                        var sb = sbValue.GetValue<string>();
                                        if (!string.Equals(sb, sourceBookId, StringComparison.Ordinal))
                                            continue;
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogOnce("LoadSubDirectoryData_SourceBookId", ex);
                                        continue;
                                    }
                                }
                                else
                                {
                                    continue;
                                }
                            }

                            filtered.Add(item?.DeepClone());
                        }
                        result[fileName] = filtered;
                    }
                    else
                    {
                        result[fileName] = JsonSerializer.Deserialize<object>(json, JsonOptions)!;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] 读取文件失败 [{file}]: {ex.Message}");
                }
            }

            return result;
        }

        private async Task<int> UpdateManifestAsync()
        {
            var manifest = GetManifest() ?? new ManifestInfo();

            var currentSourceBookId = await _workScopeService.GetCurrentScopeAsync();

            if (string.IsNullOrEmpty(currentSourceBookId))
            {
                currentSourceBookId = await InferScopeFromCreativeMaterialsAsync();
                if (!string.IsNullOrEmpty(currentSourceBookId))
                {
                    await _workScopeService.SetCurrentScopeAsync(currentSourceBookId);
                    TM.App.Log($"[PublishService] 打包时自动推断 Scope: {currentSourceBookId}");
                }
            }

            if (string.IsNullOrEmpty(currentSourceBookId))
                throw new InvalidOperationException("当前未选择来源拆书（Scope为空），禁止打包。请先在创作素材模块选择来源拆书。");

            manifest.ProjectName = manifest.ProjectName.Length > 0 ? manifest.ProjectName : "我的小说";
            manifest.SourceBookId = currentSourceBookId;
            manifest.PublishTime = DateTime.Now;
            manifest.Version = manifest.Version + 1;

            manifest.Files = BuildFilesMap();

            manifest.EnabledModules = BuildEnabledModulesMap();

            manifest.Statistics = await BuildStatisticsAsync();

            var manifestPath = GetManifestPath();
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            var tmpPath = manifestPath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, manifestPath, overwrite: true);

            _cachedManifest = manifest;
            return manifest.Version;
        }

        private Dictionary<string, List<string>> BuildFilesMap()
        {
            var map = new Dictionary<string, List<string>>();

            foreach (var mapping in GetPackageMappings())
            {
                if (!map.ContainsKey(mapping.ModuleType))
                {
                    map[mapping.ModuleType] = new List<string>();
                }
                map[mapping.ModuleType].Add(mapping.TargetFile);
            }

            return map;
        }

        private Dictionary<string, Dictionary<string, bool>> BuildEnabledModulesMap()
        {
            var map = new Dictionary<string, Dictionary<string, bool>>();

            var allStatuses = _changeDetectionService.GetAllStatuses();

            foreach (var status in allStatuses)
            {
                var parts = status.ModulePath.Split('/');
                if (parts.Length == 2)
                {
                    var moduleType = parts[0];
                    var subModule = parts[1];

                    if (!map.ContainsKey(moduleType))
                    {
                        map[moduleType] = new Dictionary<string, bool>();
                    }
                    map[moduleType][subModule] = status.IsEnabled;
                }
            }

            return map;
        }

        private async Task<StatisticsInfo> BuildStatisticsAsync()
        {
            var stats = new StatisticsInfo();

            try
            {
                stats.TotalChapters = CountChapters();
                stats.TotalWords = await CountWordsAsync();
                stats.TotalCharacters = await CountCharactersAsync();
                stats.TotalLocations = await CountLocationsAsync();

                TM.App.Log($"[PublishService] 统计完成: {stats.TotalWords}字, {stats.TotalChapters}章节, {stats.TotalCharacters}角色, {stats.TotalLocations}地点");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计失败: {ex.Message}");
            }

            return stats;
        }

        private int CountChapters()
        {
            var chaptersDir = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersDir))
                return 0;

            return Directory.GetFiles(chaptersDir, "*.md", SearchOption.TopDirectoryOnly).Length;
        }

        private async Task<long> CountWordsAsync()
        {
            var chaptersDir = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersDir))
                return 0;

            long totalWords = 0;
            var files = Directory.GetFiles(chaptersDir, "*.md", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                try
                {
                    var modified = File.GetLastWriteTime(file);
                    int cached;
                    lock (_wordCountCacheLock)
                    {
                        if (_wordCountCache.TryGetValue(file, out var entry) && entry.Modified == modified)
                        {
                            totalWords += entry.Words;
                            continue;
                        }
                    }

                    var content = await File.ReadAllTextAsync(file);
                    cached = string.IsNullOrEmpty(content) ? 0 : CountChineseWords(content);
                    lock (_wordCountCacheLock) { _wordCountCache[file] = (cached, modified); }
                    totalWords += cached;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] 跳过文件: {Path.GetFileName(file)} - {ex.Message}");
                }
            }

            return totalWords;
        }

        private int CountChineseWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            text = System.Text.RegularExpressions.Regex.Replace(text, @"[#*_`\[\]()]+", "");

            var count = 0;
            foreach (var c in text)
            {
                if ((c >= 0x4E00 && c <= 0x9FFF)
                 || (c >= 0x3400 && c <= 0x4DBF)
                 || (c >= 0xF900 && c <= 0xFAFF)
                 || (c >= 0x2E80 && c <= 0x2EFF)
                 || (c >= 0x2F00 && c <= 0x2FDF))
                    count++;
            }
            return count;
        }

        private async Task<int> CountCharactersAsync()
        {
            var configPath = StoragePathHelper.GetProjectConfigPath("Design");
            var charactersFile = Path.Combine(configPath, "elements.json");

            if (!File.Exists(charactersFile))
                return 0;

            try
            {
                var json = await File.ReadAllTextAsync(charactersFile);
                using var jsonDoc = JsonDocument.Parse(json);

                if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("characterrules", out var characterRulesProp))
                {
                    int count = 0;
                    foreach (var fileProp in characterRulesProp.EnumerateObject())
                    {
                        if (fileProp.Value.ValueKind == JsonValueKind.Array)
                        {
                            count += fileProp.Value.GetArrayLength();
                        }
                    }
                    return count;
                }
                return 0;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计角色失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<int> CountLocationsAsync()
        {
            var configPath = StoragePathHelper.GetProjectConfigPath("Design");
            var elementsFile = Path.Combine(configPath, "elements.json");

            if (!File.Exists(elementsFile))
                return 0;

            try
            {
                var json = await File.ReadAllTextAsync(elementsFile);
                using var jsonDoc = JsonDocument.Parse(json);

                if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("locationrules", out var locationRulesProp))
                {
                    int count = 0;
                    foreach (var fileProp in locationRulesProp.EnumerateObject())
                    {
                        if (fileProp.Value.ValueKind == JsonValueKind.Array)
                        {
                            count += fileProp.Value.GetArrayLength();
                        }
                    }
                    return count;
                }
                return 0;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计地点失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<string> CreateBackupAsync()
        {
            var configPath = StoragePathHelper.GetProjectConfigPath();
            var backupPath = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}");

            Directory.CreateDirectory(backupPath);

            if (Directory.Exists(configPath))
            {
                await Task.Run(() => CopyDirectory(configPath, backupPath));
            }

            var manifestSrc = GetManifestPath();
            if (File.Exists(manifestSrc))
            {
                var manifestBak = backupPath + ".manifest.json";
                await Task.Run(() => File.Copy(manifestSrc, manifestBak, true));
            }

            return backupPath;
        }

        private async Task RestoreBackupAsync(string backupPath)
        {
            var configPath = StoragePathHelper.GetProjectConfigPath();

            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, true);
            }

            await Task.Run(() => CopyDirectory(backupPath, configPath));

            var manifestBak = backupPath + ".manifest.json";
            if (File.Exists(manifestBak))
            {
                File.Copy(manifestBak, GetManifestPath(), true);
                File.Delete(manifestBak);
            }

            Directory.Delete(backupPath, true);
        }

        private void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        private void EnsureDirectoriesExist()
        {
            _ = StoragePathHelper.GetProjectConfigPath("Design");
            _ = StoragePathHelper.GetProjectConfigPath("Generate");
            _ = StoragePathHelper.GetProjectChaptersPath();
            Directory.CreateDirectory(Path.Combine(StoragePathHelper.GetProjectValidationPath(), "reports"));
            _ = StoragePathHelper.GetProjectHistoryPath();
        }

        private string GetManifestPath()
        {
            return Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json");
        }

        #endregion

        #region 指导文件生成

        private async Task GenerateGuideFilesAsync()
        {
            try
            {
                var builder = new GuideIndexBuilder(_workScopeService, modulePath => _changeDetectionService.GetStatus(modulePath).IsEnabled);
                var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
                Directory.CreateDirectory(guidesPath);

                var outlineGuide = await builder.BuildOutlineGuideAsync();
                await SaveGuideAsync(guidesPath, "outline_guide.json", outlineGuide);

                var planningGuide = await builder.BuildPlanningGuideAsync(outlineGuide);
                await SaveGuideAsync(guidesPath, "planning_guide.json", planningGuide);

                var blueprintGuide = await builder.BuildBlueprintGuideAsync(planningGuide);
                await SaveGuideAsync(guidesPath, "blueprint_guide.json", blueprintGuide);

                var contentGuide = await builder.BuildContentGuideAsync(blueprintGuide);

                if (contentGuide.Chapters.Count == 0)
                    throw new InvalidOperationException(
                        "章节指导为空（0章）：蓝图数据均未启用或不属于当前来源书目，" +
                        "请检查蓝图设计是否已启用且来源书目与当前项目一致，确认后重新打包。");

                var contentShards = new SortedDictionary<int, ContentGuide>();
                foreach (var (chapterId, entry) in contentGuide.Chapters)
                {
                    var vol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
                    if (!contentShards.TryGetValue(vol, out var shard))
                    {
                        shard = new ContentGuide { SourceBookId = contentGuide.SourceBookId };
                        contentShards[vol] = shard;
                    }
                    shard.Chapters[chapterId] = entry;
                    if (contentGuide.ChapterSummaries.TryGetValue(chapterId, out var sum))
                        shard.ChapterSummaries[chapterId] = sum;
                }
                foreach (var (vol, shard) in contentShards)
                    await SaveGuideAsync(guidesPath, GuideManager.GetVolumeFileName("content_guide.json", vol), shard);

                try
                {
                    var newShardNames = new HashSet<string>(
                        contentShards.Keys.Select(v => GuideManager.GetVolumeFileName("content_guide.json", v)),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var f in Directory.GetFiles(guidesPath, "content_guide_vol*.json", SearchOption.TopDirectoryOnly))
                    {
                        var fn = Path.GetFileName(f);
                        if (!newShardNames.Contains(fn))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
                catch { }

                var legacyCgPath = Path.Combine(guidesPath, "content_guide.json");
                if (File.Exists(legacyCgPath))
                    try { File.Delete(legacyCgPath); } catch { }

                var allChapterIds = new HashSet<string>(contentGuide.Chapters.Keys);
                var plotRules = await LoadPlotRulesAsync(await _workScopeService.GetCurrentScopeAsync());
                builder.ValidatePlotRulesChapters(plotRules, allChapterIds);

                var guideManager = ServiceLocator.Get<GuideManager>();

                var foreshadowStatus = await builder.BuildForeshadowingStatusGuideAsync();
                var existingForeshadow = await guideManager.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                foreach (var (id, entry) in existingForeshadow.Foreshadowings)
                    if (foreshadowStatus.Foreshadowings.ContainsKey(id))
                    {
                        foreshadowStatus.Foreshadowings[id].IsSetup = entry.IsSetup;
                        foreshadowStatus.Foreshadowings[id].IsResolved = entry.IsResolved;
                        foreshadowStatus.Foreshadowings[id].ActualSetupChapter = entry.ActualSetupChapter;
                        foreshadowStatus.Foreshadowings[id].ActualPayoffChapter = entry.ActualPayoffChapter;
                        foreshadowStatus.Foreshadowings[id].IsOverdue = entry.IsOverdue;
                    }
                await SaveGuideAsync(guidesPath, "foreshadowing_status_guide.json", foreshadowStatus);
                guideManager.EvictCache("foreshadowing_status_guide.json", foreshadowStatus);

                TM.App.Log("[PublishService] 指导文件生成完成（设计类Guide，追踪Guide由运行期独立管理）");

                try
                {
                    await ServiceLocator.Get<LedgerTrimService>().TrimAllAsync();
                }
                catch (Exception trimEx)
                {
                    TM.App.Log($"[PublishService] trim err (non-critical): {trimEx.Message}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 指导文件生成失败: {ex.Message}");
                throw;
            }
        }

        private async Task SaveGuideAsync<T>(string guidesPath, string fileName, T guide)
        {
            var filePath = Path.Combine(guidesPath, fileName);
            var tmpPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(guide, JsonOptions);

            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);

            TM.App.Log($"[PublishService] 已保存指导文件: {fileName}");
        }

        private async Task<List<PlotRulesData>> LoadPlotRulesAsync(string? sourceBookId = null)
        {
            var filePath = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules", "Design", "Elements", "PlotRules", "plot_rules.json");

            if (!File.Exists(filePath)) return new List<PlotRulesData>();

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var all = JsonSerializer.Deserialize<List<PlotRulesData>>(json, JsonOptions) ?? new List<PlotRulesData>();
                if (!string.IsNullOrEmpty(sourceBookId))
                    return all.Where(r => string.Equals(r.SourceBookId, sourceBookId, StringComparison.Ordinal)).ToList();
                return all;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 读取剧情规则文件失败: {ex.Message}");
                return new List<PlotRulesData>();
            }
        }

        private async Task<string?> InferScopeFromCreativeMaterialsAsync()
        {
            try
            {
                var materialsPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Templates", "CreativeMaterials", "creative_materials.json");

                if (!File.Exists(materialsPath))
                    return null;

                var json = await File.ReadAllTextAsync(materialsPath);
                using var doc = JsonDocument.Parse(json);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var isEnabled = item.TryGetProperty("IsEnabled", out var enabledProp) && enabledProp.GetBoolean();
                    if (!isEnabled) continue;

                    if (item.TryGetProperty("SourceBookId", out var sourceBookIdProp) &&
                        sourceBookIdProp.ValueKind == JsonValueKind.String)
                    {
                        var sourceBookId = sourceBookIdProp.GetString();
                        if (!string.IsNullOrEmpty(sourceBookId))
                            return sourceBookId;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 推断 Scope 失败: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region 内部类

        private class PackageMapping
        {
            public string ModuleType { get; }
            public string SubModule { get; }
            public string[] SubDirectories { get; }
            public string TargetFile { get; }

            public PackageMapping(string moduleType, string subModule, string[] subDirectories, string targetFile)
            {
                ModuleType = moduleType;
                SubModule = subModule;
                SubDirectories = subDirectories;
                TargetFile = targetFile;
            }
        }

        private class DataIntegrityResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; } = string.Empty;

            public static DataIntegrityResult Valid() => new() { IsValid = true };
            public static DataIntegrityResult WithWarnings(string message) => new() { IsValid = false, Message = message };
        }

        #endregion
    }
}
