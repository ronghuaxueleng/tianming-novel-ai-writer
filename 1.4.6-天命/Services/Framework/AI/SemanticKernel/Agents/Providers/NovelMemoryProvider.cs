#pragma warning disable SKEXP0130

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Agents.Providers
{
    public sealed class NovelMemoryProvider : AIContextProvider
    {
        private readonly StructuredMemoryExtractor _extractor;

        public NovelMemoryProvider(StructuredMemoryExtractor extractor)
        {
            _extractor = extractor;
        }

        public override Task MessageAddingAsync(
            string? threadId,
            ChatMessage newMessage,
            CancellationToken cancellationToken = default)
        {
            if (newMessage.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(newMessage.Text))
            {
                _extractor.ExtractFromResponse(newMessage.Text);
            }

            return Task.CompletedTask;
        }

        public override Task<AIContext> ModelInvokingAsync(
            ICollection<ChatMessage> newMessages,
            CancellationToken cancellationToken = default)
        {
            var context = new AIContext();

            if (_extractor.HasMemory())
            {
                var memoryText = _extractor.ToTextFormat();
                if (!string.IsNullOrWhiteSpace(memoryText))
                {
                    context.Instructions = memoryText;
                    TM.App.Log($"[NovelMemoryProvider] 注入结构化记忆 {memoryText.Length} 字符");
                }
            }

            return Task.FromResult(context);
        }
    }
}
