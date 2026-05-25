#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Agents.Providers
{
    public sealed class RAGContextProvider : AIContextProvider
    {
        private readonly VectorSearchService _vectorSearch;
        private readonly Func<bool> _isSessionCompressed;
        private HashSet<string> _lastInjectedChapterKeys = new();

        public IReadOnlyList<SearchResult> LastChapterResults { get; private set; } = Array.Empty<SearchResult>();

        public RAGContextProvider(VectorSearchService vectorSearch, Func<bool> isSessionCompressed)
        {
            _vectorSearch = vectorSearch;
            _isSessionCompressed = isSessionCompressed;
        }

        public override async Task<AIContext> ModelInvokingAsync(
            ICollection<ChatMessage> newMessages,
            CancellationToken cancellationToken = default)
        {
            var context = new AIContext();

            var userMessage = newMessages
                .LastOrDefault(m => m.Role == ChatRole.User);
            var userText = userMessage?.Text;

            if (string.IsNullOrWhiteSpace(userText))
                return context;

            LastChapterResults = Array.Empty<SearchResult>();
            var instructionBuilder = new StringBuilder();

            if (_isSessionCompressed() && _vectorSearch.IsAvailable && _vectorSearch.IndexedConversationTurns > 0)
            {
                try
                {
                    var recalled = _vectorSearch.SearchConversation(userText, topK: 3);
                    if (recalled.Count > 0)
                    {
                        var recalledText = VectorSearchService.FormatRecalledContext(recalled);
                        instructionBuilder.AppendLine(recalledText);
                        TM.App.Log($"[RAGContextProvider] RAG召回 {recalled.Count} 条相关历史，{recalledText.Length} 字符");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[RAGContextProvider] RAG召回失败（非致命）: {ex.Message}");
                }
            }

            if (ShouldTriggerChapterSearch(userText))
            {
                try
                {
                    var chapterResults = await _vectorSearch.SearchAsync(userText, topK: 3);

                    chapterResults = chapterResults.Where(r => r.Score >= 0.4).ToList();

                    var currentKeys = new HashSet<string>(chapterResults.Select(r => $"{r.ChapterId}:{r.Position}"));
                    chapterResults = chapterResults
                        .Where(r => !_lastInjectedChapterKeys.Contains($"{r.ChapterId}:{r.Position}"))
                        .ToList();
                    _lastInjectedChapterKeys = currentKeys;

                    if (chapterResults.Count > 0)
                    {
                        LastChapterResults = chapterResults;
                        var chapterContext = FormatChapterRecallContext(chapterResults);
                        instructionBuilder.AppendLine(chapterContext);
                        TM.App.Log($"[RAGContextProvider] 章节检索 {chapterResults.Count} 条（去重后），{chapterContext.Length} 字符");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[RAGContextProvider] 章节检索失败（非致命）: {ex.Message}");
                }
            }

            if (instructionBuilder.Length > 0)
            {
                context.Instructions = instructionBuilder.ToString();
            }

            return context;
        }

        public void ResetDedup()
        {
            _lastInjectedChapterKeys.Clear();
        }

        private static bool ShouldTriggerChapterSearch(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return false;
            if (userText.Length < 4) return false;

            var chatPatterns = new[] { "你好", "谢谢", "好的", "嗯嗯", "哈哈", "了解", "明白", "收到",
                "ok", "yes", "no", "hi", "hello", "thanks", "继续", "下一步" };
            var lower = userText.ToLowerInvariant();
            return !chatPatterns.Any(p => lower == p);
        }

        private static string FormatChapterRecallContext(List<SearchResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<context_block source=\"chapter_recall\">");
            foreach (var r in results)
            {
                sb.AppendLine($"[章节 {r.ChapterId}，片段 {r.Position}，相关度 {r.Score:F2}]");
                sb.AppendLine(r.Content.Length > 600 ? r.Content[..600] + "…" : r.Content);
                sb.AppendLine();
            }
            sb.AppendLine("</context_block>");
            return sb.ToString();
        }
    }
}
