using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking.Strategies;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking
{
    public class ThinkingRouter
    {
        private readonly IThinkingStrategy _strategy;

        public ThinkingRouter(string providerType)
        {
            _strategy = providerType switch
            {
                "Anthropic" => new MetadataThinkingStrategy(),
                "Google"    => new OpenAIReasoningStrategy(),
                _           => new OpenAIReasoningStrategy()
            };
        }

        public ThinkingRouteResult Route(StreamingChatMessageContent chunk)
            => _strategy.Extract(chunk);

        public ThinkingRouteResult Flush()
            => _strategy.Flush();

        public static void InjectRequestParameters(PromptExecutionSettings settings, string providerType, string modelId)
        {
            if (settings == null || string.IsNullOrEmpty(modelId)) return;

            try
            {
                var model = ServiceLocator.Get<AIService>().GetModelById(modelId);
                if (model == null || !model.SupportsEnableThinking) return;
            }
            catch
            {
                return;
            }

            settings.ExtensionData ??= new Dictionary<string, object>();

            switch (providerType)
            {
                case "Anthropic":
                    settings.ExtensionData["thinking"] = new Dictionary<string, object>
                    {
                        { "type", "enabled" },
                        { "budget_tokens", CalcBudget(settings) }
                    };
                    TM.App.Log($"[ThinkingRouter] 注入 Anthropic thinking 参数: budget={CalcBudget(settings)}");
                    break;

                case "Google":
                    settings.ExtensionData["thinkingConfig"] = new Dictionary<string, object>
                    {
                        { "thinkingBudget", CalcBudget(settings) }
                    };
                    TM.App.Log($"[ThinkingRouter] 注入 Gemini thinkingConfig 参数: budget={CalcBudget(settings)}");
                    break;

                default:
                    var lower = modelId.ToLowerInvariant();
                    if (lower.Contains("qwen3") || lower.Contains("qwq"))
                    {
                        settings.ExtensionData["enable_thinking"] = true;
                        TM.App.Log($"[ThinkingRouter] 注入 Qwen enable_thinking 参数: model={modelId}");
                    }
                    break;
            }
        }

        private static int CalcBudget(PromptExecutionSettings settings)
        {
            if (settings.ExtensionData?.TryGetValue("max_tokens", out var v) == true)
            {
                var max = v switch
                {
                    int i    => i,
                    long l   => (int)l,
                    double d => (int)d,
                    _        => 0
                };
                if (max > 0)
                    return Math.Clamp(max / 2, 4000, 16000);
            }
            return 8000;
        }
    }
}
