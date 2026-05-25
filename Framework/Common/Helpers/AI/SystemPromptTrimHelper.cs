using System;
using System.Text.RegularExpressions;

namespace TM.Framework.Common.Helpers.AI
{
    public static class SystemPromptTrimHelper
    {
        private static readonly Regex SectionRegex = new(
            @"<module_section\s+id=""(?<id>[^""]+)""\s*>(?<body>.*?)</module_section>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex MultiNewlineRegex = new(@"\n{3,}", RegexOptions.Compiled);

        public static string Trim(string prompt, string? activeHint)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(activeHint))
                return prompt;

            var matches = SectionRegex.Matches(prompt);
            if (matches.Count == 0)
                return prompt;

            Match? matched = null;
            foreach (Match m in matches)
            {
                if (string.Equals(m.Groups["id"].Value.Trim(), activeHint.Trim(), StringComparison.Ordinal))
                {
                    matched = m;
                    break;
                }
            }

            if (matched == null)
            {
                TM.App.Log($"[SystemPromptTrimHelper] ActiveModuleHint=\"{activeHint}\" 未匹配到任何 module_section，保留完整 prompt");
                return prompt;
            }

            var result = prompt;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var m = matches[i];
                if (m == matched)
                {
                    var body = m.Groups["body"].Value;
                    body = body.TrimStart('\r', '\n').TrimEnd('\r', '\n');
                    result = result.Remove(m.Index, m.Length).Insert(m.Index, body);
                }
                else
                {
                    var removeStart = m.Index;
                    var removeEnd = m.Index + m.Length;
                    while (removeEnd < result.Length && (result[removeEnd] == '\r' || result[removeEnd] == '\n'))
                        removeEnd++;
                    result = result.Remove(removeStart, removeEnd - removeStart);
                }
            }

            result = MultiNewlineRegex.Replace(result, "\n\n");

            result = ReplaceDispatchHeaders(result, activeHint.Trim());

            if (TM.App.IsDebugMode)
                TM.App.Log($"[SystemPromptTrimHelper] 裁剪完成: hint=\"{activeHint}\", 原长={prompt.Length}, 裁剪后={result.Length}, 节省={prompt.Length - result.Length}字符");

            return result;
        }

        private static string ReplaceDispatchHeaders(string result, string hint)
        {
            const string designerHeader = "【模块语义边界】\n当前模块类型代表特定设计层次，生成时必须遵守其语义边界：";
            if (result.Contains(designerHeader))
                result = result.Replace(designerHeader, $"【{hint}语义边界】");

            const string creatorDispatch = "根据非空的创作目标字段识别当前任务类型，只遵循对应模块的规范。";
            if (result.Contains(creatorDispatch))
                result = result.Replace(creatorDispatch, $"当前任务：{hint}。");

            return result;
        }
    }
}
