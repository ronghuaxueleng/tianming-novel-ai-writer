using System.Collections.Generic;

namespace TM.Framework.Common.Helpers.AI;

public class PromptGenerationResult
{
    public bool Success { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = new List<string>();

    public string? ErrorMessage { get; set; }
}
