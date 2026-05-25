using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.UI.Workspace.Services.Spec
{
    public static class SpecMerger
    {
        public static CreativeSpec Merge(params CreativeSpec?[] specs)
        {
            CreativeSpec? result = null;

            foreach (var spec in specs.Where(s => s != null))
            {
                result = CreativeSpec.Merge(result, spec);
            }

            return result ?? new CreativeSpec();
        }

        public static CreativeSpec MergeThreeLayers(
            CreativeSpec? project,
            CreativeSpec? chapter,
            CreativeSpec? temporary)
        {
            return Merge(project, chapter, temporary);
        }

        public static bool IsValid(CreativeSpec? spec)
        {
            if (spec == null) return false;

            return !string.IsNullOrEmpty(spec.WritingStyle) ||
                   !string.IsNullOrEmpty(spec.Pov) ||
                   spec.TargetWordCount.HasValue ||
                   !string.IsNullOrEmpty(spec.Tone);
        }

        public static string GetSummary(CreativeSpec? spec)
        {
            if (spec == null) return "无规格约束";

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(spec.WritingStyle))
                parts.Add($"风格:{spec.WritingStyle}");
            if (!string.IsNullOrEmpty(spec.Pov))
                parts.Add($"视角:{spec.Pov}");
            if (spec.TargetWordCount.HasValue)
                parts.Add($"字数:{spec.TargetWordCount}");
            if (!string.IsNullOrEmpty(spec.Tone))
                parts.Add($"基调:{spec.Tone}");

            return parts.Count > 0 ? string.Join(" | ", parts) : "默认规格";
        }
    }
}
