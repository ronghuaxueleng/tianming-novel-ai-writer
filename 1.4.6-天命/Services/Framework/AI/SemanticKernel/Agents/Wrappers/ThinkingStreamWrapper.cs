using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using TM.Services.Framework.AI.SemanticKernel.Chunk;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;

namespace TM.Services.Framework.AI.SemanticKernel.Agents.Wrappers
{
    public sealed class ThinkingStreamWrapper
    {
        private readonly string _providerType;

        public ThinkingStreamWrapper(string providerType)
        {
            _providerType = providerType;
        }

        public async IAsyncEnumerable<IStreamChunk> WrapAgentStreamAsync(
            IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> agentStream,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var router = new ThinkingRouter(_providerType);

            await foreach (var item in agentStream.WithCancellation(ct))
            {
                var chunk = item.Message;

                if (string.IsNullOrEmpty(chunk.Content) &&
                    chunk.InnerContent is not OpenAI.Chat.StreamingChatCompletionUpdate)
                    continue;

                var routed = router.Route(chunk);

                if (!string.IsNullOrEmpty(routed.ThinkingContent))
                    yield return new ThinkingDeltaChunk(routed.ThinkingContent);

                if (!string.IsNullOrEmpty(routed.AnswerContent))
                    yield return new TextDeltaChunk(routed.AnswerContent);
            }

            var flushed = router.Flush();

            if (!string.IsNullOrEmpty(flushed.ThinkingContent))
                yield return new ThinkingDeltaChunk(flushed.ThinkingContent);

            if (!string.IsNullOrEmpty(flushed.AnswerContent))
                yield return new TextDeltaChunk(flushed.AnswerContent);

            yield return new StreamCompleteChunk();
        }
    }
}
