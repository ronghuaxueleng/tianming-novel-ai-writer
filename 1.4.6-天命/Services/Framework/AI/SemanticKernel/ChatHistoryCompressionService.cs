using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public sealed class ChatHistoryCompressionService
    {
        private const int HybridRecentRounds = 12;

        private const double CompressionTriggerPercent = 90;
        private const double PostCompressionTokenTargetPercent = 0.75;

        private const double SummaryBudgetPercentOfContextWindow = 0.06;
        private const int SummaryMinChars = 1200;
        private const int SummaryMaxChars = 12000;

        private readonly Func<string, string, CancellationToken, Task<string>> _oneShot;
        private readonly Func<string, int> _getModelContextWindow;

        public ChatHistoryCompressionService(
            Func<string, string, CancellationToken, Task<string>> oneShot,
            Func<string, int> getModelContextWindow)
        {
            _oneShot = oneShot ?? throw new ArgumentNullException(nameof(oneShot));
            _getModelContextWindow = getModelContextWindow ?? throw new ArgumentNullException(nameof(getModelContextWindow));
        }

        public (int EstimatedTokens, int ContextWindow, double UsagePercent) GetContextUsage(
            ChatHistory history,
            string modelId,
            string? additionalText = null,
            int? overrideContextWindow = null)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (string.IsNullOrEmpty(modelId)) return (0, 0, 0);

            int contextWindow = overrideContextWindow.HasValue && overrideContextWindow.Value > 0
                ? overrideContextWindow.Value
                : _getModelContextWindow(modelId);

            if (contextWindow <= 0) return (0, 0, 0);

            int estimatedTokens = EstimateSessionTokens(history);
            if (!string.IsNullOrEmpty(additionalText))
            {
                estimatedTokens += EstimateTokenCount(additionalText);
            }

            double usagePercent = (double)estimatedTokens / contextWindow * 100;
            if (usagePercent > 100) usagePercent = 100;

            return (estimatedTokens, contextWindow, usagePercent);
        }

        public async Task<(ChatHistory CompressedHistory, bool Compressed)> EnsureCompressionIfNeededAsync(
            ChatHistory history,
            string modelId,
            string? upcomingText,
            CancellationToken cancellationToken,
            int? overrideContextWindow = null,
            string? structuredMemory = null)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (history.Count == 0) return (history, false);

            var (_, contextWindow, usagePercent) = GetContextUsage(history, modelId, upcomingText, overrideContextWindow);
            if (contextWindow <= 0) return (history, false);

            if (usagePercent < CompressionTriggerPercent)
            {
                return (history, false);
            }

            var compressed = await CompressChatHistoryAsync(history, modelId, contextWindow, cancellationToken, structuredMemory);
            return (compressed, true);
        }

        public async Task<ChatHistory> CompressChatHistoryAsync(
            ChatHistory history,
            string modelId,
            int contextWindow,
            CancellationToken cancellationToken,
            string? structuredMemory = null)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (history.Count == 0) return history;

            string? systemPrompt = null;
            foreach (var msg in history)
            {
                if (msg.Role == AuthorRole.System)
                {
                    systemPrompt = msg.Content;
                    break;
                }
            }

            string? existingSummarySystem = null;
            bool sawFirstSystem = false;
            foreach (var msg in history)
            {
                if (msg.Role != AuthorRole.System) continue;

                if (!sawFirstSystem)
                {
                    sawFirstSystem = true;
                    continue;
                }

                existingSummarySystem = msg.Content;
                break;
            }

            int keptStartIndex = 0;
            var kept = new List<Microsoft.SemanticKernel.ChatMessageContent>();
            int rounds = 0;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var msg = history[i];
                if (msg.Role == AuthorRole.System) continue;
                if (string.IsNullOrWhiteSpace(msg.Content)) continue;

                kept.Add(msg);
                if (msg.Role == AuthorRole.User)
                {
                    rounds++;
                    if (rounds >= HybridRecentRounds)
                    {
                        keptStartIndex = i;
                        break;
                    }
                }
            }

            kept.Reverse();

            var toSummarize = new List<Microsoft.SemanticKernel.ChatMessageContent>();
            if (kept.Count > 0)
            {
                for (int i = 0; i < keptStartIndex; i++)
                {
                    var msg = history[i];
                    if (msg.Role == AuthorRole.System) continue;
                    if (string.IsNullOrWhiteSpace(msg.Content)) continue;
                    toSummarize.Add(msg);
                }
            }

            if (toSummarize.Count == 0)
            {
                return CompressChatHistoryHardTruncate(history, contextWindow);
            }

            var summaryUserPromptBuilder = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(existingSummarySystem))
            {
                summaryUserPromptBuilder.AppendLine("旧记忆块：");
                summaryUserPromptBuilder.AppendLine(existingSummarySystem);
                summaryUserPromptBuilder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(structuredMemory))
            {
                summaryUserPromptBuilder.AppendLine("结构化记忆（实时抽取）：");
                summaryUserPromptBuilder.AppendLine(structuredMemory);
                summaryUserPromptBuilder.AppendLine();
            }

            summaryUserPromptBuilder.AppendLine("对话片段：");
            foreach (var msg in toSummarize)
            {
                var roleLabel = msg.Role == AuthorRole.User
                    ? "User"
                    : msg.Role == AuthorRole.Assistant
                        ? "Assistant"
                        : "System";
                summaryUserPromptBuilder.AppendLine($"{roleLabel}: {msg.Content}");
            }

            var summarySystemPrompt = BuildNovelMemorySystemPrompt();

            var rawSummary = await _oneShot(summarySystemPrompt, summaryUserPromptBuilder.ToString(), cancellationToken);
            if (string.IsNullOrWhiteSpace(rawSummary) || rawSummary.StartsWith("[错误]", StringComparison.OrdinalIgnoreCase) || rawSummary.StartsWith("[已取消]", StringComparison.OrdinalIgnoreCase))
            {
                return CompressChatHistoryHardTruncate(history, contextWindow);
            }

            var cleanedSummary = rawSummary.Trim();
            var summaryMaxChars = GetSummaryMaxChars(contextWindow);
            if (cleanedSummary.Length > summaryMaxChars)
            {
                cleanedSummary = cleanedSummary[..summaryMaxChars];
            }

            var newHistory = string.IsNullOrWhiteSpace(systemPrompt)
                ? new ChatHistory()
                : new ChatHistory(systemPrompt);

            if (!string.IsNullOrWhiteSpace(cleanedSummary))
            {
                newHistory.AddSystemMessage($"<rolling_memory>\n{cleanedSummary}\n</rolling_memory>");
            }

            foreach (var msg in kept)
            {
                var text = msg.Content ?? string.Empty;
                if (msg.Role == AuthorRole.User)
                {
                    newHistory.AddUserMessage(text);
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    newHistory.AddAssistantMessage(text);
                }
            }

            int tokenLimit = (int)(contextWindow * PostCompressionTokenTargetPercent);
            while (EstimateSessionTokens(newHistory) > tokenLimit)
            {
                int removeIndex = -1;
                for (int i = 0; i < newHistory.Count; i++)
                {
                    if (newHistory[i].Role != AuthorRole.System)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex < 0) break;
                newHistory.RemoveAt(removeIndex);
            }

            return newHistory;
        }

        private static int GetSummaryMaxChars(int contextWindow)
        {
            var chars = (int)Math.Round(contextWindow * SummaryBudgetPercentOfContextWindow);
            if (chars < SummaryMinChars) return SummaryMinChars;
            if (chars > SummaryMaxChars) return SummaryMaxChars;
            return chars;
        }

        private static string BuildNovelMemorySystemPrompt()
        {
            return "<role>Context Memory Compressor for novel writing. Merge given inputs into a new rolling memory block.</role>\n\n" +
                   "<input_description>\n" +
                   "- 旧记忆块：上一轮压缩的结果（如有）\n" +
                   "- 结构化记忆：实时抽取的角色/剧情/世界状态（如有，优先保留）\n" +
                   "- 对话片段：需要压缩的历史对话\n" +
                   "</input_description>\n\n" +
                   "<retention_priority>\n" +
                   "1. MUST RETAIN: 角色当前状态、未回收伏笔、世界规则\n" +
                   "2. IMPORTANT: 剧情里程碑、当前任务目标\n" +
                   "3. COMPRESSIBLE: 日常对话、已完成任务、已回收伏笔\n" +
                   "</retention_priority>\n\n" +
                   "<output_rules>\n" +
                   "1) 只输出纯文本，不要Markdown代码块，不要解释。\n" +
                   "2) 不要包含推理过程。\n" +
                   "3) 必须保持字段稳定，尽量不要改写已有事实，只做补充/合并/纠错。\n" +
                   "4) 结构化记忆中的信息优先级最高，必须完整保留。\n" +
                   "</output_rules>\n\n" +
                   "<output_format mandatory=\"true\">\n" +
                   "请按以下固定结构输出（缺失项可留空，但标题必须保留）：\n" +
                   "<section name=\"世界观/规则\"/>\n" +
                   "<section name=\"人物状态表\">（人物：位置/目标/关系变化/伤病/情绪/秘密）</section>\n" +
                   "<section name=\"势力/组织状态\"/>\n" +
                   "<section name=\"时间线/里程碑\"/>\n" +
                   "<section name=\"伏笔清单\">（已埋未收/已回收/待埋）</section>\n" +
                   "<section name=\"叙事约束/文风\"/>\n" +
                   "<section name=\"当前卷目标/待办\"/>\n" +
                   "</output_format>\n";
        }

        private const int MinRecentRounds = 2;

        private static ChatHistory CompressChatHistoryHardTruncate(ChatHistory history, int contextWindow)
        {
            var newHistory = new ChatHistory();

            foreach (var msg in history)
            {
                if (msg.Role == AuthorRole.System)
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        newHistory.AddSystemMessage(msg.Content);
                    }
                    break;
                }
            }

            bool sawFirstSystem = false;
            foreach (var msg in history)
            {
                if (msg.Role != AuthorRole.System) continue;
                if (!sawFirstSystem) { sawFirstSystem = true; continue; }
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    var summaryContent = msg.Content;
                    var maxLen = (int)(contextWindow * 0.04);
                    if (summaryContent.Length > maxLen)
                    {
                        summaryContent = summaryContent[..maxLen] + "\n[已截断]";
                    }
                    newHistory.AddSystemMessage(summaryContent);
                }
                break;
            }

            var kept = new List<Microsoft.SemanticKernel.ChatMessageContent>();
            int tokens = 0;
            int tokenLimit = (int)(contextWindow * PostCompressionTokenTargetPercent);
            int rounds = 0;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                var msg = history[i];
                if (msg.Role == AuthorRole.System) continue;

                var text = msg.Content;
                if (string.IsNullOrEmpty(text)) continue;

                var t = EstimateTokenCount(text);

                if (rounds >= MinRecentRounds && tokens + t > tokenLimit)
                {
                    break;
                }

                tokens += t;
                kept.Add(msg);

                if (msg.Role == AuthorRole.User)
                {
                    rounds++;
                }
            }

            kept.Reverse();

            foreach (var msg in kept)
            {
                var text = msg.Content ?? string.Empty;
                if (msg.Role == AuthorRole.User)
                {
                    newHistory.AddUserMessage(text);
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    newHistory.AddAssistantMessage(text);
                }
            }

            TM.App.Log($"[ChatHistoryCompressionService] 硬截断完成: 保留{rounds}轮对话, {tokens}tokens");

            return newHistory;
        }

        private static int EstimateSessionTokens(ChatHistory history)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(history);

        private static int EstimateTokenCount(string? text)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(text);
    }
}
