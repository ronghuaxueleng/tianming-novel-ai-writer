using System;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public class AgentModeMapper : IConversationMessageMapper
    {
        public ConversationMessage MapFromStreamingResult(
            string userInput,
            string rawContent,
            string? thinking)
        {
            return new ConversationMessage
            {
                Role = Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant,
                Timestamp = DateTime.Now,
                Summary = rawContent,
                AnalysisRaw = thinking ?? string.Empty,
                AnalysisBlocks = ThinkingBlockParser.Parse(thinking),
                Payload = new AgentPayload()
            };
        }

        public string GenerateSummary(ConversationMessage message)
        {
            return message.Summary;
        }
    }
}
