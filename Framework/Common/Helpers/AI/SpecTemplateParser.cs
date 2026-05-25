using System;
using System.Linq;
using TM.Framework.UI.Workspace.Services.Spec;

namespace TM.Framework.Common.Helpers.AI
{
    public static class SpecTemplateParser
    {
        public static CreativeSpec Parse(string? systemPrompt, string? templateName = null)
        {
            var spec = new CreativeSpec { TemplateName = templateName };
            if (string.IsNullOrEmpty(systemPrompt)) return spec;

            foreach (var line in systemPrompt.Split('\n'))
            {
                if (line.Contains("【写作风格】")) spec.WritingStyle = ExtractTagValue(line, "【写作风格】");
                else if (line.Contains("【叙述视角】")) spec.Pov = ExtractTagValue(line, "【叙述视角】");
                else if (line.Contains("【情感基调】")) spec.Tone = ExtractTagValue(line, "【情感基调】");
                else if (line.Contains("【目标字数】")) spec.TargetWordCount = ParseTagInt(ExtractTagValue(line, "【目标字数】"));
                else if (line.Contains("【段落长度】")) spec.ParagraphLength = ParseTagInt(ExtractTagValue(line, "【段落长度】"));
                else if (line.Contains("【对话比例】")) spec.DialogueRatio = ParseTagPercent(ExtractTagValue(line, "【对话比例】"));
                else if (line.Contains("【必须包含】")) spec.MustInclude = SplitList(ExtractTagValue(line, "【必须包含】"));
                else if (line.Contains("【必须避免】")) spec.MustAvoid = SplitList(ExtractTagValue(line, "【必须避免】"));
            }

            return spec;
        }

        private static string? ExtractTagValue(string line, string tag)
        {
            var idx = line.IndexOf(tag, StringComparison.Ordinal);
            return idx >= 0 ? line.Substring(idx + tag.Length).Trim() : null;
        }

        private static int? ParseTagInt(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var numStr = new string(value.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(numStr, out var r) ? r : null;
        }

        private static double? ParseTagPercent(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var numStr = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (double.TryParse(numStr, out var r)) return r > 1 ? r / 100.0 : r;
            return null;
        }

        private static string[]? SplitList(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return value.Split(',', '，', '、');
        }
    }
}
