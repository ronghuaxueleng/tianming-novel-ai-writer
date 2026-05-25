namespace TM.Services.Framework.AI.SemanticKernel.Prompts.Dialog
{
    public static class DialogPromptProvider
    {
        public const string AnalysisAnswerSpec = """
<output_format required="true">
When returning natural language answers to the user, you MUST use the following structure:
<analysis>Your analysis, reasoning process, trade-offs, and plan steps go here</analysis>
<answer>The final response content for the user goes here</answer>

When returning JSON or parameters per function/tool-calling protocol, do NOT use these tags.

IMPORTANT: <think> and <thinking> tags are NOT valid substitutes for <analysis>. Do not use them.
</output_format>
""";
    }
}
