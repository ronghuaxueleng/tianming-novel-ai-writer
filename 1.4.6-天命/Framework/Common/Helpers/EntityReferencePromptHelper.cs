using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TM.Framework.Common.Helpers
{
    public static class EntityReferencePromptHelper
    {
        public static string BuildCandidateSection(
            string title,
            IEnumerable<string> candidates,
            string fieldHint,
            string separator = "、")
        {
            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"<section name=\"candidates\" title=\"{title}\">");
            sb.AppendLine($"字段约束：{fieldHint}");
            sb.AppendLine(string.Join(separator, list));
            sb.AppendLine("输出要求：仅使用以上列表中的标准名称；多个名称使用 " + separator + " 分隔；不要输出ID或别名。");
            sb.AppendLine("</section>");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
