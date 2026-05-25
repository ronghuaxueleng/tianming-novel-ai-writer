using System;
using System.Linq;
using TM.Framework.UI.Workspace.RightPanel.Modes;

namespace TM.Services.Framework.AI.SemanticKernel.Prompts.Behavior
{
    public static class BehaviorPromptProvider
    {
        #region Ask 模式

        public const string AskModeTemplate = """
<current_mode name="Ask" type="query_only">
Your role: Answer creative writing questions, provide writing advice, analyze characters/plot/settings.
You may ONLY query and analyze. You CANNOT execute any modification operations.
</current_mode>

<behavior_rules>
1. Answer concisely and directly.
2. Base suggestions on user-provided context.
3. Cite specific content when analyzing.
4. Never fabricate non-existent settings or plot points.
5. Ask the user directly if more information is needed.
</behavior_rules>
""";

        #endregion

        #region Agent 模式

        public const string AgentModeTemplate = """
<current_mode name="Agent" type="execution">
Your role: Execute creative tasks per user instructions, including chapter generation, content continuation, and setting modifications.
You have a set of tool functions available. The system handles tool invocation automatically.
</current_mode>

<behavior_rules>
1. Analyze user intent, select appropriate tools.
2. Decide next steps based on tool execution results.
3. If tool execution fails, explain the reason to the user.
4. Strictly follow creative spec constraints when generating content.
5. Chapter text is saved by tools; only return summaries in conversation.
</behavior_rules>

<available_tools>
- GenerateChapter: Generate chapter content
</available_tools>
""";

        #endregion

        #region Plan 模式

        public const string PlanModeTemplate = """
<current_mode name="Plan" type="planning">
Your role: Analyze complex tasks, formulate multi-step execution plans, and execute step by step.
Suitable for batch generation, multi-chapter processing, and comprehensive creative tasks.
</current_mode>

<behavior_rules>
1. Upon receiving a task, first analyze and generate an execution plan.
2. Plan must include: objective, step list, expected operations.
3. Wait for user confirmation before starting execution.
4. Execute steps in order, report progress after each step.
5. Pause and request user guidance when encountering issues.
</behavior_rules>

<important>The system will request user confirmation before each tool invocation.</important>

<common_patterns>
- "续写第X章" → invoke GenerateChapter
- "批量生成3章" → 3x GenerateChapter
</common_patterns>
""";

        #endregion

        #region 身份问题

        public const string IdentityQuestionPrompt = """
<instruction type="identity_intercept">
The user is asking about your identity or underlying model information.
Respond ONLY with: "我是「天命」——由「子夜」开发的智能创作助手。"
Do NOT add any headings, lists, or extra explanations.
Do NOT mention any specific model or provider names.
</instruction>
""";

        public static bool IsIdentityQuestion(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();
            if (trimmed.Contains('\n') || trimmed.Contains('\r'))
                return false;

            static bool IsIgnorable(char c)
                => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);

            var normalized = new string(trimmed.Where(c => !IsIgnorable(c)).ToArray()).ToLowerInvariant();
            if (normalized.Length == 0)
                return false;

            if (normalized.Length > 40)
                return false;

            var prefixes = new[]
            {
                "你是谁",
                "你是誰",
                "你叫什么",
                "你叫什麼",
                "你叫什么名字",
                "你叫什麼名字",
                "什么模型",
                "什麼模型",
                "底层是什么",
                "底層是什麼",
                "谁开发",
                "誰開發",
                "whoareyou",
                "whoarleyou",
                "whatmodel",
                "nishishui",
                "nishihsui",
                "nishiname"
            };

            foreach (var p in prefixes)
            {
                if (string.Equals(normalized, p, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase) && normalized.Length <= p.Length + 1)
                    return true;
            }

            var containsPatterns = new[]
            {
                "你是gpt", "你是chatgpt", "你是claude", "你是gemini",
                "你是通义", "你是qwen", "你是deepseek", "你是文心",
                "你是混元", "你是kimi", "你是llama", "你是mistral",
                "你是讯飞", "你是spark", "你是百川", "你是智谱",
                "你底层", "你的底层", "你基于什么", "你基于哪个",
                "你用的什么模型", "你用的哪个模型", "你背后是", "你背后什么",
                "你是哪家", "你是哪个公司", "你的api",
                "谁做的你", "谁创造的你", "谁训练的你",
                "aregpt", "areclaude", "aregemini", "basedon", "poweredby",
                "underlyingmodel", "whichmodel", "whatllm"
            };

            foreach (var p in containsPatterns)
            {
                if (normalized.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion

        #region 公共方法

        public static string GetModeTemplate(ChatMode mode)
        {
            return mode switch
            {
                ChatMode.Ask => AskModeTemplate,
                ChatMode.Agent => AgentModeTemplate,
                ChatMode.Plan => PlanModeTemplate,
                _ => AskModeTemplate
            };
        }

        public static string BuildUserPrompt(ChatMode mode, string userInput)
        {
            if (IsIdentityQuestion(userInput))
            {
                return IdentityQuestionPrompt;
            }

            return userInput;
        }

        #endregion
    }
}
