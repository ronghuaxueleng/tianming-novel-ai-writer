using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.Core;

#pragma warning disable SKEXP0001

namespace TM.Services.Framework.AI.SemanticKernel
{
    public static class ChatModeSettings
    {
        public static int LastUsedMaxTokens { get; private set; } = 4096;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ChatModeSettings] {key}: {ex.Message}");
        }

        public static PromptExecutionSettings GetExecutionSettings(ChatMode mode, double temperature = 0.7, int? overrideMaxTokens = null)
        {
            int maxTokens = 4096;

            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var config = aiService.GetActiveConfiguration();
                if (config != null)
                {
                    if (config.MaxTokens > 0)
                    {
                        maxTokens = config.MaxTokens;
                    }

                    if (config.Temperature > 0)
                    {
                        temperature = config.Temperature;
                    }

                    var model = aiService.GetModelById(config.ModelId);
                    int safeLimit = model?.MaxOutputTokens > 0 ? model.MaxOutputTokens : 0;
                    if (safeLimit > 0 && maxTokens > safeLimit)
                    {
                        TM.App.Log($"[ChatModeSettings] MaxTokens={maxTokens} 超过安全上限 {safeLimit}，已裁剪");
                        maxTokens = safeLimit;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChatModeSettings] 读取配置失败: {ex.Message}");
            }

            if (overrideMaxTokens.HasValue && overrideMaxTokens.Value > 0)
            {
                maxTokens = overrideMaxTokens.Value;
                TM.App.Log($"[ChatModeSettings] override MaxTokens={maxTokens}");
            }

            LastUsedMaxTokens = maxTokens;

            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "temperature", temperature },
                    { "max_tokens", maxTokens }
                }
            };

            switch (mode)
            {
                case ChatMode.Ask:
                    settings.FunctionChoiceBehavior = null;
                    break;

                case ChatMode.Agent:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                case ChatMode.Plan:
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();
                    break;

                default:
                    settings.FunctionChoiceBehavior = null;
                    break;
            }

            TM.App.Log($"[ChatModeSettings] Mode={mode}, MaxTokens={maxTokens}, Temperature={temperature}, FunctionChoice={(settings.FunctionChoiceBehavior != null ? "Auto" : "None")}");
            return settings;
        }

        public static int GetAdaptiveMaxTokens(ChatHistory history, int baseMaxTokens)
        {
            if (baseMaxTokens <= 0)
            {
                baseMaxTokens = 4096;
            }

            try
            {
                var aiService = ServiceLocator.Get<AIService>();
                var config = aiService.GetActiveConfiguration();
                if (config == null)
                {
                    return baseMaxTokens;
                }

                var model = aiService.GetModelById(config.ModelId);

                var contextWindow = config.ContextWindow > 0
                    ? config.ContextWindow
                    : (model?.ContextWindow > 0 ? model.ContextWindow : 0);

                if (contextWindow <= 0)
                {
                    return ClampToMaxOutputTokens(baseMaxTokens, model);
                }

                var estimatedInputTokens = EstimateTokensFromHistory(history);

                const int safetyMargin = 768;

                var allowedByWindow = contextWindow - estimatedInputTokens - safetyMargin;
                if (allowedByWindow < 256)
                {
                    allowedByWindow = 256;
                }

                var adaptive = Math.Min(baseMaxTokens, allowedByWindow);
                return ClampToMaxOutputTokens(adaptive, model);
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetAdaptiveMaxTokens), ex);
                return baseMaxTokens;
            }
        }

        public static PromptExecutionSettings GetExecutionSettings(ChatMode mode, ChatHistory history, double temperature = 0.7, int? overrideMaxTokens = null)
        {
            var baseSettings = GetExecutionSettings(mode, temperature, overrideMaxTokens);

            int baseMaxTokens = 4096;
            if (baseSettings.ExtensionData != null &&
                baseSettings.ExtensionData.TryGetValue("max_tokens", out var v) &&
                v != null)
            {
                baseMaxTokens = v switch
                {
                    int i => i,
                    long l => (int)l,
                    double d => (int)d,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => baseMaxTokens
                };
            }

            var adaptive = GetAdaptiveMaxTokens(history, baseMaxTokens);

            baseSettings.ExtensionData ??= new Dictionary<string, object>();
            baseSettings.ExtensionData["max_tokens"] = adaptive;

            TM.App.Log($"[ChatModeSettings] Adaptive max_tokens: base={baseMaxTokens}, adaptive={adaptive}");
            return baseSettings;
        }

        private static int ClampToMaxOutputTokens(int value, AIModel? model)
        {
            if (value <= 0) return 4096;

            var safeLimit = model?.MaxOutputTokens > 0 ? model.MaxOutputTokens : 0;
            if (safeLimit > 0 && value > safeLimit)
            {
                return safeLimit;
            }

            return value;
        }

        private static int EstimateTokensFromHistory(ChatHistory history)
            => TM.Framework.Common.Helpers.TokenEstimator.CountTokens(history);

        private static int EstimateContextWindowByModelName(string? modelId, string? modelName)
        {
            var name = (modelName ?? modelId ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(name)) return 0;

            if (name.Contains("claude")) return 200000;
            if (name.Contains("gpt-4")) return 128000;
            if (name.Contains("qwen"))
            {
                return (name.Contains("long") || name.Contains("max")) ? 1000000 : 128000;
            }
            if (name.Contains("deepseek")) return 128000;
            if (name.Contains("gemini")) return 1000000;

            return 128000;
        }

        public static bool RequiresFunctionConfirmation(ChatMode mode)
        {
            return mode == ChatMode.Plan;
        }

        public static bool IsMaxTokensError(Exception ex)
        {
            if (ex == null) return false;

            var msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
            var inner = ex.InnerException?.Message?.ToLowerInvariant() ?? string.Empty;

            var keywords = new[] { "max_tokens", "maximum", "token limit", "too large", "exceeds", "output_tokens" };
            foreach (var kw in keywords)
            {
                if (msg.Contains(kw) || inner.Contains(kw))
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetFallbackMaxTokens(int current)
        {
            const int minValue = 4096;

            var fallback = current / 2;
            return fallback < minValue ? minValue : fallback;
        }
    }
}
