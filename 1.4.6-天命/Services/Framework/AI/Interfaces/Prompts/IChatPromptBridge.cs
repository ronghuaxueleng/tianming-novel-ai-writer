using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;

namespace TM.Services.Framework.AI.Interfaces.Prompts
{
    public sealed class ChatPromptParts
    {
        public string SystemPrompt { get; init; } = string.Empty;

        public string UserPrompt { get; init; } = string.Empty;
    }

    public interface IChatPromptBridge
    {
        ChatPromptParts BuildPromptParts(ChatMode mode, string? scopeKey, string userInput);

        string BuildPrompt(ChatMode mode, string? scopeKey, string userInput);
    }
}
