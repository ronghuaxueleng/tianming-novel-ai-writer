using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.AI;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

public partial class PromptManagementViewModel
{
    protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
    {
        IsAIGenerateEnabled = hasSelection && _currentEditingData != null;
    }

    protected override bool CanExecuteAIGenerate() => _currentEditingData != null;

    protected override async System.Threading.Tasks.Task ExecuteAIGenerateAsync()
    {
        var skChat = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
        if (skChat.IsMainConversationGenerating)
        {
            var confirmed = StandardDialog.ShowConfirm(
                "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                "互斥提醒");
            if (!confirmed)
                return;
            skChat.CancelCurrentRequest();
        }

        try
        {
            var metadata = ModuleMetadataRegistry.GetMetadata(FormCategory);

            var (rootCategory, subCategory) = ResolvePromptCategories(FormCategory);

            var context = new PromptGenerationContext
            {
                PromptRootCategory = rootCategory,
                PromptSubCategory = subCategory,
                ModuleKey = $"PromptTools.{FormCategory}",
                ModuleDisplayName = FormCategory ?? "提示词管理",
                TemplateName = FormName,
                ExtraRequirement = FormDescription,
                FieldNames = new[] { "Prompt正文", "说明" },
                OutputFieldNames = metadata?.OutputFields,
                InputVariableNames = metadata?.InputVariables,
                ModuleType = metadata?.ModuleType,
                Description = metadata?.Description
            };

            var result = await _promptService.GenerateModulePromptAsync(context);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            {
                TM.App.Log($"[PromptManagement] 生成提示词失败: {result.ErrorMessage}");
                GlobalToast.Error("生成失败", $"AI未返回有效内容：{result.ErrorMessage ?? "未知错误"}");
                return;
            }

            FormSystemPrompt = result.Content;

            if (metadata?.InputVariables != null && metadata.InputVariables.Length > 0)
            {
                FormVariables = string.Join(",", metadata.InputVariables);
            }

            if (!string.IsNullOrWhiteSpace(result.Description))
            {
                FormDescription = result.Description;
            }
            else if (!string.IsNullOrWhiteSpace(metadata?.Description))
            {
                FormDescription = metadata.Description;
            }
            else
            {
                var fieldsToGenerate = new Dictionary<string, string>
                {
                    { "说明", "用一句话（不超过50字）描述该模板的用途和适用场景" }
                };

                var generatedFields = await GenerateFieldsAsync(result.Content, FormName, fieldsToGenerate);

                if (generatedFields.TryGetValue("说明", out var desc))
                {
                    FormDescription = desc;
                }
            }

            var metaInfo = metadata != null ? "（已应用业务元数据）" : "";
            GlobalToast.Success("生成完成", $"已为当前模板生成Prompt正文{metaInfo}");
        }
        catch (System.Exception ex)
        {
            TM.App.Log($"[PromptManagement] AI生成提示词失败: {ex.Message}");
            GlobalToast.Error("生成失败", $"生成失败：{ex.Message}");
        }
    }

    private (string? RootCategory, string? SubCategory) ResolvePromptCategories(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return (null, null);

        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (!lookup.TryGetValue(categoryName, out var current))
            return (null, categoryName);

        var sub = current.Level == 1 ? null : current.Name;
        var node = current;
        while (!string.IsNullOrWhiteSpace(node.ParentCategory) && lookup.TryGetValue(node.ParentCategory, out var parent))
        {
            node = parent;
        }

        return (node.Name, sub ?? categoryName);
    }

    private async System.Threading.Tasks.Task<Dictionary<string, string>> GenerateFieldsAsync(
        string generatedContent,
        string? templateName,
        Dictionary<string, string> fieldDescriptions)
    {
        var result = new Dictionary<string, string>();
        if (fieldDescriptions == null || fieldDescriptions.Count == 0)
            return result;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("根据以下已生成的内容，补全相关字段。");
            sb.AppendLine();
            sb.AppendLine($"<template_name>{templateName ?? "未命名"}</template_name>");
            sb.AppendLine();
            sb.AppendLine("<generated_content>");
            sb.AppendLine(generatedContent);
            sb.AppendLine("</generated_content>");
            sb.AppendLine();
            sb.AppendLine("<fields_to_fill>");
            foreach (var kv in fieldDescriptions)
            {
                sb.AppendLine($"- {kv.Key}：{kv.Value}");
            }
            sb.AppendLine("</fields_to_fill>");
            sb.AppendLine();
            sb.AppendLine("<output_rules>");
            sb.AppendLine("请以JSON格式输出，只包含字段名和对应的值，不要任何额外解释。示例：");
            sb.AppendLine("{");
            foreach (var kv in fieldDescriptions)
            {
                sb.AppendLine($"  \"{kv.Key}\": \"生成的内容\",");
            }
            sb.AppendLine("}");
            sb.AppendLine("</output_rules>");

            var aiResult = await _aiTextGenerationService.GenerateAsync(sb.ToString());
            if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.Content))
            {
                var json = aiResult.Content.Trim();
                var jsonStart = json.IndexOf('{');
                var jsonEnd = json.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var kv in fieldDescriptions)
                    {
                        if (doc.RootElement.TryGetProperty(kv.Key, out var prop) &&
                            prop.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var val = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                result[kv.Key] = val;
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            TM.App.Log($"[PromptManagement] 补全字段失败: {ex.Message}");
        }
        return result;
    }
}
