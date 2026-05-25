using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TM.Framework.Common.Services;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.Services
{
    public class ReferenceParser
    {
        private readonly GuideContextService _guideContextService;

        private static readonly Regex ReferencePattern = new(
            @"@(续写|continue|重写|rewrite|仿写|imitate)(?:[:uff1a]\s*)?([^\s@]+)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public ReferenceParser(GuideContextService guideContextService)
        {
            _guideContextService = guideContextService;
        }

        public List<Reference> ParseReferences(string text)
        {
            var references = new List<Reference>();
            var matches = ReferencePattern.Matches(text);

            foreach (Match match in matches)
            {
                references.Add(new Reference
                {
                    FullMatch = match.Value,
                    Type = NormalizeType(match.Groups[1].Value),
                    Name = match.Groups[2].Success ? match.Groups[2].Value : null,
                    StartIndex = match.Index,
                    Length = match.Length
                });
            }

            return references;
        }

        public async Task<string> ExpandReferencesAsync(string text)
        {
            var references = ParseReferences(text);
            if (references.Count == 0)
                return text;

            references.Sort((a, b) => b.StartIndex.CompareTo(a.StartIndex));

            foreach (var reference in references)
            {
                var content = await ResolveReferenceAsync(reference);
                if (!string.IsNullOrEmpty(content))
                {
                    text = text.Remove(reference.StartIndex, reference.Length)
                               .Insert(reference.StartIndex, content);
                }
            }

            return text;
        }

        private async Task<string?> ResolveReferenceAsync(Reference reference)
        {
            try
            {
                switch (reference.Type)
                {
                    case "chapter":
                    case "rewrite":
                        return await ResolveChapterAsync(reference.Name);

                    default:
                        return reference.FullMatch;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ReferenceParser] 解析引用失败: {ex.Message}");
                return reference.FullMatch;
            }
        }

        private async Task<string?> ResolveChapterAsync(string? chapterId)
        {
            if (string.IsNullOrEmpty(chapterId))
                return "[请指定章节ID]";

            var genContext = await _guideContextService.BuildContentContextAsync(chapterId);
            if (genContext == null)
                return $"[未找到章节: {chapterId}]";

            var sb = new System.Text.StringBuilder();
            var safeTitle = (genContext.Title ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
            sb.AppendLine($"<context_block type=\"chapter_reference\" title=\"{safeTitle}\">{genContext.Summary}</context_block>");

            try
            {
                var vectorSearch = ServiceLocator.Get<VectorSearchService>();
                var results = await vectorSearch.SearchByChapterAsync(chapterId, topK: 2);
                if (results != null && results.Count > 0)
                {
                    sb.AppendLine("<context_block type=\"key_excerpts\">");
                    foreach (var r in results)
                    {
                        var snippet = r.Content.Length > 400 ? r.Content[..400] + "…" : r.Content;
                        sb.AppendLine(snippet);
                    }
                    sb.AppendLine("</context_block>");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ReferenceParser] 章节片段检索失败（非致命）: {ex.Message}");
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeType(string type)
        {
            return type.ToLower() switch
            {
                "续写" or "continue" => "chapter",
                "重写" or "rewrite" => "rewrite",
                "仿写" or "imitate" => "imitate",
                _ => type.ToLower()
            };
        }

        public static List<ReferenceTypeInfo> GetAvailableTypes()
        {
            return new List<ReferenceTypeInfo>
            {
                new() { Type = "续写", Icon = "\U0001F4D6", Description = "注入章节上下文" },
                new() { Type = "重写", Icon = "\u267B\uFE0F", Description = "重写指定章节" },
                new() { Type = "仿写", Icon = "\u270D\uFE0F", Description = "引用爬取内容" },
            };
        }
    }

    public class Reference
    {
        public string FullMatch { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }

    public class ReferenceTypeInfo
    {
        public string Type { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
