using System.Collections.Generic;

namespace TM.Framework.Common.Helpers.AI;

public class PromptGenerationContext
{
    public string? PromptRootCategory { get; set; }

    public string? PromptSubCategory { get; set; }

    public string ModuleKey { get; set; } = string.Empty;

    public string ModuleDisplayName { get; set; } = string.Empty;

    public string? TemplateName { get; set; }

    public string? ExtraRequirement { get; set; }

    public IReadOnlyList<string>? FieldNames { get; set; }

    public IReadOnlyList<string>? OutputFieldNames { get; set; }

    public IReadOnlyList<string>? InputVariableNames { get; set; }

    public string? ModuleType { get; set; }

    public string? Description { get; set; }
}
