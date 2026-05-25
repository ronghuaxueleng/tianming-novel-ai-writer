using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.SemanticKernel;
using OpenAI.Chat;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies
{
    public class OpenAIReasoningStrategy : IThinkingStrategy
    {
        private readonly TagBasedStrategy _tagFallback = new();

        private static bool _reflectionResolved;
        private static FieldInfo? _choicesField;
        private static FieldInfo? _deltaField;
        private static FieldInfo? _deltaRawDataField;

        public ThinkingRouteResult Extract(StreamingChatMessageContent chunk)
        {
            var reasoning = TryExtractReasoning(chunk);

            if (reasoning != null)
            {
                return new ThinkingRouteResult
                {
                    ThinkingContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
                    AnswerContent = null
                };
            }

            return _tagFallback.Extract(chunk);
        }

        public ThinkingRouteResult Flush() => _tagFallback.Flush();

        private static string? TryExtractReasoning(StreamingChatMessageContent chunk)
        {
            if (chunk.InnerContent is not StreamingChatCompletionUpdate update)
                return null;

            try
            {
                return TryExtractViaReflection(update);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OpenAIReasoningStrategy] reasoning 提取异常（非致命）: {ex.Message}");
                return null;
            }
        }

        private static string? TryExtractViaReflection(StreamingChatCompletionUpdate update)
        {
            EnsureReflectionResolved(update);

            if (_choicesField == null)
                return null;

            var choices = _choicesField.GetValue(update);
            if (choices == null)
                return null;

            object? firstChoice = null;
            if (choices is System.Collections.IList list && list.Count > 0)
            {
                firstChoice = list[0];
            }
            else if (choices is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    firstChoice = item;
                    break;
                }
            }

            if (firstChoice == null)
                return null;

            if (_deltaField == null)
            {
                _deltaField = firstChoice.GetType().GetField("Delta",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? FindFieldByNameContains(firstChoice.GetType(), "delta");
            }

            var delta = _deltaField?.GetValue(firstChoice);
            if (delta == null)
                return null;

            if (_deltaRawDataField == null)
            {
                _deltaRawDataField = FindFieldByNameContains(delta.GetType(), "serializedAdditionalRawData")
                    ?? FindFieldByNameContains(delta.GetType(), "additionalRawData")
                    ?? FindFieldByNameContains(delta.GetType(), "rawData");
            }

            var rawData = _deltaRawDataField?.GetValue(delta);
            if (rawData is IDictionary<string, BinaryData> binaryDict)
            {
                if (binaryDict.TryGetValue("reasoning_content", out var binaryValue))
                {
                    var jsonStr = binaryValue.ToString();
                    if (jsonStr.Length >= 2 && jsonStr[0] == '"' && jsonStr[^1] == '"')
                    {
                        try
                        {
                            return System.Text.Json.JsonSerializer.Deserialize<string>(jsonStr);
                        }
                        catch
                        {
                            return jsonStr[1..^1];
                        }
                    }
                    return jsonStr;
                }
            }

            return null;
        }

        private static void EnsureReflectionResolved(StreamingChatCompletionUpdate update)
        {
            if (_reflectionResolved)
                return;

            _reflectionResolved = true;

            try
            {
                var updateType = update.GetType();
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _choicesField = updateType.GetField("Choices", flags)
                    ?? FindFieldByNameContains(updateType, "choices");

                TM.App.Log($"[OpenAIReasoningStrategy] 反射解析完成: choices={_choicesField?.Name ?? "null"}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OpenAIReasoningStrategy] 反射解析失败（将使用 TagBased 兜底）: {ex.Message}");
            }
        }

        private static FieldInfo? FindFieldByNameContains(Type type, string namePart)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (field.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                    return field;
            }
            return null;
        }
    }
}
