using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class AIOutputParser
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ValidationSummaryData ParseAIOutput(string jsonOutput, int volumeNumber)
        {
            if (string.IsNullOrWhiteSpace(jsonOutput))
            {
                throw new JsonException("AI输出为空");
            }

            var jsonStr = ExtractJsonFromContent(jsonOutput);

            AIValidationOutput aiResult;
            try
            {
                aiResult = JsonSerializer.Deserialize<AIValidationOutput>(jsonStr, _jsonOptions)
                    ?? throw new JsonException("JSON反序列化结果为null");
            }
            catch (JsonException ex)
            {
                throw new JsonException($"JSON解析失败: {ex.Message}", ex);
            }

            ValidateModuleResultsCount(aiResult.ModuleResults);

            ValidateExtendedDataFields(aiResult.ModuleResults);

            return ConvertToValidationSummaryData(aiResult, volumeNumber);
        }

        private string ExtractJsonFromContent(string content)
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                throw new JsonException("AI返回内容中未找到有效JSON");
            }

            return content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        private void ValidateModuleResultsCount(List<AIModuleResult>? moduleResults)
        {
            if (moduleResults == null)
            {
                throw new InvalidOperationException("moduleResults为null，必须输出完整规则清单");
            }

            if (moduleResults.Count != ValidationRules.TotalRuleCount)
            {
                throw new InvalidOperationException(
                    $"moduleResults必须输出{ValidationRules.TotalRuleCount}项，实际为{moduleResults.Count}项");
            }

            var providedModules = moduleResults.Select(m => m.ModuleName).ToHashSet();
            var expectedModules = ValidationRules.AllModuleNames.ToHashSet();

            var missingModules = expectedModules.Except(providedModules).ToList();
            if (missingModules.Count > 0)
            {
                throw new InvalidOperationException(
                    $"moduleResults缺少以下规则: {string.Join(", ", missingModules)}");
            }
        }

        private void ValidateExtendedDataFields(List<AIModuleResult> moduleResults)
        {
            foreach (var result in moduleResults)
            {
                var expectedFields = ValidationRules.GetExtendedDataSchema(result.ModuleName);
                if (expectedFields.Length == 0)
                {
                    continue;
                }

                if (result.ExtendedData == null)
                {
                    throw new JsonException(
                        $"规则 {result.ModuleName} 的extendedData为null，必须包含字段键名");
                }

                var expectedCamelCaseFields = expectedFields
                    .Select(f => char.ToLowerInvariant(f[0]) + f.Substring(1))
                    .ToHashSet();

                var providedFields = result.ExtendedData.Keys.ToHashSet();
                var missingFields = expectedCamelCaseFields.Except(providedFields).ToList();

                if (missingFields.Count > 0)
                {
                    throw new JsonException(
                        $"规则 {result.ModuleName} 的extendedData缺少字段: {string.Join(", ", missingFields)}");
                }
            }
        }

        private ValidationSummaryData ConvertToValidationSummaryData(AIValidationOutput aiResult, int volumeNumber)
        {
            var moduleResults = aiResult.ModuleResults.Select(m => new ModuleValidationResult
            {
                ModuleName = m.ModuleName,
                DisplayName = m.DisplayName ?? ValidationRules.GetDisplayName(m.ModuleName),
                VerificationType = m.VerificationType ?? string.Empty,
                Result = m.Result ?? "未校验",
                IssueDescription = m.IssueDescription ?? string.Empty,
                FixSuggestion = m.FixSuggestion ?? string.Empty,
                ExtendedDataJson = m.ExtendedData != null 
                    ? JsonSerializer.Serialize(m.ExtendedData, _jsonOptions) 
                    : string.Empty,
                ProblemItemsJson = m.ProblemItems != null 
                    ? JsonSerializer.Serialize(m.ProblemItems, _jsonOptions) 
                    : "[]"
            }).ToList();

            var volume = aiResult.Volume ?? new AIVolumeInfo { VolumeNumber = volumeNumber };

            return new ValidationSummaryData
            {
                Id = ShortIdGenerator.New("D"),
                Name = $"第{volume.VolumeNumber}卷校验",
                Icon = GetOverallResultIcon(aiResult.OverallResult ?? "未校验"),
                Category = $"第{volume.VolumeNumber}卷",
                TargetVolumeNumber = volume.VolumeNumber,
                TargetVolumeName = volume.VolumeName ?? $"第{volume.VolumeNumber}卷",
                SampledChapterCount = volume.SampledChapterCount,
                SampledChapterIds = volume.SampledChapterIds ?? new List<string>(),
                LastValidatedTime = volume.ValidatedTime ?? DateTime.Now,
                OverallResult = aiResult.OverallResult ?? "未校验",
                ModuleResults = moduleResults,
                DependencyModuleVersions = aiResult.DependencyModuleVersions ?? new Dictionary<string, int>()
            };
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
    }

    #region AI输出数据模型

    public class AIValidationOutput
    {
        [System.Text.Json.Serialization.JsonPropertyName("Volume")] public AIVolumeInfo? Volume { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("OverallResult")] public string? OverallResult { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DependencyModuleVersions")] public Dictionary<string, int>? DependencyModuleVersions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ModuleResults")] public List<AIModuleResult> ModuleResults { get; set; } = new();
    }

    public class AIVolumeInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("VolumeNumber")] public int VolumeNumber { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("VolumeName")] public string? VolumeName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ValidatedTime")] public DateTime? ValidatedTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SampledChapterCount")] public int SampledChapterCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SampledChapterIds")] public List<string>? SampledChapterIds { get; set; }
    }

    public class AIModuleResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("ModuleName")] public string ModuleName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DisplayName")] public string? DisplayName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("VerificationType")] public string? VerificationType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Result")] public string? Result { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IssueDescription")] public string? IssueDescription { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("FixSuggestion")] public string? FixSuggestion { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExtendedData")] public Dictionary<string, string>? ExtendedData { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ProblemItems")] public List<AIProblemItem>? ProblemItems { get; set; }
    }

    public class AIProblemItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Reason")] public string Reason { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Details")] public string? Details { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Suggestion")] public string? Suggestion { get; set; }
    }

    #endregion
}
