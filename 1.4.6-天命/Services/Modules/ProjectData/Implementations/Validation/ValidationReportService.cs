using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;
using TM.Services.Modules.ProjectData.Models.Validation;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ValidationReportService : IValidationReportService
    {
        private readonly IContextService _contextService;
        private readonly IGeneratedContentService _contentService;
        private readonly IPublishService _publishService;
        private readonly IUnifiedValidationService _unifiedValidationService;
        private readonly JsonSerializerOptions _jsonOptions;

        private string ReportsDirectory => Path.Combine(StoragePathHelper.GetProjectValidationPath(), "reports");

        public ValidationReportService(
            IContextService contextService,
            IGeneratedContentService contentService,
            IPublishService publishService,
            IUnifiedValidationService unifiedValidationService)
        {
            _contextService = contextService;
            _contentService = contentService;
            _publishService = publishService;
            _unifiedValidationService = unifiedValidationService;

            _jsonOptions = JsonHelper.CnDefault;

            var reportsDir = ReportsDirectory;
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }
        }

        public async Task<ValidationReport> ValidateChapterAsync(string chapterId)
        {
            TM.App.Log($"[ValidationReportService] 开始校验章节: {chapterId}");

            var report = new ValidationReport
            {
                Id = ShortIdGenerator.New("D"),
                ChapterId = chapterId,
                ValidatedTime = DateTime.Now
            };

            try
            {
                var unified = await _unifiedValidationService.ValidateChapterAsync(chapterId);

                report.ChapterTitle = unified.ChapterTitle;

                foreach (var moduleName in ValidationRules.AllModuleNames)
                {
                    var displayName = ValidationRules.GetDisplayName(moduleName);

                    if (unified.IssuesByModule.TryGetValue(moduleName, out var issues) && issues.Count > 0)
                    {
                        foreach (var issue in issues)
                        {
                            report.Items.Add(new ValidationItem
                            {
                                Id = ShortIdGenerator.New("D"),
                                ValidationType = moduleName,
                                Name = displayName,
                                Description = issue.Message,
                                Details = issue.Message,
                                Suggestion = issue.Suggestion,
                                Location = issue.Location,
                                Result = issue.Severity switch
                                {
                                    "Error" => ValidationItemResult.Error,
                                    "Warning" => ValidationItemResult.Warning,
                                    "Info" => ValidationItemResult.Pass,
                                    _ => ValidationItemResult.Warning
                                }
                            });
                        }
                    }
                    else
                    {
                        report.Items.Add(new ValidationItem
                        {
                            Id = ShortIdGenerator.New("D"),
                            ValidationType = moduleName,
                            Name = displayName,
                            Description = $"{displayName}校验通过",
                            Details = string.Empty,
                            Suggestion = string.Empty,
                            Location = string.Empty,
                            Result = ValidationItemResult.Pass
                        });
                    }
                }

                report.Result = unified.OverallResult switch
                {
                    "通过" => ValidationResult.Pass,
                    "警告" => ValidationResult.Warning,
                    "失败" => ValidationResult.Error,
                    _ => ValidationResult.Error
                };

                report.Summary = string.IsNullOrWhiteSpace(unified.OverallResult)
                    ? "校验完成"
                    : $"校验完成：{unified.OverallResult}（问题数：{unified.TotalIssueCount}）";

                await SaveReportAsync(report);

                TM.App.Log($"[ValidationReportService] 校验完成: {report.Result}");
            }
            catch (Exception ex)
            {
                report.Result = ValidationResult.Error;
                report.Summary = $"校验异常: {ex.Message}";
                TM.App.Log($"[ValidationReportService] 校验异常: {ex.Message}");
            }

            return report;
        }

        public async Task<List<ValidationReport>> GetReportsAsync(string chapterId)
        {
            var reports = new List<ValidationReport>();
            var chapterDir = Path.Combine(ReportsDirectory, chapterId);

            if (!Directory.Exists(chapterDir))
                return reports;

            var files = Directory.GetFiles(chapterDir, "*.json");
            foreach (var file in files.OrderByDescending(f => f))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var report = JsonSerializer.Deserialize<ValidationReport>(json, _jsonOptions);
                    if (report != null)
                    {
                        reports.Add(report);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ValidationReportService] 读取报告失败: {ex.Message}");
                }
            }

            return reports;
        }

        public async Task<ValidationReport?> GetLatestReportAsync(string chapterId)
        {
            var reports = await GetReportsAsync(chapterId);
            return reports.OrderByDescending(r => r.ValidatedTime).FirstOrDefault();
        }

        public async Task SaveReportAsync(ValidationReport report)
        {
            var chapterDir = Path.Combine(ReportsDirectory, report.ChapterId);
            if (!Directory.Exists(chapterDir))
            {
                Directory.CreateDirectory(chapterDir);
            }

            var fileName = $"{report.ValidatedTime:yyyyMMdd_HHmmss}_{report.Id}.json";
            var filePath = Path.Combine(chapterDir, fileName);

            var json = JsonSerializer.Serialize(report, _jsonOptions);
            var tmpVrs = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpVrs, json);
            File.Move(tmpVrs, filePath, overwrite: true);

            TM.App.Log($"[ValidationReportService] 保存报告: {filePath}");
        }

        public async Task DeleteReportAsync(string reportId)
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(ReportsDirectory, $"*_{reportId}.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    File.Delete(file);
                    TM.App.Log($"[ValidationReportService] 删除报告: {file}");
                }
            });
        }

        public async Task<Dictionary<string, ValidationResult>> GetAllChapterStatusAsync()
        {
            var status = new Dictionary<string, ValidationResult>();

            if (!Directory.Exists(ReportsDirectory))
                return status;

            var chapterDirs = Directory.GetDirectories(ReportsDirectory);
            foreach (var dir in chapterDirs)
            {
                var chapterId = Path.GetFileName(dir);
                var latestReport = await GetLatestReportAsync(chapterId);
                if (latestReport != null)
                {
                    status[chapterId] = latestReport.Result;
                }
            }

            return status;
        }
    }
}
