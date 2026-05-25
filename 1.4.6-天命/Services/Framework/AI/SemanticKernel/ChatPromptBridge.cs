using System;
using System.Linq;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class ChatPromptBridge : IChatPromptBridge
    {
        private readonly IPromptRepository _promptRepository;

        public ChatPromptBridge()
            : this(ServiceLocator.Get<TM.Modules.AIAssistant.PromptTools.PromptManagement.Services.PromptService>())
        {
        }

        public ChatPromptBridge(IPromptRepository promptRepository)
        {
            _promptRepository = promptRepository ?? throw new ArgumentNullException(nameof(promptRepository));
        }

        public ChatPromptParts BuildPromptParts(ChatMode mode, string? scopeKey, string userInput)
        {
            userInput ??= string.Empty;

            if (Prompts.PromptLibrary.IsIdentityQuestion(userInput))
                return Prompts.PromptLibrary.BuildSimplePromptParts(mode, userInput);

            if (mode == ChatMode.Ask)
            {
                if (!IsWritingScenarioInput(userInput))
                    return Prompts.PromptLibrary.BuildSimplePromptParts(mode, userInput);

                var spec = TryLoadProjectSpec();
                if (spec != null)
                {
                    return Prompts.PromptLibrary.BuildWritingPromptParts(mode, userInput, spec);
                }
            }

            return Prompts.PromptLibrary.BuildSimplePromptParts(mode, userInput);
        }

        public string BuildPrompt(ChatMode mode, string? scopeKey, string userInput)
        {
            var parts = BuildPromptParts(mode, scopeKey, userInput);

            if (string.IsNullOrWhiteSpace(parts.SystemPrompt))
                return parts.UserPrompt;

            if (string.IsNullOrWhiteSpace(parts.UserPrompt))
                return parts.SystemPrompt;

            return parts.SystemPrompt + "\n\n" + parts.UserPrompt;
        }

        #region 静态便捷访问

        private static ChatPromptBridge? _staticInstance;
        private static ChatPromptBridge StaticInstance => _staticInstance ??= ServiceLocator.Get<ChatPromptBridge>();

        public static ChatPromptParts BuildParts(ChatMode mode, string userInput, string? scopeKey = null)
            => StaticInstance.BuildPromptParts(mode, scopeKey, userInput);

        public static ChatPromptParts BuildWritingParts(ChatMode mode, string userInput, CreativeSpec? spec = null)
            => Prompts.PromptLibrary.BuildWritingPromptParts(mode, userInput, spec);

        public static string Build(ChatMode mode, string userInput, string? scopeKey = null)
            => StaticInstance.BuildPrompt(mode, scopeKey, userInput);

        #endregion

        #region 辅助方法

        private static CreativeSpec? TryLoadProjectSpec()
        {
            try
            {
                return ServiceLocator.Get<SpecLoader>().LoadProjectSpecSync();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWritingScenarioInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (input.Contains("<writing_context type=\"chapter\">", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("<writing_context type=\"mimicry\">", StringComparison.OrdinalIgnoreCase)) return true;

            if (input.Contains("@续写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("@重写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("@仿写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("续写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("改写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("重写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("仿写", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("润色", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("写一", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Contains("生成", StringComparison.OrdinalIgnoreCase) && input.Contains("正文", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        #endregion
    }
}
