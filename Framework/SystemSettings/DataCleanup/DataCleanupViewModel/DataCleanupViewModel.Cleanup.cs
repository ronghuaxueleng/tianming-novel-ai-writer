using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.DataCleanup.Models;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Framework.SystemSettings.DataCleanup
{
    public partial class DataCleanupViewModel
    {
        private static string GetDefaultIcon(string layer)
        {
            return layer switch
            {
                "Framework" => "Icon.Cog",
                "Modules" => "Icon.Package",
                "Services" => "Icon.Wrench",
                "Projects" => "Icon.Folder",
                _ => "Icon.Document"
            };
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            while (normalized.Contains("//", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            }

            return normalized.Trim();
        }

        private void SelectAll()
        {
            foreach (var module in Modules)
            {
                module.IsSelected = true;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private void SelectNone()
        {
            foreach (var module in Modules)
            {
                module.IsSelected = false;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private void SelectModule(CleanupModule? module)
        {
            if (module == null) return;

            foreach (var item in module.Items)
            {
                item.IsSelected = true;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private async Task ExecuteCleanup()
        {
            var selectedItems = Modules
                .SelectMany(m => m.Items)
                .Where(i => i.IsSelected)
                .ToList();

            if (selectedItems.Count == 0)
            {
                GlobalToast.Warning("提示", "请先选择要清理的数据");
                return;
            }

            var hasHighRisk = selectedItems.Any(i => i.RiskLevel == RiskLevel.High);

            var itemList = string.Join("\n", selectedItems.Select(i => $"• {i.Name}"));
            var confirmMessage = $"将清理以下 {selectedItems.Count} 项数据：\n\n{itemList}";

            if (!StandardDialog.ShowConfirm(confirmMessage, "确认清理"))
                return;

            if (hasHighRisk)
            {
                var highRiskItems = selectedItems.Where(i => i.RiskLevel == RiskLevel.High).ToList();
                var warningList = string.Join("\n", highRiskItems.Select(i => $"[!] {i.Name}: {i.WarningMessage}"));
                var warningMessage = $"[!] 警告：以下高危数据将被清除！\n\n{warningList}\n\n此操作不可恢复，确定继续？";

                if (!StandardDialog.ShowConfirm(warningMessage, "[!] 高危操作确认"))
                    return;
            }

            var storageRoot = StoragePathHelper.GetProjectRoot();
            int successCount = 0, failCount = 0;

            await System.Threading.Tasks.Task.WhenAll(selectedItems.Select(async item =>
            {
                try
                {
                    var fullPath = Path.Combine(storageRoot, item.FilePath);
                    await ExecuteCleanupItemAsync(item, fullPath).ConfigureAwait(false);
                    System.Threading.Interlocked.Increment(ref successCount);
                    TM.App.Log($"[DataCleanup] 清理成功: {item.Name}");
                }
                catch (Exception ex)
                {
                    System.Threading.Interlocked.Increment(ref failCount);
                    TM.App.Log($"[DataCleanup] 清理失败: {item.Name} - {ex.Message}");
                }
            })).ConfigureAwait(true);

            if (failCount == 0)
                GlobalToast.Success("清理完成", $"成功清理 {successCount} 项数据");
            else
                GlobalToast.Warning("清理完成", $"成功 {successCount} 项，失败 {failCount} 项");

            RefreshServicesAfterCleanup(selectedItems);
            SelectNone();
            StatusMessage = $"清理完成：成功 {successCount} 项，失败 {failCount} 项";
        }

        private async System.Threading.Tasks.Task ExecuteCleanupItemAsync(CleanupItem item, string fullPath)
        {
            if (!item.FilePath.StartsWith("Storage/", StringComparison.Ordinal))
            {
                TM.App.Log($"[DataCleanup] 拒绝非 Storage/ 路径，疑似误配: {item.Name} → {item.FilePath}");
                return;
            }

            switch (item.CleanupMethod)
            {
                case CleanupMethod.ClearContent:
                    await ClearFileContentAsync(fullPath).ConfigureAwait(false);
                    break;
                case CleanupMethod.DeleteFile:
                    DeleteFileIfExists(fullPath);
                    break;
                case CleanupMethod.ClearDirectory:
                    await ClearDirectoryFilesAsync(fullPath, item.FilePath).ConfigureAwait(false);
                    break;
                case CleanupMethod.DeleteNonBuiltIn:
                    await DeleteNonBuiltInTemplatesAsync(fullPath).ConfigureAwait(false);
                    break;
                case CleanupMethod.ClearProjectCategories:
                    await ClearAllProjectCategoriesAsync().ConfigureAwait(false);
                    break;
                case CleanupMethod.ClearModelCategoriesKeepLevel1:
                    await ClearModelCategoriesKeepLevel1Async(fullPath).ConfigureAwait(false);
                    break;
                case CleanupMethod.ClearProjectVolumesAndChapters:
                    await ClearProjectVolumesAndChaptersAsync().ConfigureAwait(false);
                    break;
                case CleanupMethod.ClearProjectConfigData:
                    await ClearProjectConfigDataAsync().ConfigureAwait(false);
                    break;
                case CleanupMethod.ClearProjectHistory:
                    await System.Threading.Tasks.Task.Run(ClearProjectHistory).ConfigureAwait(false);
                    break;
            }
        }

        private async System.Threading.Tasks.Task ClearFileContentAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            if (content.TrimStart().StartsWith('['))
            {
                await File.WriteAllTextAsync(filePath, "[]").ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(filePath, "{}").ConfigureAwait(false);
            }
        }

        private void DeleteFileIfExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private async System.Threading.Tasks.Task ClearDirectoryFilesAsync(string dirPath, string relativePath)
        {
            if (relativePath.StartsWith("Storage/Projects", StringComparison.Ordinal))
            {
                var storageRoot = StoragePathHelper.GetProjectRoot();
                var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

                if (!Directory.Exists(projectsDir)) return;

                var deletedCount = 0;
                var failedCount = 0;

                foreach (var projectDir in Directory.GetDirectories(projectsDir))
                {
                    if (relativePath.Contains("Generated/chapters"))
                    {
                        var chaptersDir = Path.Combine(projectDir, "Generated", "chapters");
                        if (Directory.Exists(chaptersDir))
                        {
                            foreach (var file in Directory.GetFiles(chaptersDir, "*.*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Config/guides"))
                    {
                        var guidesDir = Path.Combine(projectDir, "Config", "guides");
                        if (Directory.Exists(guidesDir))
                        {
                            foreach (var file in Directory.GetFiles(guidesDir, "*.*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Validation/reports"))
                    {
                        var reportsDir = Path.Combine(projectDir, "Validation", "reports");
                        if (Directory.Exists(reportsDir))
                        {
                            foreach (var file in Directory.GetFiles(reportsDir, "*.json", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Sessions"))
                    {
                        var sessionsDir = Path.Combine(projectDir, "Sessions");
                        if (Directory.Exists(sessionsDir))
                        {
                            var indexFile = Path.Combine(sessionsDir, "_index.json");
                            if (File.Exists(indexFile))
                            {
                                try { await File.WriteAllTextAsync(indexFile, "{}").ConfigureAwait(false); }
                                catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}"); }
                            }
                            foreach (var file in Directory.GetFiles(sessionsDir, "*.json")
                                .Where(f => !f.EndsWith("_index.json", StringComparison.OrdinalIgnoreCase)))
                            {
                                try { File.Delete(file); deletedCount++; }
                                catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}"); }
                            }
                        }
                    }
                    else if (relativePath.Contains("VersionRegistry"))
                    {
                        var registryFile = Path.Combine(projectDir, "version_registry.json");
                        if (File.Exists(registryFile))
                        {
                            try { File.Delete(registryFile); deletedCount++; }
                            catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 删除文件失败: {registryFile} - {ex.Message}"); }
                        }
                    }
                }

                TM.App.Log($"[DataCleanup] Projects层目录清理完成: {relativePath}，成功 {deletedCount}，失败 {failedCount}");
                return;
            }

            if (!Directory.Exists(dirPath))
            {
                TM.App.Log($"[DataCleanup] 目录不存在: {dirPath}");
                return;
            }

            if (relativePath.Contains("Profile/BasicInfo"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                foreach (var file in Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                TM.App.Log($"[DataCleanup] 用户资料目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
                return;
            }

            else if (relativePath.Contains("Conversations"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                var indexFile = Path.Combine(dirPath, "conversation_index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        await File.WriteAllTextAsync(indexFile, "{\"Sessions\":[]}").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}");
                    }
                }
                var sessionsDir = Path.Combine(dirPath, "sessions");
                if (Directory.Exists(sessionsDir))
                {
                    foreach (var file in Directory.GetFiles(sessionsDir, "*.json"))
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                        }
                    }
                }

                TM.App.Log($"[DataCleanup] Conversations目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
            }
            else if (relativePath.Contains("Sessions") && !relativePath.Contains("Conversations"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                var indexFile = Path.Combine(dirPath, "_index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        await File.WriteAllTextAsync(indexFile, "{}").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}");
                    }
                }
                foreach (var file in Directory.GetFiles(dirPath, "*.messages.json"))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                TM.App.Log($"[DataCleanup] Sessions目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
            }
            else
            {
                var deletedCount = 0;
                var failedCount = 0;

                foreach (var file in Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                if (failedCount > 0)
                    TM.App.Log($"[DataCleanup] 目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
                else
                    System.Diagnostics.Debug.WriteLine($"[DataCleanup] 目录清理完成: {dirPath}，成功 {deletedCount}");
            }
        }

        private async System.Threading.Tasks.Task DeleteNonBuiltInTemplatesAsync(string templatesDir)
        {
            if (!Directory.Exists(templatesDir)) return;

            foreach (var file in Directory.GetFiles(templatesDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    var jsonDoc = JsonDocument.Parse(content);

                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var templates = new List<JsonElement>();
                        foreach (var item in jsonDoc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("IsBuiltIn", out var isBuiltIn) && isBuiltIn.GetBoolean())
                            {
                                templates.Add(item);
                            }
                        }

                        var options = JsonHelper.Default;
                        var newContent = JsonSerializer.Serialize(templates, options);
                        var tmpDc1 = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllTextAsync(tmpDc1, newContent).ConfigureAwait(false);
                        File.Move(tmpDc1, file, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(DeleteNonBuiltInTemplatesAsync), file, ex);
                }
            }
        }

        private async System.Threading.Tasks.Task ClearProjectVolumesAndChaptersAsync()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir))
            {
                TM.App.Log($"[DataCleanup] 项目目录不存在: {projectsDir}");
                return;
            }

            var clearedVolumes = 0;
            var clearedChapters = 0;
            var failedCount = 0;

            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var categoriesFile = Path.Combine(projectDir, "Generated", "categories.json");
                if (File.Exists(categoriesFile))
                {
                    try
                    {
                        await File.WriteAllTextAsync(categoriesFile, "[]").ConfigureAwait(false);
                        clearedVolumes++;
                        TM.App.Log($"[DataCleanup] 已清空卷数据: {categoriesFile}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 清空卷数据失败: {categoriesFile} - {ex.Message}");
                    }
                }

                var chaptersDir = Path.Combine(projectDir, "Generated", "chapters");
                if (Directory.Exists(chaptersDir))
                {
                    var files = Directory.GetFiles(chaptersDir, "*.md", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            clearedChapters++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除章节失败: {file} - {ex.Message}");
                        }
                    }
                    foreach (var ext in new[] { "*.bak", "*.staging" })
                    {
                        foreach (var file in Directory.GetFiles(chaptersDir, ext, SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); }
                            catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理{ext}失败: {ex.Message}"); }
                        }
                    }
                    TM.App.Log($"[DataCleanup] 已删除 {files.Length} 个章节文件: {chaptersDir}");

                    var metaIndexFile = Path.Combine(chaptersDir, "_meta_index.json");
                    if (File.Exists(metaIndexFile))
                    {
                        try
                        {
                            await File.WriteAllTextAsync(metaIndexFile, "{}").ConfigureAwait(false);
                            TM.App.Log($"[DataCleanup] 联动重置章节元数据索引: {metaIndexFile}");
                        }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 重置元数据索引失败: {ex.Message}"); }
                    }
                }
            }

            TM.App.Log($"[DataCleanup] 已清除 {clearedVolumes} 个项目的卷数据，{clearedChapters} 个章节文件，失败 {failedCount}");

            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var guidesDir = Path.Combine(projectDir, "Config", "guides");
                if (!Directory.Exists(guidesDir)) continue;

                var trackingPrefixes = new[]
                {
                    "character_state_guide",
                    "location_state_guide",
                    "faction_state_guide",
                    "item_state_guide",
                    "timeline_guide",
                    "conflict_progress_guide",
                    "foreshadowing_status_guide",
                    "chapter_summary"
                };

                foreach (var file in Directory.GetFiles(guidesDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var fn = Path.GetFileName(file);
                    if (trackingPrefixes.Any(p => fn.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        try { File.Delete(file); TM.App.Log($"[DataCleanup] 联动清理追踪Guide: {fn}"); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理追踪Guide失败: {fn} - {ex.Message}"); }
                    }
                }

                var archivesDir = Path.Combine(guidesDir, "fact_archives");
                if (Directory.Exists(archivesDir))
                {
                    foreach (var file in Directory.GetFiles(archivesDir, "*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理卷末存档失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理卷末事实存档: {archivesDir}");
                }

                var milestonesDir = Path.Combine(guidesDir, "milestones");
                if (Directory.Exists(milestonesDir))
                {
                    foreach (var file in Directory.GetFiles(milestonesDir, "*.*"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理里程碑失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理里程碑文件: {milestonesDir}");
                }

                var keyEventsDir = Path.Combine(guidesDir, "keyevents");
                if (Directory.Exists(keyEventsDir))
                {
                    foreach (var file in Directory.GetFiles(keyEventsDir, "vol*.jsonl"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理关键事件索引失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理关键事件索引: {keyEventsDir}");
                }

                var plotPointsDir = Path.Combine(guidesDir, "plot_points");
                if (Directory.Exists(plotPointsDir))
                {
                    foreach (var file in Directory.GetFiles(plotPointsDir, "*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理情节点分片失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理情节点分片: {plotPointsDir}");
                }

                var summariesDir = Path.Combine(guidesDir, "summaries");
                if (Directory.Exists(summariesDir))
                {
                    foreach (var file in Directory.GetFiles(summariesDir, "vol*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理摘要分片失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理章节摘要分片: {summariesDir}");
                }

                var kwIndexFile = Path.Combine(guidesDir, "keyword_index.json");
                if (File.Exists(kwIndexFile))
                {
                    try { File.Delete(kwIndexFile); TM.App.Log($"[DataCleanup] 已清理关键词索引: keyword_index.json"); }
                    catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理关键词索引失败: {ex.Message}"); }
                }

                foreach (var vecFile in new[] { "chapter_embeddings.json", "chunk_embeddings.json", "entity_first_chapter.json" })
                {
                    var vecPath = Path.Combine(guidesDir, vecFile);
                    if (File.Exists(vecPath))
                    {
                        try { File.Delete(vecPath); TM.App.Log($"[DataCleanup] 已清理向量索引: {vecFile}"); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理向量索引失败: {vecFile} - {ex.Message}"); }
                    }
                    var vecBak = vecPath + ".bak";
                    if (File.Exists(vecBak))
                    {
                        try { File.Delete(vecBak); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理向量索引 .bak 失败: {vecFile}.bak - {ex.Message}"); }
                    }
                }
            }
            TM.App.Log("[DataCleanup] 联动追踪Guide清理完成");

            try
            {
                ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict();
                ServiceLocator.Get<GuideContextService>().ClearCache();
                ServiceLocator.Get<GlobalSummaryService>().InvalidateCache();
                ServiceLocator.Get<RelationStrengthService>().InvalidateCache();
                ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache();
                ServiceLocator.Get<ChapterEmbeddingIndex>().InvalidateCache();
                ServiceLocator.Get<ChunkEmbeddingIndex>().InvalidateCache();
                ServiceLocator.Get<EntityFirstChapterIndex>().InvalidateCache();
                TM.App.Log("[DataCleanup] 内存缓存已失效");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 缓存失效调用失败（不影响清理结果）: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ClearProjectConfigDataAsync()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir)) return;

            var projectDirs = Directory.GetDirectories(projectsDir);
            int clearedCount = 0, failedCount = 0;

            await System.Threading.Tasks.Task.WhenAll(projectDirs.Select(async projectDir =>
            {
                var configDir = Path.Combine(projectDir, "Config");
                if (!Directory.Exists(configDir)) return;

                foreach (var file in Directory.GetFiles(configDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(file);
                        System.Threading.Interlocked.Increment(ref clearedCount);
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref failedCount);
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                var subDirs = new[] { "Design", "Generate" };
                await System.Threading.Tasks.Task.WhenAll(subDirs.Select(async subDir =>
                {
                    var subPath = Path.Combine(configDir, subDir);
                    if (!Directory.Exists(subPath)) return;
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            Directory.Delete(subPath, true);
                            TM.App.Log($"[DataCleanup] 已删除打包目录: {subPath}");
                        }
                        catch (Exception ex)
                        {
                            System.Threading.Interlocked.Increment(ref failedCount);
                            TM.App.Log($"[DataCleanup] 删除目录失败: {subPath} - {ex.Message}");
                        }
                    }).ConfigureAwait(false);
                })).ConfigureAwait(false);
            })).ConfigureAwait(false);

            foreach (var projectDir in projectDirs)
            {
                var manifestFile = Path.Combine(projectDir, "manifest.json");
                if (File.Exists(manifestFile))
                {
                    try
                    {
                        File.Delete(manifestFile);
                        TM.App.Log($"[DataCleanup] 联动删除打包清单: {manifestFile}");
                    }
                    catch (Exception ex) { TM.App.Log($"[DataCleanup] 删除打包清单失败: {ex.Message}"); }
                }
            }

            TM.App.Log($"[DataCleanup] 已清除打包配置数据: 成功 {clearedCount}，失败 {failedCount}");
        }

        private void ClearProjectConfigData()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir)) return;

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var configDir = Path.Combine(projectDir, "Config");
                if (Directory.Exists(configDir))
                {
                    foreach (var file in Directory.GetFiles(configDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            File.Delete(file);
                            clearedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                        }
                    }
                    var subDirs = new[] { "Design", "Generate" };
                    foreach (var subDir in subDirs)
                    {
                        var subPath = Path.Combine(configDir, subDir);
                        if (Directory.Exists(subPath))
                        {
                            try
                            {
                                Directory.Delete(subPath, true);
                                TM.App.Log($"[DataCleanup] 已删除打包目录: {subPath}");
                            }
                            catch (Exception ex)
                            {
                                failedCount++;
                                TM.App.Log($"[DataCleanup] 删除目录失败: {subPath} - {ex.Message}");
                            }
                        }
                    }
                }

                var manifestFile = Path.Combine(projectDir, "manifest.json");
                if (File.Exists(manifestFile))
                {
                    try
                    {
                        File.Delete(manifestFile);
                        TM.App.Log($"[DataCleanup] 联动删除打包清单: {manifestFile}");
                    }
                    catch (Exception ex) { TM.App.Log($"[DataCleanup] 删除打包清单失败: {ex.Message}"); }
                }
            }
            TM.App.Log($"[DataCleanup] 已清除打包配置数据: 成功 {clearedCount}，失败 {failedCount}");
        }

        private void ClearProjectHistory()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir)) return;

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var historyDir = Path.Combine(projectDir, "History");
                if (Directory.Exists(historyDir))
                {
                    foreach (var versionDir in Directory.GetDirectories(historyDir))
                    {
                        try
                        {
                            Directory.Delete(versionDir, true);
                            clearedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除目录失败: {versionDir} - {ex.Message}");
                        }
                    }
                    TM.App.Log($"[DataCleanup] 已删除打包历史: {historyDir}");
                }
            }
            TM.App.Log($"[DataCleanup] 已清除历史版本: 成功 {clearedCount}，失败 {failedCount}");
        }

        private async System.Threading.Tasks.Task ClearAllProjectCategoriesAsync()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir))
            {
                TM.App.Log($"[DataCleanup] 项目目录不存在: {projectsDir}");
                return;
            }

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var categoriesFile = Path.Combine(projectDir, "Generated", "categories.json");
                if (File.Exists(categoriesFile))
                {
                    try
                    {
                        await File.WriteAllTextAsync(categoriesFile, "[]").ConfigureAwait(false);
                        clearedCount++;
                        TM.App.Log($"[DataCleanup] 已清空分类文件: {categoriesFile}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 清空分类文件失败: {categoriesFile} - {ex.Message}");
                    }
                }
            }
            TM.App.Log($"[DataCleanup] 已清空项目分类数据: 成功 {clearedCount}，失败 {failedCount}");
        }

        private async System.Threading.Tasks.Task ClearModelCategoriesKeepLevel1Async(string filePath)
        {
            if (!File.Exists(filePath))
            {
                TM.App.Log($"[DataCleanup] 文件不存在: {filePath}");
                return;
            }

            try
            {
                var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var jsonDoc = JsonDocument.Parse(content);

                if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    TM.App.Log($"[DataCleanup] categories.json 不是数组格式");
                    return;
                }

                var level1Categories = new List<Dictionary<string, object>>();
                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("Level", out var levelProp) && levelProp.GetInt32() == 1)
                    {
                        var category = new Dictionary<string, object>();
                        foreach (var prop in item.EnumerateObject())
                        {
                            category[prop.Name] = GetJsonValue(prop.Value) ?? string.Empty;
                        }
                        level1Categories.Add(category);
                    }
                }

                var options = JsonHelper.Default;
                var newContent = JsonSerializer.Serialize(level1Categories, options);
                var tmpDc2 = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpDc2, newContent).ConfigureAwait(false);
                File.Move(tmpDc2, filePath, overwrite: true);

                TM.App.Log($"[DataCleanup] 已清除模型分类，保留 {level1Categories.Count} 个LV1分类");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 清除模型分类失败: {ex.Message}");
                throw;
            }
        }

        private object? GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        private void RefreshServicesAfterCleanup(List<CleanupItem> cleanedItems)
        {
            try
            {
                var refreshedServices = new List<string>();

                var aiRelated = cleanedItems.Any(i =>
                    i.FilePath.Contains("Services/AI/Library") ||
                    i.FilePath.Contains("Services/AI/Configurations"));

                if (aiRelated)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try { await _aiService.ReloadLibraryAsync().ConfigureAwait(false); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 刷新AI库失败: {ex.Message}"); }
                    });
                    refreshedServices.Add("AIService");

                    try
                    {
                        var writingService = ServiceLocator.Get<WritingSettingsService>();
                        var availableIds = _aiService.GetAllConfigurations()
                            .Where(c => c.IsEnabled)
                            .Select(c => c.Id);
                        writingService.NormalizeAgainstAvailableIds(availableIds);
                        refreshedServices.Add("WritingSettingsService");
                    }
                    catch (Exception ex) { TM.App.Log($"[DataCleanup] 校验写作配置失败: {ex.Message}"); }
                }

                var sessionRelated = cleanedItems.Any(i =>
                    i.FilePath.Contains("Projects/Sessions"));

                if (sessionRelated)
                {
                    _sessionManager.ReloadIndex();
                    refreshedServices.Add("SessionManager");
                }

                if (refreshedServices.Count > 0)
                {
                    TM.App.Log($"[DataCleanup] 已刷新服务: {string.Join(", ", refreshedServices)}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 刷新服务失败: {ex.Message}");
            }
        }

    }
}

