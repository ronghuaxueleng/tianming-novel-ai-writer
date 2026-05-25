namespace TM.Services.Framework.AI.SemanticKernel.Chunk
{
    public interface IStreamChunk { }

    public record TextDeltaChunk(string Content) : IStreamChunk;
    public record ThinkingDeltaChunk(string Content) : IStreamChunk;
    public record ThinkingCompleteChunk(string FullContent, int DurationMs) : IStreamChunk;
    public record ToolCallChunk(string ToolName, string Arguments) : IStreamChunk;
    public record ErrorChunk(string Category, string Message) : IStreamChunk;
    public record UsageChunk(int PromptTokens, int CompletionTokens) : IStreamChunk;
    public record StreamCompleteChunk() : IStreamChunk;
}
