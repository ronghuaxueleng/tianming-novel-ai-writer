using System;
using System.Text;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies
{
    public class TagBasedStrategy : IThinkingStrategy
    {
        private readonly StringBuilder _buffer = new();
        private TagParseState _state = TagParseState.Content;

        private enum TagParseState
        {
            Content,
            InThinking,
            BetweenTags,
            InAnswer,
            Done
        }

        private static readonly string[] ThinkingOpenTags = { "<think>", "<analysis>" };
        private static readonly string[] ThinkingCloseTags = { "</think>", "</analysis>" };
        private const string AnswerOpenTag = "<answer>";
        private const string AnswerCloseTag = "</answer>";

        private static readonly System.Text.RegularExpressions.Regex NoiseLinePattern = new(
            @"^(?:Thought\s+for\s+[\d\.]+\s*s|Thinking\.{2,})\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private string? _activeCloseTag;

        public ThinkingRouteResult Extract(StreamingChatMessageContent chunk)
        {
            var text = chunk.Content;
            if (string.IsNullOrEmpty(text))
                return default;

            _buffer.Append(text);
            return ParseBuffer();
        }

        public ThinkingRouteResult Flush()
        {
            if (_buffer.Length == 0)
                return default;

            var remaining = _buffer.ToString();
            _buffer.Clear();

            return _state switch
            {
                TagParseState.InThinking => new ThinkingRouteResult
                {
                    ThinkingContent = remaining,
                    AnswerContent = null
                },
                TagParseState.InAnswer or TagParseState.Done => new ThinkingRouteResult
                {
                    ThinkingContent = null,
                    AnswerContent = remaining
                },
                _ => new ThinkingRouteResult
                {
                    ThinkingContent = null,
                    AnswerContent = string.IsNullOrWhiteSpace(remaining) ? null : remaining
                }
            };
        }

        private ThinkingRouteResult ParseBuffer()
        {
            StringBuilder? thinkingOut = null;
            StringBuilder? answerOut = null;

            var text = _buffer.ToString();
            var consumed = 0;

            while (consumed < text.Length)
            {
                switch (_state)
                {
                    case TagParseState.Content:
                    {
                        var (tagPos, tag) = FindFirst(text, consumed, ThinkingOpenTags);
                        var answerPos = IndexOfCI(text, AnswerOpenTag, consumed);

                        if (tagPos >= 0 && (answerPos < 0 || tagPos <= answerPos))
                        {
                            if (tagPos > consumed)
                            {
                                var before = text.Substring(consumed, tagPos - consumed);
                                before = StripHighConfidenceNoise(before);
                                if (!string.IsNullOrEmpty(before))
                                    (answerOut ??= new()).Append(before);
                            }
                            _activeCloseTag = GetMatchingCloseTag(tag);
                            consumed = tagPos + tag.Length;
                            _state = TagParseState.InThinking;
                        }
                        else if (answerPos >= 0)
                        {
                            if (answerPos > consumed)
                            {
                                var before = text.Substring(consumed, answerPos - consumed);
                                before = StripHighConfidenceNoise(before);
                                if (!string.IsNullOrEmpty(before))
                                    (answerOut ??= new()).Append(before);
                            }
                            consumed = answerPos + AnswerOpenTag.Length;
                            _state = TagParseState.InAnswer;
                        }
                        else
                        {
                            var tail = text.Substring(consumed);
                            var safeCut = FindSafeCut(tail, ThinkingOpenTags, AnswerOpenTag);
                            if (safeCut < tail.Length)
                            {
                                if (safeCut > 0)
                                {
                                    var safe = tail.Substring(0, safeCut);
                                    safe = StripHighConfidenceNoise(safe);
                                    if (!string.IsNullOrEmpty(safe))
                                        (answerOut ??= new()).Append(safe);
                                }
                                consumed += safeCut;
                                goto done;
                            }
                            else
                            {
                                var output = StripHighConfidenceNoise(tail);
                                if (!string.IsNullOrEmpty(output))
                                    (answerOut ??= new()).Append(output);
                                consumed = text.Length;
                            }
                        }
                        break;
                    }

                    case TagParseState.InThinking:
                    {
                        var closeTag = _activeCloseTag ?? "</think>";
                        var end = IndexOfCI(text, closeTag, consumed);
                        if (end >= 0)
                        {
                            if (end > consumed)
                                (thinkingOut ??= new()).Append(text, consumed, end - consumed);
                            consumed = end + closeTag.Length;
                            _state = TagParseState.BetweenTags;
                        }
                        else
                        {
                            var tail = text.Substring(consumed);
                            var safeCut = FindSafeCutSingle(tail, closeTag);
                            if (safeCut < tail.Length)
                            {
                                if (safeCut > 0)
                                    (thinkingOut ??= new()).Append(tail, 0, safeCut);
                                consumed += safeCut;
                                goto done;
                            }
                            else
                            {
                                (thinkingOut ??= new()).Append(tail);
                                consumed = text.Length;
                            }
                        }
                        break;
                    }

                    case TagParseState.BetweenTags:
                    {
                        var answerPos = IndexOfCI(text, AnswerOpenTag, consumed);
                        var (thinkPos, thinkTag) = FindFirst(text, consumed, ThinkingOpenTags);

                        if (answerPos >= 0 && (thinkPos < 0 || answerPos <= thinkPos))
                        {
                            consumed = answerPos + AnswerOpenTag.Length;
                            _state = TagParseState.InAnswer;
                        }
                        else if (thinkPos >= 0)
                        {
                            _activeCloseTag = GetMatchingCloseTag(thinkTag);
                            consumed = thinkPos + thinkTag.Length;
                            _state = TagParseState.InThinking;
                        }
                        else
                        {
                            var tail = text.Substring(consumed);
                            var safeCut = FindSafeCut(tail, ThinkingOpenTags, AnswerOpenTag);
                            if (safeCut < tail.Length)
                            {
                                if (safeCut > 0)
                                {
                                    var safe = tail.Substring(0, safeCut).TrimStart('\r', '\n');
                                    if (!string.IsNullOrEmpty(safe))
                                        (answerOut ??= new()).Append(safe);
                                }
                                consumed += safeCut;
                                goto done;
                            }
                            else
                            {
                                var output = tail.TrimStart('\r', '\n');
                                if (!string.IsNullOrEmpty(output))
                                    (answerOut ??= new()).Append(output);
                                consumed = text.Length;
                            }
                        }
                        break;
                    }

                    case TagParseState.InAnswer:
                    {
                        var end = IndexOfCI(text, AnswerCloseTag, consumed);
                        if (end >= 0)
                        {
                            if (end > consumed)
                                (answerOut ??= new()).Append(text, consumed, end - consumed);
                            consumed = end + AnswerCloseTag.Length;
                            _state = TagParseState.Done;
                        }
                        else
                        {
                            var tail = text.Substring(consumed);
                            var safeCut = FindSafeCutSingle(tail, AnswerCloseTag);
                            if (safeCut < tail.Length)
                            {
                                if (safeCut > 0)
                                    (answerOut ??= new()).Append(tail, 0, safeCut);
                                consumed += safeCut;
                                goto done;
                            }
                            else
                            {
                                (answerOut ??= new()).Append(tail);
                                consumed = text.Length;
                            }
                        }
                        break;
                    }

                    case TagParseState.Done:
                    default:
                    {
                        if (consumed < text.Length)
                        {
                            (answerOut ??= new()).Append(text, consumed, text.Length - consumed);
                        }
                        consumed = text.Length;
                        goto done;
                    }
                }
            }

        done:
            if (consumed > 0 && consumed <= _buffer.Length)
            {
                _buffer.Remove(0, consumed);
            }

            return new ThinkingRouteResult
            {
                ThinkingContent = thinkingOut?.ToString(),
                AnswerContent = answerOut?.ToString()
            };
        }

        #region 辅助方法

        private static int IndexOfCI(string source, string value, int startIndex)
        {
            if (startIndex >= source.Length) return -1;
            return source.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        private static (int Index, string Tag) FindFirst(string source, int startIndex, string[] tags)
        {
            var bestIdx = -1;
            var bestTag = string.Empty;
            foreach (var tag in tags)
            {
                var idx = IndexOfCI(source, tag, startIndex);
                if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                {
                    bestIdx = idx;
                    bestTag = tag;
                }
            }
            return (bestIdx, bestTag);
        }

        private static string GetMatchingCloseTag(string openTag)
        {
            if (openTag.Equals("<think>", StringComparison.OrdinalIgnoreCase))
                return "</think>";
            return "</analysis>";
        }

        private static int FindSafeCut(string tail, string[] tags1, string singleTag)
        {
            var lastLt = tail.LastIndexOf('<');
            if (lastLt < 0) return tail.Length;

            var suffix = tail.Substring(lastLt);
            foreach (var tag in tags1)
            {
                if (tag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return lastLt;
            }
            if (singleTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return lastLt;

            return tail.Length;
        }

        private static int FindSafeCutSingle(string tail, string closeTag)
        {
            var lastLt = tail.LastIndexOf('<');
            if (lastLt < 0) return tail.Length;

            var suffix = tail.Substring(lastLt);
            if (closeTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return lastLt;

            return tail.Length;
        }

        private static string StripHighConfidenceNoise(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var trimmed = text.TrimStart();
            if (trimmed.Length == 0) return text;

            var eol = trimmed.IndexOf('\n');
            var firstLine = eol >= 0 ? trimmed.Substring(0, eol) : trimmed;

            if (NoiseLinePattern.IsMatch(firstLine))
            {
                if (eol < 0) return string.Empty;
                var rest = trimmed.Substring(eol + 1);
                return string.IsNullOrWhiteSpace(rest) ? string.Empty : rest;
            }

            return text;
        }

        #endregion
    }
}
