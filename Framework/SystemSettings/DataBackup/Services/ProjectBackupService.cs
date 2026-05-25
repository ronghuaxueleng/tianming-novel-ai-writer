using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.DataBackup.Models;

namespace TM.Framework.SystemSettings.DataBackup.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ProjectBackupService
    {
        private const string ManifestFileName = "backup_manifest.json";
        private const string SafetyRootName = "_backup_safety";
        private const string PendingRestoreFileName = "pending_restore.json";
        private const int DefaultSafetyCopyKeepCount = 3;

        #region 备份源描述

        private sealed class BackupSource
        {
            public string LogicalKey = string.Empty;
            public string ZipPrefix = string.Empty;
            public string Description = string.Empty;
            public string SourcePath = string.Empty;
        }

        private static List<BackupSource> BuildFullBackupSources()
        {
            return new List<BackupSource>
            {
                new()
                {
                    LogicalKey = BackupScopeKeys.Project,
                    ZipPrefix = "project/",
                    Description = "项目数据（配置/正文/会话/历史）",
                    SourcePath = StoragePathHelper.GetCurrentProjectPath()
                },
                new()
                {
                    LogicalKey = BackupScopeKeys.ModulesDesign,
                    ZipPrefix = "modules-design/",
                    Description = "设计模块（角色/势力/地点/剧情/拆书/模板）",
                    SourcePath = StoragePathHelper.GetModulesStoragePath("Design")
                },
                new()
                {
                    LogicalKey = BackupScopeKeys.ModulesGenerate,
                    ZipPrefix = "modules-generate/",
                    Description = "创作模块（蓝图/章节/分卷/大纲）",
                    SourcePath = StoragePathHelper.GetModulesStoragePath("Generate")
                }
            };
        }

        #endregion

        #region 完整备份

        public async Task<BackupResult> ExportFullBackupAsync(string targetZipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetZipPath))
                    return BackupResult.Fail("未指定保存路径");

                var sources = BuildFullBackupSources();

                var projectSource = sources.First(s => s.LogicalKey == BackupScopeKeys.Project);
                if (!Directory.Exists(projectSource.SourcePath))
                    return BackupResult.Fail($"项目目录不存在: {projectSource.SourcePath}");

                var scopes = new List<BackupScopeEntry>();
                long totalSize = 0;
                foreach (var src in sources)
                {
                    var size = await CalculateDirectorySizeAsync(src.SourcePath);
                    totalSize += size;
                    scopes.Add(new BackupScopeEntry
                    {
                        LogicalKey = src.LogicalKey,
                        ZipPrefix = src.ZipPrefix,
                        Description = src.Description,
                        SizeBytes = size
                    });
                }

                var manifest = new BackupManifest
                {
                    Type = BackupTypes.FullBackup,
                    ProjectName = StoragePathHelper.CurrentProjectName,
                    AppVersion = GetAppVersion(),
                    CreatedAtUtc = DateTime.UtcNow,
                    OriginalSizeBytes = totalSize,
                    Scopes = scopes
                };

                await Task.Run(() => CreateZipWithMultipleSources(targetZipPath, manifest, sources));

                var fileSize = new FileInfo(targetZipPath).Length;
                TM.App.Log($"[ProjectBackupService] 完整备份完成: {targetZipPath} (压缩前 {FormatSize(totalSize)} → 压缩后 {FormatSize(fileSize)})");

                return BackupResult.Ok($"已备份到 {targetZipPath}", targetZipPath, fileSize);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 完整备份失败: {ex.Message}");
                return BackupResult.Fail($"备份失败：{ex.Message}");
            }
        }

        #endregion

        #region 章节导出

        public async Task<BackupResult> ExportChaptersAsync(string targetZipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetZipPath))
                    return BackupResult.Fail("未指定保存路径");

                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!Directory.Exists(chaptersPath))
                    return BackupResult.Fail("章节目录不存在，请先生成章节内容");

                var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.AllDirectories);
                if (mdFiles.Length == 0)
                    return BackupResult.Fail("没有可导出的章节内容");

                var manifest = new BackupManifest
                {
                    Type = BackupTypes.ChaptersExport,
                    ProjectName = StoragePathHelper.CurrentProjectName,
                    AppVersion = GetAppVersion(),
                    CreatedAtUtc = DateTime.UtcNow,
                    ChapterCount = mdFiles.Length,
                    OriginalSizeBytes = mdFiles.Sum(f => new FileInfo(f).Length)
                };

                var singleSource = new List<BackupSource>
                {
                    new()
                    {
                        LogicalKey = "chapters",
                        ZipPrefix = string.Empty,
                        Description = "章节正文",
                        SourcePath = chaptersPath
                    }
                };

                await Task.Run(() => CreateZipWithMultipleSources(targetZipPath, manifest, singleSource));

                var fileSize = new FileInfo(targetZipPath).Length;
                TM.App.Log($"[ProjectBackupService] 章节导出完成: {mdFiles.Length} 章 -> {targetZipPath} ({FormatSize(fileSize)})");

                return BackupResult.Ok($"已导出 {mdFiles.Length} 章到 {targetZipPath}", targetZipPath, fileSize);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 章节导出失败: {ex.Message}");
                return BackupResult.Fail($"导出失败：{ex.Message}");
            }
        }

        #endregion

        #region 校验

        public async Task<BackupValidationResult> ValidateBackupAsync(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                    return BackupValidationResult.Invalid("文件不存在");

                return await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    var manifestEntry = archive.GetEntry(ManifestFileName);
                    if (manifestEntry == null)
                        return BackupValidationResult.Invalid("该文件不是有效的项目备份（缺少备份清单）");

                    string json;
                    using (var reader = new StreamReader(manifestEntry.Open()))
                        json = reader.ReadToEnd();

                    var manifest = JsonHelper.TryDeserialize<BackupManifest>(json);
                    if (manifest == null || manifest.Signature != "TM_PROJECT_BACKUP")
                        return BackupValidationResult.Invalid("备份清单格式异常或非本工具生成");

                    if (manifest.Type != BackupTypes.FullBackup)
                        return BackupValidationResult.Invalid(
                            "该文件是「章节导出」ZIP，无法用于数据恢复。\n请选择由「数据备份」生成的完整备份文件。");

                    if (manifest.ManifestVersion >= 2)
                    {
                        if (manifest.Scopes == null || manifest.Scopes.Count == 0)
                            return BackupValidationResult.Invalid("备份清单缺少范围信息（Scopes 为空）");

                        var requiredKeys = new[] { BackupScopeKeys.Project, BackupScopeKeys.ModulesDesign, BackupScopeKeys.ModulesGenerate };
                        var missing = requiredKeys.Where(k => !manifest.Scopes.Any(s => s.LogicalKey == k)).ToList();
                        if (missing.Count > 0)
                            return BackupValidationResult.Invalid($"备份缺少必需范围: {string.Join(", ", missing)}");
                    }

                    return BackupValidationResult.Valid(manifest);
                });
            }
            catch (InvalidDataException)
            {
                return BackupValidationResult.Invalid("不是有效的 ZIP 文件");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 校验备份失败: {ex.Message}");
                return BackupValidationResult.Invalid($"校验失败：{ex.Message}");
            }
        }

        #endregion

        #region 恢复

        public async Task<BackupResult> RestoreFromBackupAsync(string zipPath)
        {
            try
            {
                var validation = await ValidateBackupAsync(zipPath);
                if (!validation.IsValid || validation.Manifest == null)
                    return BackupResult.Fail(validation.Message);

                var manifest = validation.Manifest;

                var targets = ResolveRestoreTargets();

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safetyRoot = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    SafetyRootName,
                    $"{StoragePathHelper.CurrentProjectName}_{timestamp}");
                Directory.CreateDirectory(safetyRoot);
                var safetyRootCreated = true;

                var movedPairs = new List<(string LogicalKey, string OriginalPath, string SafetyPath)>();
                try
                {
                    foreach (var (logicalKey, targetPath) in targets)
                    {
                        if (!Directory.Exists(targetPath))
                            continue;
                        var safetyPath = Path.Combine(safetyRoot, logicalKey);
                        Directory.Move(targetPath, safetyPath);
                        movedPairs.Add((logicalKey, targetPath, safetyPath));
                        TM.App.Log($"[ProjectBackupService] 安全副本: {logicalKey} -> {safetyPath}");
                    }
                }
                catch (Exception moveEx)
                {
                    TM.App.Log($"[ProjectBackupService] 创建安全副本失败，回滚: {moveEx.Message}");
                    RollbackMovedPairs(movedPairs);
                    if (safetyRootCreated)
                        TryDeleteEmptyDirectory(safetyRoot);
                    return BackupResult.Fail($"无法创建安全副本：{moveEx.Message}");
                }

                try
                {
                    var prefixToTarget = new List<(string ZipPrefix, string DestDir)>();
                    foreach (var scope in manifest.Scopes)
                    {
                        if (!targets.TryGetValue(scope.LogicalKey, out var destDir))
                            continue;
                        Directory.CreateDirectory(destDir);
                        prefixToTarget.Add((scope.ZipPrefix, destDir));
                    }

                    await Task.Run(() => ExtractZipMultipleTargets(zipPath, prefixToTarget));
                    TM.App.Log($"[ProjectBackupService] 备份恢复完成: 来源={zipPath}, 范围数={prefixToTarget.Count}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectBackupService] 解压失败，开始回滚: {ex.Message}");
                    try
                    {
                        foreach (var (_, originalPath, _) in movedPairs)
                        {
                            if (Directory.Exists(originalPath))
                                Directory.Delete(originalPath, recursive: true);
                        }
                        RollbackMovedPairs(movedPairs);
                        TM.App.Log("[ProjectBackupService] 回滚成功");
                    }
                    catch (Exception rollbackEx)
                    {
                        TM.App.Log($"[ProjectBackupService] 回滚失败: {rollbackEx.Message}");
                        return BackupResult.Fail(
                            $"恢复失败且回滚异常！原始数据保留在: {safetyRoot}\n错误: {ex.Message}");
                    }
                    if (safetyRootCreated)
                        TryDeleteEmptyDirectory(safetyRoot);
                    return BackupResult.Fail($"恢复失败已回滚：{ex.Message}");
                }

                CleanupOldSafetyCopies(DefaultSafetyCopyKeepCount);

                return BackupResult.Ok(
                    $"恢复成功（项目: {manifest.ProjectName}, 备份时间: {manifest.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}, 范围: {manifest.Scopes.Count} 项）",
                    StoragePathHelper.GetCurrentProjectPath(),
                    new FileInfo(zipPath).Length,
                    safetyRoot);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 恢复异常: {ex.Message}");
                return BackupResult.Fail($"恢复失败：{ex.Message}");
            }
        }

        private static Dictionary<string, string> ResolveRestoreTargets()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BackupScopeKeys.Project] = StoragePathHelper.GetCurrentProjectPath(),
                [BackupScopeKeys.ModulesDesign] = StoragePathHelper.GetModulesStoragePath("Design"),
                [BackupScopeKeys.ModulesGenerate] = StoragePathHelper.GetModulesStoragePath("Generate")
            };
        }

        private static void RollbackMovedPairs(List<(string LogicalKey, string OriginalPath, string SafetyPath)> moved)
        {
            foreach (var (_, original, safety) in moved)
            {
                try
                {
                    if (!Directory.Exists(original) && Directory.Exists(safety))
                        Directory.Move(safety, original);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectBackupService] 回滚单项失败 {original}: {ex.Message}");
                }
            }
        }

        public int CleanupOldSafetyCopies(int keepCount)
        {
            try
            {
                if (keepCount < 0) keepCount = 0;
                var safetyDir = Path.Combine(StoragePathHelper.GetStorageRoot(), SafetyRootName);
                if (!Directory.Exists(safetyDir))
                    return 0;

                var dirs = new DirectoryInfo(safetyDir)
                    .GetDirectories()
                    .OrderByDescending(d => d.CreationTimeUtc)
                    .Skip(keepCount)
                    .ToList();

                int deleted = 0;
                foreach (var d in dirs)
                {
                    try
                    {
                        d.Delete(recursive: true);
                        deleted++;
                        TM.App.Log($"[ProjectBackupService] 清理过期安全副本: {d.Name}");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ProjectBackupService] 清理安全副本失败 {d.Name}: {ex.Message}");
                    }
                }
                return deleted;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 扫描安全副本目录失败: {ex.Message}");
                return 0;
            }
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                if (Directory.EnumerateFileSystemEntries(path).Any()) return;
                Directory.Delete(path);
                TM.App.Log($"[ProjectBackupService] 删除空安全副本目录: {path}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 删除空目录失败 {path}: {ex.Message}");
            }
        }

        #endregion

        #region 延迟恢复（启动期消费）

        private static string GetPendingRestorePath()
            => Path.Combine(StoragePathHelper.GetStorageRoot(), SafetyRootName, PendingRestoreFileName);

        public static bool HasPendingRestore()
        {
            try
            {
                return File.Exists(GetPendingRestorePath());
            }
            catch
            {
                return false;
            }
        }

        public async Task<BackupResult> SchedulePendingRestoreAsync(string zipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(zipPath))
                    return BackupResult.Fail("未指定备份文件路径");

                var validation = await ValidateBackupAsync(zipPath);
                if (!validation.IsValid || validation.Manifest == null)
                    return BackupResult.Fail(validation.Message);

                var info = new PendingRestoreInfo
                {
                    ZipPath = Path.GetFullPath(zipPath),
                    ScheduledAtUtc = DateTime.UtcNow,
                    ProjectName = StoragePathHelper.CurrentProjectName,
                    RetryCount = 0
                };

                var statePath = GetPendingRestorePath();
                var stateDir = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(stateDir))
                    Directory.CreateDirectory(stateDir);

                await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(info, JsonHelper.CnDefault));
                TM.App.Log($"[ProjectBackupService] 已安排恢复任务，将于下次启动执行: {info.ZipPath}");

                return BackupResult.Ok("恢复任务已安排，请重启应用以完成恢复", statePath, new FileInfo(zipPath).Length);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 安排恢复任务失败: {ex.Message}");
                return BackupResult.Fail($"安排恢复失败：{ex.Message}");
            }
        }

        public async Task<bool> TryConsumePendingRestoreAsync()
        {
            var statePath = GetPendingRestorePath();
            if (!File.Exists(statePath))
                return false;

            PendingRestoreInfo? info = null;
            try
            {
                var json = await File.ReadAllTextAsync(statePath);
                info = JsonSerializer.Deserialize<PendingRestoreInfo>(json, JsonHelper.CnDefault);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 读取待恢复任务失败，已删除状态文件: {ex.Message}");
                TryDeleteFile(statePath);
                return false;
            }

            if (info == null || string.IsNullOrWhiteSpace(info.ZipPath))
            {
                TM.App.Log("[ProjectBackupService] 待恢复任务内容为空，已清理");
                TryDeleteFile(statePath);
                return false;
            }

            if (!File.Exists(info.ZipPath))
            {
                TM.App.Log($"[ProjectBackupService] 待恢复的备份文件已不存在，放弃任务: {info.ZipPath}");
                TryDeleteFile(statePath);
                return false;
            }

            if (info.RetryCount >= PendingRestoreInfo.MaxRetryCount)
            {
                TM.App.Log($"[ProjectBackupService] 恢复任务已达最大重试次数（{info.RetryCount}），放弃: {info.ZipPath}");
                TryDeleteFile(statePath);
                return false;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(info.ProjectName) &&
                    !string.Equals(info.ProjectName, StoragePathHelper.CurrentProjectName, StringComparison.Ordinal))
                {
                    StoragePathHelper.CurrentProjectName = info.ProjectName;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 切换项目名失败（继续尝试恢复）: {ex.Message}");
            }

            try
            {
                info.RetryCount++;
                await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(info, JsonHelper.CnDefault));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 更新重试次数失败（继续尝试恢复）: {ex.Message}");
            }

            TM.App.Log($"[ProjectBackupService] 启动期开始恢复: {info.ZipPath} (项目: {info.ProjectName}, 重试: {info.RetryCount}/{PendingRestoreInfo.MaxRetryCount})");

            var result = await RestoreFromBackupAsync(info.ZipPath);
            if (result.Success)
            {
                TryDeleteFile(statePath);
                TM.App.Log($"[ProjectBackupService] 启动期恢复成功，已清理状态文件");
                return true;
            }
            else
            {
                TM.App.Log($"[ProjectBackupService] 启动期恢复失败: {result.Message}");
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    TM.App.Log($"[ProjectBackupService] 删除文件: {path}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectBackupService] 删除文件失败 {path}: {ex.Message}");
            }
        }

        #endregion

        #region 大小计算

        public async Task<long> CalculateProjectSizeAsync()
        {
            var sources = BuildFullBackupSources();
            long total = 0;
            foreach (var src in sources)
                total += await CalculateDirectorySizeAsync(src.SourcePath);
            return total;
        }

        public Task<long> CalculateChaptersSizeAsync()
            => CalculateDirectorySizeAsync(StoragePathHelper.GetProjectChaptersPath());

        private static Task<long> CalculateDirectorySizeAsync(string path)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(path))
                    return 0L;
                try
                {
                    return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                                    .Sum(f => new FileInfo(f).Length);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ProjectBackupService] 计算目录大小失败 {path}: {ex.Message}");
                    return 0L;
                }
            });
        }

        #endregion

        #region ZIP 内部工具

        private static void CreateZipWithMultipleSources(string zipPath, BackupManifest manifest, IEnumerable<BackupSource> sources)
        {
            var targetDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            var manifestEntry = archive.CreateEntry(ManifestFileName, CompressionLevel.Fastest);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest, JsonHelper.CnDefault));
            }

            foreach (var src in sources)
            {
                if (!Directory.Exists(src.SourcePath))
                {
                    TM.App.Log($"[ProjectBackupService] 跳过不存在的源: {src.LogicalKey} ({src.SourcePath})");
                    continue;
                }

                var sourceFullPath = Path.GetFullPath(src.SourcePath).TrimEnd(Path.DirectorySeparatorChar);
                var prefixLen = sourceFullPath.Length + 1;
                var zipPrefix = string.IsNullOrEmpty(src.ZipPrefix) ? string.Empty : src.ZipPrefix;
                if (zipPrefix.Length > 0 && !zipPrefix.EndsWith('/'))
                    zipPrefix += "/";

                foreach (var filePath in Directory.EnumerateFiles(src.SourcePath, "*", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (string.Equals(fileName, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fullFilePath = Path.GetFullPath(filePath);
                    if (fullFilePath.Length <= prefixLen)
                        continue;

                    var relativePath = fullFilePath.Substring(prefixLen).Replace(Path.DirectorySeparatorChar, '/');
                    var entryName = zipPrefix + relativePath;

                    try
                    {
                        archive.CreateEntryFromFile(fullFilePath, entryName, CompressionLevel.Optimal);
                    }
                    catch (IOException ex)
                    {
                        TM.App.Log($"[ProjectBackupService] 跳过被占用文件: {entryName} ({ex.Message})");
                    }
                }
            }
        }

        private static void ExtractZipMultipleTargets(string zipPath, IEnumerable<(string ZipPrefix, string DestDir)> targets)
        {
            var normalized = targets.Select(t =>
            {
                var p = t.ZipPrefix ?? string.Empty;
                if (p.Length > 0 && !p.EndsWith('/'))
                    p += "/";
                return (Prefix: p, DestDir: Path.GetFullPath(t.DestDir));
            }).ToList();

            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                    continue;

                if (string.Equals(entry.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = normalized.FirstOrDefault(t => entry.FullName.StartsWith(t.Prefix, StringComparison.OrdinalIgnoreCase));
                if (match.DestDir == null)
                    continue;

                var relativeName = entry.FullName.Substring(match.Prefix.Length);
                if (string.IsNullOrEmpty(relativeName))
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(match.DestDir, relativeName));

                var destDirWithSep = match.DestDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!targetPath.StartsWith(destDirWithSep, StringComparison.OrdinalIgnoreCase) &&
                    !targetPath.Equals(match.DestDir, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"备份包包含非法路径: {entry.FullName}");

                if (entry.FullName.EndsWith("/"))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                var parentDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        #endregion

        #region 通用辅助

        private static string GetAppVersion()
        {
            try
            {
                return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        #endregion
    }
}
